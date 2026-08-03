// ─────────────────────────────────────────────────────────────────────────────
// Ballast — BallastWindow.cs  (v2: multi-account)
//
// Sits next to your DOM. Polls every monitored account once a second, feeds each
// into the tested DisciplineEngine, and shows ONE headline action driven by
// whichever account is in the most trouble — plus a row per account.
//
// ADVISORY ONLY. Never submits, modifies or cancels an order. Never flattens.
// Deliberate v1 decision: software that closes positions costs real money when
// it's wrong, and this hasn't earned that trust yet.
//
// Polling rather than event subscription keeps every read on the UI thread and
// avoids cross-thread marshalling bugs. One second is far finer than human tilt.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using Ballast;

// ─────────────────────────────────────────────────────────────────────────────
// Explicit aliases for every Ballast type this file touches.
//
// NinjaTrader.Cbi is a large namespace with its own JournalEntry, Trade, Order
// and friends. Any Ballast type whose name happens to match one of theirs
// becomes an ambiguous reference the moment both namespaces are imported, and
// the failure surfaces only when compiling inside NinjaTrader - which is the
// one place this file cannot be tested beforehand.
//
// Aliasing every type up front makes that impossible rather than unlikely. An
// alias for a name that never collides costs nothing; the one for a name that
// does collide saves a broken build. If Ballast gains a type used here, add it.
// ─────────────────────────────────────────────────────────────────────────────
using AccountSnapshot     = Ballast.AccountSnapshot;
using BallastJournal      = Ballast.BallastJournal;
using BallastMonitor      = Ballast.BallastMonitor;
using BallastTracker      = Ballast.BallastTracker;
using BallastTrade        = Ballast.BallastTrade;
using DisciplineAction    = Ballast.DisciplineAction;
using DisciplineDecision  = Ballast.DisciplineDecision;
using DisciplineEngine    = Ballast.DisciplineEngine;
using DrawdownType        = Ballast.DrawdownType;
using FirmAccountSpec     = Ballast.FirmAccountSpec;
using JournalBucket       = Ballast.JournalBucket;
using NaturalNameComparer = Ballast.NaturalNameComparer;
using RuleBook            = Ballast.RuleBook;
using RuleBookUpdater     = Ballast.RuleBookUpdater;
using ChartSnapshot       = Ballast.ChartSnapshot;
using AccountGeneration   = Ballast.AccountGeneration;
using BallastState        = Ballast.BallastState;
using TradeReport         = Ballast.TradeReport;
using RiskProfile         = Ballast.RiskProfile;
using SettingsCodec       = Ballast.SettingsCodec;
using RiskProfiles        = Ballast.RiskProfiles;
using RuleUpdateResult    = Ballast.RuleUpdateResult;
using TrackerConfig       = Ballast.TrackerConfig;
using Urgency             = Ballast.Urgency;

namespace NinjaTrader.NinjaScript.AddOns
{
    public class BallastWindow : NTWindow, IWorkspacePersistence
    {
        // ── Surviving a restart ──────────────────────────────────────────────
        //
        // NinjaTrader only writes a window into a saved workspace if that window
        // says who it is, and it says so by implementing IWorkspacePersistence.
        // Without it Ballast was closed every time NinjaTrader started, however
        // the workspace had been saved - so the one thing that is supposed to be
        // watching an account all session had to be remembered and switched on
        // by hand, by the person it is watching.
        //
        // Nothing of Ballast's own goes in the workspace file. Its settings, its
        // journal, its overrides and its session baseline all live in their own
        // files and are the same whichever workspace is open, because they
        // describe accounts rather than a screen layout. These two methods are
        // deliberately empty: the interface is here to be reopened, not to store
        // anything.
        public WorkspaceOptions WorkspaceOptions { get; set; }

        public void Restore(XDocument document, XElement element) { }
        public void Save(XDocument document, XElement element) { }

        private readonly BallastMonitor monitor = new BallastMonitor();
        private DispatcherTimer timer;

        // UI
        private StackPanel accountListPanel;
        private CheckBox monitorAllBox;
        private TextBlock headlineText, urgencyText, headlineAccountText;
        private StackPanel bulletPanel, rowsPanel;
        private Border card;
        private TextBlock statCushion, statPnl, statAccounts, statCushionWho, statCushionCap;
        private ComboBox editTargetBox, ddTypeBox, firmBox, accountTypeBox, generationBox;
        private AccountGeneration generation = AccountGeneration.Auto;
        private readonly RuleBook ruleBook = new RuleBook();
        private TextBox tbBalance, tbDrawdown, tbMaxLosses, tbDailyLoss, tbTarget, tbMaxTrades, tbMaxContracts, tbLockAt;
        private TextBox tbWindowStart, tbWindowEnd;
        private TextBlock windowClock;
        private CheckBox windowAnyTimeBox;
        private CheckBox automatedBox, planStandingBox;
        private ComboBox acctGenBox;
        private TextBlock detectionNote;
        private readonly Dictionary<string, string> accountLabels = new Dictionary<string, string>();

        private readonly Dictionary<string, CheckBox> accountBoxes = new Dictionary<string, CheckBox>();
        /// <summary>The line under each account's tick box, so one row can be
        /// updated without rebuilding the list the trader is clicking on.</summary>
        private readonly Dictionary<string, TextBlock> accountSubs =
            new Dictionary<string, TextBlock>(StringComparer.OrdinalIgnoreCase);
        private DateTime lastAccountRefresh = DateTime.MinValue;
        private bool suppressEditTargetReload;
        /// <summary>Set while the account-type list is being rebuilt, so repopulating it does not count as a choice.</summary>
        private bool suppressTypeApply;

        /// <summary>
        /// Which config the fields on screen currently belong to. null = nothing
        /// loaded yet, "" = the defaults, anything else = that account.
        ///
        /// This exists because of the single most expensive thing the Setup page
        /// did: a trader setting different limits on each account would pick an
        /// account, type their numbers, pick the next account, type its numbers,
        /// and press "Apply and save" once at the end. Switching the selector
        /// reloaded the fields from the newly chosen account, so everything typed
        /// for the previous one was gone - silently, with no warning, and with
        /// the Setup line still showing the old figures. It looked exactly like
        /// Ballast refusing to hold different rules per account.
        ///
        /// Now switching commits what is on screen to the account it was typed
        /// for, first.
        /// </summary>
        private string editingKey;

        /// <summary>
        /// Accounts whose day was picked up from a saved baseline, and the clock
        /// time Ballast was last watching them. Both feed the one-shot check that
        /// works out whether anything happened while it was closed.
        /// </summary>
        private readonly Dictionary<string, DateTime> lastSeenAt =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// When a difference between the account's own P&L and the journal was
        /// first seen. It has to hold still for a few seconds before it is
        /// believed - a round trip lands in the account's realised figure a
        /// moment before it lands in the journal.
        /// </summary>
        private readonly Dictionary<string, DateTime> gapSince =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private DateTime lastSessionSave = DateTime.MinValue;

        // Journal
        private Border journalStripBorder;
        private StackPanel journalStrip;
        private TextBox tbSessionPlan;
        private TextBlock journalInsight, journalSummary, journalPathNote;
        private bool journalDirty;
        private DateTime lastPrune = DateTime.MinValue;
        private StackPanel instrumentPanel, tradesPanel;
        private TextBlock outsideNote;
        private TextBox tbEvents;
        private StackPanel planConfirmRow;
        private bool planPendingConfirm;
        private readonly List<string> events = new List<string>();
        private Button btnToday, btnAllTime;
        private bool tradesTodayOnly = true;
        private bool showBotTrades = false;
        /// <summary>
        /// Which account groups are open. Starts empty, so everything starts
        /// collapsed - and a collapsed group builds no rows and decodes no chart
        /// thumbnails, which is most of the cost of showing this at all.
        /// </summary>
        private readonly List<string> expandedAccounts = new List<string>();
        private string lastTradesSignature = "";
        private TextBlock chartDiag;
        private Grid zoomHost;
        private TextBlock zoomLabel;
        private int zoomIndex;
        /// <summary>Accounts restored from the settings file - never auto-overwritten.</summary>
        private readonly List<string> configuredFromDisk = new List<string>();
        private int lastPendingCount = -1;

        // ── The wall ─────────────────────────────────────────────────────────
        // Everything here exists for one moment: the trader has stopped trading
        // and started trying to get even. See TiltLockout.cs for why it is built
        // the way it is.
        private readonly TiltLog tiltLog = new TiltLog();
        private readonly TiltGate tiltGate = new TiltGate();
        private Border tiltOverlay;
        private TextBlock tiltTitle, tiltLine, tiltAsk, tiltToday, tiltHistory, tiltStood;
        private TextBox tiltTypeBox;
        private ProgressBar tiltProgress;
        private Button tiltGoOn, tiltFixConfig, tiltStandAll;
        private StackPanel tiltAllRow;
        private TextBlock tiltAllNote;
        /// <summary>One live trigger per account, so the wall can be answered for all of them at once.</summary>
        private List<TiltTrigger> tiltDue = new List<TiltTrigger>();
        private StackPanel tiltConfigRow;
        private TiltTrigger tiltCurrent;
        private CheckBox tiltOnBox, tiltGiveBackBox;
        private bool tiltEnabled = true;
        private bool tiltOnGiveBack;
        private TextBlock tiltJournalLine;
        private StackPanel tiltZoomHost;
        private Border imageOverlay;
        private Image imageBig;
        private TextBlock imageCaption;
        private ScrollViewer imageScroll;
        private Button imageZoomBtn;
        private string imagePath = "";
        private bool imageActualSize;

        private bool tiltDirty;
        private DateTime lastTiltSave = DateTime.MinValue;
        private string lastTiltRecord = "\u0000";

        private Button tabNow, tabJournal, tabSetup;
        private StackPanel pageNow, pageJournal, pageSetup;
        private int activeTab;
        private TextBlock planReminder, emptyNote;

        private TextBlock firmSummary;
        private Button firmToggle;
        private TextBlock editingScope, applyNote, realisedNote, coherenceNote;
        private CheckBox trustRealisedBox;
        private StackPanel firmFields;

        private ComboBox profileBox;
        private TextBlock profileDetail;
        private TextBox tbRiskPerTrade;
        private TextBlock stopCostHint;

        /// <summary>
        /// FROZEN, every one of them, and the window will not reopen without it.
        ///
        /// NinjaTrader builds an AddOn window on a UI thread of its choosing, and
        /// closing and reopening Ballast can land it on a different one. A WPF
        /// brush belongs to the thread that made it, so a static brush created
        /// the first time the window opened cannot be attached to a control
        /// created the second time - it throws "cannot use a DependencyObject
        /// that belongs to a different thread than its parent Freezable" during
        /// construction, and the window then refuses to open at all until
        /// NinjaTrader restarts.
        ///
        /// Freezing makes a Freezable immutable and therefore thread-safe to
        /// share. Exactly the same trap as the chart indicator's brushes; fixed
        /// there first, which is how this one got found.
        /// </summary>
        private static Brush Frozen(byte r, byte g, byte b)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        /// <summary>Same, for the semi-transparent overlay backdrops.</summary>
        private static Brush FrozenA(byte a, byte r, byte g, byte b)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }

        private static readonly Brush ColBg    = Frozen(0x0e, 0x11, 0x16);
        private static readonly Brush ColPanel = Frozen(0x16, 0x1b, 0x22);
        private static readonly Brush ColCard  = Frozen(0x12, 0x16, 0x1c);
        private static readonly Brush ColInk   = Frozen(0xe8, 0xed, 0xf3);
        private static readonly Brush ColMuted = Frozen(0xb4, 0xc0, 0xcd);
        private static readonly Brush ColGreen = Frozen(0x3f, 0xb9, 0x50);
        private static readonly Brush ColAmber = Frozen(0xe3, 0xb3, 0x41);
        private static readonly Brush ColRed   = Frozen(0xf4, 0x52, 0x3b);
        private static readonly Brush ColLine   = Frozen(0x25, 0x2c, 0x36);
        private static readonly Brush ColHeader = Frozen(0x11, 0x15, 0x1b);
        private static readonly Brush ColAccent = Frozen(0x4d, 0xa3, 0xff);
        private static readonly Brush ColFaint  = Frozen(0x8b, 0x97, 0xa5);
        private static readonly Brush ColTransparent = Brushes.Transparent;

        public BallastWindow()
        {
            Caption = "Ballast";
            Width = 480;
            Height = 780;
            Background = ColBg;

            Content = BuildUi();
            LoadRuleBook();
            LoadSettings();
            LoadSessionState();
            LoadJournal();
            RefreshAccountList(true);

            // Land on Setup when there is nothing to watch yet, because then setup
            // IS the task. Otherwise land on Now, which is what a configured
            // trader opened the window for.
            LoadTiltLog();

            ApplyZoom();
            if (generationBox != null) generationBox.SelectedIndex = (int)generation;
            if (tiltOnBox != null) tiltOnBox.IsChecked = tiltEnabled;
            if (tiltGiveBackBox != null) tiltGiveBackBox.IsChecked = tiltOnGiveBack;
            ShowTab(monitor.Count == 0 ? 2 : 0);

            // Silent daily check so the trader never maintains rules by hand.
            CheckForRuleUpdates(false);

            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += OnTick;
            timer.Start();

            Closed += OnClosedCleanup;

            // Claimed on load rather than in the constructor, because a window
            // being restored FROM a workspace already has one and must keep it -
            // overwriting it here would give it a new identity every start and
            // quietly accumulate dead entries in the workspace file.
            Loaded += delegate
            {
                if (WorkspaceOptions == null)
                    WorkspaceOptions = new WorkspaceOptions(
                        "BallastWindow-" + Guid.NewGuid().ToString("N"), this);
            };
        }

        // ── UI ───────────────────────────────────────────────────────────────

        // ─────────────────────────────────────────────────────────────────────
        // Layout
        //
        // Three tabs, not one long scroll.
        //
        // Everything used to live in a single column, which meant a trader with a
        // position on was looking at account checkboxes, firm dropdowns and rule
        // editors with exactly the same visual weight as the number telling them
        // whether the account was about to die. That is not a styling problem, it
        // is a hierarchy problem, and no amount of colour fixes it.
        //
        //   NOW      - what you look at with a position on. Nothing else.
        //   JOURNAL  - your plan and what your trades have proved so far.
        //   SETUP    - accounts, firm rules, sizing. Touched once, then forgotten.
        //
        // The explanatory paragraphs that used to sit under every field are gone
        // from the surface. They are still there, behind a "Why?" link, because
        // the reasoning matters the first time and is noise every time after.
        // ─────────────────────────────────────────────────────────────────────

        private UIElement BuildUi()
        {
            Grid shell = new Grid();
            shell.Background = ColBg;
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            shell.RowDefinitions.Add(new RowDefinition());

            shell.Children.Add(BuildTabBar());

            ScrollViewer scroller = new ScrollViewer();
            scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroller.Background = ColBg;
            Grid.SetRow(scroller, 1);

            Grid pages = new Grid();
            zoomHost = pages;
            pages.Margin = new Thickness(Pad, 16, Pad, 24);
            // A form field 1300px wide is unreadable no matter what size the type
            // is. Cap the column and let the window be as wide as it likes.
            pages.MaxWidth = 760;
            pages.HorizontalAlignment = HorizontalAlignment.Left;

            pageNow = BuildNowPage();
            pageJournal = BuildJournalPage();
            pageSetup = BuildSetupPage();

            pages.Children.Add(pageNow);
            pages.Children.Add(pageJournal);
            pages.Children.Add(pageSetup);

            scroller.Content = pages;
            shell.Children.Add(scroller);

            // Sits on top of everything, including the tab bar, so there is no
            // clicking around it. It is the last child and has the highest
            // Z-index for the same reason.
            // The picture viewer sits above the pages and below the tilt wall.
            // If an account is dying, that outranks looking at a screenshot.
            imageOverlay = BuildImageOverlay();
            Grid.SetRow(imageOverlay, 0);
            Grid.SetRowSpan(imageOverlay, 2);
            Panel.SetZIndex(imageOverlay, 900);
            shell.Children.Add(imageOverlay);

            tiltOverlay = BuildTiltOverlay();
            Grid.SetRow(tiltOverlay, 0);
            Grid.SetRowSpan(tiltOverlay, 2);
            Panel.SetZIndex(tiltOverlay, 999);
            shell.Children.Add(tiltOverlay);

            return shell;
        }

        // ── The wall ─────────────────────────────────────────────────────────
        //
        // What this is for, in the trader's own words: "i would get so mad
        // trading sometimes and keep trading saying i would get it back or just
        // lose it and just trade anyway whether it was planned or not."
        //
        // So: the screen goes away. Not the orders - Ballast has never placed,
        // modified or cancelled one and does not start here. The market is still
        // one Alt-Tab away, and that is deliberate. Software that genuinely
        // trapped a trader would be both wrong and, the first time it was wrong
        // about a configuration, uninstalled.
        //
        // The shape of the choice is the whole design:
        //
        //   "I'm done for the day"  - one click, big, first, calm colour.
        //   Carry on anyway         - type a sentence, about ten seconds, small.
        //
        // Ten seconds is not a punishment. It is roughly the time it takes for
        // the impulse to stop being automatic and start being a decision, which
        // is the only thing standing between "I'll get it back" and a blown
        // account. And whichever way it goes, it is written down and costed, so
        // in a month this stops being an opinion about his trading and becomes
        // his own record of it.

        /// <summary>
        /// Full-window picture viewer.
        ///
        /// The journal photographs the chart at entry and exit, which is the one
        /// field hindsight cannot rewrite - and then showed it at 260 pixels
        /// wide, where it proves a trade happened but nobody can read a single
        /// price on it. Clicking a thumbnail opens it here, as large as the
        /// window allows, with a switch to full resolution for the moments where
        /// the detail is the point.
        /// </summary>
        private Border BuildImageOverlay()
        {
            Border o = new Border();
            o.Visibility = Visibility.Collapsed;
            o.Background = FrozenA(0xF9, 0x07, 0x09, 0x0d);

            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            g.RowDefinitions.Add(new RowDefinition());

            // Bar
            Border bar = new Border();
            bar.Background = ColHeader;
            bar.BorderBrush = ColLine;
            bar.BorderThickness = new Thickness(0, 0, 0, 1);
            bar.Padding = new Thickness(14, 8, 14, 8);

            Grid barGrid = new Grid();
            barGrid.ColumnDefinitions.Add(new ColumnDefinition());
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

            imageCaption = new TextBlock();
            imageCaption.Foreground = ColInk;
            imageCaption.FontSize = 13;
            imageCaption.FontWeight = FontWeights.Bold;
            imageCaption.VerticalAlignment = VerticalAlignment.Center;
            imageCaption.TextTrimming = TextTrimming.CharacterEllipsis;
            imageCaption.Margin = new Thickness(0, 0, 16, 0);
            barGrid.Children.Add(imageCaption);

            StackPanel btns = new StackPanel();
            btns.Orientation = Orientation.Horizontal;
            Grid.SetColumn(btns, 1);

            imageZoomBtn = QuietButton("Actual size", delegate { ToggleImageZoom(); });
            btns.Children.Add(imageZoomBtn);

            btns.Children.Add(QuietButton("Open in Windows", delegate
            {
                try { if (imagePath.Length > 0) System.Diagnostics.Process.Start(imagePath); }
                catch { }
            }));

            btns.Children.Add(PrimaryButton("Close", delegate { HideImage(); }));
            barGrid.Children.Add(btns);

            bar.Child = barGrid;
            g.Children.Add(bar);

            imageScroll = new ScrollViewer();
            imageScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            imageScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            imageScroll.Margin = new Thickness(10);
            Grid.SetRow(imageScroll, 1);

            imageBig = new Image();
            imageBig.Stretch = Stretch.Uniform;
            imageBig.HorizontalAlignment = HorizontalAlignment.Center;
            imageBig.VerticalAlignment = VerticalAlignment.Center;
            imageScroll.Content = imageBig;
            g.Children.Add(imageScroll);

            o.Child = g;
            return o;
        }

        private void ShowImage(string path, string which, BallastTrade trade)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

                imagePath = path;
                imageActualSize = false;

                // Decoded at full resolution here, unlike the thumbnail - the
                // whole point is to be able to read it. One at a time, and
                // released when the viewer closes.
                BitmapImage bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();

                imageBig.Source = bmp;
                ApplyImageZoom();

                string label = which;
                if (trade != null)
                    label = which + "  -  " + trade.AccountName + "   " + trade.ShortLabel;
                imageCaption.Text = label;

                imageOverlay.Visibility = Visibility.Visible;
            }
            catch { }
        }

        private void ToggleImageZoom()
        {
            imageActualSize = !imageActualSize;
            ApplyImageZoom();
        }

        private void ApplyImageZoom()
        {
            if (imageBig == null) return;

            if (imageActualSize)
            {
                // Native pixels, scrollable. For reading the price ladder.
                imageBig.Stretch = Stretch.None;
                imageBig.HorizontalAlignment = HorizontalAlignment.Left;
                imageBig.VerticalAlignment = VerticalAlignment.Top;
                if (imageZoomBtn != null) imageZoomBtn.Content = "Fit to window";
            }
            else
            {
                imageBig.Stretch = Stretch.Uniform;
                imageBig.HorizontalAlignment = HorizontalAlignment.Center;
                imageBig.VerticalAlignment = VerticalAlignment.Center;
                if (imageZoomBtn != null) imageZoomBtn.Content = "Actual size";
            }
        }

        private void HideImage()
        {
            if (imageOverlay != null) imageOverlay.Visibility = Visibility.Collapsed;

            // Let the full-resolution bitmap go. A day of trades is a lot of
            // screenshots to keep decoded in memory for no reason.
            if (imageBig != null) imageBig.Source = null;
            imagePath = "";
        }

        private Border BuildTiltOverlay()
        {
            Border o = new Border();
            o.Visibility = Visibility.Collapsed;
            // Near-solid rather than solid: the numbers behind stay faintly
            // visible, so it reads as Ballast getting in the way rather than as
            // the platform having crashed.
            o.Background = FrozenA(0xF7, 0x1a, 0x06, 0x06);

            ScrollViewer sv = new ScrollViewer();
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

            StackPanel p = new StackPanel();
            p.Margin = new Thickness(26, 30, 26, 30);
            p.MaxWidth = 620;
            p.HorizontalAlignment = HorizontalAlignment.Left;
            // The overlay is outside the zoomed page host, so it has to honour
            // the text-size setting itself. A trader who sized Ballast up because
            // they cannot read it at 100% must not be handed this at 100%.
            tiltZoomHost = p;

            TextBlock kicker = new TextBlock();
            kicker.Text = "BALLAST";
            kicker.Foreground = Frozen(0xff, 0xb4, 0xb4);
            kicker.FontSize = 11;
            kicker.FontWeight = FontWeights.Bold;
            p.Children.Add(kicker);

            tiltTitle = new TextBlock();
            tiltTitle.Foreground = Brushes.White;
            tiltTitle.FontSize = 34;
            tiltTitle.FontWeight = FontWeights.Bold;
            tiltTitle.TextWrapping = TextWrapping.Wrap;
            tiltTitle.Margin = new Thickness(0, 8, 0, 0);
            p.Children.Add(tiltTitle);

            tiltLine = new TextBlock();
            tiltLine.Foreground = Frozen(0xff, 0xdc, 0xdc);
            tiltLine.FontSize = 16;
            tiltLine.TextWrapping = TextWrapping.Wrap;
            tiltLine.Margin = new Thickness(0, 14, 0, 0);
            p.Children.Add(tiltLine);

            tiltAsk = new TextBlock();
            tiltAsk.Foreground = Frozen(0xff, 0xdc, 0xdc);
            tiltAsk.FontSize = 14;
            tiltAsk.TextWrapping = TextWrapping.Wrap;
            tiltAsk.Margin = new Thickness(0, 10, 0, 0);
            p.Children.Add(tiltAsk);

            tiltToday = new TextBlock();
            tiltToday.Foreground = Brushes.White;
            tiltToday.FontSize = 15;
            tiltToday.FontWeight = FontWeights.Bold;
            tiltToday.TextWrapping = TextWrapping.Wrap;
            tiltToday.Margin = new Thickness(0, 12, 0, 0);
            p.Children.Add(tiltToday);

            // His own record. This is the part that has to do the persuading,
            // because nothing Ballast invents is as convincing as what he has
            // already done.
            tiltHistory = new TextBlock();
            tiltHistory.Foreground = Frozen(0xff, 0xc9, 0xc9);
            tiltHistory.FontSize = 13;
            tiltHistory.TextWrapping = TextWrapping.Wrap;
            tiltHistory.Margin = new Thickness(0, 12, 0, 0);
            p.Children.Add(tiltHistory);

            tiltStood = new TextBlock();
            tiltStood.Foreground = Frozen(0xb8, 0xf0, 0xc0);
            tiltStood.FontSize = 13;
            tiltStood.TextWrapping = TextWrapping.Wrap;
            tiltStood.Margin = new Thickness(0, 4, 0, 0);
            p.Children.Add(tiltStood);

            // The right choice: biggest, first, and one click.
            Button stand = new Button();
            stand.Content = "I'm done for the day";
            stand.FontSize = 19;
            stand.FontWeight = FontWeights.Bold;
            stand.Padding = new Thickness(26, 14, 26, 14);
            stand.Margin = new Thickness(0, 24, 0, 0);
            stand.HorizontalAlignment = HorizontalAlignment.Left;
            stand.Background = Brushes.White;
            stand.Foreground = Frozen(0x1a, 0x06, 0x06);
            stand.BorderBrush = Brushes.White;
            stand.Click += delegate { OnTiltStandDown(); };
            p.Children.Add(stand);

            TextBlock standNote = new TextBlock();
            standNote.Text = "Closes this and leaves the account alone until tomorrow. "
                           + "Ballast keeps watching; it just stops arguing with you.";
            standNote.Foreground = Frozen(0xff, 0xc9, 0xc9);
            standNote.FontSize = 11;
            standNote.TextWrapping = TextWrapping.Wrap;
            standNote.Margin = new Thickness(0, 8, 0, 0);
            p.Children.Add(standNote);

            // When several accounts are at a line at once, answering one at a
            // time means the same wall four times. Shown only when it applies.
            tiltAllRow = new StackPanel();
            tiltAllRow.Margin = new Thickness(0, 12, 0, 0);
            tiltAllRow.Visibility = Visibility.Collapsed;

            tiltStandAll = new Button();
            tiltStandAll.Content = "I'm done on every account";
            tiltStandAll.FontSize = 14;
            tiltStandAll.Padding = new Thickness(18, 9, 18, 9);
            tiltStandAll.HorizontalAlignment = HorizontalAlignment.Left;
            tiltStandAll.Background = ColTransparent;
            tiltStandAll.Foreground = Brushes.White;
            tiltStandAll.BorderBrush = Brushes.White;
            tiltStandAll.Click += delegate { OnTiltStandDownAll(); };
            tiltAllRow.Children.Add(tiltStandAll);

            tiltAllNote = new TextBlock();
            tiltAllNote.Foreground = Frozen(0xff, 0xc9, 0xc9);
            tiltAllNote.FontSize = 11;
            tiltAllNote.TextWrapping = TextWrapping.Wrap;
            tiltAllNote.Margin = new Thickness(0, 6, 0, 0);
            tiltAllRow.Children.Add(tiltAllNote);

            p.Children.Add(tiltAllRow);

            // The configuration escape. Only shown for past-floor, where a wrong
            // account size in Setup is far more likely than a dead account - and
            // being accused of revenge trading by a settings bug is exactly how a
            // feature like this gets switched off forever.
            tiltConfigRow = new StackPanel();
            tiltConfigRow.Margin = new Thickness(0, 14, 0, 0);
            tiltConfigRow.Visibility = Visibility.Collapsed;

            tiltFixConfig = new Button();
            tiltFixConfig.Content = "That number is wrong - open Setup";
            tiltFixConfig.FontSize = 13;
            tiltFixConfig.Padding = new Thickness(16, 8, 16, 8);
            tiltFixConfig.HorizontalAlignment = HorizontalAlignment.Left;
            tiltFixConfig.Background = ColTransparent;
            tiltFixConfig.Foreground = Brushes.White;
            tiltFixConfig.BorderBrush = Frozen(0xff, 0x9a, 0x9a);
            tiltFixConfig.Click += delegate { OnTiltFixConfig(); };
            tiltConfigRow.Children.Add(tiltFixConfig);

            TextBlock cfgNote = new TextBlock();
            cfgNote.Text = "Nothing is recorded against you for this one.";
            cfgNote.Foreground = Frozen(0xff, 0xc9, 0xc9);
            cfgNote.FontSize = 11;
            cfgNote.Margin = new Thickness(0, 6, 0, 0);
            tiltConfigRow.Children.Add(cfgNote);

            p.Children.Add(tiltConfigRow);

            // ── the override ──
            Border rule = new Border();
            rule.Height = 1;
            rule.Background = FrozenA(0x55, 0xff, 0xff, 0xff);
            rule.Margin = new Thickness(0, 26, 0, 20);
            p.Children.Add(rule);

            TextBlock orLabel = new TextBlock();
            orLabel.Text = "Or carry on trading. Type this out first:";
            orLabel.Foreground = Frozen(0xff, 0xc9, 0xc9);
            orLabel.FontSize = 12;
            orLabel.TextWrapping = TextWrapping.Wrap;
            p.Children.Add(orLabel);

            TextBlock sentence = new TextBlock();
            sentence.Text = TiltLockout.ReleaseSentence;
            sentence.Foreground = Brushes.White;
            sentence.FontSize = 15;
            sentence.FontStyle = FontStyles.Italic;
            sentence.TextWrapping = TextWrapping.Wrap;
            sentence.Margin = new Thickness(0, 8, 0, 8);
            p.Children.Add(sentence);

            tiltTypeBox = new TextBox();
            tiltTypeBox.FontSize = 15;
            tiltTypeBox.MinHeight = 56;
            tiltTypeBox.AcceptsReturn = false;
            tiltTypeBox.TextWrapping = TextWrapping.Wrap;
            tiltTypeBox.Padding = new Thickness(8, 6, 8, 6);
            tiltTypeBox.Background = Frozen(0x2a, 0x10, 0x10);
            tiltTypeBox.Foreground = Brushes.White;
            tiltTypeBox.BorderBrush = Frozen(0xff, 0x9a, 0x9a);
            tiltTypeBox.TextChanged += OnTiltTyped;

            // Pasting it defeats the entire mechanism, so pasting is refused.
            // Ten seconds of typing is the feature; one Ctrl-V is not.
            DataObject.AddPastingHandler(tiltTypeBox, OnTiltPaste);

            // A text box accepts drops by default, and a drop does NOT raise the
            // pasting event - so without this the sentence could be dragged in
            // from Notepad in one gesture and the whole mechanism walks out the
            // door through a side entrance.
            tiltTypeBox.AllowDrop = false;
            p.Children.Add(tiltTypeBox);

            tiltProgress = new ProgressBar();
            tiltProgress.Minimum = 0;
            tiltProgress.Maximum = 1;
            tiltProgress.Value = 0;
            tiltProgress.Height = 4;
            tiltProgress.Margin = new Thickness(0, 6, 0, 0);
            tiltProgress.Foreground = Frozen(0xff, 0x9a, 0x9a);
            tiltProgress.Background = FrozenA(0x44, 0xff, 0xff, 0xff);
            tiltProgress.BorderBrush = ColTransparent;
            p.Children.Add(tiltProgress);

            tiltGoOn = new Button();
            tiltGoOn.Content = "Carry on anyway";
            tiltGoOn.FontSize = 12;
            tiltGoOn.Padding = new Thickness(14, 6, 14, 6);
            tiltGoOn.Margin = new Thickness(0, 12, 0, 0);
            tiltGoOn.HorizontalAlignment = HorizontalAlignment.Left;
            tiltGoOn.IsEnabled = false;
            tiltGoOn.Background = ColTransparent;
            tiltGoOn.Foreground = Frozen(0xff, 0xc9, 0xc9);
            tiltGoOn.BorderBrush = Frozen(0x99, 0x55, 0x55);
            tiltGoOn.Click += delegate { OnTiltOverride(); };
            p.Children.Add(tiltGoOn);

            TextBlock small = new TextBlock();
            small.Text = "Buys you " + TiltLockout.DefaultReleaseMinutes
                       + " minutes. If it is still true after that, this comes back. "
                       + "Either way it goes in your journal with what the rest of the day did.";
            small.Foreground = Frozen(0xd8, 0x9a, 0x9a);
            small.FontSize = 11;
            small.TextWrapping = TextWrapping.Wrap;
            small.Margin = new Thickness(0, 8, 0, 0);
            p.Children.Add(small);

            TextBlock foot = new TextBlock();
            foot.Text = "Ballast has not touched your orders and cannot. Your platform is still there.";
            foot.Foreground = Frozen(0xc0, 0x88, 0x88);
            foot.FontSize = 10;
            foot.TextWrapping = TextWrapping.Wrap;
            foot.Margin = new Thickness(0, 18, 0, 0);
            p.Children.Add(foot);

            sv.Content = p;
            o.Child = sv;
            return o;
        }

        private void OnTiltPaste(object sender, DataObjectPastingEventArgs e)
        {
            e.CancelCommand();
        }

        private void OnTiltTyped(object sender, TextChangedEventArgs e)
        {
            if (tiltTypeBox == null) return;

            string typed = tiltTypeBox.Text;
            bool ok = TiltLockout.Accepts(typed);

            if (tiltGoOn != null) tiltGoOn.IsEnabled = ok;
            if (tiltProgress != null) tiltProgress.Value = TiltLockout.Progress(typed);

            // Off-track typing turns the box red rather than throwing an error.
            // Being told off by a text box while already angry helps nobody.
            tiltTypeBox.BorderBrush = ok
                ? Frozen(0xb8, 0xf0, 0xc0)
                : TiltLockout.OnTrack(typed)
                    ? Frozen(0xff, 0x9a, 0x9a)
                    : Frozen(0xff, 0x50, 0x50);
        }

        private void ShowTilt(TiltTrigger t, DateTime now)
        {
            if (t == null || tiltOverlay == null) return;

            tiltCurrent = t;
            tiltMissTicks = 0;

            tiltTitle.Text = t.Title;
            tiltLine.Text = t.Line;
            tiltAsk.Text = t.Ask;

            string today = TiltLockout.TodayLine(tiltLog, t.AccountName, now);
            tiltToday.Text = today;
            tiltToday.Visibility = today.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

            string hist = tiltLog.Summary(now, 30);
            tiltHistory.Text = hist;
            tiltHistory.Visibility = hist.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

            string stood = tiltLog.StoodSummary(now, 30);
            tiltStood.Text = stood;
            tiltStood.Visibility = stood.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

            tiltConfigRow.Visibility = t.ConfigSuspect ? Visibility.Visible : Visibility.Collapsed;

            // Whether there is anything else waiting behind this one.
            int others = 0;
            System.Text.StringBuilder names = new System.Text.StringBuilder();
            for (int i = 0; i < tiltDue.Count; i++)
            {
                if (string.Equals(tiltDue[i].AccountName, t.AccountName,
                                  StringComparison.OrdinalIgnoreCase)) continue;
                others++;
                if (names.Length > 0) names.Append(", ");
                names.Append(tiltDue[i].AccountName);
            }

            if (tiltAllRow != null)
            {
                tiltAllRow.Visibility = others > 0 ? Visibility.Visible : Visibility.Collapsed;
                if (others > 0 && tiltAllNote != null)
                    tiltAllNote.Text = others == 1
                        ? names + " is at a line too, and its own wall is next. This answers both "
                          + "at once and leaves them alone until tomorrow."
                        : names + " are at a line too, and their walls are next. This answers all "
                          + (others + 1) + " at once and leaves them alone until tomorrow.";
            }

            tiltTypeBox.Text = "";
            tiltProgress.Value = 0;
            tiltGoOn.IsEnabled = false;

            tiltOverlay.Visibility = Visibility.Visible;
        }

        private void HideTilt()
        {
            tiltCurrent = null;
            tiltMissTicks = 0;
            if (tiltOverlay != null) tiltOverlay.Visibility = Visibility.Collapsed;
            if (tiltTypeBox != null) tiltTypeBox.Text = "";
        }

        private void OnTiltStandDown()
        {
            if (tiltCurrent == null) { HideTilt(); return; }

            DateTime now = Core.Globals.Now;
            RecordTilt(tiltCurrent, true, now);
            tiltGate.ReleaseAccountForDay(tiltCurrent.AccountName, now);
            SaveTiltGate();
            HideTilt();
        }

        /// <summary>
        /// Standing down on everything that is currently at a line. Each account
        /// is recorded separately, because each is its own decision and the
        /// record is what the wall shows back later.
        /// </summary>
        private void OnTiltStandDownAll()
        {
            DateTime now = Core.Globals.Now;
            List<TiltTrigger> all = new List<TiltTrigger>(tiltDue);

            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == null || string.IsNullOrEmpty(all[i].AccountName)) continue;
                RecordTilt(all[i], true, now);
                tiltGate.ReleaseAccountForDay(all[i].AccountName, now);
            }

            // The one on screen may not be in the list if it arrived this tick.
            if (tiltCurrent != null && !tiltGate.IsReleased(tiltCurrent.AccountName, tiltCurrent.Kind, now))
            {
                RecordTilt(tiltCurrent, true, now);
                tiltGate.ReleaseAccountForDay(tiltCurrent.AccountName, now);
            }

            SaveTiltGate();
            HideTilt();
        }

        private void OnTiltFixConfig()
        {
            if (tiltCurrent == null) { HideTilt(); return; }

            // No record either way: this is a settings problem, not a discipline
            // one, and filing it as tilt would poison the numbers that make the
            // rest of this worth reading.
            tiltGate.ReleaseForDay(tiltCurrent.AccountName, tiltCurrent.Kind, Core.Globals.Now);
            SaveTiltGate();
            string acct = tiltCurrent.AccountName;
            HideTilt();

            ShowTab(2);

            // Land them on the account in question rather than on a settings page
            // and a hunt.
            try
            {
                if (editTargetBox != null && editTargetBox.Items.Contains(acct))
                    editTargetBox.SelectedItem = acct;
            }
            catch { }
        }

        private void OnTiltOverride()
        {
            if (tiltCurrent == null) { HideTilt(); return; }
            if (!TiltLockout.Accepts(tiltTypeBox == null ? "" : tiltTypeBox.Text)) return;

            DateTime now = Core.Globals.Now;
            RecordTilt(tiltCurrent, false, now);
            tiltGate.Release(tiltCurrent.AccountName, tiltCurrent.Kind, now);
            SaveTiltGate();
            HideTilt();
        }

        private void RecordTilt(TiltTrigger t, bool stood, DateTime now)
        {
            TiltEvent e = new TiltEvent();
            e.At = now;
            e.AccountName = t.AccountName;
            e.Kind = t.Kind;
            e.Stood = stood;
            e.PnlAtEvent = t.DailyPnl;
            tiltLog.Add(e);
            tiltLog.Save(TiltPath());
        }

        private void RenderTiltRecord()
        {
            if (tiltJournalLine == null) return;

            DateTime now;
            try { now = Core.Globals.Now; } catch { now = DateTime.Now; }

            string over = tiltLog.Summary(now, 30);
            string stood = tiltLog.StoodSummary(now, 30);

            string text = over;
            if (stood.Length > 0) text = text.Length > 0 ? text + "  " + stood : stood;

            // This runs every second. Only touch the visual tree when the words
            // have actually changed.
            if (text == lastTiltRecord) return;
            lastTiltRecord = text;

            tiltJournalLine.Text = text;
            tiltJournalLine.Foreground = over.Length > 0 ? ColAmber : ColGreen;
            tiltJournalLine.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LoadTiltLog()
        {
            try
            {
                tiltLog.Load(TiltPath());

                // Anything from a previous date is frozen at whatever figure it
                // held. Without this, a crash mid-session would leave yesterday's
                // override quietly tracking today's P&L and reporting a number
                // that never happened.
                tiltLog.SettleStale(Core.Globals.Now);
            }
            catch { }

            LoadTiltGate();
        }

        private string TiltPath()
        {
            try { return Path.Combine(Core.Globals.UserDataDir, "ballast-overrides.csv"); }
            catch { return "ballast-overrides.csv"; }
        }

        private string TiltGatePath()
        {
            try { return Path.Combine(Core.Globals.UserDataDir, "ballast-standdown.txt"); }
            catch { return "ballast-standdown.txt"; }
        }

        /// <summary>
        /// Write down which walls have been answered and until when.
        ///
        /// Standing down said "leaves the account alone until tomorrow" and then
        /// forgot it the moment Ballast was closed, so the same wall was in front
        /// of the trader again on the next open, asking the same question about a
        /// decision he had already made. A promise the software cannot keep
        /// across a restart is not a promise, and being asked twice is exactly
        /// the nagging the wall exists to replace.
        /// </summary>
        private void SaveTiltGate()
        {
            try
            {
                List<string> lines = tiltGate.Serialise();
                File.WriteAllLines(TiltGatePath(), lines.ToArray());
            }
            catch { }
        }

        private void LoadTiltGate()
        {
            try
            {
                string p = TiltGatePath();
                if (!File.Exists(p)) return;

                DateTime now;
                try { now = Core.Globals.Now; } catch { now = DateTime.Now; }

                tiltGate.Restore(new List<string>(File.ReadAllLines(p)), now);
            }
            catch { }
        }

        /// <summary>
        /// Runs every tick. Keeps the cost of past overrides current, decides
        /// whether a wall belongs on the screen, and tells the chart indicator
        /// either way.
        /// </summary>
        /// <summary>
        /// A hard breaker has to stop being true for this many consecutive ticks
        /// before the wall comes down or the chart banner clears.
        ///
        /// Without it these signals strobe. Cushion is worked out from equity
        /// INCLUDING unrealised P&L, so an account sitting on its floor with a
        /// position open crosses back and forth every second as price wobbles,
        /// and a single dropped account update reads as "no valid equity" for one
        /// tick. Either would tear the wall down mid-sentence and wipe what the
        /// trader had typed - which means they could never finish typing it, and
        /// the escape hatch would become a trap.
        /// </summary>
        private const int TiltGraceTicks = 8;

        private int tiltMissTicks;
        private readonly Dictionary<string, int> lockMissTicks =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private void UpdateTilt(List<AccountSnapshot> snaps, DateTime now)
        {
            if (tiltLog.SettleStale(now)) tiltDirty = true;

            TiltTrigger firstDue = null;
            bool currentStillTrue = false;

            // One live trigger per account, so the wall can offer to answer for
            // all of them at once. Standing down on one account and then being
            // shown the next account's wall, and the next, is the same question
            // four times over - which is exactly what the wall is supposed to
            // replace.
            List<TiltTrigger> due = new List<TiltTrigger>();

            for (int n = 0; n < snaps.Count; n++)
            {
                AccountSnapshot s = snaps[n];

                // What an override actually cost is measured against where the
                // day finishes, so today's open records track the live figure.
                if (s.Input.HasValidEquity && tiltLog.Touch(s.AccountName, s.Input.DailyPnl, now))
                    tiltDirty = true;

                List<TiltTrigger> triggers =
                    TiltLockout.EvaluateAll(s.AccountName, s.Input, s.Decision, tiltOnGiveBack);

                // The chart is told about hard breakers only, and is told
                // regardless of whether the overlay is switched on or has been
                // typed past. Turning off the wall is a choice about Ballast's
                // own window; it is not a request to make a dying account look
                // clean on the chart the trader is actually watching.
                bool hard = false;
                string hardLine = "";
                for (int k = 0; k < triggers.Count; k++)
                {
                    if (!TiltLockout.IsHardBreaker(triggers[k].Kind)) continue;
                    hard = true;
                    hardLine = triggers[k].Title;
                    break;
                }
                PublishLockSticky(s.AccountName, hard, hardLine, now);

                if (!tiltEnabled) continue;

                for (int k = 0; k < triggers.Count; k++)
                {
                    TiltTrigger t = triggers[k];

                    if (tiltCurrent != null
                        && string.Equals(t.AccountName, tiltCurrent.AccountName, StringComparison.OrdinalIgnoreCase)
                        && t.Kind == tiltCurrent.Kind)
                        currentStillTrue = true;

                    // Dismissing one reason must not disarm the others. Skipping
                    // rather than stopping is the whole point of walking the list.
                    if (tiltGate.IsReleased(t.AccountName, t.Kind, now)) continue;
                    if (firstDue == null) firstDue = t;

                    bool already = false;
                    for (int j = 0; j < due.Count; j++)
                        if (string.Equals(due[j].AccountName, t.AccountName,
                                          StringComparison.OrdinalIgnoreCase)) { already = true; break; }
                    if (!already) due.Add(t);
                }
            }

            if (!tiltEnabled) { if (tiltCurrent != null) HideTilt(); return; }

            if (tiltCurrent != null)
            {
                if (currentStillTrue) { tiltMissTicks = 0; return; }

                // Ride out a brief gap rather than tearing the wall down on one
                // bad tick. Only a sustained clear takes it away.
                tiltMissTicks++;
                if (tiltMissTicks < TiltGraceTicks) return;

                HideTilt();
            }

            tiltDue = due;
            if (firstDue != null) ShowTilt(firstDue, now);
        }

        /// <summary>
        /// Publish the chart's hard-breaker flag with the same anti-strobe grace
        /// the overlay gets, so the banner does not flash on and off once a
        /// second while a position sits on the floor.
        /// </summary>
        private void PublishLockSticky(string account, bool hard, string line, DateTime now)
        {
            if (string.IsNullOrEmpty(account)) return;

            if (hard)
            {
                lockMissTicks[account] = 0;
                BallastState.PublishLock(account, true, line, now);
                return;
            }

            int misses;
            if (!lockMissTicks.TryGetValue(account, out misses)) misses = TiltGraceTicks;
            if (misses < TiltGraceTicks)
            {
                lockMissTicks[account] = misses + 1;
                return;   // hold the last published state through the gap
            }

            BallastState.PublishLock(account, false, "", now);
        }

        private const double Pad = 18;

        private UIElement BuildTabBar()
        {
            Border bar = new Border();
            bar.Background = ColHeader;
            bar.BorderBrush = ColLine;
            bar.BorderThickness = new Thickness(0, 0, 0, 1);
            bar.Padding = new Thickness(Pad, 10, Pad, 0);

            StackPanel row = new StackPanel();
            row.Orientation = Orientation.Horizontal;

            tabNow = TabButton("Now");
            tabNow.Click += delegate { ShowTab(0); };
            row.Children.Add(tabNow);

            tabJournal = TabButton("Journal");
            tabJournal.Click += delegate { ShowTab(1); };
            row.Children.Add(tabJournal);

            tabSetup = TabButton("Setup");
            tabSetup.Click += delegate { ShowTab(2); };
            row.Children.Add(tabSetup);

            // Text size. Windows scaling is a machine-wide decision a trader makes
            // for other reasons; this is a per-window one, so Ballast can be
            // readable across the desk without resizing everything else.
            zoomLabel = new TextBlock();
            zoomLabel.Foreground = ColFaint;
            zoomLabel.FontSize = 11;
            zoomLabel.Margin = new Thickness(18, 4, 6, 0);
            row.Children.Add(zoomLabel);

            row.Children.Add(ZoomButton("A-", -1));
            row.Children.Add(ZoomButton("A+", 1));

            bar.Child = row;
            return bar;
        }

        private Button ZoomButton(string text, int direction)
        {
            Button b = new Button();
            b.Content = text;
            b.FontSize = 12;
            b.FontWeight = FontWeights.Bold;
            b.Padding = new Thickness(9, 1, 9, 1);
            b.Margin = new Thickness(0, 0, 4, 6);
            b.Background = ColPanel;
            b.Foreground = ColInk;
            b.BorderBrush = ColLine;
            b.Click += delegate { NudgeZoom(direction); };
            return b;
        }

        /// <summary>
        /// Steps through sensible sizes rather than free zooming, so a mis-click
        /// cannot leave the window unreadable and unclickable back to normal.
        /// </summary>
        private static readonly double[] ZoomSteps = new double[] { 1.0, 1.15, 1.3, 1.5, 1.75, 2.0 };

        private void NudgeZoom(int direction)
        {
            zoomIndex += direction;
            if (zoomIndex < 0) zoomIndex = 0;
            if (zoomIndex >= ZoomSteps.Length) zoomIndex = ZoomSteps.Length - 1;
            ApplyZoom();
            SaveSettings();
        }

        private void ApplyZoom()
        {
            double z = ZoomSteps[zoomIndex];
            // Frozen for the same reason the brushes are - a Transform is a
            // Freezable too, and these outlive the call that created them.
            ScaleTransform scale = new ScaleTransform(z, z);
            scale.Freeze();

            if (zoomHost != null) zoomHost.LayoutTransform = scale;
            if (tiltZoomHost != null) tiltZoomHost.LayoutTransform = scale;
            if (zoomLabel != null) zoomLabel.Text = (z * 100).ToString("0") + "%";
        }

        private Button TabButton(string text)
        {
            Button b = new Button();
            b.Content = text;
            b.Padding = new Thickness(2, 0, 22, 10);
            b.FontSize = 14;
            b.Background = ColTransparent;
            b.BorderBrush = ColTransparent;
            b.BorderThickness = new Thickness(0, 0, 0, 2);
            b.Foreground = ColMuted;
            return b;
        }

        /// <summary>
        /// Switch tabs. The badge on Journal is the only nagging Ballast does, and
        /// it is a count rather than a popup: it says "there is something here"
        /// without deciding for the trader that now is the moment to deal with it.
        /// </summary>
        private void ShowTab(int index)
        {
            activeTab = index;

            pageNow.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
            pageJournal.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
            pageSetup.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;

            StyleTab(tabNow, index == 0);
            StyleTab(tabJournal, index == 1);
            StyleTab(tabSetup, index == 2);

            // The stop-cost hint reads the journal, so it is worked out when
            // Setup is opened rather than on every tick.
            if (index == 2) { RefreshStopCostHint(); RefreshRealisedNote(); RefreshCoherence(); RefreshWindowClock(); }
        }

        private void StyleTab(Button b, bool on)
        {
            if (b == null) return;
            b.Foreground = on ? ColInk : ColMuted;
            b.BorderBrush = on ? ColAccent : ColTransparent;
            b.FontWeight = on ? FontWeights.Bold : FontWeights.Normal;
        }

        // ── NOW ──────────────────────────────────────────────────────────────

        private StackPanel BuildNowPage()
        {
            StackPanel p = new StackPanel();

            // The action card. Deliberately the largest thing on screen: at a
            // glance, from across the desk, with a position on, this is the only
            // thing that has to be readable.
            card = new Border();
            card.BorderBrush = ColGreen;
            card.BorderThickness = new Thickness(0, 0, 0, 0);
            card.CornerRadius = new CornerRadius(10);
            card.Padding = new Thickness(20, 18, 20, 18);
            card.Background = ColCard;
            card.Margin = new Thickness(0, 0, 0, 16);

            StackPanel cardInner = new StackPanel();

            urgencyText = new TextBlock();
            urgencyText.Text = "NEXT ACTION";
            urgencyText.Foreground = ColGreen;
            urgencyText.FontSize = 10;
            urgencyText.FontWeight = FontWeights.Bold;
            cardInner.Children.Add(urgencyText);

            headlineText = new TextBlock();
            headlineText.Text = "Tick an account to begin";
            headlineText.Foreground = ColInk;
            headlineText.FontSize = 22;
            headlineText.FontWeight = FontWeights.Bold;
            headlineText.TextWrapping = TextWrapping.Wrap;
            headlineText.Margin = new Thickness(0, 6, 0, 0);
            cardInner.Children.Add(headlineText);

            headlineAccountText = new TextBlock();
            headlineAccountText.Foreground = ColMuted;
            headlineAccountText.FontSize = 11;
            headlineAccountText.TextWrapping = TextWrapping.Wrap;
            headlineAccountText.Margin = new Thickness(0, 6, 0, 0);
            cardInner.Children.Add(headlineAccountText);

            bulletPanel = new StackPanel();
            bulletPanel.Margin = new Thickness(0, 10, 0, 0);
            cardInner.Children.Add(bulletPanel);

            planReminder = new TextBlock();
            planReminder.Foreground = ColAmber;
            planReminder.FontSize = 11;
            planReminder.TextWrapping = TextWrapping.Wrap;
            planReminder.Margin = new Thickness(0, 12, 0, 0);
            planReminder.Visibility = Visibility.Collapsed;
            cardInner.Children.Add(planReminder);

            card.Child = cardInner;
            p.Children.Add(card);

            // Three numbers. No labels longer than the number they describe.
            Grid stats = new Grid();
            stats.Margin = new Thickness(0, 0, 0, 18);
            for (int c = 0; c < 3; c++) stats.ColumnDefinitions.Add(new ColumnDefinition());
            stats.RowDefinitions.Add(new RowDefinition());
            statCushion  = StatBlock(stats, 0, 0, "CLOSEST TO ITS FLOOR", out statCushionCap);
            statPnl      = StatBlock(stats, 0, 1, "DAY P&L, ALL ACCOUNTS");
            statAccounts = StatBlock(stats, 0, 2, "ACCOUNTS WATCHED");
            p.Children.Add(stats);

            statCushionWho = new TextBlock();
            statCushionWho.Foreground = ColFaint;
            statCushionWho.FontSize = 10;
            statCushionWho.TextWrapping = TextWrapping.Wrap;
            statCushionWho.Margin = new Thickness(2, -12, 0, 14);
            p.Children.Add(statCushionWho);

            // Trades waiting to be tagged. Inert - never a popup, never steals
            // focus. A tool that interrupts a trader mid-decision to sell them
            // discipline is doing harm to make a point.
            journalStripBorder = new Border();
            journalStripBorder.BorderBrush = ColAmber;
            journalStripBorder.BorderThickness = new Thickness(0, 0, 0, 0);
            journalStripBorder.CornerRadius = new CornerRadius(8);
            journalStripBorder.Background = ColPanel;
            journalStripBorder.Padding = new Thickness(14, 12, 14, 12);
            journalStripBorder.Margin = new Thickness(0, 0, 0, 18);
            journalStripBorder.Visibility = Visibility.Collapsed;

            journalStrip = new StackPanel();
            journalStripBorder.Child = journalStrip;
            p.Children.Add(journalStripBorder);

            p.Children.Add(SectionHeader("ACCOUNTS"));

            // Six columns, and every heading is a phrase rather than a label.
            // "DO" was a column heading nobody could read - it meant "what to do
            // now", which is the single most important thing on the row and was
            // being announced in two letters.
            Grid hdr = AccountsGrid();
            hdr.Margin = new Thickness(12, 0, 12, 6);
            hdr.Children.Add(MicroCell("ACCOUNT", 0));
            hdr.Children.Add(MicroCell("TRADES", 1));
            hdr.Children.Add(MicroCell("LOSSES IN A ROW", 2));
            hdr.Children.Add(MicroCell("LEFT TO LOSE", 3));
            hdr.Children.Add(MicroCell("TODAY'S TARGET", 4));
            hdr.Children.Add(MicroCell("TO THE FLOOR", 5));
            hdr.Children.Add(MicroCell("WHAT TO DO", 6));
            p.Children.Add(hdr);

            rowsPanel = new StackPanel();
            p.Children.Add(rowsPanel);

            emptyNote = new TextBlock();
            emptyNote.Text = "No accounts being watched yet. Open Setup and tick the ones you trade.";
            emptyNote.Foreground = ColMuted;
            emptyNote.FontSize = 11;
            emptyNote.TextWrapping = TextWrapping.Wrap;
            emptyNote.Margin = new Thickness(0, 4, 0, 0);
            p.Children.Add(emptyNote);

            return p;
        }

        // ── JOURNAL ──────────────────────────────────────────────────────────

        private StackPanel BuildJournalPage()
        {
            StackPanel p = new StackPanel();
            p.Visibility = Visibility.Collapsed;

            p.Children.Add(SectionHeader("TODAY'S PLAN"));

            TextBlock hint = new TextBlock();
            hint.Text = "One line, written as \"if X, then I will Y\".";
            hint.Foreground = ColMuted;
            hint.FontSize = 12;
            hint.Margin = new Thickness(0, 0, 0, 6);
            p.Children.Add(hint);

            tbSessionPlan = new TextBox();
            tbSessionPlan.Background = ColPanel;
            tbSessionPlan.Foreground = ColInk;
            tbSessionPlan.BorderBrush = ColLine;
            tbSessionPlan.FontSize = 13;
            tbSessionPlan.Padding = new Thickness(10, 8, 10, 8);
            tbSessionPlan.Margin = new Thickness(0, 0, 0, 6);
            tbSessionPlan.TextWrapping = TextWrapping.Wrap;
            tbSessionPlan.MinLines = 2;
            tbSessionPlan.LostFocus += delegate { CommitSessionPlan(); };
            p.Children.Add(tbSessionPlan);

            planStandingBox = new CheckBox();
            planStandingBox.Content = "This is my standing plan - bring it back every day";
            planStandingBox.Foreground = ColInk;
            planStandingBox.FontSize = 12;
            planStandingBox.Margin = new Thickness(0, 2, 0, 6);
            planStandingBox.Checked += delegate { SaveStandingPlan(); };
            planStandingBox.Unchecked += delegate { SaveStandingPlan(); };
            p.Children.Add(planStandingBox);

            planConfirmRow = new StackPanel();
            planConfirmRow.Orientation = Orientation.Horizontal;
            planConfirmRow.Margin = new Thickness(0, 0, 0, 6);
            planConfirmRow.Visibility = Visibility.Collapsed;

            TextBlock cq = new TextBlock();
            cq.Text = "Still your plan today?";
            cq.Foreground = ColAmber;
            cq.FontSize = 12;
            cq.Margin = new Thickness(0, 6, 10, 0);
            planConfirmRow.Children.Add(cq);
            planConfirmRow.Children.Add(PrimaryButton("Yes, commit to it", delegate { ConfirmPlan(); }));
            p.Children.Add(planConfirmRow);

            p.Children.Add(Why("Why a plan written this way?",
                "A vague intention (\"trade well today\") does almost nothing. A specific if-then plan "
              + "does, because it hands the decision to the situation instead of to your willpower at "
              + "the moment it is weakest. Example: \"If I lose two trades, then I close the platform.\" "
              + "It gets stamped onto every trade you take today, so at the end of the month you can "
              + "see which plans you actually kept. It does not carry over to tomorrow - a plan you "
              + "did not write this morning is one you have not committed to."));

            p.Children.Add(Spacer(16));
            p.Children.Add(SectionHeader("WHAT YOUR TRADES SHOW"));

            Border insightCard = new Border();
            insightCard.CornerRadius = new CornerRadius(8);
            insightCard.Background = ColPanel;
            insightCard.Padding = new Thickness(14, 12, 14, 12);
            insightCard.Margin = new Thickness(0, 0, 0, 10);

            StackPanel insightInner = new StackPanel();

            journalInsight = new TextBlock();
            journalInsight.Text = "Ballast is recording every trade. Insights appear once there are enough of them.";
            journalInsight.Foreground = ColInk;
            journalInsight.FontSize = 14;
            journalInsight.TextWrapping = TextWrapping.Wrap;
            insightInner.Children.Add(journalInsight);

            journalSummary = new TextBlock();
            journalSummary.Foreground = ColMuted;
            journalSummary.FontSize = 11;
            journalSummary.TextWrapping = TextWrapping.Wrap;
            journalSummary.Margin = new Thickness(0, 10, 0, 0);
            insightInner.Children.Add(journalSummary);

            // What carrying on has cost. Kept here rather than only on the wall,
            // because the wall is read in a temper and this is read calmly - and
            // the calm reading is the one that changes what happens next time.
            tiltJournalLine = new TextBlock();
            tiltJournalLine.Foreground = ColAmber;
            tiltJournalLine.FontSize = 12;
            tiltJournalLine.TextWrapping = TextWrapping.Wrap;
            tiltJournalLine.Margin = new Thickness(0, 10, 0, 0);
            tiltJournalLine.Visibility = Visibility.Collapsed;
            insightInner.Children.Add(tiltJournalLine);

            insightCard.Child = insightInner;
            p.Children.Add(insightCard);

            p.Children.Add(Spacer(16));
            p.Children.Add(SectionHeader("WATCH OUT FOR TODAY"));

            TextBlock newsHint = new TextBlock();
            newsHint.Text = "One per line, as \"08:30 CPI\". Ballast warns you 15 minutes before each.";
            newsHint.Foreground = ColMuted;
            newsHint.FontSize = 11;
            newsHint.Margin = new Thickness(0, 0, 0, 6);
            p.Children.Add(newsHint);

            tbEvents = new TextBox();
            tbEvents.Background = ColPanel;
            tbEvents.Foreground = ColInk;
            tbEvents.BorderBrush = ColLine;
            tbEvents.FontSize = 13;
            tbEvents.Padding = new Thickness(10, 8, 10, 8);
            tbEvents.Margin = new Thickness(0, 0, 0, 6);
            tbEvents.TextWrapping = TextWrapping.Wrap;
            tbEvents.MinLines = 3;
            tbEvents.LostFocus += delegate { SaveEvents(); };
            p.Children.Add(tbEvents);

            p.Children.Add(Why("Why type these in rather than have them fetched?",
                "A news calendar Ballast fetched could be wrong - wrong timezone, wrong day, a "
              + "revision it missed - and a wrong time is worse than no time, because you would "
              + "trade through the real one trusting it. What you type is right by definition. A "
              + "fetched calendar can come later, once there is a server to keep it honest."));

            p.Children.Add(Spacer(18));

            Grid tradesHead = new Grid();
            tradesHead.ColumnDefinitions.Add(new ColumnDefinition());
            tradesHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            tradesHead.Margin = new Thickness(0, 0, 0, 8);

            TextBlock th = SectionHeader("TRADES");
            th.Margin = new Thickness(0, 6, 0, 0);
            tradesHead.Children.Add(th);

            StackPanel rangeBtns = new StackPanel();
            rangeBtns.Orientation = Orientation.Horizontal;
            Grid.SetColumn(rangeBtns, 1);

            btnToday = QuietButton("Today", delegate { SetTradeRange(true); });
            btnAllTime = QuietButton("All time", delegate { SetTradeRange(false); });
            btnToday.Margin = new Thickness(0, 0, 6, 0);
            btnAllTime.Margin = new Thickness(0);
            rangeBtns.Children.Add(btnToday);
            rangeBtns.Children.Add(btnAllTime);
            rangeBtns.Children.Add(QuietButton("Expand all", delegate { SetAllAccountsExpanded(true); }));
            rangeBtns.Children.Add(QuietButton("Collapse all", delegate { SetAllAccountsExpanded(false); }));
            rangeBtns.Children.Add(QuietButton("Open as a page", delegate { OpenDayReport(); }));
            rangeBtns.Children.Add(QuietButton("Save a copy", delegate { SaveReportCopy(); }));
            tradesHead.Children.Add(rangeBtns);

            p.Children.Add(tradesHead);

            tradesPanel = new StackPanel();
            tradesPanel.Margin = new Thickness(0, 0, 0, 6);
            p.Children.Add(tradesPanel);

            outsideNote = new TextBlock();
            outsideNote.Foreground = ColFaint;
            outsideNote.FontSize = 11;
            outsideNote.TextWrapping = TextWrapping.Wrap;
            outsideNote.Margin = new Thickness(0, 4, 0, 0);
            outsideNote.Visibility = Visibility.Collapsed;
            p.Children.Add(outsideNote);

            p.Children.Add(Spacer(18));
            p.Children.Add(SectionHeader("BY INSTRUMENT"));

            instrumentPanel = new StackPanel();
            instrumentPanel.Margin = new Thickness(0, 0, 0, 12);
            p.Children.Add(instrumentPanel);

            journalPathNote = new TextBlock();
            journalPathNote.Foreground = ColFaint;
            journalPathNote.FontSize = 11;
            journalPathNote.TextWrapping = TextWrapping.Wrap;
            journalPathNote.Margin = new Thickness(0, 0, 0, 8);
            p.Children.Add(journalPathNote);

            StackPanel diagRow = new StackPanel();
            diagRow.Orientation = Orientation.Horizontal;
            diagRow.Children.Add(QuietButton("Test chart photos", delegate
            {
                chartDiag.Text = ChartSnapshot.Diagnose();
                chartDiag.Visibility = Visibility.Visible;
            }));
            p.Children.Add(diagRow);

            chartDiag = new TextBlock();
            chartDiag.Foreground = ColMuted;
            chartDiag.FontSize = 11;
            chartDiag.TextWrapping = TextWrapping.Wrap;
            chartDiag.Margin = new Thickness(0, 8, 0, 0);
            chartDiag.Visibility = Visibility.Collapsed;
            p.Children.Add(chartDiag);

            return p;
        }

        // ── SETUP ────────────────────────────────────────────────────────────

        private StackPanel BuildSetupPage()
        {
            StackPanel p = new StackPanel();
            p.Visibility = Visibility.Collapsed;

            // 1. Accounts
            p.Children.Add(SectionHeader("ACCOUNTS TO WATCH"));

            monitorAllBox = new CheckBox();
            monitorAllBox.Content = "Watch all accounts";
            monitorAllBox.Foreground = ColInk;
            monitorAllBox.FontSize = 13;
            monitorAllBox.Margin = new Thickness(0, 0, 0, 8);
            monitorAllBox.Checked += delegate { SetAllAccounts(true); };
            monitorAllBox.Unchecked += delegate { SetAllAccounts(false); };
            p.Children.Add(monitorAllBox);

            Border accBorder = new Border();
            accBorder.CornerRadius = new CornerRadius(8);
            accBorder.Background = ColPanel;
            accBorder.Padding = new Thickness(12, 8, 12, 8);
            accBorder.Margin = new Thickness(0, 0, 0, 20);

            ScrollViewer accScroll = new ScrollViewer();
            accScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            accScroll.MaxHeight = 260;

            accountListPanel = new StackPanel();
            accScroll.Content = accountListPanel;
            accBorder.Child = accScroll;
            p.Children.Add(accBorder);

            // 2. What kind of account
            p.Children.Add(SectionHeader("WHAT KIND OF ACCOUNT"));

            p.Children.Add(Label("Firm"));
            firmBox = new ComboBox();
            firmBox.Margin = new Thickness(0, 0, 0, 10);
            firmBox.SelectionChanged += delegate { PopulateAccountTypes(); };
            p.Children.Add(firmBox);

            p.Children.Add(Label("Which generation of accounts do you hold?"));
            generationBox = new ComboBox();
            generationBox.Margin = new Thickness(0, 0, 0, 10);
            generationBox.Items.Add("Work it out from the balance (cautious)");
            generationBox.Items.Add("Legacy accounts");
            generationBox.Items.Add("Current accounts (Apex 4.0)");
            generationBox.SelectedIndex = 0;
            generationBox.SelectionChanged += delegate { OnGenerationChanged(); };
            p.Children.Add(generationBox);

            p.Children.Add(Why("Why does this matter?",
                "The same account size can carry different drawdowns depending on when you bought "
              + "it. A legacy Apex 50K trails $2,500; a 4.0 50K trails $2,000. A 150K is $5,000 "
              + "legacy against $4,000 on 4.0. Your balance is identical either way, so nothing in "
              + "the account itself can tell Ballast which you hold.\n\n"
              + "Left on cautious, it picks the TIGHTER drawdown - reporting less room than you may "
              + "really have, which is the safe way to be wrong. Say Legacy once here and every "
              + "account gets the right figure instead. Sizes that only exist in one generation "
              + "(75K, 250K, 300K are legacy-only) are never ambiguous."));

            // Picking a type IS the instruction. There is nothing a trader could
            // mean by choosing "Evaluation (intraday) - 250K" other than "this
            // account is that", so making them then hunt for a button called
            // "Apply to selected" - one of four buttons on the page with a
            // similar name - was ceremony rather than safety. It applies on
            // selection, and the sentence lower down confirms what it did.
            p.Children.Add(Label("Account type"));
            accountTypeBox = new ComboBox();
            accountTypeBox.Margin = new Thickness(0, 0, 0, 6);
            accountTypeBox.SelectionChanged += delegate
            {
                if (suppressTypeApply) return;
                ApplyChosenType(false);
            };
            p.Children.Add(accountTypeBox);

            TextBlock typeNote = new TextBlock();
            typeNote.Text = "Choosing one fills in that account's size, drawdown and floor straight away.";
            typeNote.Foreground = ColFaint;
            typeNote.FontSize = 11;
            typeNote.TextWrapping = TextWrapping.Wrap;
            typeNote.Margin = new Thickness(0, 0, 0, 8);
            p.Children.Add(typeNote);

            StackPanel firmBtns = new StackPanel();
            firmBtns.Orientation = Orientation.Horizontal;
            firmBtns.Margin = new Thickness(0, 0, 0, 8);
            firmBtns.Children.Add(QuietButton("Match all my accounts by balance", delegate { AutoConfigure(); }));
            firmBtns.Children.Add(QuietButton("Check for updates", delegate { CheckForRuleUpdates(true); }));
            p.Children.Add(firmBtns);

            detectionNote = new TextBlock();
            detectionNote.Text = "Trading your own money? Pick \"My own account\" - the floor stops trailing "
                               + "and becomes a fixed max loss you set.";
            detectionNote.Foreground = ColMuted;
            detectionNote.FontSize = 12;
            detectionNote.TextWrapping = TextWrapping.Wrap;
            detectionNote.Margin = new Thickness(0, 0, 0, 20);
            p.Children.Add(detectionNote);

            // 3. Recommended settings
            p.Children.Add(SectionHeader("RECOMMENDED SETTINGS"));

            profileBox = new ComboBox();
            profileBox.Margin = new Thickness(0, 0, 0, 8);
            profileBox.Items.Add("Choose a starting point...");
            List<RiskProfile> profs = RiskProfiles.All();
            for (int i = 0; i < profs.Count; i++) profileBox.Items.Add(profs[i].Name);
            profileBox.SelectedIndex = 0;
            profileBox.SelectionChanged += delegate { ShowProfileDetail(); };
            p.Children.Add(profileBox);

            Border profCard = new Border();
            profCard.CornerRadius = new CornerRadius(8);
            profCard.Background = ColPanel;
            profCard.Padding = new Thickness(14, 12, 14, 12);
            profCard.Margin = new Thickness(0, 0, 0, 10);

            profileDetail = new TextBlock();
            profileDetail.Foreground = ColMuted;
            profileDetail.FontSize = 12;
            profileDetail.TextWrapping = TextWrapping.Wrap;
            profCard.Child = profileDetail;
            p.Children.Add(profCard);

            p.Children.Add(Label("What your usual stop costs on ONE contract ($) - optional"));
            tbRiskPerTrade = Field("0");
            p.Children.Add(tbRiskPerTrade);

            // Their own number, from their own trades. "What does your stop
            // cost?" is a fair question with an unfair answer when the stop moves
            // with the setup - so Ballast works out what theirs has actually been
            // costing rather than asking them to average it in their head.
            stopCostHint = new TextBlock();
            stopCostHint.Foreground = ColMuted;
            stopCostHint.FontSize = 11;
            stopCostHint.TextWrapping = TextWrapping.Wrap;
            stopCostHint.Margin = new Thickness(0, -6, 0, 10);
            p.Children.Add(stopCostHint);

            p.Children.Add(Why("My stop is different on every trade - what do I put?",
                "Your usual one, and if they vary, the biggest one you take regularly. This number "
              + "is doing exactly one job: turning a per-trade risk allowance in dollars into a "
              + "number of contracts. Allowance $450, one contract's stop costs $150, so three "
              + "contracts. A bigger number here means fewer contracts, so guessing high is the safe "
              + "direction and guessing low is not.\n\n"
              + "It is the DOLLAR cost of one contract hitting your stop, not the points - and mind "
              + "the difference between a POINT and a TICK, because it is a factor of four and it "
              + "is the easiest mistake to make here. NQ is $20 a point and $5 a tick, so a "
              + "20-point stop costs $400 a contract. MNQ is $2 a point, $0.50 a tick. ES is $50 a "
              + "point, $12.50 a tick; MES is $5 a point, $1.25 a tick. If your ATM already has a "
              + "stop in it, that stop in dollars on one contract is the number.\n\n"
              + "Nothing live uses it. It is not a rule, it never stops you, and it is never checked "
              + "against a trade. It is only read when you press \"Use this starting point\", and if "
              + "you leave it at 0 the starting point leaves your contract count exactly as you set "
              + "it."));

            StackPanel profBtns = new StackPanel();
            profBtns.Orientation = Orientation.Horizontal;
            profBtns.Margin = new Thickness(0, 4, 0, 8);
            profBtns.Children.Add(PrimaryButton("Use this starting point", delegate { ApplyProfile(false); }));
            profBtns.Children.Add(QuietButton("Use it on every account", delegate { ApplyProfile(true); }));
            p.Children.Add(profBtns);

            p.Children.Add(Why("Why percentages of the drawdown, not the account?",
                "On a funded account your real capital is the drawdown, not the balance. Risking "
              + "\"1% of a 50K account\" is $500 - a quarter of an Apex 50K's entire $2,000 life. Four "
              + "of those in a row and the account is gone, having lost 4% of its stated size. So every "
              + "profile here is a percentage of YOUR drawdown, and the same profile gives your 25K and "
              + "150K accounts different numbers. These are starting points, not advice. None of the "
              + "traders cited endorsed them for your account - the principle is borrowed and stated, "
              + "the numbers are worked out from your figures."));

            p.Children.Add(Spacer(20));

            // 4. Rules
            p.Children.Add(SectionHeader("RULES"));

            p.Children.Add(Label("Whose rules am I editing?"));
            editTargetBox = new ComboBox();
            editTargetBox.Margin = new Thickness(0, 0, 0, 6);
            editTargetBox.SelectionChanged += delegate { OnEditTargetChanged(); };
            p.Children.Add(editTargetBox);

            // Which account these fields belong to, spelled out where the fields
            // are rather than only in a dropdown above them.
            editingScope = new TextBlock();
            editingScope.FontSize = 11;
            editingScope.TextWrapping = TextWrapping.Wrap;
            editingScope.Margin = new Thickness(0, 0, 0, 10);
            p.Children.Add(editingScope);

            p.Children.Add(Why("Can each account have different numbers?",
                "Yes - and they are meant to. Pick an account here and everything below it belongs to "
              + "that account alone: how much you are willing to lose today, how many trades, how many "
              + "losses in a row, your target and your size. A 50K evaluation and a funded 150K have no "
              + "business running the same daily stop.\n\n"
              + "Switching this dropdown now SAVES what you typed for the account you were on before it "
              + "loads the next one. Until this build it did not - it just reloaded the fields, so if "
              + "you set up three accounts one after another and pressed Apply at the end, only the "
              + "last one was kept. That is what \"it won't let me set different dailies\" was.\n\n"
              + "\"All accounts (default)\" is not an account. It is the starting point handed to the "
              + "next account you tick. Changing it does nothing to accounts you are already watching "
              + "unless you press \"Copy to all accounts\", which deliberately makes them all identical."));

            // ── The two that are actually yours ──────────────────────────────
            //
            // An evaluation or funded account arrives with its size, its
            // drawdown, its drawdown type and its floor-lock level already
            // decided by the firm. Presenting those as questions, in the same
            // type and the same box as the two real decisions, made a page of
            // eleven fields out of what is genuinely a page of two.
            //
            // These two are the ones a trader chooses fresh each day, so they go
            // first, they are larger, and nothing shares the row with them.
            Border decisions = new Border();
            decisions.CornerRadius = new CornerRadius(8);
            decisions.Background = ColCard;
            decisions.BorderBrush = ColAccent;
            decisions.BorderThickness = new Thickness(1);
            decisions.Padding = new Thickness(14, 12, 14, 12);
            decisions.Margin = new Thickness(0, 0, 0, 14);

            StackPanel dInner = new StackPanel();

            TextBlock dHead = new TextBlock();
            dHead.Text = "The only two you have to decide";
            dHead.Foreground = ColInk;
            dHead.FontSize = 14;
            dHead.FontWeight = FontWeights.Bold;
            dHead.Margin = new Thickness(0, 0, 0, 2);
            dInner.Children.Add(dHead);

            TextBlock dSub = new TextBlock();
            dSub.Text = "Everything else about a prop account is set by the firm, and Ballast fills "
                      + "it in from the account type below.";
            dSub.Foreground = ColMuted;
            dSub.FontSize = 11;
            dSub.TextWrapping = TextWrapping.Wrap;
            dSub.Margin = new Thickness(0, 0, 0, 10);
            dInner.Children.Add(dSub);

            Grid gD = TwoCol();
            tbDailyLoss = FieldIn(gD, 0, 0, "How much am I willing to lose today ($)", "500");
            tbMaxTrades = FieldIn(gD, 0, 1, "How many trades", "4");
            dInner.Children.Add(gD);

            // Whether the two numbers above agree with each other, with the size
            // cap, and with the account's whole drawdown. See RefreshCoherence.
            coherenceNote = new TextBlock();
            coherenceNote.FontSize = 12;
            coherenceNote.TextWrapping = TextWrapping.Wrap;
            coherenceNote.Margin = new Thickness(0, 10, 0, 0);
            dInner.Children.Add(coherenceNote);

            decisions.Child = dInner;
            p.Children.Add(decisions);

            p.Children.Add(Why("How much should I be willing to lose in a day?",
                "It is not really chosen, it is derived - and the arithmetic starts somewhere most "
              + "advice gets wrong.\n\n"
              + "On a prop account your capital is the DRAWDOWN, not the balance. A 250K evaluation "
              + "with a $6,500 trailing drawdown has $6,500 of capital. Every \"risk 1% of the "
              + "account\" rule is written for money you own, and applying it here gives $2,500 a "
              + "day - 38% of everything you have, on a rule that was meant to be conservative.\n\n"
              + "So ask the question you can actually answer: how many bad days in a row should this "
              + "account survive? Five is a reasonable place to start, and it makes the daily limit "
              + "your drawdown divided by five. On $6,500 that is $1,300. Two red days ending the "
              + "account is not a limit, it is a coin toss with an extra step.\n\n"
              + "Then check it against your own trading, which is what the line above does. Your "
              + "typical loss times the number of losses you will take should land near your daily "
              + "limit. If they are far apart, one of them is decorative - whichever fires first is "
              + "your real rule and the other is a number on a page.\n\n"
              + "None of this is advice, and nobody can pick the number for you without knowing your "
              + "edge. In a few weeks your own journal will answer it better than any rule of thumb "
              + "can: the right daily limit is a number you read off your own distribution of "
              + "losses, not one you inherit."));

            // ── The rest of the trader's own limits ──────────────────────────
            p.Children.Add(Label("Your other limits"));

            Grid g2 = TwoCol();
            tbMaxLosses = FieldIn(g2, 0, 0, "Stop after N losses", "2");
            tbTarget = FieldIn(g2, 0, 1, "Daily target ($) - one good day", "500");
            p.Children.Add(g2);

            Grid g4 = TwoCol();
            tbMaxContracts = FieldIn(g4, 0, 0, "Max contracts", "1");
            p.Children.Add(g4);

            // ── The trading window ───────────────────────────────────────────
            //
            // This was hard-coded at 09:30-11:30 with no way to change it, so the
            // chart indicator told anyone who trades the afternoon that they were
            // "outside your trading window" every single day - about a window
            // they had never set and could not find. A warning nobody can turn
            // off is a warning everybody learns to ignore, and the ones next to
            // it get ignored with it.
            p.Children.Add(Label("When do you trade? (this account)"));

            Grid gW = TwoCol();
            tbWindowStart = FieldIn(gW, 0, 0, "From", "09:30");
            tbWindowEnd = FieldIn(gW, 0, 1, "To", "11:30");
            p.Children.Add(gW);

            // What time Ballast thinks it is, next to the window it is judging you
            // against. "Outside your trading window" is unarguable and invisible
            // otherwise: NinjaTrader's clock is whatever Tools -> Options ->
            // General -> Time zone says, which need not be the clock on your
            // charts or the one on your wall.
            windowClock = new TextBlock();
            windowClock.FontSize = 11;
            windowClock.TextWrapping = TextWrapping.Wrap;
            windowClock.Margin = new Thickness(0, -6, 0, 8);
            p.Children.Add(windowClock);

            windowAnyTimeBox = new CheckBox();
            windowAnyTimeBox.Content = "I trade whenever I like - never mention the clock";
            windowAnyTimeBox.Foreground = ColInk;
            windowAnyTimeBox.FontSize = 13;
            windowAnyTimeBox.Margin = new Thickness(0, 0, 0, 8);
            p.Children.Add(windowAnyTimeBox);

            p.Children.Add(Why("What does the trading window do?",
                "Nothing on its own. It never blocks anything and it is not one of the hard lines "
              + "that puts a wall on your screen. All it does is say \"outside your trading window\" "
              + "on the chart and in the account's line, because for most traders the damage is done "
              + "at a specific time of day - the afternoon session that was never part of the plan, "
              + "entered because the morning went badly.\n\n"
              + "Times are in whatever clock NinjaTrader is set to (Tools -> Options -> General -> "
              + "Time zone), so they match the times on your charts. Type them however you like: "
              + "09:30, 9:30 and 930 all work.\n\n"
              + "A window may cross midnight - 18:00 to 02:00 for the overnight session is fine. And "
              + "if you do not want one, tick the box: nothing about the clock will be said again on "
              + "this account. It is per account, like everything else here, so an account you only "
              + "trade at the open and an account you run overnight can each have their own."));

            p.Children.Add(Why("Is the daily target my evaluation target?",
                "No - and if it currently reads $15,000 or similar, that was Ballast's fault. Until "
              + "this build the rule book wrote your firm's PASS target into this box, which broke "
              + "two things quietly: an account could never be told to bank a good day, and the "
              + "\"you were up and have handed it back\" warning could never fire either, because "
              + "both of those trigger off this number.\n\n"
              + "This is one good day. The number that would make you happy to stop. Your firm's "
              + "pass target is tracked separately and shown on the Now tab as progress."));

            p.Children.Add(Spacer(14));

            // ── What the firm decided ────────────────────────────────────────
            //
            // Folded away, because on a prop account these are facts to check
            // once rather than settings to maintain. The summary line above the
            // fold is what a trader actually wants: confirmation that Ballast has
            // the right numbers, in one sentence, without a form.
            firmSummary = new TextBlock();
            firmSummary.Foreground = ColMuted;
            firmSummary.FontSize = 12;
            firmSummary.TextWrapping = TextWrapping.Wrap;
            firmSummary.Margin = new Thickness(0, 0, 0, 6);
            p.Children.Add(firmSummary);

            firmToggle = QuietButton("Show the firm's figures", delegate { ToggleFirmFields(); });
            firmToggle.Margin = new Thickness(0, 0, 0, 10);
            p.Children.Add(firmToggle);

            firmFields = new StackPanel();
            firmFields.Visibility = Visibility.Collapsed;

            TextBlock firmNote = new TextBlock();
            firmNote.Text = "Only change these if Ballast has the account wrong. Picking the account "
                          + "type above fills them in from your firm's published rules.";
            firmNote.Foreground = ColFaint;
            firmNote.FontSize = 11;
            firmNote.TextWrapping = TextWrapping.Wrap;
            firmNote.Margin = new Thickness(0, 0, 0, 8);
            firmFields.Children.Add(firmNote);

            Grid g = TwoCol();
            tbBalance = FieldIn(g, 0, 0, "Account size ($)", "50000");
            tbDrawdown = FieldIn(g, 0, 1, "Max loss / drawdown ($)", "2500");
            firmFields.Children.Add(g);

            firmFields.Children.Add(Label("Drawdown type"));
            ddTypeBox = new ComboBox();
            ddTypeBox.Items.Add("Intraday trailing");
            ddTypeBox.Items.Add("End-of-day trailing");
            ddTypeBox.SelectedIndex = 0;
            ddTypeBox.Margin = new Thickness(0, 0, 0, 12);
            firmFields.Children.Add(ddTypeBox);

            firmFields.Children.Add(Label("Stops trailing at ($, 0 = never)"));
            tbLockAt = Field("0");
            firmFields.Children.Add(tbLockAt);

            firmFields.Children.Add(Why("What is this?",
                "Most firms freeze the floor once your balance passes a set level - Apex at your "
              + "starting size + $100, Topstep at your starting size. Past that point the drawdown no "
              + "longer follows you up. Leave 0 if you are unsure: Ballast will keep trailing, which "
              + "understates your cushion rather than overstating it. For your own (non-prop) account, "
              + "set this to your starting balance minus your max loss and the floor becomes fixed."));

            firmFields.Children.Add(Label("Account generation (this account)"));
            acctGenBox = new ComboBox();
            acctGenBox.Margin = new Thickness(0, 0, 0, 12);
            acctGenBox.Items.Add("Use the setting above");
            acctGenBox.Items.Add("Legacy");
            acctGenBox.Items.Add("Current (4.0)");
            acctGenBox.SelectedIndex = 0;
            firmFields.Children.Add(acctGenBox);

            p.Children.Add(firmFields);

            // ── Where the day's P&L comes from ───────────────────────────────
            trustRealisedBox = new CheckBox();
            trustRealisedBox.Content = "Take today's P&L from the account itself";
            trustRealisedBox.Foreground = ColInk;
            trustRealisedBox.FontSize = 13;
            trustRealisedBox.IsChecked = true;
            trustRealisedBox.Margin = new Thickness(0, 8, 0, 4);
            p.Children.Add(trustRealisedBox);

            realisedNote = new TextBlock();
            realisedNote.Foreground = ColMuted;
            realisedNote.FontSize = 11;
            realisedNote.TextWrapping = TextWrapping.Wrap;
            realisedNote.Margin = new Thickness(20, 0, 0, 6);
            p.Children.Add(realisedNote);

            p.Children.Add(Why("Why would Ballast disagree with my platform?",
                "Because it used to measure the day itself, and it can only measure what it is "
              + "there for.\n\n"
              + "The day's P&L was \"the account's realised P&L now, less what it was when Ballast "
              + "opened\". Exact while Ballast is running, and silently wrong the moment it is not: "
              + "a trade taken with the window closed fell outside the measurement entirely, so the "
              + "loss shown was smaller than the loss taken and the room left was bigger than the "
              + "room left. That is the dangerous direction to be wrong in, and there was nothing "
              + "on screen to say so.\n\n"
              + "With this ticked, Ballast simply reads the number your platform already keeps - the "
              + "Realized PnL column in NinjaTrader's Accounts tab - and the two always agree, "
              + "whether Ballast saw the trade or not. Any difference between that figure and what "
              + "the journal can account for is written into the journal as a reconstructed trade, "
              + "so the trade count and the loss streak are not short either.\n\n"
              + "Untick it for a feed whose Realized PnL adds up across days instead of resetting "
              + "each session - NinjaTrader's own Sim accounts do that until you reset them. You "
              + "will know immediately: the line above will show a number that is nothing like your "
              + "day. With it unticked, Ballast goes back to measuring from its own baseline, which "
              + "it now saves so that closing the window no longer loses anything either."));

            p.Children.Add(Spacer(6));

            automatedBox = new CheckBox();
            automatedBox.Content = "This account is traded by a strategy, not by hand";
            automatedBox.Foreground = ColInk;
            automatedBox.FontSize = 13;
            automatedBox.Margin = new Thickness(0, 4, 0, 4);
            p.Children.Add(automatedBox);

            p.Children.Add(Why("What changes if I tick that?",
                "Risk monitoring is unchanged - if anything it matters more, because nobody is "
              + "watching. The cushion, the floor and the daily loss limit all still apply, and a bot "
              + "grinding an account toward its floor will still take over the headline.\n\n"
              + "What changes is the journal. A strategy's trades are still recorded, but they never "
              + "join the tagging queue and never count toward your discipline numbers. There is no "
              + "point asking a bot whether a trade was planned - they all were - and if a busy "
              + "strategy took four hundred trades while you took three, letting them into the "
              + "planned-versus-unplanned split would bury the only three that measure you."));

            p.Children.Add(Spacer(18));
            p.Children.Add(SectionHeader("WHEN YOU ARE TILTING"));

            // Everything above this point on the page is per-account. This is
            // not, and a trader who assumed it was would think they had switched
            // it on when they had not.
            TextBlock tiltScope = new TextBlock();
            tiltScope.Text = "Unlike the rules above, this applies to every account you watch.";
            tiltScope.Foreground = ColMuted;
            tiltScope.FontSize = 11;
            tiltScope.TextWrapping = TextWrapping.Wrap;
            tiltScope.Margin = new Thickness(0, 0, 0, 6);
            p.Children.Add(tiltScope);

            tiltOnBox = new CheckBox();
            tiltOnBox.Content = "Put a wall on the screen when I blow through a hard line";
            tiltOnBox.Foreground = ColInk;
            tiltOnBox.FontSize = 13;
            tiltOnBox.IsChecked = true;
            tiltOnBox.Margin = new Thickness(0, 4, 0, 4);
            tiltOnBox.Checked += delegate { tiltEnabled = true; SaveSettings(); };
            tiltOnBox.Unchecked += delegate { tiltEnabled = false; SaveSettings(); };
            p.Children.Add(tiltOnBox);

            tiltGiveBackBox = new CheckBox();
            tiltGiveBackBox.Content = "Also when I am handing back a green day";
            tiltGiveBackBox.Foreground = ColInk;
            tiltGiveBackBox.FontSize = 13;
            tiltGiveBackBox.Margin = new Thickness(16, 0, 0, 4);
            tiltGiveBackBox.Checked += delegate { tiltOnGiveBack = true; SaveSettings(); };
            tiltGiveBackBox.Unchecked += delegate { tiltOnGiveBack = false; SaveSettings(); };
            p.Children.Add(tiltGiveBackBox);

            p.Children.Add(Why("What does the wall actually do?",
                "It covers Ballast - the whole window, every tab - when an account goes past its "
              + "floor, past its daily loss limit, or past the number of losses you said was your "
              + "line. Not when you are merely over your trade count or outside your window: a wall "
              + "that fires for small things stops meaning anything.\n\n"
              + "It does NOT touch your orders. Ballast has never placed, modified or cancelled one, "
              + "and this does not change that. Your platform is one Alt-Tab away and always will be. "
              + "What this takes away is the screen, not the market.\n\n"
              + "There are two ways out. 'I'm done for the day' is one click, and it is the big "
              + "button on purpose. Carrying on means typing out a sentence - about ten seconds. Ten "
              + "seconds is roughly how long it takes for an impulse to stop being automatic and "
              + "start being a decision, which is the entire mechanism. You cannot paste it.\n\n"
              + "Both are written down. Every override is logged with your P&L at that moment and "
              + "what the rest of the day did afterwards, and next time the wall shows you that "
              + "record. Nothing Ballast can say about your trading is as convincing as what you have "
              + "already done, so it stops arguing and shows you your own numbers - including the "
              + "times carrying on worked out, because a record that only ever reported losses would "
              + "be worth nothing the first time you checked it.\n\n"
              + "Turning this off only turns off the wall in this window. If you have the Ballast "
              + "indicator on a chart it still paints STOP across the top when an account goes past "
              + "a hard line, and typing past the wall does not clear that either. Quiet in here is "
              + "not the same as a clean chart."));

            StackPanel btns = new StackPanel();
            btns.Orientation = Orientation.Horizontal;
            btns.Margin = new Thickness(0, 8, 0, 6);
            btns.Children.Add(PrimaryButton("Apply and save", delegate { OnApplyAndSave(); }));
            btns.Children.Add(QuietButton("Give every account these same rules",
                delegate { OnCopyToAll(); }));
            p.Children.Add(btns);

            // What was just saved, and to whom. A settings page that changes
            // nothing visible when you press its main button is indistinguishable
            // from one that is ignoring you.
            applyNote = new TextBlock();
            applyNote.FontSize = 12;
            applyNote.TextWrapping = TextWrapping.Wrap;
            applyNote.Foreground = ColMuted;
            applyNote.Margin = new Thickness(0, 0, 0, 14);
            p.Children.Add(applyNote);

            TextBlock foot = new TextBlock();
            foot.Text = "Advisory only. Ballast never places, modifies or cancels an order, and never "
                      + "flattens a position.";
            foot.Foreground = ColFaint;
            foot.FontSize = 10;
            foot.TextWrapping = TextWrapping.Wrap;
            p.Children.Add(foot);

            return p;
        }

        // ── Small building blocks ────────────────────────────────────────────

        private Grid TwoCol()
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 0, 0, 0);
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.RowDefinitions.Add(new RowDefinition());
            return g;
        }

        private TextBox FieldIn(Grid g, int row, int col, string label, string initial)
        {
            StackPanel sp = new StackPanel();
            sp.Margin = new Thickness(col == 0 ? 0 : 8, 0, col == 0 ? 8 : 0, 12);
            sp.Children.Add(Label(label));

            TextBox tb = Field(initial);
            tb.Margin = new Thickness(0);
            sp.Children.Add(tb);

            Grid.SetRow(sp, row);
            Grid.SetColumn(sp, col);
            g.Children.Add(sp);
            return tb;
        }

        private TextBox Field(string initial)
        {
            TextBox tb = new TextBox();
            tb.Text = initial;
            tb.Background = ColPanel;
            tb.Foreground = ColInk;
            tb.BorderBrush = ColLine;
            tb.FontSize = 15;
            tb.Padding = new Thickness(10, 8, 10, 8);
            tb.Margin = new Thickness(0, 0, 0, 14);
            return tb;
        }

        private Button PrimaryButton(string text, RoutedEventHandler onClick)
        {
            Button b = new Button();
            b.Content = text;
            b.Padding = new Thickness(16, 9, 16, 9);
            b.Margin = new Thickness(0, 0, 8, 0);
            b.FontSize = 13;
            b.FontWeight = FontWeights.Bold;
            b.Background = ColAccent;
            b.Foreground = ColBg;
            b.BorderBrush = ColAccent;
            b.Click += onClick;
            return b;
        }

        private Button QuietButton(string text, RoutedEventHandler onClick)
        {
            Button b = new Button();
            b.Content = text;
            b.Padding = new Thickness(14, 9, 14, 9);
            b.Margin = new Thickness(0, 0, 8, 0);
            b.FontSize = 13;
            b.Background = ColPanel;
            b.Foreground = ColInk;
            b.BorderBrush = ColLine;
            b.Click += onClick;
            return b;
        }

        private UIElement Spacer(double h)
        {
            Border b = new Border();
            b.Height = h;
            return b;
        }

        private UIElement MicroCell(string text, int col)
        {
            TextBlock t = new TextBlock();
            t.Text = text;
            t.Foreground = ColFaint;
            t.FontSize = 9;
            t.FontWeight = FontWeights.Bold;
            Grid.SetColumn(t, col);
            return t;
        }

        /// <summary>
        /// A one-line "Why?" that expands to the reasoning. The explanation earns
        /// its place the first time a trader reads it and is clutter every time
        /// after, so it starts folded away rather than shouting on every launch.
        /// </summary>
        private UIElement Why(string question, string answer)
        {
            StackPanel sp = new StackPanel();
            sp.Margin = new Thickness(0, 0, 0, 4);

            TextBlock body = new TextBlock();
            body.Text = answer;
            body.Foreground = ColMuted;
            body.FontSize = 12;
            body.TextWrapping = TextWrapping.Wrap;
            body.Margin = new Thickness(0, 6, 0, 4);
            body.Visibility = Visibility.Collapsed;

            Button link = new Button();
            link.Content = question;
            link.FontSize = 12;
            link.Padding = new Thickness(0);
            link.Background = ColTransparent;
            link.BorderBrush = ColTransparent;
            link.BorderThickness = new Thickness(0);
            link.Foreground = ColAccent;
            link.HorizontalAlignment = HorizontalAlignment.Left;
            link.Click += delegate
            {
                body.Visibility = body.Visibility == Visibility.Visible
                    ? Visibility.Collapsed : Visibility.Visible;
            };

            sp.Children.Add(link);
            sp.Children.Add(body);
            return sp;
        }

        private TextBlock Label(string text)
        {
            TextBlock t = new TextBlock();
            t.Text = text;
            t.Foreground = ColMuted;
            t.FontSize = 12;
            t.TextWrapping = TextWrapping.Wrap;
            t.Margin = new Thickness(0, 0, 0, 5);
            return t;
        }

        private TextBlock SectionHeader(string text)
        {
            TextBlock t = new TextBlock();
            t.Text = text;
            t.Foreground = ColFaint;
            t.FontSize = 11;
            t.FontWeight = FontWeights.Bold;
            t.Margin = new Thickness(0, 0, 0, 10);
            return t;
        }

        private TextBlock StatBlock(Grid grid, int row, int col, string caption)
        {
            TextBlock ignored;
            return StatBlock(grid, row, col, caption, out ignored);
        }

        /// <summary>
        /// A stat card whose caption can be rewritten later. The cushion figure
        /// needed it: "TIGHTEST ACCOUNT CAN LOSE $4,052" named no account, so the
        /// one number in the window that belongs to exactly one account read as
        /// if it belonged to all of them.
        /// </summary>
        private TextBlock StatBlock(Grid grid, int row, int col, string caption, out TextBlock captionBlock)
        {
            Border b = new Border();
            b.CornerRadius = new CornerRadius(8);
            b.Padding = new Thickness(12, 10, 12, 10);
            b.Margin = new Thickness(col == 0 ? 0 : 6, 0, 0, 0);
            b.Background = ColPanel;

            StackPanel sp = new StackPanel();

            TextBlock cap = new TextBlock();
            cap.Text = caption;
            cap.Foreground = ColFaint;
            cap.FontSize = 9;
            cap.FontWeight = FontWeights.Bold;
            cap.TextWrapping = TextWrapping.Wrap;
            sp.Children.Add(cap);
            captionBlock = cap;

            // The number is the point. Everything around it is smaller than it.
            TextBlock val = new TextBlock();
            val.Text = "-"; val.Foreground = ColInk;
            val.FontSize = 19; val.FontWeight = FontWeights.Bold;
            val.Margin = new Thickness(0, 3, 0, 0);
            sp.Children.Add(val);

            b.Child = sp;
            Grid.SetRow(b, row);
            Grid.SetColumn(b, col);
            grid.Children.Add(b);
            return val;
        }

        // ── Accounts ─────────────────────────────────────────────────────────

        private List<string> AvailableAccountNames()
        {
            List<string> names = new List<string>();
            try
            {
                lock (Account.All)
                {
                    foreach (Account a in Account.All) names.Add(a.Name);
                }
            }
            catch { }
            names.Sort(NaturalNameComparer.Instance);
            return names;
        }

        private Account FindAccount(string name)
        {
            try
            {
                lock (Account.All)
                {
                    foreach (Account a in Account.All)
                        if (a.Name == name) return a;
                }
            }
            catch { }
            return null;
        }

        /// <summary>Rebuild the checkbox list if the set of accounts changed.</summary>
        /// <summary>
        /// Write the line under one account's tick box. Split out so that ticking
        /// an account can update its own row rather than rebuilding the whole
        /// list - which would destroy the very checkbox the trader just clicked,
        /// from inside that checkbox's own event handler.
        /// </summary>
        private void DescribeAccount(string name)
        {
            TextBlock sub;
            if (name == null || !accountSubs.TryGetValue(name, out sub) || sub == null) return;

            BallastTracker t = monitor.Get(name);
            if (t != null)
            {
                // If the settings contradict what the account's own name says it
                // is, that outranks the summary - it is the difference between a
                // cushion figure that is right and one that is generous.
                string bad = "";
                try { bad = ruleBook.SanityWarning(name, t.Config, PlatformOf(FindAccount(name))); } catch { }

                sub.Text = bad.Length > 0
                    ? ConfigSummary(name, t.Config) + "\n" + bad
                    : ConfigSummary(name, t.Config);
                sub.Foreground = bad.Length > 0 ? ColRed : ColMuted;
                return;
            }

            // An un-ticked account that still has rules says so, and says what
            // they are. Otherwise "not watched" is indistinguishable from "never
            // set up", and the only way to tell them apart is to tick it and see
            // - which is exactly the doubt that made a trader re-enter settings
            // they had already entered.
            TrackerConfig kept = monitor.RememberedConfig(name);
            if (kept != null)
            {
                sub.Text = "not watched - rules kept: " + ConfigSummary(name, kept);
                sub.Foreground = ColFaint;
            }
            else
            {
                sub.Text = "not watched";
                sub.Foreground = ColFaint;
            }
        }

        private void RefreshAccountList(bool force)
        {
            if (!force && (Core.Globals.Now - lastAccountRefresh).TotalSeconds < 10) return;
            lastAccountRefresh = Core.Globals.Now;

            List<string> names = AvailableAccountNames();

            bool changed = force || names.Count != accountBoxes.Count;
            if (!changed)
            {
                for (int i = 0; i < names.Count; i++)
                    if (!accountBoxes.ContainsKey(names[i])) { changed = true; break; }
            }
            if (!changed) return;

            accountListPanel.Children.Clear();
            accountBoxes.Clear();
            accountSubs.Clear();

            if (names.Count == 0)
            {
                TextBlock none = new TextBlock();
                none.Text = "No accounts found. Connect a data feed or open a Sim account.";
                none.Foreground = ColMuted;
                none.FontSize = 11;
                none.TextWrapping = TextWrapping.Wrap;
                accountListPanel.Children.Add(none);
                return;
            }

            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];

                StackPanel rowSp = new StackPanel();
                rowSp.Margin = new Thickness(0, 3, 0, 7);

                StackPanel head = new StackPanel();
                head.Orientation = Orientation.Horizontal;

                CheckBox cb = new CheckBox();
                cb.Content = name;
                cb.Foreground = ColInk;
                cb.FontSize = 13;
                cb.VerticalAlignment = VerticalAlignment.Center;
                cb.IsChecked = monitor.IsMonitored(name);
                cb.Checked += delegate { OnAccountToggled(name, true); };
                cb.Unchecked += delegate { OnAccountToggled(name, false); };
                head.Children.Add(cb);

                // Straight from the account to its own rules. The editor lives
                // several screens further down the page, and a trader who wanted
                // different limits per account had no reason to believe the
                // dropdown down there had anything to do with the list up here.
                Button edit = new Button();
                edit.Content = "set its rules";
                edit.FontSize = 11;
                edit.Margin = new Thickness(10, 0, 0, 0);
                edit.Padding = new Thickness(0);
                edit.Background = ColTransparent;
                edit.BorderBrush = ColTransparent;
                edit.BorderThickness = new Thickness(0);
                edit.Foreground = ColAccent;
                edit.VerticalAlignment = VerticalAlignment.Center;
                edit.Click += delegate { EditAccount(name); };
                head.Children.Add(edit);

                rowSp.Children.Add(head);

                // The rules this account is actually running, spelled out. Without
                // it there is no way to tell a saved account from an unsaved one,
                // which is exactly the doubt that makes a trader re-enter settings
                // they already entered.
                TextBlock sub = new TextBlock();
                sub.FontSize = 11;
                sub.TextWrapping = TextWrapping.Wrap;
                sub.Margin = new Thickness(20, 1, 0, 0);

                rowSp.Children.Add(sub);
                accountListPanel.Children.Add(rowSp);
                accountBoxes[name] = cb;
                accountSubs[name] = sub;
                DescribeAccount(name);
            }

            RefreshEditTargets();
        }

        /// <summary>
        /// One line describing what an account is set to run. Deliberately shows
        /// the numbers that decide whether it survives - size, max loss, whether
        /// the floor trails - rather than a reassuring tick.
        /// </summary>
        private string ConfigSummary(string name, TrackerConfig c)
        {
            if (c == null) return "no rules set";

            string label;
            string firm = accountLabels.TryGetValue(name, out label) && !string.IsNullOrEmpty(label)
                ? label + "  -  " : "";

            string dd = c.TrailingDrawdown > 0 ? Money(c.TrailingDrawdown) + " max loss" : "no max loss set";

            // Where the floor stops mattering, spelled out. "intraday trail" alone
            // does not tell a trader that their threshold freezes at $265,000,
            // which is the moment profit starts being worth something.
            string trail;
            if (c.LockFloorAt > 0 && c.LockFloorAt <= c.StartingBalance)
            {
                trail = "fixed floor at " + Money(c.LockFloorAt);
            }
            else
            {
                trail = c.DrawdownType == DrawdownType.Intraday ? "intraday trail" : "end-of-day trail";
                if (c.LockFloorAt > 0) trail += " stopping at " + Money(c.LockFloorAt);
                else trail += ", never stops";
            }

            string prof = "";
            if (c.ProfileKey.Length > 0)
            {
                RiskProfile p = RiskProfiles.ByKey(c.ProfileKey);
                if (p != null)
                {
                    int cut = p.Name.IndexOf(" - ", StringComparison.Ordinal);
                    prof = "  -  " + (cut > 0 ? p.Name.Substring(0, cut) : p.Name);
                }
            }

            // "$0/day" is not a daily loss limit, it is the absence of one - and
            // read literally it says stop the moment you are down a cent. Apex
            // publishes no separate daily limit at all, so most accounts here sit
            // at zero and every one of them was saying something false.
            string daily = c.DailyLossLimit > 0
                ? "stop at " + Money(c.DailyLossLimit) + " down"
                : "no daily loss limit";
            if (c.FirmDailyLossLimit > 0) daily += " (firm's own: " + Money(c.FirmDailyLossLimit) + ")";

            // Whose maximum this is.
            //
            // It sat at the end of a sentence otherwise made entirely of the
            // firm's own facts - the size, the drawdown, the trailing type - so
            // "max 4" read as "Apex allows 4". Apex allows 27 on a legacy 250K.
            // Four is the trader's own choice, and the two numbers mean opposite
            // things: one is a ceiling imposed on you, the other is a decision
            // you made. The firm's cap only ever lowers yours; it never raises it,
            // because a cap is a limit and not a recommendation.
            int mine = c.BaseMaxContracts > 0 ? c.BaseMaxContracts : c.MaxContracts;
            string size = mine + (mine == 1 ? " contract" : " contracts");
            if (c.FirmMaxContracts > 0) size += " of " + c.FirmMaxContracts + " allowed";

            string target = c.DailyTarget > 0 ? "target " + Money(c.DailyTarget) : "no target";

            // YOUR line first, and every number on it.
            //
            // This line used to carry the drawdown, the trailing type and the
            // size cap - three facts the firm decided - and not one of the four
            // the trader decides. Two accounts set up with completely different
            // daily stops, trade counts and loss streaks therefore printed
            // identical summaries, which is indistinguishable from Ballast having
            // ignored the settings entirely. It is the only way to check that a
            // per-account rule took, so it says all four.
            string window = DisciplineEngine.WindowLabel(c.SessionStartMinute, c.SessionEndMinute);

            string yours = "YOURS: " + daily
                         + "  -  " + c.MaxTrades + (c.MaxTrades == 1 ? " trade" : " trades") + " max"
                         + "  -  stop after " + c.MaxLossesBeforeStop
                         + (c.MaxLossesBeforeStop == 1 ? " loss" : " losses in a row")
                         + "  -  " + target
                         + "  -  " + size
                         + "  -  " + window
                         + prof;

            string bot = c.IsAutomated ? "BOT  -  " : "";
            string theirs = "FIRM: " + bot + firm + dd + "  -  " + trail;

            return yours + "\n" + theirs;
        }

        /// <summary>
        /// Point the rules editor at one account, ticking it first if it is not
        /// being watched - an account nobody is watching cannot hold rules that
        /// do anything, and silently editing one would be worse than refusing.
        /// </summary>
        private void EditAccount(string name)
        {
            if (string.IsNullOrEmpty(name)) return;

            if (!monitor.IsMonitored(name))
            {
                CheckBox cb;
                if (accountBoxes.TryGetValue(name, out cb) && cb != null) cb.IsChecked = true;
                else OnAccountToggled(name, true);
                RefreshEditTargets();
            }

            try
            {
                if (editTargetBox != null && editTargetBox.Items.Contains(name))
                {
                    if (!Equals(editTargetBox.SelectedItem, name)) editTargetBox.SelectedItem = name;
                    else OnEditTargetChanged();   // already selected - still take them to it
                    editTargetBox.BringIntoView();
                    editTargetBox.Focus();
                }
            }
            catch { }

            if (applyNote != null)
            {
                applyNote.Text = "Editing " + name + ". Change the numbers below and press "
                               + "\"Apply and save\" - they apply to this account and no other.";
                applyNote.Foreground = ColAccent;
            }
        }

        private void OnAccountToggled(string name, bool on)
        {
            if (on)
            {
                bool isNew = !monitor.IsMonitored(name);

                // Rules held from a previous tick. GetOrCreate restores them, so
                // this has to be read BEFORE the call, not after.
                bool hadRules = monitor.RememberedConfig(name) != null;

                BallastTracker t = monitor.GetOrCreate(name);
                WireCapture(t, name);
                Account a = FindAccount(name);
                if (a != null && t != null)
                {
                    double realised = SafeGet(a, AccountItem.RealizedProfitLoss);
                    double unreal = SafeGet(a, AccountItem.UnrealizedProfitLoss);
                    double cash = SafeGet(a, AccountItem.CashValue);
                    t.EnsureSession(Core.Globals.Now, realised, cash + unreal);

                    // Read the firm off the account's own name and the size off its
                    // balance. The trader should not have to tell Ballast something
                    // the broker already told it. Only for accounts we have never
                    // configured - settings already saved are never overwritten.
                    if (isNew && !configuredFromDisk.Contains(name) && !hadRules) AutoDetectOne(name, t, cash);
                }
            }
            else
            {
                // Keeps this account's rules - see BallastMonitor.Remove. The
                // trader is saying "stop watching", not "throw my setup away".
                monitor.Remove(name);
                BallastState.Clear(name);   // stop any chart still showing its banner
            }

            RefreshEditTargets();
            DescribeAccount(name);

            // Ticking an account IS a setting. It used to survive only if the
            // trader happened to press Apply and save afterwards, so a tick made
            // and then slept on was gone in the morning.
            SaveSettings();
        }

        /// <summary>
        /// Configure one account from its name and balance. Silent when it works,
        /// silent when it cannot - it never guesses, so there is nothing to warn
        /// about. A Sim account, or a balance matching no standard size, is simply
        /// left for the trader to set by hand.
        /// </summary>
        /// <summary>
        /// Which platform this account connects through - "RITHMIC", "TRADOVATE"
        /// or "" when it cannot be determined.
        ///
        /// This is not cosmetic. Apex's intraday evaluation threshold stops
        /// trailing at the target profit balance on Rithmic and WealthCharts, and
        /// never stops on Tradovate. Same firm, same size, same evaluation,
        /// different floor. Getting it from the connection means the trader does
        /// not have to know that Apex has three different rules and which one
        /// their data feed puts them under.
        ///
        /// Reflection, because the connection types are not something worth
        /// taking a hard reference on for one string, and a failure here should
        /// only ever mean "unknown platform" rather than a broken add-on.
        /// </summary>
        private string PlatformOf(Account a)
        {
            try
            {
                if (a == null) return "";

                object conn = null;
                System.Reflection.PropertyInfo cp = a.GetType().GetProperty("Connection");
                if (cp != null) conn = cp.GetValue(a, null);
                if (conn == null) return "";

                // The readable name lives on the connection's options.
                string found = "";
                System.Reflection.PropertyInfo op = conn.GetType().GetProperty("Options");
                if (op != null)
                {
                    object opts = op.GetValue(conn, null);
                    if (opts != null)
                    {
                        System.Reflection.PropertyInfo np = opts.GetType().GetProperty("Name");
                        if (np != null) found = np.GetValue(opts, null) as string;

                        if (string.IsNullOrEmpty(found))
                        {
                            System.Reflection.PropertyInfo pp = opts.GetType().GetProperty("Provider");
                            if (pp != null)
                            {
                                object prov = pp.GetValue(opts, null);
                                if (prov != null) found = prov.ToString();
                            }
                        }
                    }
                }

                if (string.IsNullOrEmpty(found)) found = conn.ToString();
                return RuleBook.PlatformFromConnection(found);
            }
            catch { return ""; }
        }

        private void AutoDetectOne(string name, BallastTracker t, double balance)
        {
            try
            {
                if (ruleBook == null || ruleBook.Count == 0) return;

                bool preferIntraday = monitor.DefaultConfig.DrawdownType == DrawdownType.Intraday;
                AccountGeneration g = t.Config.Generation != AccountGeneration.Auto
                    ? t.Config.Generation : generation;
                string platform = PlatformOf(FindAccount(name));
                FirmAccountSpec s = ruleBook.AutoDetect(name, balance, preferIntraday, g, platform);
                if (s == null) return;

                t.Config = RuleBook.ToConfig(s, t.Config);
                t.Config.BaseMaxContracts = t.Config.MaxContracts;
                accountLabels[name] = s.Firm + " " + s.Label;

                detectionNote.Text = "Recognised " + name + " as " + s.Firm + " " + s.Label
                                   + (platform.Length > 0
                                        ? " - connected over " + platform.ToLowerInvariant()
                                          + ", which decides how its threshold trails"
                                        : "")
                                   + ". Verify against your firm, then add your own trading rules below.";
                detectionNote.Foreground = ColMuted;
                SaveSettings();
            }
            catch { }
        }

        private void SetAllAccounts(bool on)
        {
            foreach (KeyValuePair<string, CheckBox> kv in accountBoxes)
            {
                if (kv.Value.IsChecked != on) kv.Value.IsChecked = on;
            }
        }

        private void RefreshEditTargets()
        {
            suppressEditTargetReload = true;
            object previous = editTargetBox.SelectedItem;

            editTargetBox.Items.Clear();
            editTargetBox.Items.Add("All accounts (default)");
            foreach (string n in monitor.MonitoredNames) editTargetBox.Items.Add(n);

            if (previous != null && editTargetBox.Items.Contains(previous))
                editTargetBox.SelectedItem = previous;
            else
                editTargetBox.SelectedIndex = 0;

            suppressEditTargetReload = false;

            // If the account being edited has just been un-ticked, the selector
            // has silently fallen back to "All accounts". The fields still show
            // that account's numbers, and the next Apply would write them into
            // the defaults instead. Reload so what is on screen always belongs to
            // what the selector says.
            string now = CurrentEditKey();
            if (editingKey != null && editingKey != now)
            {
                CommitPendingEdits();
                editingKey = now;
                TrackerConfig c = ConfigForKey(now);
                if (c != null) LoadConfigIntoFields(c);
            }
            ShowEditingScope();
        }

        /// <summary>
        /// Say, in the page itself, whose rules are being edited. The selector
        /// alone was not enough: it sits above a long form, and a trader who
        /// scrolled down to the fields could not see whether they were about to
        /// change one account or every account at once.
        /// </summary>
        private void ShowEditingScope()
        {
            if (editingScope == null) return;

            string key = CurrentEditKey();
            if (key.Length == 0)
            {
                editingScope.Text = "Editing the DEFAULTS - the starting point for accounts you tick "
                                  + "from now on. Nothing you change here touches an account you are "
                                  + "already watching until you press \"Copy to all accounts\".";
                editingScope.Foreground = ColAmber;
            }
            else
            {
                editingScope.Text = "Editing " + key + " only. Every number below belongs to this "
                                  + "account alone - your daily loss, your target, your trade count "
                                  + "and your losses in a row can all differ from every other account.";
                editingScope.Foreground = ColAccent;
            }
        }

        private bool EditingDefault()
        {
            return editTargetBox.SelectedIndex <= 0;
        }

        private void ToggleFirmFields()
        {
            if (firmFields == null) return;

            bool show = firmFields.Visibility != Visibility.Visible;
            firmFields.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (firmToggle != null)
                firmToggle.Content = show ? "Hide the firm's figures" : "Show the firm's figures";
        }

        /// <summary>
        /// One sentence confirming what Ballast believes this account is. On a
        /// prop account these numbers are not decisions, they are facts to check,
        /// and a sentence is a far better way to check a fact than four form
        /// fields are.
        /// </summary>
        private void RefreshFirmSummary(TrackerConfig c)
        {
            if (firmSummary == null) return;

            if (c == null) { firmSummary.Text = ""; return; }

            string dd = c.DrawdownType == DrawdownType.Intraday
                ? "trailing intraday" : "trailing end-of-day";

            string text = "Ballast has this as " + Money(c.StartingBalance)
                        + " with " + Money(c.TrailingDrawdown) + " " + dd;

            if (c.LockFloorAt > 0) text += ", floor fixed once it reaches " + Money(c.LockFloorAt);
            else text += ", floor never stops trailing";

            if (c.FirmMaxContracts > 0)
                text += ". Your firm's own cap is " + c.FirmMaxContracts
                     + (c.FirmMaxContracts == 1 ? " contract" : " contracts");

            firmSummary.Text = text + ".";
        }

        /// <summary>
        /// The config the fields on screen were loaded from, or null if that
        /// config no longer exists (the account was un-ticked while being edited).
        /// </summary>
        private TrackerConfig ConfigForKey(string key)
        {
            if (key == null) return null;
            if (key.Length == 0) return monitor.DefaultConfig;

            BallastTracker t = monitor.Get(key);
            return t != null ? t.Config : null;
        }

        private string CurrentEditKey()
        {
            if (editTargetBox == null) return "";
            if (EditingDefault()) return "";
            string n = editTargetBox.SelectedItem as string;
            return n == null ? "" : n;
        }

        /// <summary>
        /// Save whatever is on screen back to the account it was typed for.
        /// Called before the selector moves somewhere else, so per-account edits
        /// are never lost by navigating away from them.
        ///
        /// Silent when nothing was changed, so simply reading through accounts
        /// never rewrites them - which matters, because writing a config back
        /// resets the size throttle to its base.
        /// </summary>
        private void CommitPendingEdits()
        {
            TrackerConfig c = ConfigForKey(editingKey);
            if (c == null) return;
            if (!FieldsDifferFrom(c)) return;

            ReadFieldsInto(c);

            if (editingKey.Length > 0) DescribeAccount(editingKey);
            SaveSettings();
        }

        /// <summary>True when at least one field on screen says something the config does not.</summary>
        private bool FieldsDifferFrom(TrackerConfig c)
        {
            try
            {
                if (ParseD(tbDailyLoss, c.DailyLossLimit) != c.DailyLossLimit) return true;
                if (ParseD(tbTarget, c.DailyTarget) != c.DailyTarget) return true;
                if (ParseI(tbMaxTrades, c.MaxTrades) != c.MaxTrades) return true;
                if (ParseI(tbMaxLosses, c.MaxLossesBeforeStop) != c.MaxLossesBeforeStop) return true;

                int shown = c.BaseMaxContracts > 0 ? c.BaseMaxContracts : c.MaxContracts;
                if (ParseI(tbMaxContracts, shown) != shown) return true;

                if (ParseD(tbBalance, c.StartingBalance) != c.StartingBalance) return true;
                if (ParseD(tbDrawdown, c.TrailingDrawdown) != c.TrailingDrawdown) return true;
                if (ParseD(tbLockAt, c.LockFloorAt) != c.LockFloorAt) return true;

                if (automatedBox != null && (automatedBox.IsChecked == true) != c.IsAutomated) return true;
                if (trustRealisedBox != null
                    && (trustRealisedBox.IsChecked == true) != c.TrustAccountRealised) return true;
                if (acctGenBox != null && acctGenBox.SelectedIndex >= 0
                    && (AccountGeneration)acctGenBox.SelectedIndex != c.Generation) return true;

                DrawdownType shownDd = ddTypeBox.SelectedIndex == 1
                    ? DrawdownType.EndOfDay : DrawdownType.Intraday;
                if (shownDd != c.DrawdownType) return true;

                bool anyTimeNow = windowAnyTimeBox != null && windowAnyTimeBox.IsChecked == true;
                bool anyTimeCfg = c.SessionStartMinute == c.SessionEndMinute;
                if (anyTimeNow != anyTimeCfg) return true;
                if (!anyTimeNow)
                {
                    int ws = DisciplineEngine.ParseHourMinute(tbWindowStart == null ? null : tbWindowStart.Text);
                    int we = DisciplineEngine.ParseHourMinute(tbWindowEnd == null ? null : tbWindowEnd.Text);
                    if (ws >= 0 && ws != c.SessionStartMinute) return true;
                    if (we >= 0 && we != c.SessionEndMinute) return true;
                }
            }
            catch { return false; }

            return false;
        }

        private void OnEditTargetChanged()
        {
            if (suppressEditTargetReload) return;

            string next = CurrentEditKey();
            if (editingKey != null && editingKey != next) CommitPendingEdits();

            editingKey = next;
            ShowEditingScope();
            RefreshStopCostHint();
            RefreshRealisedNote();
            RefreshCoherence();
            RefreshWindowClock();

            TrackerConfig c = ConfigForKey(next);
            if (c == null) return;
            LoadConfigIntoFields(c);

            // The profile preview quotes dollar figures for whichever account is
            // being edited, so it has to follow the selection.
            if (profileDetail != null) ShowProfileDetail();
        }

        private double SafeGet(Account a, AccountItem item)
        {
            try { return a.Get(item, Currency.UsDollar); }
            catch { return 0; }
        }

        // ── Rule book ────────────────────────────────────────────────────────

        private string RuleBookPath()
        {
            try
            {
                return Path.Combine(Core.Globals.UserDataDir,
                    Path.Combine("bin", Path.Combine("Custom",
                        Path.Combine("AddOns", Path.Combine("Ballast", "ballast-rules.txt")))));
            }
            catch { return "ballast-rules.txt"; }
        }

        private void LoadRuleBook()
        {
            bool ok = ruleBook.Load(RuleBookPath());

            firmBox.Items.Clear();
            if (ok)
            {
                List<string> firms = ruleBook.Firms();
                for (int i = 0; i < firms.Count; i++) firmBox.Items.Add(firms[i]);
                if (firmBox.Items.Count > 0) firmBox.SelectedIndex = 0;

                detectionNote.Text = ruleBook.Count + " account types loaded from the rule book (figures checked "
                                   + ruleBook.VerifiedDate + "). Verify against your firm - prop rules change often. "
                                   + "You can edit ballast-rules.txt yourself and press Reload.";
                detectionNote.Foreground = ColMuted;
            }
            else
            {
                detectionNote.Text = "Rule book not loaded: " + (ruleBook.LoadError ?? "unknown error")
                                   + " - enter your figures manually below.";
                detectionNote.Foreground = ColAmber;
            }

            PopulateAccountTypes();
        }

        /// <summary>
        /// Ask the server for a newer rule book. Runs on a background thread so a
        /// slow or dead network can never touch the trading UI. Silent unless
        /// something actually changed (or the trader pressed the button).
        /// </summary>
        private void CheckForRuleUpdates(bool force)
        {
            string path = RuleBookPath();

            if (force)
            {
                detectionNote.Text = "Checking tradeballast.com for rule updates...";
                detectionNote.Foreground = ColMuted;
            }

            RuleBookUpdater.CheckInBackground(path, force, delegate (RuleUpdateResult r)
            {
                // Marshal back to the UI thread before touching anything.
                try
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        try
                        {
                            if (r.Updated)
                            {
                                LoadRuleBook();
                                detectionNote.Text = r.Message + " Rules now current - verify against your firm.";
                                detectionNote.Foreground = ColGreen;
                            }
                            else if (force)
                            {
                                detectionNote.Text = r.Message ?? "No update available.";
                                detectionNote.Foreground = r.Checked ? ColMuted : ColAmber;
                            }
                        }
                        catch { }
                    }));
                }
                catch { }
            });
        }

        /// <summary>
        /// Hide the generation the trader has said they do not hold. Offering a
        /// legacy-only trader ten current-generation drawdowns is ten chances to
        /// pick a wrong one.
        /// </summary>
        private List<FirmAccountSpec> FilterByGeneration(List<FirmAccountSpec> all)
        {
            if (generation == AccountGeneration.Auto || all == null) return all;

            List<FirmAccountSpec> keep = new List<FirmAccountSpec>();
            for (int i = 0; i < all.Count; i++)
            {
                bool legacy = RuleBook.IsLegacyPlanName(all[i].Plan);
                if (generation == AccountGeneration.Legacy && !legacy) continue;
                if (generation == AccountGeneration.Current && legacy) continue;
                keep.Add(all[i]);
            }

            // A firm with no rows in that generation keeps all of them, rather
            // than presenting an empty dropdown.
            return keep.Count > 0 ? keep : all;
        }

        private void PopulateAccountTypes()
        {
            // Rebuilding the list fires SelectionChanged, and the type dropdown
            // now writes to the account on selection. Without this guard, merely
            // switching firms would silently reconfigure whichever account was
            // being edited to the first type in the new list.
            suppressTypeApply = true;
            try
            {
                accountTypeBox.Items.Clear();
                string firm = firmBox.SelectedItem as string;
                if (string.IsNullOrEmpty(firm)) return;

                List<FirmAccountSpec> list = FilterByGeneration(ruleBook.ForFirm(firm));
                for (int i = 0; i < list.Count; i++) accountTypeBox.Items.Add(list[i].Label);
                if (accountTypeBox.Items.Count > 0) accountTypeBox.SelectedIndex = 0;
            }
            finally { suppressTypeApply = false; }
        }

        private FirmAccountSpec SelectedSpec()
        {
            string firm = firmBox.SelectedItem as string;
            string label = accountTypeBox.SelectedItem as string;
            if (string.IsNullOrEmpty(firm) || string.IsNullOrEmpty(label)) return null;

            List<FirmAccountSpec> list = ruleBook.ForFirm(firm);
            for (int i = 0; i < list.Count; i++)
                if (list[i].Label == label) return list[i];
            return null;
        }

        /// <summary>Apply the chosen firm/account type to the account being edited.</summary>
        private void ApplyChosenType(bool silent)
        {
            FirmAccountSpec s = SelectedSpec();
            if (s == null)
            {
                detectionNote.Text = "Pick a firm and account type first.";
                detectionNote.Foreground = ColAmber;
                return;
            }

            if (EditingDefault())
            {
                monitor.DefaultConfig = RuleBook.ToConfig(s, monitor.DefaultConfig);
                LoadConfigIntoFields(monitor.DefaultConfig);
                detectionNote.Text = "Default set to " + s.Firm + " " + s.Label + ". "
                                   + (string.IsNullOrEmpty(s.Note) ? "" : s.Note + " ")
                                   + "Use \"Copy to all accounts\" to push it everywhere.";
            }
            else
            {
                string name = editTargetBox.SelectedItem as string;
                BallastTracker t = monitor.Get(name);
                if (t == null) return;

                t.Config = RuleBook.ToConfig(s, t.Config);
                accountLabels[name] = s.Firm + " " + s.Label;
                LoadConfigIntoFields(t.Config);
                detectionNote.Text = name + " set to " + s.Firm + " " + s.Label + ". "
                                   + (string.IsNullOrEmpty(s.Note) ? "" : s.Note + " ")
                                   + "Verify against your firm.";
            }

            detectionNote.Foreground = ColMuted;
            SaveSettings();
        }

        /// <summary>
        /// Match every monitored account to an account type of the selected firm by
        /// its balance. Anything uncertain is reported rather than guessed — a
        /// silently wrong drawdown is worse than none.
        /// </summary>
        private void AutoConfigure()
        {
            string firm = firmBox.SelectedItem as string;
            if (string.IsNullOrEmpty(firm))
            {
                detectionNote.Text = "Pick a firm first.";
                detectionNote.Foreground = ColAmber;
                return;
            }

            // If an account type is selected, prefer that plan when matching.
            FirmAccountSpec chosen = SelectedSpec();
            string preferredPlan = chosen != null ? chosen.Plan : null;

            List<string> configured = new List<string>();
            List<string> skipped = new List<string>();

            foreach (string name in new List<string>(monitor.MonitoredNames))
            {
                Account a = FindAccount(name);
                BallastTracker t = monitor.Get(name);
                if (a == null || t == null) { skipped.Add(name + " (not connected)"); continue; }

                double balance = SafeGet(a, AccountItem.CashValue);

                // Through AutoDetect so the stated generation and the
                // evaluation/funded distinction apply here too - matching by
                // balance alone is what put 4.0 figures on legacy accounts.
                bool preferIntraday = t.Config.DrawdownType == DrawdownType.Intraday;
                AccountGeneration g = t.Config.Generation != AccountGeneration.Auto
                    ? t.Config.Generation : generation;
                FirmAccountSpec s = ruleBook.AutoDetect(name, balance, preferIntraday, g);
                if (s == null) s = ruleBook.MatchByBalance(firm, balance, preferredPlan);

                if (s == null)
                {
                    skipped.Add(name + " (balance " + Money(balance) + " matches no " + firm + " size)");
                    continue;
                }

                t.Config = RuleBook.ToConfig(s, t.Config);   // keeps personal guardrails
                accountLabels[name] = s.Firm + " " + s.Label;
                configured.Add(name + " = " + s.Label);
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            if (configured.Count > 0)
                sb.Append("Matched " + configured.Count + ": " + string.Join(", ", configured.ToArray())
                        + ". Verify against your firm before trusting the cushion. ");
            if (skipped.Count > 0)
                sb.Append("Not set: " + string.Join("; ", skipped.ToArray()) + ".");
            if (sb.Length == 0)
                sb.Append("Nothing to match - tick some accounts first.");

            detectionNote.Text = sb.ToString();
            detectionNote.Foreground = skipped.Count > 0 ? ColAmber : ColMuted;

            RefreshEditTargets();
            OnEditTargetChanged();
            SaveSettings();
            RefreshAccountList(true);
        }

        /// <summary>
        /// The account's position, taken from whichever instrument is carrying
        /// the most contracts.
        ///
        /// It used to be the NET of every position on the account: longs added,
        /// shorts subtracted. Hold two contracts of one instrument and two short
        /// of another and that nets to zero - which Ballast read as "flat", so it
        /// closed the round-trip, banked a trade, and then opened a fresh one on
        /// the very next poll a second later. One trade became two, the loss
        /// streak counted a trade that never happened, and the journal grew a row
        /// with an entry time that made no sense.
        ///
        /// Taking the dominant instrument instead means flat is only ever
        /// reported when every position really is flat, and the direction and
        /// size agree with OpenInstrument, which already picks the same one.
        /// </summary>
        private int SignedPosition(Account a)
        {
            int best = 0;
            int bestAbs = 0;

            try
            {
                lock (a.Positions)
                {
                    foreach (Position p in a.Positions)
                    {
                        int q;
                        if (p.MarketPosition == MarketPosition.Long) q = p.Quantity;
                        else if (p.MarketPosition == MarketPosition.Short) q = -p.Quantity;
                        else continue;

                        int abs = q < 0 ? -q : q;
                        if (abs > bestAbs) { bestAbs = abs; best = q; }
                    }
                }
            }
            catch { }

            return best;
        }

        /// <summary>
        /// Name of whatever is open on this account, for the journal. If several
        /// instruments are open at once we take the largest, since that is the
        /// one the trade was really about.
        /// </summary>
        private string OpenInstrument(Account a)
        {
            string best = "";
            int bestQty = 0;
            try
            {
                lock (a.Positions)
                {
                    foreach (Position p in a.Positions)
                    {
                        if (p.MarketPosition == MarketPosition.Flat) continue;
                        if (p.Quantity > bestQty && p.Instrument != null)
                        {
                            bestQty = p.Quantity;
                            best = p.Instrument.FullName;
                        }
                    }
                }
            }
            catch { }
            return best;
        }

        /// <summary>
        /// Work out whether this account traded while Ballast was closed, and if
        /// it did, put a row in the journal for it.
        ///
        /// With the session baseline restored, the day's P&L is already right -
        /// it includes whatever happened while the window was shut, because it is
        /// measured from a point before that. What is missing is the RECORD: no
        /// journal row, no trade counted, no loss counted, so the max-trades rule
        /// and the loss streak were both short and the journal had a hole in it.
        ///
        /// The difference between what the account says the day has made and what
        /// the journal can account for IS the missing trading, to the cent. It
        /// cannot be broken back into individual trades - one row, one figure,
        /// clearly labelled as reconstructed rather than watched. There is no
        /// screenshot and no entry context, and inventing either would be worse
        /// than admitting the gap.
        ///
        /// Runs once per account per session, and only for accounts whose
        /// baseline actually came back from disk. Without that guard the first
        /// open of a day would decide the entire morning was a missing trade.
        /// </summary>
        /// <summary>
        /// What the reconstructed row says about itself. Rewritten whenever the
        /// gap grows, so the sentence and the number never drift apart.
        /// </summary>
        private string GapNote(string name, BallastTracker t, double total, DateTime from, DateTime to)
        {
            return "Reconstructed, not watched. " + Money(total)
                 + " of today's P&L on this account happened while Ballast was not running"
                 + (from < to ? ", between " + from.ToString("HH:mm", CultureInfo.InvariantCulture)
                                + " and " + to.ToString("HH:mm", CultureInfo.InvariantCulture) : "")
                 + ". The figure is exact - it is the difference between what your broker says today "
                 + "has made and what Ballast watched - and commission has been allowed for. What it "
                 + "cannot tell you is how many trades are inside it, or how deep the day went while "
                 + "Ballast was closed. It counts as one trade, and as one loss if it lost, because "
                 + "that is the least it can have been.";
        }

        private void ReconcileClosedPeriod(string name, BallastTracker t, DateTime now)
        {
            if (t == null || !t.BaselineAuthoritative) return;

            // Wait for a believable balance and for the account to be flat. A
            // reading taken mid-position would book a difference that is about to
            // resolve itself.
            if (!t.HasValidEquity) return;
            if (t.OpenContracts != 0) return;

            double accounted = 0;
            double watchedCommission = 0;
            List<BallastTrade> today = monitor.Journal.ForDay(now);
            for (int i = 0; i < today.Count; i++)
            {
                if (today[i] == null) continue;
                if (!string.Equals(today[i].AccountName, name, StringComparison.OrdinalIgnoreCase)) continue;
                accounted += today[i].Pnl;
                watchedCommission += today[i].Commission;
            }

            double missing = t.DailyPnl - accounted;

            // How much of a difference is explainable as commission timing.
            //
            // The account's own commission total for the day is the bound, and it
            // is the right one for a simple reason: this drift is CAUSED by
            // commission posting a beat apart from the fill it belongs to, so it
            // can never exceed the commission actually paid. It is exact, it
            // needs no history, and it scales by itself - a few dollars for one
            // contract, forty for ten minis.
            //
            // Summing the journal's own commission was the first attempt and it
            // failed on the case that matters: rows written before commission was
            // recorded all read zero, so the bound collapsed to five dollars on
            // every restart and a $26 commission residue was booked as a trade.
            // The journal figure is kept only as a fallback for a feed that does
            // not report commission at all.
            double noise = Math.Max(t.CurrentCommission, watchedCommission) + 5.0;

            // Small change is commission, not a trade.
            //
            // The account's realised P&L and Ballast's own round-trip figures
            // both include commission, but they do not always post at the same
            // instant, and a few dollars of drift is normal on a day with any
            // volume in it. The first version booked $4 as a missing trade and
            // put it in the tagging queue - which is worse than useless, because
            // a queue full of four-dollar phantoms is a queue nobody reads.
            //
            // A real futures trade that nets less than this is possible but rare,
            // and missing one costs nothing: the day's P&L comes from the account
            // either way, so it is already in every number that matters. Only the
            // journal ROW is skipped.
            // Both directions. The first version only tolerated a gap where the
            // account looked WORSE than the journal, on the assumption that a
            // missing commission charge could only ever go one way. It goes both:
            // a charge that lands after Ballast has closed one round trip is
            // absorbed by the next one, so the journal can just as easily
            // over-state the loss. The trader's own case was +$26 - Ballast had
            // counted MORE loss than the account had.
            if (missing > -noise && missing < noise)
            {
                gapSince.Remove(name);
                return;
            }

            // The gap has to hold still before it is believed. A round trip that
            // has just closed shows up in the account's realised P&L a moment
            // before it shows up in the journal, and booking that instant as a
            // missing trade would invent one on every single trade the trader
            // takes while watching.
            DateTime since;
            if (!gapSince.TryGetValue(name, out since)) { gapSince[name] = now; return; }
            if ((now - since).TotalSeconds < 20) return;
            gapSince.Remove(name);

            DateTime from;
            if (!lastSeenAt.TryGetValue(name, out from)) from = now;

            // One row per account per day. Ballast may be closed and reopened a
            // dozen times in a session; each time it finds a little more that it
            // cannot account for, and each of those used to become its own
            // journal row. Twelve rows is not twelve trades, it is one gap
            // measured twelve times - and counting them as trades drove accounts
            // to limits their owner never went near.
            BallastTrade existing = null;
            for (int i = 0; i < today.Count; i++)
            {
                if (today[i] == null || !today[i].IsReconstructed) continue;
                if (!string.Equals(today[i].AccountName, name, StringComparison.OrdinalIgnoreCase)) continue;
                existing = today[i];
                break;
            }

            if (existing != null)
            {
                existing.Pnl += missing;
                existing.ExitTime = now;
                existing.Note = GapNote(name, t, existing.Pnl, existing.EntryTime, now);
                journalDirty = true;
                SeedTodaysCounts(monitor.Journal.ForDay(now));
                return;
            }

            BallastTrade e = new BallastTrade();
            e.AccountName = name;
            e.Instrument = "(Ballast was closed)";
            e.IsLong = missing >= 0;
            e.MaxContracts = 0;
            e.EntryTime = from;
            e.ExitTime = now;
            e.Pnl = missing;
            e.TradeNumberToday = t.TradesToday + 1;
            e.DailyPnlBefore = accounted;
            e.AdviceAtEntry = "Ballast was not running";

            // Not "outside your session window". Ballast has no idea when this
            // was opened, so claiming it broke the clock rule would be inventing
            // a discipline failure out of a gap in its own records.
            e.InsideSessionWindow = true;

            // Not a question. "Was that by the book?" is unanswerable about a
            // trade Ballast cannot describe - it has no entry, no size, and may
            // be more than one trade. It goes in the journal as a record and
            // stays out of the queue.
            e.Dismissed = true;
            e.SessionPlan = monitor.Journal.SessionPlan;
            e.Note = GapNote(name, t, missing, from, now);

            monitor.Journal.Add(e);
            journalDirty = true;

            // Fold it into today's counts, so the trade count and the loss streak
            // include it. One row counts as one trade even if it covered several -
            // under-counting, never over-counting.
            SeedTodaysCounts(monitor.Journal.ForDay(now));
        }

        // ── Loop ─────────────────────────────────────────────────────────────

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                RefreshAccountList(false);

                DateTime now = Core.Globals.Now;

                foreach (string name in new List<string>(monitor.MonitoredNames))
                {
                    Account a = FindAccount(name);
                    if (a == null) continue;

                    BallastTracker t = monitor.Get(name);
                    if (t == null) continue;
                    if (t.CaptureChart == null) WireCapture(t, name);

                    double realised = SafeGet(a, AccountItem.RealizedProfitLoss);
                    double unreal   = SafeGet(a, AccountItem.UnrealizedProfitLoss);
                    double cash     = SafeGet(a, AccountItem.CashValue);
                    double equity   = cash + unreal;

                    // Handed over before the position update so a round trip that
                    // closes on this tick can record what it cost.
                    t.CurrentCommission = Math.Abs(SafeGet(a, AccountItem.Commission));

                    t.EnsureSession(now, realised, equity);
                    t.OnEquity(equity, realised);

                    // Routed through the monitor so a completed round-trip lands
                    // in the journal without the trader doing anything at all.
                    BallastTrade closed = monitor.OnPosition(
                        name, SignedPosition(a), realised, now, OpenInstrument(a));

                    if (closed != null) journalDirty = true;

                    ReconcileClosedPeriod(name, t, now);
                }

                // The baseline is worth nothing if it is only written at close -
                // NinjaTrader does not always get to close politely.
                if ((now - lastSessionSave).TotalSeconds >= 30)
                {
                    lastSessionSave = now;
                    SaveSessionState();
                }

                List<AccountSnapshot> snaps = monitor.EvaluateAll(now);

                // Drawing is allowed to fail; the wall is not. A malformed
                // journal row or a bad thumbnail must never be able to take the
                // one hard-stop mechanism in the product off the screen for the
                // rest of the session, so rendering gets its own net.
                try
                {
                    Render(snaps);
                    RenderJournal();
                }
                catch (Exception rex)
                {
                    if (headlineText != null)
                    {
                        headlineText.Text = "Ballast display error: " + rex.Message;
                        headlineText.Foreground = ColRed;
                    }
                }

                // After Render, so the row warnings are already on the shared
                // board and the lock flag lands on top of them rather than under.
                UpdateTilt(snaps, now);

                // The cost of an override only means anything if it survives a
                // restart, so the running figure is flushed rather than held in
                // memory until the next override that may never come.
                if (tiltDirty && (now - lastTiltSave).TotalSeconds >= 30)
                {
                    tiltDirty = false;
                    lastTiltSave = now;
                    tiltLog.Save(TiltPath());
                }

                if (journalDirty)
                {
                    journalDirty = false;
                    monitor.Journal.Save(JournalPath());

                    // Housekeeping on write rather than on a timer: images are the
                    // only thing here that grows without bound.
                    if (lastPrune.Date != now.Date)
                    {
                        lastPrune = now;
                        ChartSnapshot.Prune(ImageRoot(), now);
                    }
                }
            }
            catch (Exception ex)
            {
                // Never let the error handler be the thing that throws.
                if (headlineText != null)
                {
                    headlineText.Text = "Ballast error: " + ex.Message;
                    headlineText.Foreground = ColRed;
                }
            }
        }

        private void Render(List<AccountSnapshot> snaps)
        {
            if (snaps.Count == 0)
            {
                card.BorderBrush = ColLine;
                urgencyText.Text = "NEXT ACTION";
                urgencyText.Foreground = ColMuted;
                headlineText.Text = "Tick an account to begin";
                headlineAccountText.Text = "";
                bulletPanel.Children.Clear();
                rowsPanel.Children.Clear();
                statCushion.Text = "-"; statPnl.Text = "-"; statAccounts.Text = "0";
                if (emptyNote != null) emptyNote.Visibility = Visibility.Visible;
                return;
            }

            AccountSnapshot worst = monitor.MostUrgent(snaps);
            DisciplineDecision d = worst.Decision;

            Brush colour = ColGreen; string urg = "CALM";
            if (d.Urgency == Urgency.Caution) { colour = ColAmber; urg = "CAUTION"; }
            else if (d.Urgency == Urgency.Alert) { colour = ColRed; urg = "ALERT"; }

            card.BorderBrush = colour;
            urgencyText.Foreground = colour;
            urgencyText.Text = "NEXT ACTION - " + urg;
            // Name the account IN the headline, not underneath it.
            //
            // "Stop - you're at your max trades for the day" reads as a statement
            // about the trader's whole day. It is not: it is about one account
            // that has hit its own limit, while another may have four trades left
            // and a different limit entirely. Burying that in a smaller line
            // below made a per-account fact look like a global one.
            headlineText.Text = snaps.Count > 1
                ? worst.AccountName + " - " + LowerFirst(d.Headline)
                : d.Headline;
            headlineText.Foreground = ColInk;
            string ctx = snaps.Count > 1 ? "driven by " + worst.AccountName : worst.AccountName;
            if (!worst.Input.HasValidEquity)
            {
                ctx += "  -  no balance yet (account not connected?)";
            }
            else if (worst.Input.PastFloor)
            {
                ctx += "  -  balance " + Money(worst.Input.CurrentEquity)
                     + " is at or below its floor of " + Money(worst.Input.FloorLevel)
                     + "  -  check the account size in Setup";
            }
            else
            {
                ctx += "  -  balance " + Money(worst.Input.CurrentEquity)
                     + ", closed at " + Money(worst.Input.FloorLevel)
                     + ", so " + Money(worst.Input.CushionToFloor) + " of room"
                     + (worst.Input.FloorLocked
                        ? "  -  the floor is fixed at " + Money(worst.Input.FloorLevel)
                          + ", so profit above that is real cushion"
                        : "");
            }
            headlineAccountText.Text = ctx;

            bulletPanel.Children.Clear();
            for (int n = 0; n < d.Bullets.Count; n++)
            {
                TextBlock b = new TextBlock();
                b.Text = "- " + d.Bullets[n];
                b.Foreground = ColMuted; b.FontSize = 11;
                b.TextWrapping = TextWrapping.Wrap;
                b.Margin = new Thickness(0, 2, 0, 0);
                bulletPanel.Children.Add(b);
            }

            if (monitor.AnyValidEquity(snaps))
            {
                double minCushion = monitor.MinCushion(snaps);

                // Whose number this is. It is the single worst account, never a
                // total - adding cushions together would suggest you can lose the
                // sum of them, when in fact the first account to hit its floor is
                // the one that ends.
                string who = "";
                for (int i = 0; i < snaps.Count; i++)
                {
                    if (!snaps[i].Input.HasValidEquity) continue;
                    if (Math.Abs(snaps[i].Input.CushionToFloor - minCushion) < 0.01)
                    { who = snaps[i].AccountName; break; }
                }

                if (minCushion <= 0)
                {
                    statCushion.Text = "past floor";
                    statCushion.Foreground = ColRed;
                }
                else
                {
                    statCushion.Text = Money(minCushion);
                    statCushion.Foreground = minCushion < 400 ? ColRed : ColGreen;
                }

                // Name the account in the caption, not in a footnote under three
                // cards. Whose number this is matters more than what it is.
                if (statCushionCap != null)
                    statCushionCap.Text = who.Length > 0
                        ? "CLOSEST TO ITS FLOOR - " + who.ToUpperInvariant()
                        : "CLOSEST TO ITS FLOOR";

                if (statCushionWho != null)
                {
                    if (who.Length == 0)
                        statCushionWho.Text = "";
                    else if (minCushion <= 0)
                        statCushionWho.Text = who + " is at or below its floor. Every other account "
                                            + "has more room than this one.";
                    else if (snaps.Count > 1)
                        statCushionWho.Text = who + " can lose " + Money(minCushion)
                                            + " before that account is finished - the least room of "
                                            + "the " + snaps.Count + " you are watching. It is one "
                                            + "account's number, never a total: cushions are not "
                                            + "added up, because the first account to reach its "
                                            + "floor is the one that ends.";
                    else
                        statCushionWho.Text = who + " can lose " + Money(minCushion)
                                            + " before that account is finished.";
                }
            }
            else
            {
                // Better to admit we don't know than to invent a frightening number.
                statCushion.Text = "no data";
                statCushion.Foreground = ColMuted;
                if (statCushionCap != null) statCushionCap.Text = "CLOSEST TO ITS FLOOR";
                if (statCushionWho != null)
                    statCushionWho.Text = "No account has reported a balance yet. Better to say so "
                                        + "than to work a cushion out from a zero.";
            }

            double total = monitor.TotalDailyPnl(snaps);
            statPnl.Text = Money(total);
            statPnl.Foreground = total >= 0 ? ColGreen : ColRed;

            statAccounts.Text = snaps.Count.ToString(CultureInfo.InvariantCulture);

            RenderRows(snaps, worst.AccountName);
            if (emptyNote != null) emptyNote.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// One definition of the accounts table's columns, used by both the
        /// heading row and every data row. They used to be declared separately
        /// and had already drifted, which is why the headings sat slightly left
        /// of the figures they described.
        /// </summary>
        private static Grid AccountsGrid()
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.7, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            return g;
        }

        /// <summary>
        /// Dollars left before today's loss limit is reached.
        ///
        /// Two things this is NOT, both of which it gets asked about.
        ///
        /// It is not the sum of your losing trades. Win 300, lose 200, lose 300,
        /// win 100 and you are down 100 for the day, not 500 - so 100 is what has
        /// come out of today's budget. Losers are only ever counted net against
        /// winners, which is why this reads off the day's P&L and never off the
        /// individual trades.
        ///
        /// And it does not GROW when you are winning. Up 500 on a 2,500 limit
        /// used to read 3,000 left, which is arithmetically true - you could fall
        /// 3,000 from there before the rule fires - and behaviourally poison. It
        /// tells a trader that a good morning has bought them a bigger afternoon
        /// to lose, in the same window that spends the rest of its space telling
        /// them not to hand back a green day. So it is capped: your daily loss
        /// limit is a fixed budget, it is the most you can lose today, and profit
        /// does not top it up.
        /// </summary>
        private static double RoomToday(DisciplineInput i)
        {
            if (i == null || i.DailyLossLimit <= 0) return 0;

            // Once the limit has been hit today there is no budget left to
            // report, whatever the P&L has done since. Winning some of it back
            // does not hand the day back - see DisciplineInput.DailyLossLimitHit.
            if (i.DailyLossLimitHit) return 0;

            double room = i.DailyLossLimit + i.DailyPnl;
            if (room > i.DailyLossLimit) room = i.DailyLossLimit;
            return room < 0 ? 0 : room;
        }

        private void RenderRows(List<AccountSnapshot> snaps, string worstName)
        {
            rowsPanel.Children.Clear();

            for (int i = 0; i < snaps.Count; i++)
            {
                AccountSnapshot s = snaps[i];

                Brush col = ColGreen;
                if (s.Decision.Urgency == Urgency.Caution) col = ColAmber;
                else if (s.Decision.Urgency == Urgency.Alert) col = ColRed;

                Border row = new Border();
                row.BorderBrush = s.AccountName == worstName ? col : ColLine;
                row.BorderThickness = new Thickness(s.AccountName == worstName ? 2 : 1);
                row.CornerRadius = new CornerRadius(5);
                row.Background = ColPanel;
                row.Padding = new Thickness(8, 5, 8, 5);
                row.Margin = new Thickness(0, 0, 0, 4);

                Grid g = AccountsGrid();

                string label;
                string shown = accountLabels.TryGetValue(s.AccountName, out label) && !string.IsNullOrEmpty(label)
                    ? s.AccountName + "  (" + label + ")"
                    : s.AccountName;
                g.Children.Add(Cell(shown, ColInk, 0, FontWeights.Bold));

                // Trades taken. The row used to show losses only, so a trader who
                // had set a max-trades rule had no way of seeing where they stood
                // against it - and reasonably concluded the setting did nothing.
                string tradesText = s.Input.MaxTrades > 0
                    ? s.Input.TradesToday + " / " + s.Input.MaxTrades
                    : s.Input.TradesToday.ToString(CultureInfo.InvariantCulture);
                Brush tradesCol = s.Input.MaxTrades > 0 && s.Input.TradesToday >= s.Input.MaxTrades
                    ? ColAmber : ColMuted;
                g.Children.Add(Cell(tradesText, tradesCol, 1, FontWeights.Normal));

                string lossText = s.Input.MaxLossesBeforeStop > 0
                    ? s.Input.LossesToday + " / " + s.Input.MaxLossesBeforeStop
                    : s.Input.LossesToday.ToString(CultureInfo.InvariantCulture);
                Brush lossCol = s.Input.MaxLossesBeforeStop > 0
                                && s.Input.LossesToday >= s.Input.MaxLossesBeforeStop
                    ? ColRed : ColMuted;
                g.Children.Add(Cell(lossText, lossCol, 2, FontWeights.Normal));

                // How much of today's budget is left. This is the number a trader
                // actually keeps in their head, and until now it was nowhere on
                // screen - only the raw P&L, which you then had to do arithmetic
                // against your own rule to make sense of.
                double room = RoomToday(s.Input);
                string roomText;
                Brush roomCol;
                if (s.Input.DailyLossLimit <= 0)
                {
                    roomText = "no limit set";
                    roomCol = ColFaint;
                }
                else if (room <= 0)
                {
                    roomText = s.Input.DailyLossLimitHit && s.Input.DailyPnl > -s.Input.DailyLossLimit
                        ? "spent earlier" : "spent";
                    roomCol = ColRed;
                }
                else
                {
                    roomText = Money(room);
                    roomCol = room < s.Input.DailyLossLimit * 0.34 ? ColAmber : ColMuted;
                }
                g.Children.Add(Cell(roomText, roomCol, 3, FontWeights.Normal));

                // Today's target, next to today's budget, because they are the
                // two ends of the same decision: this is the number that lets you
                // stop while you are ahead, and it was set on the Setup page with
                // nothing anywhere confirming it had taken.
                string targetText;
                Brush targetCol;
                if (s.Input.DailyTarget <= 0)
                {
                    targetText = "no target set";
                    targetCol = ColFaint;
                }
                else if (s.Input.DailyPnl >= s.Input.DailyTarget)
                {
                    targetText = "hit  " + Money(s.Input.DailyPnl);
                    targetCol = ColGreen;
                }
                else
                {
                    targetText = Money(s.Input.DailyPnl > 0 ? s.Input.DailyPnl : 0)
                               + " / " + Money(s.Input.DailyTarget);
                    targetCol = ColMuted;
                }
                g.Children.Add(Cell(targetText, targetCol, 4, FontWeights.Normal));

                string cushionText;
                Brush cushionCol;
                if (!s.Input.HasValidEquity)
                {
                    cushionText = "no data";
                    cushionCol = ColMuted;
                }
                else if (s.Input.PastFloor)
                {
                    // You cannot lose a negative amount. Say what is actually true.
                    cushionText = "past floor";
                    cushionCol = ColRed;
                }
                else
                {
                    cushionText = Money(s.Input.CushionToFloor)
                                + (s.Input.FloorLocked ? "  fixed" : "");
                    cushionCol = s.Input.CushionToFloor < 400 ? ColRed : ColMuted;
                }
                g.Children.Add(Cell(cushionText, cushionCol, 5, FontWeights.Normal));

                g.Children.Add(Cell(LongAction(s.Decision.Action), col, 6, FontWeights.Bold));

                StackPanel rowStack = new StackPanel();
                rowStack.Children.Add(g);

                // What THIS account is doing, in its own words. Without it only
                // the worst account said anything and the rest were four numbers.
                string warn = DisciplineEngine.RowWarning(s.Input, s.Decision);

                // Push it onto the shared board so a chart indicator can paint the
                // same message where the trader is actually looking.
                BallastState.Publish(s.AccountName, warn,
                    s.Decision.Urgency == Urgency.Alert ? 2 : s.Decision.Urgency == Urgency.Caution ? 1 : 0,
                    s.Decision.Headline,
                    s.Input.HasValidEquity ? s.Input.CushionToFloor : 0,
                    s.Input.HasValidEquity && !s.Input.PastFloor,
                    Core.Globals.Now);

                // The same running count the row shows, so the chart can show it
                // too. A calm chart used to say "BALLAST OK" and nothing else,
                // which is why editing an account's rules appeared to have no
                // effect on it whatsoever.
                BallastState.PublishCount(s.AccountName,
                    s.Input.TradesToday, s.Input.MaxTrades,
                    s.Input.LossesToday, s.Input.MaxLossesBeforeStop,
                    room, s.Input.DailyLossLimit,
                    s.Input.DailyPnl, s.Input.DailyTarget, Core.Globals.Now);

                if (warn.Length > 0 && warn != "clear")
                {
                    TextBlock wt = new TextBlock();
                    wt.Text = warn;
                    wt.Foreground = s.Decision.Urgency == Urgency.Alert ? ColRed
                                  : s.Decision.Urgency == Urgency.Caution ? ColAmber : ColMuted;
                    wt.FontSize = 11;
                    wt.TextWrapping = TextWrapping.Wrap;
                    wt.Margin = new Thickness(0, 4, 0, 0);
                    rowStack.Children.Add(wt);
                }

                // A configuration that gives the account more room than the firm
                // does. Shown here as well as in Setup, because Setup is a page
                // nobody opens twice and this is the number being trusted.
                string mismatch = "";
                try
                {
                    BallastTracker cfgT = monitor.Get(s.AccountName);
                    if (cfgT != null)
                        mismatch = ruleBook.SanityWarning(s.AccountName, cfgT.Config,
                                                         PlatformOf(FindAccount(s.AccountName)));
                }
                catch { }

                if (mismatch.Length > 0)
                {
                    TextBlock mm = new TextBlock();
                    mm.Text = "check this: " + mismatch;
                    mm.Foreground = ColRed;
                    mm.FontSize = 10;
                    mm.TextWrapping = TextWrapping.Wrap;
                    mm.Margin = new Thickness(0, 3, 0, 0);
                    rowStack.Children.Add(mm);
                }

                // Progress toward passing, for an evaluation that has one.
                //
                // This is where the firm's profit target belongs. It used to be
                // written into the daily target, where it did nothing but break
                // the protect-your-green logic; here it is what a trader on an
                // evaluation actually wants to know.
                if (s.Input.ProfitTarget > 0 && s.Input.HasValidEquity && !s.Input.PastFloor)
                {
                    double made = s.Input.CurrentEquity - s.Input.StartingBalance;
                    TextBlock pt = new TextBlock();

                    if (made >= s.Input.ProfitTarget)
                    {
                        pt.Text = "target met - " + Money(made) + " of " + Money(s.Input.ProfitTarget)
                                + " - check your firm's remaining conditions before you request a payout";
                        pt.Foreground = ColGreen;
                    }
                    else
                    {
                        pt.Text = "to pass: " + Money(made > 0 ? made : 0) + " of "
                                + Money(s.Input.ProfitTarget) + "  -  "
                                + Money(s.Input.ProfitTarget - (made > 0 ? made : 0)) + " to go";
                        pt.Foreground = ColMuted;
                    }

                    pt.FontSize = 10;
                    pt.TextWrapping = TextWrapping.Wrap;
                    pt.Margin = new Thickness(0, 3, 0, 0);
                    rowStack.Children.Add(pt);
                }

                // The throttled size, spelled out under the row. Advising a
                // smaller size silently would be useless - the number has to be
                // visible before the order is placed, not implied.
                if (s.Input.SizeThrottled)
                {
                    TextBlock th = new TextBlock();
                    th.Text = "max " + s.Input.MaxContracts
                            + (s.Input.MaxContracts == 1 ? " contract" : " contracts")
                            + " now, down from " + s.Input.BaseMaxContracts
                            + " - this account has spent part of its drawdown";
                    th.Foreground = ColAmber;
                    th.FontSize = 10;
                    th.TextWrapping = TextWrapping.Wrap;
                    th.Margin = new Thickness(0, 3, 0, 0);
                    rowStack.Children.Add(th);
                }

                row.Child = rowStack;
                rowsPanel.Children.Add(row);
            }
        }

        /// <summary>
        /// What to do, in words. The row used to carry a four-letter code - LOCK,
        /// STOP, BANK, WAIT, SIZE - under a heading reading "DO". Between them
        /// they managed to make the most important cell on the row unreadable.
        /// </summary>
        private static string LongAction(DisciplineAction a)
        {
            switch (a)
            {
                case DisciplineAction.Lockout:      return "STOP NOW";
                case DisciplineAction.StopForDay:   return "DONE TODAY";
                case DisciplineAction.ProtectGreen: return "BANK IT";
                case DisciplineAction.Cooldown:     return "WAIT";
                case DisciplineAction.SizeDown:     return "SIZE DOWN";
                case DisciplineAction.None:         return "HOLD OFF";
                default:                            return "CLEAR";
            }
        }

        private static string ShortAction(DisciplineAction a)
        {
            switch (a)
            {
                case DisciplineAction.Lockout:      return "LOCK";
                case DisciplineAction.StopForDay:   return "STOP";
                case DisciplineAction.ProtectGreen: return "BANK";
                case DisciplineAction.Cooldown:     return "WAIT";
                case DisciplineAction.SizeDown:     return "SIZE";
                case DisciplineAction.None:         return "HOLD";
                default:                            return "OK";
            }
        }

        private TextBlock Cell(string text, Brush brush, int col, FontWeight weight)
        {
            TextBlock t = new TextBlock();
            t.Text = text; t.Foreground = brush; t.FontSize = 11;
            t.FontWeight = weight;
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(t, col);
            return t;
        }

        private static string Money(double n)
        {
            double r = Math.Round(n);
            return (r < 0 ? "-$" : "$") + Math.Abs(r).ToString("N0", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// "Stop - ..." becomes "stop - ..." when it follows an account name, so
        /// the headline reads as one sentence rather than two collided ones.
        /// </summary>
        private static string LowerFirst(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return char.ToLowerInvariant(s[0]) + s.Substring(1);
        }

        // ── Settings ─────────────────────────────────────────────────────────

        private static double ParseD(TextBox tb, double fallback)
        {
            double v;
            if (tb != null && double.TryParse(tb.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out v)) return v;
            return fallback;
        }

        private static int ParseI(TextBox tb, int fallback)
        {
            int v;
            if (tb != null && int.TryParse(tb.Text, out v)) return v;
            return fallback;
        }

        private void LoadConfigIntoFields(TrackerConfig c)
        {
            tbBalance.Text      = c.StartingBalance.ToString(CultureInfo.InvariantCulture);
            tbDrawdown.Text     = c.TrailingDrawdown.ToString(CultureInfo.InvariantCulture);
            tbMaxLosses.Text    = c.MaxLossesBeforeStop.ToString(CultureInfo.InvariantCulture);
            tbDailyLoss.Text    = c.DailyLossLimit.ToString(CultureInfo.InvariantCulture);
            tbTarget.Text       = c.DailyTarget.ToString(CultureInfo.InvariantCulture);
            tbMaxTrades.Text    = c.MaxTrades.ToString(CultureInfo.InvariantCulture);
            tbMaxContracts.Text = (c.BaseMaxContracts > 0 ? c.BaseMaxContracts : c.MaxContracts)
                                    .ToString(CultureInfo.InvariantCulture);
            tbLockAt.Text       = c.LockFloorAt.ToString(CultureInfo.InvariantCulture);
            if (automatedBox != null) automatedBox.IsChecked = c.IsAutomated;
            if (trustRealisedBox != null) trustRealisedBox.IsChecked = c.TrustAccountRealised;
            if (acctGenBox != null) acctGenBox.SelectedIndex = (int)c.Generation;
            ddTypeBox.SelectedIndex = c.DrawdownType == DrawdownType.EndOfDay ? 1 : 0;

            // No window at all is stored as start == end. The boxes keep showing
            // the last real times so that un-ticking "whenever I like" hands back
            // the window the trader had, rather than 00:00 to 00:00.
            bool anyTime = c.SessionStartMinute == c.SessionEndMinute;
            if (windowAnyTimeBox != null) windowAnyTimeBox.IsChecked = anyTime;
            if (tbWindowStart != null && !anyTime)
                tbWindowStart.Text = DisciplineEngine.HourMinute(c.SessionStartMinute);
            if (tbWindowEnd != null && !anyTime)
                tbWindowEnd.Text = DisciplineEngine.HourMinute(c.SessionEndMinute);

            // The sentence above the fold has to move with the fields under it.
            RefreshFirmSummary(c);
        }

        private void ReadFieldsInto(TrackerConfig c)
        {
            c.StartingBalance     = ParseD(tbBalance, c.StartingBalance);
            c.TrailingDrawdown    = ParseD(tbDrawdown, c.TrailingDrawdown);
            c.MaxLossesBeforeStop = ParseI(tbMaxLosses, c.MaxLossesBeforeStop);
            c.DailyLossLimit      = ParseD(tbDailyLoss, c.DailyLossLimit);
            c.DailyTarget         = ParseD(tbTarget, c.DailyTarget);
            c.MaxTrades           = ParseI(tbMaxTrades, c.MaxTrades);
            c.MaxContracts        = ParseI(tbMaxContracts, c.MaxContracts);
            // A hand-typed size replaces the profile's base, or the throttle would
            // keep counting down from a number the trader has already overridden.
            c.BaseMaxContracts    = c.MaxContracts;
            c.LockFloorAt         = ParseD(tbLockAt, c.LockFloorAt);
            if (automatedBox != null) c.IsAutomated = automatedBox.IsChecked == true;
            if (trustRealisedBox != null) c.TrustAccountRealised = trustRealisedBox.IsChecked == true;
            if (acctGenBox != null && acctGenBox.SelectedIndex >= 0)
                c.Generation = (AccountGeneration)acctGenBox.SelectedIndex;
            c.DrawdownType        = ddTypeBox.SelectedIndex == 1 ? DrawdownType.EndOfDay : DrawdownType.Intraday;

            // A time that cannot be read is left alone rather than defaulted. A
            // typo silently becoming 00:00 would put a trader outside their own
            // window all day and give them no clue why.
            if (windowAnyTimeBox != null && windowAnyTimeBox.IsChecked == true)
            {
                c.SessionStartMinute = 0;
                c.SessionEndMinute = 0;
            }
            else
            {
                int ws = DisciplineEngine.ParseHourMinute(tbWindowStart == null ? null : tbWindowStart.Text);
                int we = DisciplineEngine.ParseHourMinute(tbWindowEnd == null ? null : tbWindowEnd.Text);
                if (ws >= 0) c.SessionStartMinute = ws;
                if (we >= 0) c.SessionEndMinute = we;

                // Both ends the same would mean "no window", which is not what
                // un-ticking the box asked for. Nudge it to a real minute so the
                // setting says what the trader meant.
                if (c.SessionStartMinute == c.SessionEndMinute)
                    c.SessionEndMinute = (c.SessionStartMinute + 1) % 1440;
            }
        }

        private void ApplyEdits()
        {
            if (EditingDefault())
            {
                ReadFieldsInto(monitor.DefaultConfig);
            }
            else
            {
                BallastTracker t = monitor.Get(editTargetBox.SelectedItem as string);
                if (t != null) ReadFieldsInto(t.Config);
            }
        }

        private void OnApplyAndSave()
        {
            ApplyEdits();
            editingKey = CurrentEditKey();
            SaveSettings();
            RefreshCoherence();
            RefreshAccountList(true);
            DescribeAccount(CurrentEditKey());

            TrackerConfig c = ConfigForKey(CurrentEditKey());
            string who = CurrentEditKey();
            if (applyNote != null)
            {
                if (c == null)
                {
                    applyNote.Text = "Nothing to save - tick an account first.";
                    applyNote.Foreground = ColAmber;
                }
                else
                {
                    applyNote.Text = "Saved" + (who.Length > 0 ? " to " + who : " to the defaults")
                                   + ": " + LimitsSentence(c)
                                   + ". Check the line under "
                                   + (who.Length > 0 ? who : "each account")
                                   + " at the top of this page - it now says the same thing.";
                    applyNote.Foreground = ColAccent;
                }
            }
        }

        /// <summary>
        /// Push what is on screen onto every watched account. Deliberately named
        /// for what it does: it makes them all identical, which is the opposite
        /// of per-account rules and used to be one unlabelled click away.
        ///
        /// It also used to copy the wrong thing. Whatever you had typed was
        /// applied to the account you were editing, and then the DEFAULT config -
        /// not your edit - was copied over every account, including the one you
        /// had just typed into.
        /// </summary>
        private void OnCopyToAll()
        {
            ApplyEdits();

            TrackerConfig source = ConfigForKey(CurrentEditKey());
            if (source == null) source = monitor.DefaultConfig;

            List<string> names = monitor.MonitoredNames;
            for (int i = 0; i < names.Count; i++)
            {
                BallastTracker t = monitor.Get(names[i]);
                if (t == null) continue;

                TrackerConfig n = BallastMonitor.CloneConfig(source);

                // The firm's own facts stay with the account they belong to. A
                // 50K evaluation does not become a 250K because a 250K was on
                // screen when this was pressed - copying those would hand a
                // trader a cushion figure that is wrong by $4,000 and looks fine.
                n.StartingBalance   = t.Config.StartingBalance;
                n.TrailingDrawdown  = t.Config.TrailingDrawdown;
                n.DrawdownType      = t.Config.DrawdownType;
                n.LockFloorAt       = t.Config.LockFloorAt;
                n.ProfitTarget      = t.Config.ProfitTarget;
                n.Generation        = t.Config.Generation;
                n.FirmMaxContracts  = t.Config.FirmMaxContracts;
                n.FirmDailyLossLimit = t.Config.FirmDailyLossLimit;
                n.IsAutomated       = t.Config.IsAutomated;

                // And the firm's caps still bind the copied numbers.
                if (n.FirmMaxContracts > 0)
                {
                    if (n.MaxContracts > n.FirmMaxContracts) n.MaxContracts = n.FirmMaxContracts;
                    if (n.BaseMaxContracts > n.FirmMaxContracts) n.BaseMaxContracts = n.FirmMaxContracts;
                }
                if (n.FirmDailyLossLimit > 0
                    && (n.DailyLossLimit <= 0 || n.DailyLossLimit > n.FirmDailyLossLimit))
                    n.DailyLossLimit = n.FirmDailyLossLimit;

                t.Config = n;
            }

            monitor.DefaultConfig = BallastMonitor.CloneConfig(source);

            SaveSettings();
            RefreshAccountList(true);

            if (applyNote != null)
            {
                applyNote.Text = "Every watched account now runs " + LimitsSentence(source)
                               + ". Each keeps its own size, drawdown and floor - those belong to the "
                               + "firm, not to you.";
                applyNote.Foreground = ColAmber;
            }
        }

        /// <summary>
        /// Do the trader's own numbers agree with each other?
        ///
        /// Four settings describe one decision, and it is entirely possible to
        /// set them so that three of them can never fire. If your typical loss at
        /// your size cap times your max-losses rule comes to $3,900 and your
        /// daily limit is $3,000, the daily limit is your real rule and the loss
        /// streak is decoration. Worth knowing before a bad Tuesday rather than
        /// during one.
        ///
        /// Everything here is measured against the DRAWDOWN, because on a prop
        /// account that is the capital. A daily limit worth half the drawdown
        /// means two red days end it, and that is a fact about the arithmetic
        /// rather than an opinion about the trader.
        /// </summary>
        /// <summary>
        /// Say what time it is by Ballast's reckoning, and whether that is inside
        /// the window on screen. Cheap, and it turns an argument into a fact.
        /// </summary>
        private void RefreshWindowClock()
        {
            if (windowClock == null) return;

            try
            {
                DateTime now;
                try { now = Core.Globals.Now; } catch { now = DateTime.Now; }

                int mins = now.Hour * 60 + now.Minute;

                bool anyTime = windowAnyTimeBox != null && windowAnyTimeBox.IsChecked == true;
                int start = DisciplineEngine.ParseHourMinute(tbWindowStart == null ? null : tbWindowStart.Text);
                int end = DisciplineEngine.ParseHourMinute(tbWindowEnd == null ? null : tbWindowEnd.Text);

                string clock = "Ballast's clock says " + DisciplineEngine.HourMinute(mins)
                             + " - this is NinjaTrader's time zone, the same one your charts use. ";

                if (anyTime || start < 0 || end < 0)
                {
                    windowClock.Text = clock + "No window is set, so nothing is said about the clock.";
                    windowClock.Foreground = ColMuted;
                    return;
                }

                bool inside = DisciplineEngine.InSessionWindow(mins, start, end);
                windowClock.Text = clock + (inside
                    ? "That is inside " + DisciplineEngine.WindowLabel(start, end) + "."
                    : "That is OUTSIDE " + DisciplineEngine.WindowLabel(start, end)
                      + ". If that looks wrong, NinjaTrader's clock is not the one you are reading "
                      + "the times off - check Tools, Options, General, Time zone.");
                windowClock.Foreground = inside ? ColMuted : ColAmber;
            }
            catch { windowClock.Text = ""; }
        }

        private void RefreshCoherence()
        {
            if (coherenceNote == null) return;

            try
            {
                TrackerConfig c = ConfigForKey(CurrentEditKey());
                if (c == null) { coherenceNote.Text = ""; return; }

                double daily = ParseD(tbDailyLoss, c.DailyLossLimit);
                int maxLosses = ParseI(tbMaxLosses, c.MaxLossesBeforeStop);
                int size = ParseI(tbMaxContracts, c.BaseMaxContracts > 0 ? c.BaseMaxContracts : c.MaxContracts);
                double drawdown = ParseD(tbDrawdown, c.TrailingDrawdown);

                StringBuilder sb = new StringBuilder();
                bool warn = false;

                // Against the account's whole life.
                if (daily > 0 && drawdown > 0)
                {
                    double pct = daily / drawdown * 100.0;
                    double days = drawdown / daily;

                    sb.Append(Money(daily)).Append(" is ").Append(pct.ToString("0"))
                      .Append("% of this account's ").Append(Money(drawdown))
                      .Append(" drawdown - the drawdown is the real capital here, not the balance. ");

                    if (days < 3)
                    {
                        sb.Append("Two red days end it.");
                        warn = true;
                    }
                    else
                    {
                        sb.Append("It survives ").Append(Math.Floor(days).ToString("0"))
                          .Append(" red days in a row.");
                    }
                }

                // Against the trader's own losses.
                List<double> costs = StopCosts(CurrentEditKey());
                if (costs.Count < 3) costs = StopCosts("");

                if (costs.Count >= 3 && size > 0 && maxLosses > 0 && daily > 0)
                {
                    costs.Sort();
                    double perContract = costs[costs.Count / 2];
                    double typical = perContract * size;
                    double streak = typical * maxLosses;

                    sb.Append("\n\nYour typical loss has been ").Append(Money(perContract))
                      .Append(" a contract, so ").Append(size)
                      .Append(size == 1 ? " contract" : " contracts").Append(" is about ")
                      .Append(Money(typical)).Append(" a trade and ").Append(maxLosses)
                      .Append(maxLosses == 1 ? " loss is " : " losses is ").Append(Money(streak))
                      .Append(". ");

                    if (streak < daily * 0.8)
                    {
                        sb.Append("You will always be stopped by the loss count first - the daily "
                                + "limit above never gets a chance to bind.");
                        warn = true;
                    }
                    else if (streak > daily * 1.25)
                    {
                        sb.Append("The daily limit will stop you after about ")
                          .Append(Math.Floor(daily / typical).ToString("0"))
                          .Append(", so the loss count above never gets a chance to bind.");
                        warn = true;
                    }
                    else
                    {
                        sb.Append("Those two agree, which is what you want.");
                    }
                }
                else if (costs.Count > 0)
                {
                    sb.Append("\n\nToo few losing trades on record yet to check these against what "
                            + "your losses actually cost. Ballast will start saying so once there "
                            + "are a few.");
                }

                coherenceNote.Text = sb.ToString();
                coherenceNote.Foreground = warn ? ColAmber : ColMuted;
            }
            catch { coherenceNote.Text = ""; }
        }

        /// <summary>
        /// Put the account's own figure for today next to Ballast's, so a wrong
        /// setting is obvious rather than theoretical. If a feed reports realised
        /// P&L cumulatively rather than per session, this line says so in one
        /// glance - the number will be nothing like the trader's day.
        /// </summary>
        private void RefreshRealisedNote()
        {
            if (realisedNote == null) return;

            try
            {
                string name = CurrentEditKey();
                if (name.Length == 0)
                {
                    realisedNote.Text = "Pick an account above to see what it is reporting.";
                    realisedNote.Foreground = ColFaint;
                    return;
                }

                Account a = FindAccount(name);
                BallastTracker t = monitor.Get(name);
                if (a == null || t == null)
                {
                    realisedNote.Text = name + " is not connected, so there is nothing to compare yet.";
                    realisedNote.Foreground = ColFaint;
                    return;
                }

                double realised = SafeGet(a, AccountItem.RealizedProfitLoss);

                DateTime now;
                try { now = Core.Globals.Now; } catch { now = DateTime.Now; }

                double accounted = 0;
                int rows = 0;
                List<BallastTrade> today = monitor.Journal.ForDay(now);
                for (int i = 0; i < today.Count; i++)
                {
                    if (today[i] == null) continue;
                    if (!string.Equals(today[i].AccountName, name, StringComparison.OrdinalIgnoreCase)) continue;
                    accounted += today[i].Pnl;
                    rows++;
                }

                realisedNote.Text = name + " reports " + Money(realised)
                                  + " realised. Ballast is counting " + Money(t.DailyPnl)
                                  + " for today from " + rows
                                  + (rows == 1 ? " journal row" : " journal rows")
                                  + " worth " + Money(accounted) + ". These should match your "
                                  + "platform's Accounts tab - if they do not, the box above is the "
                                  + "reason.";
                realisedNote.Foreground = Math.Abs(realised - t.DailyPnl) < 1 ? ColMuted : ColAmber;
            }
            catch { realisedNote.Text = ""; }
        }

        /// <summary>
        /// What this trader's losing trades have actually cost per contract.
        ///
        /// "What does one contract's stop cost you?" is a fair question with an
        /// unfair answer when the stop moves with the setup - a trader running
        /// several ATM templates has no single number, and the field looked like
        /// it wanted one. Their own journal already knows, so Ballast works it
        /// out instead of asking them to average it in their head.
        ///
        /// Median rather than mean, because one runaway loss - a stop that was
        /// never there, a fill through a news print - would drag an average up
        /// and quietly halve the size this recommends.
        /// </summary>
        private void RefreshStopCostHint()
        {
            if (stopCostHint == null) return;

            try
            {
                string only = CurrentEditKey();
                List<double> costs = StopCosts(only);

                // Too few on this account to say anything honest about it: fall
                // back to every account rather than quote a sample of one.
                if (costs.Count < 3 && only.Length > 0)
                {
                    only = "";
                    costs = StopCosts("");
                }

                if (costs.Count == 0)
                {
                    stopCostHint.Text = "Leave it at 0 if you are not sure - nothing here changes "
                                      + "until you press \"Use this starting point\". Once the journal "
                                      + "has a few losing trades in it, Ballast will tell you what "
                                      + "yours have actually cost.";
                    stopCostHint.Foreground = ColFaint;
                    return;
                }

                costs.Sort();
                double typical = costs[costs.Count / 2];
                double worst = costs[costs.Count - 1];

                stopCostHint.Text = "Your own trades: " + costs.Count
                                  + (costs.Count == 1 ? " losing trade" : " losing trades")
                                  + (only.Length > 0 ? " on " + only : " across your accounts")
                                  + ". A typical one cost " + Money(typical)
                                  + " per contract; the worst cost " + Money(worst)
                                  + ". A full stop is usually nearer the worst of those than the "
                                  + "typical one, because a typical loss includes the ones you cut "
                                  + "early.";
                stopCostHint.Foreground = ColMuted;
            }
            catch { stopCostHint.Text = ""; }
        }

        /// <summary>
        /// Per-contract cost of every losing round trip, for one account or for
        /// all of them. Bot accounts are left out of the all-accounts figure: a
        /// strategy that took four hundred scratches would decide the median for
        /// a trader who took three trades by hand.
        /// </summary>
        private List<double> StopCosts(string account)
        {
            List<double> list = new List<double>();

            List<BallastTrade> all = monitor.Journal.All;
            for (int i = 0; i < all.Count; i++)
            {
                BallastTrade e = all[i];
                if (e == null || e.Pnl >= 0 || e.MaxContracts <= 0) continue;

                if (account.Length > 0)
                {
                    if (!string.Equals(e.AccountName, account, StringComparison.OrdinalIgnoreCase))
                        continue;
                }
                else if (IsAutomatedAccount(e.AccountName)) continue;

                list.Add(-e.Pnl / e.MaxContracts);
            }
            return list;
        }

        private bool IsAutomatedAccount(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            BallastTracker t = monitor.Get(name);
            if (t != null && t.Config != null) return t.Config.IsAutomated;

            TrackerConfig kept = monitor.RememberedConfig(name);
            return kept != null && kept.IsAutomated;
        }

        /// <summary>The four numbers the trader actually chose, in one clause.</summary>
        private string LimitsSentence(TrackerConfig c)
        {
            if (c == null) return "nothing";

            string daily = c.DailyLossLimit > 0 ? "stop at " + Money(c.DailyLossLimit) + " down"
                                                : "no daily loss limit";
            string target = c.DailyTarget > 0 ? "target " + Money(c.DailyTarget) : "no target";

            return daily + ", " + c.MaxTrades + (c.MaxTrades == 1 ? " trade" : " trades")
                 + ", stop after " + c.MaxLossesBeforeStop
                 + (c.MaxLossesBeforeStop == 1 ? " loss" : " losses in a row")
                 + ", " + target
                 + ", trading " + DisciplineEngine.WindowLabel(c.SessionStartMinute, c.SessionEndMinute);
        }

        // ── Risk profiles ────────────────────────────────────────────────────

        private RiskProfile SelectedProfile()
        {
            int idx = profileBox.SelectedIndex - 1;   // 0 is the placeholder
            if (idx < 0) return null;

            List<RiskProfile> all = RiskProfiles.All();
            return idx < all.Count ? all[idx] : null;
        }

        /// <summary>
        /// Show what the chosen profile would actually do to THIS account, in
        /// dollars, before it is applied. A percentage means nothing until it is
        /// turned into the number that will stop you trading.
        /// </summary>
        private void ShowProfileDetail()
        {
            RiskProfile p = SelectedProfile();
            if (p == null)
            {
                profileDetail.Text = "Pick a starting point and Ballast will work the dollar figures "
                                   + "out from this account's own trailing drawdown.";
                profileDetail.Foreground = ColMuted;
                return;
            }

            TrackerConfig basis = EditingDefault()
                ? monitor.DefaultConfig
                : (monitor.Get(editTargetBox.SelectedItem as string) != null
                    ? monitor.Get(editTargetBox.SelectedItem as string).Config
                    : monitor.DefaultConfig);

            double dd = basis.TrailingDrawdown;

            string sb = p.Summary + "\n\nSource: " + p.Source;

            if (dd > 0)
            {
                double perTrade = dd * p.RiskPctOfDrawdown / 100.0;
                double daily = RiskProfiles.Round25(dd * p.DailyLossPctOfDrawdown / 100.0);

                sb += "\n\nOn this account (drawdown " + Money(dd) + "): risk about "
                    + Money(perTrade) + " per trade, stop for the day at " + Money(daily)
                    + ", target " + Money(RiskProfiles.Round25(daily * p.TargetMultiple))
                    + ", stop after " + p.MaxLossesBeforeStop
                    + (p.MaxLossesBeforeStop == 1 ? " loss" : " losses")
                    + ", max " + p.MaxTrades + " trades.";

                if (p.HasThrottle)
                    sb += " Your contract cap drops " + p.ThrottleCutPct.ToString("0")
                        + "% for every " + p.ThrottleStepPct.ToString("0")
                        + "% of the drawdown you spend.";
            }
            else
            {
                sb += "\n\nSet this account's trailing drawdown first - every figure here is worked "
                    + "out from it.";
            }

            profileDetail.Text = sb;
            profileDetail.Foreground = ColMuted;
        }

        private void ApplyProfile(bool toAll)
        {
            RiskProfile p = SelectedProfile();
            if (p == null)
            {
                detectionNote.Text = "Pick a starting point from the dropdown first.";
                detectionNote.Foreground = ColAmber;
                return;
            }

            double riskPerContract = ParseD(tbRiskPerTrade, 0);

            if (toAll)
            {
                List<string> names = monitor.MonitoredNames;
                for (int i = 0; i < names.Count; i++)
                {
                    BallastTracker t = monitor.Get(names[i]);
                    if (t != null) t.Config = ApplyOne(p, t.Config, riskPerContract);
                }
                monitor.DefaultConfig = ApplyOne(p, monitor.DefaultConfig, riskPerContract);

                detectionNote.Text = "Applied \"" + p.Name + "\" to " + names.Count
                                   + (names.Count == 1 ? " account" : " accounts")
                                   + ". Each got figures based on its own drawdown, so they differ. "
                                   + "Verify against your firm before trusting them.";
            }
            else if (EditingDefault())
            {
                monitor.DefaultConfig = ApplyOne(p, monitor.DefaultConfig, riskPerContract);
                detectionNote.Text = "Applied \"" + p.Name + "\" to the default settings.";
            }
            else
            {
                string name = editTargetBox.SelectedItem as string;
                BallastTracker t = monitor.Get(name);
                if (t != null) t.Config = ApplyOne(p, t.Config, riskPerContract);
                detectionNote.Text = "Applied \"" + p.Name + "\" to " + name + ".";
            }

            detectionNote.Foreground = ColMuted;
            OnEditTargetChanged();
            ShowProfileDetail();
            SaveSettings();
            RefreshAccountList(true);
        }

        private static TrackerConfig ApplyOne(RiskProfile p, TrackerConfig c, double riskPerContract)
        {
            TrackerConfig n = RiskProfiles.Apply(p, c, riskPerContract);
            // Remember the un-throttled size so the throttle has something to
            // count down from and something to restore to.
            n.BaseMaxContracts = n.MaxContracts;
            return n;
        }

        // ── Journal ──────────────────────────────────────────────────────────

        /// <summary>Where chart photographs live, beside the journal CSV.</summary>
        private string ImageRoot()
        {
            try { return Path.Combine(Core.Globals.UserDataDir, "ballast-charts"); }
            catch { return "ballast-charts"; }
        }

        /// <summary>
        /// Give a tracker the ability to photograph charts. Passed in as a
        /// delegate rather than referenced directly, so the tracker keeps knowing
        /// nothing about NinjaTrader and stays unit-testable.
        /// </summary>
        private void WireCapture(BallastTracker t, string accountName)
        {
            if (t == null) return;
            string acct = accountName;
            t.CaptureChart = delegate(string instrument, DateTime when, bool isEntry)
            {
                return ChartSnapshot.Capture(ImageRoot(), acct, instrument, when, isEntry);
            };
        }

        private string SessionPath()
        {
            try { return Path.Combine(Core.Globals.UserDataDir, "ballast-session.txt"); }
            catch { return "ballast-session.txt"; }
        }

        /// <summary>
        /// Write down what each account's day is being measured from, so closing
        /// Ballast does not lose the trades taken while it is shut.
        ///
        /// Ballast measures a day as the change in the account's realised P&L
        /// since a baseline taken when the session opened. Reopening used to take
        /// a fresh baseline from wherever the account happened to be, so anything
        /// traded in between simply was not in the day's figure - and since the
        /// journal seed can only restore what Ballast SAW, it could not put it
        /// back either. The trader was shown a smaller loss than they had taken
        /// and more room than they had left.
        ///
        /// Saving the baseline itself fixes it without Ballast needing to know
        /// anything about the trade: if the measurement starts from the same
        /// point it started from this morning, everything since is inside it.
        /// </summary>
        private void SaveSessionState()
        {
            try
            {
                DateTime now;
                try { now = Core.Globals.Now; } catch { now = DateTime.Now; }

                List<string> lines = new List<string>();
                lines.Add("*SESSION*|1");

                foreach (string name in monitor.MonitoredNames)
                {
                    BallastTracker t = monitor.Get(name);
                    if (t == null || t.SessionDate != now.Date) continue;

                    lines.Add(string.Join("|", new string[] {
                        name.Replace("|", "/"),
                        now.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                        t.SessionStartRealised.ToString(CultureInfo.InvariantCulture),
                        t.PeakEquity.ToString(CultureInfo.InvariantCulture),
                        t.PeakDailyPnl.ToString(CultureInfo.InvariantCulture),
                        t.WorstDailyPnl.ToString(CultureInfo.InvariantCulture),
                        t.DailyLossLimitHit ? "1" : "0",
                        now.ToString("HHmm", CultureInfo.InvariantCulture)
                    }));
                }

                File.WriteAllLines(SessionPath(), lines.ToArray());
            }
            catch { }
        }

        private void LoadSessionState()
        {
            try
            {
                string p = SessionPath();
                if (!File.Exists(p)) return;

                DateTime now;
                try { now = Core.Globals.Now; } catch { now = DateTime.Now; }
                string today = now.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

                string[] lines = File.ReadAllLines(p);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i] == null || lines[i].StartsWith("*")) continue;

                    string[] f = lines[i].Split('|');
                    if (f.Length < 7) continue;

                    // Only today's. Yesterday's baseline would describe a day that
                    // is over, and applying it would make this morning look like
                    // a continuation of last night.
                    if (f[1] != today) continue;

                    BallastTracker t = monitor.Get(f[0]);
                    if (t == null) continue;

                    double start, peakEq, peak, worst;
                    if (!double.TryParse(f[2], NumberStyles.Any, CultureInfo.InvariantCulture, out start)) continue;
                    double.TryParse(f[3], NumberStyles.Any, CultureInfo.InvariantCulture, out peakEq);
                    double.TryParse(f[4], NumberStyles.Any, CultureInfo.InvariantCulture, out peak);
                    double.TryParse(f[5], NumberStyles.Any, CultureInfo.InvariantCulture, out worst);

                    t.SeedSession(now.Date, start, peakEq, peak, worst, f[6] == "1");

                    int hhmm;
                    if (f.Length > 7 && int.TryParse(f[7], NumberStyles.Integer,
                                                     CultureInfo.InvariantCulture, out hhmm)
                        && hhmm >= 0 && hhmm <= 2359)
                        lastSeenAt[f[0]] = now.Date.AddHours(hhmm / 100).AddMinutes(hhmm % 100);

                }
            }
            catch { }
        }

        private string JournalPath()
        {
            try { return Path.Combine(Core.Globals.UserDataDir, "ballast-journal.csv"); }
            catch { return "ballast-journal.csv"; }
        }

        private void LoadJournal()
        {
            monitor.Journal.Load(JournalPath());

            // A journal written before reconstructed rows were folded together
            // may hold several describing the same gap. Now that one of them
            // counts as a trade, several of them is an account at a limit its
            // owner never went near - so they are collapsed once, here.
            if (monitor.Journal.ConsolidateReconstructed() > 0) journalDirty = true;

            // Recover today's plan from the trades it was stamped on, so reopening
            // the window mid-session doesn't lose it. Yesterday's plan is not
            // carried forward on purpose: a plan you didn't write this morning is
            // one you haven't actually committed to.
            List<BallastTrade> today = monitor.Journal.ForDay(Core.Globals.Now);
            for (int i = today.Count - 1; i >= 0; i--)
            {
                if (today[i].SessionPlan.Length > 0)
                {
                    monitor.Journal.SessionPlan = today[i].SessionPlan;
                    break;
                }
            }
            tbSessionPlan.Text = monitor.Journal.SessionPlan;
            SeedTodaysCounts(today);
            LoadStandingPlan();
            LoadEvents();
            RenderJournal();
        }

        /// <summary>
        /// Give every tracker back today's trade and loss count from the journal.
        ///
        /// Without this, closing and reopening the Ballast window resets both to
        /// zero, and the max-trades rule, the loss-streak stop and the tilt
        /// lockout all quietly start again from nothing. A discipline rule that a
        /// trader can clear by closing a window is not a rule - and it would be
        /// cleared, because closing the window is exactly what someone does when
        /// they do not like what it says.
        /// </summary>
        private void SeedTodaysCounts(List<BallastTrade> today)
        {
            if (today == null) return;

            DateTime day;
            try { day = Core.Globals.Now.Date; } catch { day = DateTime.Now.Date; }

            Dictionary<string, int> trades = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> losses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, DateTime> lastLoss = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, bool> lastWasLoss = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, DateTime> lastExit = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, double> pnl = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, double> worst = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            // In the order they actually closed, so the running total passes
            // through the same low point the trader lived through. Out of order,
            // the trough is meaningless - and the trough is what says whether the
            // daily loss limit was hit this morning.
            today = new List<BallastTrade>(today);
            today.Sort(delegate(BallastTrade a, BallastTrade b)
            {
                if (a == null) return b == null ? 0 : -1;
                if (b == null) return 1;
                return a.ExitTime.CompareTo(b.ExitTime);
            });

            for (int i = 0; i < today.Count; i++)
            {
                BallastTrade e = today[i];
                if (e == null || string.IsNullOrEmpty(e.AccountName)) continue;

                string a = e.AccountName;

                double p;
                double running = pnl.TryGetValue(a, out p) ? p + e.Pnl : e.Pnl;
                pnl[a] = running;

                double w;
                if (!worst.TryGetValue(a, out w) || running < w) worst[a] = running < 0 ? running : 0;

                // A reconstructed row counts as ONE trade, and as one loss if it
                // lost. Not none, and never more than one.
                //
                // Both extremes have now been wrong in production. Counting every
                // row meant a day of restarts showed five trades against a limit
                // of five and three losses against a limit of three, and Ballast
                // said STOP on the strength of its own bookkeeping. Counting none
                // meant a trader who had taken two losing trades was told he had
                // taken one, against a rule that stops him at three.
                //
                // The fix is upstream - there is now at most one of these per
                // account per day, because a fresh gap is folded into the
                // existing row instead of adding another. So one row is a
                // truthful minimum: at least one trade happened, and if the row
                // is negative at least one of them lost. It may have been three,
                // and Ballast will say one. Under-counting, never over-counting.
                int n;
                trades[a] = trades.TryGetValue(a, out n) ? n + 1 : 1;

                if (e.Pnl < 0)
                    losses[a] = losses.TryGetValue(a, out n) ? n + 1 : 1;

                // But it can never drive a rule about TIME. The cooldown and the
                // revenge window ask "how long since your last loss", and the
                // honest answer for a gap is that nobody knows - its timestamps
                // are the span Ballast was closed, not when the trade happened.
                if (e.IsReconstructed) continue;

                if (e.Pnl < 0)
                {
                    DateTime prev;
                    if (!lastLoss.TryGetValue(a, out prev) || e.ExitTime > prev) lastLoss[a] = e.ExitTime;
                }

                // Whether the MOST RECENT trade lost, which is what the cooldown
                // and revenge-window checks actually read.
                DateTime seen;
                if (!lastExit.TryGetValue(a, out seen) || e.ExitTime >= seen)
                {
                    lastExit[a] = e.ExitTime;
                    lastWasLoss[a] = e.Pnl < 0;
                }
            }

            foreach (string name in monitor.MonitoredNames)
            {
                BallastTracker t = monitor.Get(name);
                if (t == null) continue;

                int tr, ls;
                trades.TryGetValue(name, out tr);
                losses.TryGetValue(name, out ls);

                DateTime ll;
                bool haveLoss = lastLoss.TryGetValue(name, out ll);

                bool wasLoss;
                lastWasLoss.TryGetValue(name, out wasLoss);

                double dayPnl;
                pnl.TryGetValue(name, out dayPnl);

                double dayWorst;
                worst.TryGetValue(name, out dayWorst);

                t.SeedToday(day, tr, ls, haveLoss ? (DateTime?)ll : null, wasLoss, dayPnl, dayWorst);
            }
        }

        private string StandingPlanPath()
        {
            try { return Path.Combine(Core.Globals.UserDataDir, "ballast-standing-plan.txt"); }
            catch { return "ballast-standing-plan.txt"; }
        }

        private string EventsPath()
        {
            try { return Path.Combine(Core.Globals.UserDataDir, "ballast-events.txt"); }
            catch { return "ballast-events.txt"; }
        }

        private void SaveStandingPlan()
        {
            try
            {
                bool on = planStandingBox != null && planStandingBox.IsChecked == true;
                string plan = tbSessionPlan.Text == null ? "" : tbSessionPlan.Text.Trim();
                File.WriteAllText(StandingPlanPath(), (on ? "1" : "0") + "\n" + plan);
            }
            catch { }
        }

        /// <summary>
        /// A standing plan is carried forward, but it still has to be confirmed
        /// each morning with one tap.
        ///
        /// I argued against carrying a plan forward at all, on the grounds that a
        /// plan you did not write today is one you have not committed to. That is
        /// right about the commitment and wrong about the typing: retyping the
        /// same sentence daily is friction, not commitment. So the sentence is
        /// remembered and the commitment is re-made.
        /// </summary>
        private void LoadStandingPlan()
        {
            try
            {
                if (!File.Exists(StandingPlanPath())) return;

                string[] lines = File.ReadAllLines(StandingPlanPath());
                if (lines.Length == 0) return;

                bool on = lines[0].Trim() == "1";
                if (planStandingBox != null) planStandingBox.IsChecked = on;
                if (!on) return;

                string plan = lines.Length > 1 ? string.Join(" ", lines, 1, lines.Length - 1).Trim() : "";
                if (plan.Length == 0) return;

                // Already committed today? Then it is simply the plan.
                if (monitor.Journal.SessionPlan.Length > 0) return;

                tbSessionPlan.Text = plan;
                planPendingConfirm = true;
                if (planConfirmRow != null) planConfirmRow.Visibility = Visibility.Visible;
            }
            catch { }
        }

        private void ConfirmPlan()
        {
            planPendingConfirm = false;
            if (planConfirmRow != null) planConfirmRow.Visibility = Visibility.Collapsed;
            CommitSessionPlan();
            SaveStandingPlan();
            RenderJournal();
        }

        private void SaveEvents()
        {
            try
            {
                events.Clear();
                string raw = tbEvents.Text == null ? "" : tbEvents.Text;
                string[] lines = raw.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0) continue;
                    events.Add(line);
                }
                File.WriteAllLines(EventsPath(), events.ToArray());
            }
            catch { }
        }

        private void LoadEvents()
        {
            try
            {
                events.Clear();
                if (!File.Exists(EventsPath())) return;
                string[] lines = File.ReadAllLines(EventsPath());
                for (int i = 0; i < lines.Length; i++)
                    if (lines[i] != null && lines[i].Trim().Length > 0) events.Add(lines[i].Trim());
                if (tbEvents != null) tbEvents.Text = string.Join("\r\n", events.ToArray());
            }
            catch { }
        }

        /// <summary>
        /// The next thing on the watch list within the warning window, or "".
        /// Parses only "HH:mm rest" - anything it cannot read is ignored rather
        /// than guessed at, because a wrong time is worse than no time.
        /// </summary>
        public static string NextEventWarning(List<string> lines, DateTime now, int warnMinutes)
        {
            if (lines == null) return "";

            string best = "";
            int bestMins = int.MaxValue;

            for (int i = 0; i < lines.Count; i++)
            {
                string line = (lines[i] ?? "").Trim();
                if (line.Length < 5) continue;

                int sp = line.IndexOf(' ');
                string timePart = sp > 0 ? line.Substring(0, sp) : line;
                string label = sp > 0 ? line.Substring(sp + 1).Trim() : "";

                string[] hm = timePart.Split(':');
                if (hm.Length != 2) continue;

                int hh, mm;
                if (!int.TryParse(hm[0], out hh) || !int.TryParse(hm[1], out mm)) continue;
                if (hh < 0 || hh > 23 || mm < 0 || mm > 59) continue;

                int eventMin = hh * 60 + mm;
                int nowMin = now.Hour * 60 + now.Minute;
                int delta = eventMin - nowMin;

                if (delta < 0 || delta > warnMinutes) continue;
                if (delta < bestMins)
                {
                    bestMins = delta;
                    best = delta == 0
                        ? (label.Length > 0 ? label + " is now" : "a scheduled event is now")
                        : (label.Length > 0 ? label : "a scheduled event") + " in " + delta + " min";
                }
            }
            return best;
        }

        private void CommitSessionPlan()
        {
            string plan = tbSessionPlan.Text == null ? "" : tbSessionPlan.Text.Trim();
            if (plan == monitor.Journal.SessionPlan) return;

            monitor.Journal.SessionPlan = plan;

            // Stamp it onto anything already taken today that has no plan yet, so
            // writing the plan late still attaches it to the morning's trades.
            List<BallastTrade> today = monitor.Journal.ForDay(Core.Globals.Now);
            for (int i = 0; i < today.Count; i++)
                if (today[i].SessionPlan.Length == 0) today[i].SessionPlan = plan;

            journalDirty = true;
        }

        /// <summary>
        /// Rebuild the pending-tag strip. Rebuilt only when the queue actually
        /// changes - redrawing buttons under a trader's cursor once a second
        /// would make them un-clickable.
        /// </summary>
        private void RenderJournal()
        {
            RenderTiltRecord();

            List<BallastTrade> pending = monitor.Journal.Pending();

            if (pending.Count != lastPendingCount)
            {
                lastPendingCount = pending.Count;
                journalStrip.Children.Clear();

                if (pending.Count == 0)
                {
                    journalStripBorder.Visibility = Visibility.Collapsed;
                }
                else
                {
                    journalStripBorder.Visibility = Visibility.Visible;

                    TextBlock head = new TextBlock();
                    head.Text = pending.Count == 1
                        ? "1 trade to tag - one tap, then Done"
                        : pending.Count + " trades to tag - one tap each, then Done";
                    head.Foreground = ColAmber;
                    head.FontWeight = FontWeights.Bold;
                    head.FontSize = 11;
                    head.TextWrapping = TextWrapping.Wrap;
                    head.Margin = new Thickness(0, 0, 0, 6);
                    journalStrip.Children.Add(head);

                    // Oldest first, capped. A wall of twenty rows gets dismissed
                    // wholesale; three at a time gets cleared.
                    int show = pending.Count < 4 ? pending.Count : 4;
                    for (int i = 0; i < show; i++)
                        journalStrip.Children.Add(BuildPendingRow(pending[i]));

                    if (pending.Count > show)
                    {
                        TextBlock more = new TextBlock();
                        more.Text = (pending.Count - show) + " more behind these.";
                        more.Foreground = ColMuted;
                        more.FontSize = 10;
                        more.Margin = new Thickness(0, 4, 0, 0);
                        journalStrip.Children.Add(more);
                    }
                }
            }

            if (tabJournal != null)
                tabJournal.Content = pending.Count > 0 ? "Journal (" + pending.Count + ")" : "Journal";

            // The numbers below refresh every tick; they contain no controls.
            List<BallastTrade> all = monitor.Journal.All;
            journalInsight.Text = monitor.Journal.HeadlineInsight(all, monitor.DefaultConfig.CooldownMinutes, 20);

            List<BallastTrade> today = monitor.Journal.ForDay(Core.Globals.Now);
            List<JournalBucket> adv = monitor.Journal.AdviceSplit(all);
            List<JournalBucket> pl = monitor.Journal.PlannedSplit(all);

            List<BallastTrade> mine = BallastJournal.ManualOnly(all);
            int botCount = all.Count - mine.Count;

            // Every clause says which period it covers. It used to read "Today: 7
            // trades recorded. Your own trades: 10" - today's number followed
            // immediately by an all-time one, with nothing to say they were
            // different periods, so it simply looked wrong.
            List<BallastTrade> mineToday = BallastJournal.ManualOnly(today);

            journalSummary.Text =
                "Today: " + today.Count + (today.Count == 1 ? " trade" : " trades") + " recorded, "
              + mineToday.Count + " of them your own. "
              + "All time: " + mine.Count + (mine.Count == 1 ? " trade" : " trades") + " of yours, "
              + (mine.Count - monitor.Journal.Untagged().Count) + " tagged - "
              + "planned " + pl[0].Count + " (" + BallastTrade.Money(pl[0].Net) + "), "
              + "unplanned " + pl[1].Count + " (" + BallastTrade.Money(pl[1].Net) + "), "
              + "taken after a stop signal " + adv[0].Count + " (" + BallastTrade.Money(adv[0].Net) + ")."
              + (botCount > 0
                    ? "  " + botCount + " strategy trades are recorded but excluded from these - they measure you, not a bot."
                    : "");

            RenderInstruments(all);
            RenderTrades();

            journalPathNote.Foreground = ColFaint;
            journalPathNote.Text = "Saved continuously to " + JournalPath()
                                 + " - plain CSV, open it in Excel and add whatever columns you like. "
                                 + "Ballast only ever rewrites the columns it owns.";
        }

        /// <summary>
        /// Money made and lost per instrument. Most traders have one that quietly
        /// funds the others and one that bleeds, and it is invisible in a total.
        /// </summary>
        private void RenderInstruments(List<BallastTrade> all)
        {
            if (instrumentPanel == null) return;
            instrumentPanel.Children.Clear();

            List<JournalBucket> buckets = monitor.Journal.InstrumentSplit(all);

            if (buckets.Count == 0)
            {
                TextBlock none = new TextBlock();
                none.Text = "Nothing recorded yet.";
                none.Foreground = ColFaint;
                none.FontSize = 11;
                instrumentPanel.Children.Add(none);
                return;
            }

            for (int i = 0; i < buckets.Count; i++)
            {
                JournalBucket b = buckets[i];

                Grid g = new Grid();
                g.Margin = new Thickness(0, 0, 0, 6);
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition());

                g.Children.Add(Cell(b.Label, ColInk, 0, FontWeights.Bold));
                g.Children.Add(Cell(BallastTrade.Money(b.Net), b.Net >= 0 ? ColGreen : ColRed, 1, FontWeights.Bold));
                g.Children.Add(Cell(b.Count + (b.Count == 1 ? " trade" : " trades"), ColMuted, 2, FontWeights.Normal));
                g.Children.Add(Cell((b.WinRate * 100).ToString("0") + "% won", ColMuted, 3, FontWeights.Normal));

                instrumentPanel.Children.Add(g);
            }
        }

        private void OnGenerationChanged()
        {
            if (generationBox == null) return;

            int i = generationBox.SelectedIndex;
            generation = i == 1 ? AccountGeneration.Legacy
                       : i == 2 ? AccountGeneration.Current
                       : AccountGeneration.Auto;

            PopulateAccountTypes();
            SaveSettings();

            detectionNote.Text = generation == AccountGeneration.Auto
                ? "Auto-matching will pick the tighter drawdown when a size exists in both generations."
                : "Auto-matching will use " + (generation == AccountGeneration.Legacy ? "legacy" : "current")
                  + " figures. Existing accounts keep their saved settings - use \"Match all by balance\" to redo them.";
            detectionNote.Foreground = ColMuted;
        }

        private void SetTradeRange(bool todayOnly)
        {
            tradesTodayOnly = todayOnly;
            lastTradesSignature = "";   // force a rebuild
            RenderTrades();
        }

        /// <summary>
        /// Every recorded trade for the accounts currently being watched, grouped
        /// under the account it belongs to.
        ///
        /// Rebuilt only when something actually changed. This runs on the
        /// one-second tick, and re-creating a few hundred rows every second would
        /// burn CPU during the session for no reason - and make the buttons
        /// inside them impossible to click.
        /// </summary>
        private void RenderTrades()
        {
            if (tradesPanel == null) return;

            List<string> watched = monitor.MonitoredNames;
            DateTime today = Core.Globals.Now;
            List<BallastTrade> trades = monitor.Journal.ForAccounts(watched, tradesTodayOnly, today);

            string sig = trades.Count + "|" + tradesTodayOnly + "|" + showBotTrades
                       + "|" + string.Join(",", expandedAccounts.ToArray()) + "|" + watched.Count
                       + "|" + monitor.Journal.Count + "|" + monitor.Journal.Pending().Count;
            if (sig == lastTradesSignature) return;
            lastTradesSignature = sig;

            StyleRange();
            tradesPanel.Children.Clear();

            if (trades.Count == 0)
            {
                TextBlock none = new TextBlock();
                none.Text = watched.Count == 0
                    ? "No accounts are being watched. Tick some in Setup."
                    : (tradesTodayOnly
                        ? "No trades recorded today on the accounts you are watching."
                        : "No trades recorded yet on the accounts you are watching.");
                none.Foreground = ColFaint;
                none.FontSize = 12;
                none.TextWrapping = TextWrapping.Wrap;
                tradesPanel.Children.Add(none);
            }
            else
            {
                // Hand-traded accounts first and always open. Bot accounts after,
                // folded away - they are the many, and the few are the point.
                int botAccounts = 0, botTrades = 0;
                double botNet = 0;

                for (int pass = 0; pass < 2; pass++)
                {
                    bool wantBots = pass == 1;

                    for (int a = 0; a < watched.Count; a++)
                    {
                        string acct = watched[a];

                        BallastTracker tr = monitor.Get(acct);
                        bool isBot = tr != null && tr.Config.IsAutomated;
                        if (isBot != wantBots) continue;

                        List<BallastTrade> mine = new List<BallastTrade>();
                        double net = 0;
                        for (int i = 0; i < trades.Count; i++)
                        {
                            if (!string.Equals(trades[i].AccountName, acct, StringComparison.OrdinalIgnoreCase)) continue;
                            mine.Add(trades[i]);
                            net += trades[i].Pnl;
                        }
                        if (mine.Count == 0) continue;

                        if (isBot)
                        {
                            botAccounts++;
                            botTrades += mine.Count;
                            botNet += net;
                            if (!showBotTrades) continue;
                        }

                        bool open = expandedAccounts.Contains(acct);
                        tradesPanel.Children.Add(AccountGroupHeader(acct, mine.Count, net, isBot, open));

                        if (open)
                            for (int i = 0; i < mine.Count; i++)
                                tradesPanel.Children.Add(TradeRow(mine[i]));
                    }

                    // Between the two passes, the summary line for the bots.
                    if (pass == 1 && botAccounts > 0)
                    {
                        StackPanel botBar = new StackPanel();
                        botBar.Orientation = Orientation.Horizontal;
                        botBar.Margin = new Thickness(0, 12, 0, 0);

                        TextBlock bt = new TextBlock();
                        bt.Text = botTrades + (botTrades == 1 ? " trade on " : " trades on ")
                                + botAccounts + (botAccounts == 1 ? " automated account   " : " automated accounts   ")
                                + BallastTrade.Money(botNet);
                        bt.Foreground = botNet >= 0 ? ColGreen : ColRed;
                        bt.FontSize = 12;
                        bt.Margin = new Thickness(0, 6, 12, 0);
                        botBar.Children.Add(bt);

                        botBar.Children.Add(QuietButton(showBotTrades ? "Hide bot trades" : "Show bot trades",
                            delegate { showBotTrades = !showBotTrades; lastTradesSignature = ""; RenderTrades(); }));

                        tradesPanel.Children.Insert(pass == 1 && showBotTrades ? 0 : tradesPanel.Children.Count, botBar);
                    }
                }
            }

            int outside = monitor.Journal.CountOutside(watched);
            if (outside > 0)
            {
                outsideNote.Text = outside + (outside == 1 ? " trade is" : " trades are")
                    + " recorded against accounts you are not currently watching. Tick them in "
                    + "Setup to see those, or open the CSV - nothing is ever deleted.";
                outsideNote.Visibility = Visibility.Visible;
            }
            else outsideNote.Visibility = Visibility.Collapsed;
        }

        private void StyleRange()
        {
            if (btnToday == null) return;

            btnToday.Background = tradesTodayOnly ? ColAccent : ColPanel;
            btnToday.Foreground = tradesTodayOnly ? ColBg : ColInk;
            btnToday.FontWeight = tradesTodayOnly ? FontWeights.Bold : FontWeights.Normal;

            btnAllTime.Background = tradesTodayOnly ? ColPanel : ColAccent;
            btnAllTime.Foreground = tradesTodayOnly ? ColInk : ColBg;
            btnAllTime.FontWeight = tradesTodayOnly ? FontWeights.Normal : FontWeights.Bold;
        }

        /// <summary>
        /// A clickable account header. Collapsed it still carries the two figures
        /// worth knowing - how many trades and the net - so a closed group is a
        /// summary rather than a hidden thing.
        /// </summary>
        private UIElement AccountGroupHeader(string account, int count, double net, bool isBot, bool open)
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

            TextBlock name = new TextBlock();
            name.Text = (open ? "\u25be  " : "\u25b8  ") + account + (isBot ? "   (strategy)" : "");
            name.Foreground = isBot ? ColMuted : ColInk;
            name.FontSize = 13;
            name.FontWeight = FontWeights.Bold;
            name.VerticalAlignment = VerticalAlignment.Center;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            // A long account name must not run into the totals on its right.
            name.Margin = new Thickness(0, 0, 18, 0);
            g.Children.Add(name);

            TextBlock sum = new TextBlock();
            sum.Text = count + (count == 1 ? " trade" : " trades") + "     " + BallastTrade.Money(net);
            sum.Foreground = net >= 0 ? ColGreen : ColRed;
            sum.FontSize = 12;
            sum.FontWeight = FontWeights.Bold;
            sum.VerticalAlignment = VerticalAlignment.Center;
            sum.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(sum, 1);
            g.Children.Add(sum);

            // A whole-row button rather than a small chevron - the target should
            // be the size of the thing it opens.
            Button b = new Button();
            b.Content = g;
            b.Padding = new Thickness(10, 8, 10, 8);
            b.Margin = new Thickness(0, 10, 0, 4);
            b.Background = ColPanel;
            b.BorderBrush = ColLine;
            b.BorderThickness = new Thickness(1);

            // A button centres its content by default, which collapsed the grid
            // to the width of its text and printed the account name hard up
            // against its own totals - "Sim1042 trades $2,801". The row has to
            // fill the button for the two columns to separate at all.
            b.HorizontalContentAlignment = HorizontalAlignment.Stretch;

            b.Click += delegate { ToggleAccount(account); };
            return b;
        }

        private void ToggleAccount(string account)
        {
            if (expandedAccounts.Contains(account)) expandedAccounts.Remove(account);
            else expandedAccounts.Add(account);

            lastTradesSignature = "";   // force a rebuild
            RenderTrades();
        }

        private void SetAllAccountsExpanded(bool open)
        {
            expandedAccounts.Clear();

            if (open)
            {
                List<string> watched = monitor.MonitoredNames;
                for (int i = 0; i < watched.Count; i++) expandedAccounts.Add(watched[i]);
            }

            lastTradesSignature = "";
            RenderTrades();
        }

        private UIElement TradeRow(BallastTrade e)
        {
            Border b = new Border();
            b.CornerRadius = new CornerRadius(6);
            b.Background = ColPanel;
            b.Padding = new Thickness(12, 9, 12, 9);
            b.Margin = new Thickness(0, 0, 0, 5);

            StackPanel sp = new StackPanel();

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition());

            g.Children.Add(Cell(e.EntryTime.ToString("HH:mm", CultureInfo.InvariantCulture)
                              + "-" + e.ExitTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                              ColMuted, 0, FontWeights.Normal));

            string size = e.SizeLabel;
            string what = (size.Length > 0 ? size + "  " : "")
                        + (e.Instrument.Length > 0 ? e.Instrument : "position");
            g.Children.Add(Cell(what, ColInk, 1, FontWeights.Normal));

            g.Children.Add(Cell(BallastTrade.Money(e.Pnl), e.Pnl >= 0 ? ColGreen : ColRed, 2, FontWeights.Bold));

            string verdict = BallastJournal.VerdictLabel(e.Planned);
            Brush vcol = e.Planned.Length == 0 ? ColFaint
                       : e.Planned == BallastJournal.Verdict_ByTheBook ? ColGreen
                       : e.Planned == BallastJournal.Verdict_Sloppy ? ColAmber
                       : ColRed;
            g.Children.Add(Cell(verdict, vcol, 3, FontWeights.Normal));

            sp.Children.Add(g);

            // Second line: the context Ballast recorded on its own, plus whatever
            // the trader added. Only shown when there is something to say.
            List<string> bits = new List<string>();
            if (e.Feeling.Length > 0) bits.Add(e.Feeling);
            if (e.TakenAgainstAdvice) bits.Add("opened after Ballast said " + Humanise(e.AdviceAtEntry));
            if (e.PreviousTradeWasLoss && e.MinutesSincePreviousLoss >= 0 && e.MinutesSincePreviousLoss < 10)
                bits.Add(e.MinutesSincePreviousLoss + " min after a loss");
            if (e.DurationMinutes >= 1) bits.Add(Math.Round(e.DurationMinutes) + " min held");

            if (bits.Count > 0)
            {
                TextBlock sub = new TextBlock();
                sub.Text = string.Join("  -  ", bits.ToArray());
                sub.Foreground = e.TakenAgainstAdvice ? ColAmber : ColFaint;
                sub.FontSize = 11;
                sub.TextWrapping = TextWrapping.Wrap;
                sub.Margin = new Thickness(0, 5, 0, 0);
                sp.Children.Add(sub);
            }

            if (e.Note.Length > 0)
            {
                TextBlock note = new TextBlock();
                note.Text = "\"" + e.Note + "\"";
                note.Foreground = ColMuted;
                note.FontSize = 11;
                note.TextWrapping = TextWrapping.Wrap;
                note.Margin = new Thickness(0, 5, 0, 0);
                sp.Children.Add(note);
            }

            if (e.HasImages)
            {
                // The pictures themselves, in the journal. A button that launched
                // an external viewer was not "seeing" them.
                StackPanel shots = new StackPanel();
                shots.Orientation = Orientation.Horizontal;
                shots.Margin = new Thickness(0, 8, 0, 0);

                UIElement a = Thumbnail(e.EntryImage, "entry", e);
                UIElement b2 = Thumbnail(e.ExitImage, "exit", e);
                if (a != null) shots.Children.Add(a);
                if (b2 != null) shots.Children.Add(b2);

                if (shots.Children.Count > 0) sp.Children.Add(shots);

                // Say when one is missing rather than leaving a gap. A trade with
                // an entry photo and no exit looks like the picture is still
                // loading; it is not, and the trader should know the record is
                // incomplete rather than quietly assume it is not.
                bool haveEntry = a != null, haveExit = b2 != null;
                if (haveEntry != haveExit)
                {
                    TextBlock miss = new TextBlock();
                    miss.Text = haveEntry
                        ? "No exit photo for this one - the chart it was taken on may have been closed or retitled before it filled."
                        : "No entry photo for this one.";
                    miss.Foreground = ColFaint;
                    miss.FontSize = 10;
                    miss.TextWrapping = TextWrapping.Wrap;
                    miss.Margin = new Thickness(0, 4, 0, 0);
                    sp.Children.Add(miss);
                }

                StackPanel picBtns = new StackPanel();
                picBtns.Orientation = Orientation.Horizontal;
                picBtns.Margin = new Thickness(0, 7, 0, 0);

                Button card = new Button();
                card.Content = "Open trade card";
                card.FontSize = 11;
                card.Padding = new Thickness(10, 4, 10, 4);
                card.Margin = new Thickness(0, 0, 6, 0);
                card.Background = ColCard;
                card.Foreground = ColInk;
                card.BorderBrush = ColLine;
                card.Click += delegate { OpenTradeCard(e); };
                picBtns.Children.Add(card);

                sp.Children.Add(picBtns);
            }

            b.Child = sp;
            return b;
        }

        private UIElement BuildPendingRow(BallastTrade e)
        {
            Border b = new Border();
            b.CornerRadius = new CornerRadius(6);
            b.Background = ColCard;
            b.Padding = new Thickness(12, 10, 12, 10);
            b.Margin = new Thickness(0, 0, 0, 8);

            StackPanel sp = new StackPanel();

            TextBlock what = new TextBlock();
            what.Text = e.AccountName + "   " + e.ShortLabel;
            what.Foreground = e.Pnl >= 0 ? ColGreen : ColRed;
            what.FontSize = 13;
            what.FontWeight = FontWeights.Bold;
            what.TextWrapping = TextWrapping.Wrap;
            sp.Children.Add(what);

            string ctx = "Trade " + e.TradeNumberToday + " of the day";
            if (e.PreviousTradeWasLoss && e.MinutesSincePreviousLoss >= 0)
                ctx += ", " + e.MinutesSincePreviousLoss + " min after a loss";
            if (e.TakenAgainstAdvice)
                ctx += ", opened while Ballast said " + Humanise(e.AdviceAtEntry);
            if (!e.InsideSessionWindow)
                ctx += ", outside your session window";

            TextBlock ctxBlock = new TextBlock();
            ctxBlock.Text = ctx;
            ctxBlock.Foreground = e.TakenAgainstAdvice ? ColAmber : ColMuted;
            ctxBlock.FontSize = 11;
            ctxBlock.TextWrapping = TextWrapping.Wrap;
            ctxBlock.Margin = new Thickness(0, 3, 0, 8);
            sp.Children.Add(ctxBlock);

            // Row 1: the verdict. Four options now, still one tap - "planned or
            // not" was answering two questions at once, and the trader can pick
            // the right setup and still execute it badly.
            StackPanel verdictRow = new StackPanel();
            verdictRow.Orientation = Orientation.Horizontal;
            verdictRow.Margin = new Thickness(0, 0, 0, 8);

            List<Button> verdictButtons = new List<Button>();

            for (int v = 0; v < BallastJournal.PlannedOptions.Length; v++)
            {
                string key = BallastJournal.PlannedOptions[v];

                Button vb = new Button();
                vb.Content = BallastJournal.VerdictLabel(key);
                vb.Padding = new Thickness(12, 7, 12, 7);
                vb.Margin = new Thickness(0, 0, 6, 0);
                vb.FontSize = 12;
                vb.Click += delegate
                {
                    e.Planned = key;
                    StyleVerdicts(e, verdictButtons);
                    journalDirty = true;
                };
                verdictButtons.Add(vb);
                verdictRow.Children.Add(vb);
            }

            if (e.HasImages)
            {
                Button pics = new Button();
                pics.Content = "Chart";
                pics.FontSize = 12;
                pics.Padding = new Thickness(12, 7, 12, 7);
                pics.Background = ColPanel;
                pics.Foreground = ColInk;
                pics.BorderBrush = ColLine;
                pics.Click += delegate { OpenImages(e); };
                verdictRow.Children.Add(pics);
            }

            sp.Children.Add(verdictRow);
            StyleVerdicts(e, verdictButtons);

            // Row 2: feeling and a free note. Both optional, both still available
            // after the verdict has landed.
            // Row 2: did you move your stop or target?
            //
            // Ballast watches the position, not the working orders, so this is
            // genuinely invisible to it - and it is the break that costs the
            // most. A stop moved away turns a planned loss into an unplanned one.
            // One tap, same as the verdict, and the note prompt underneath then
            // asks why.
            StackPanel movedRow = new StackPanel();
            movedRow.Orientation = Orientation.Horizontal;
            movedRow.Margin = new Thickness(0, 0, 0, 8);

            TextBlock movedLabel = new TextBlock();
            movedLabel.Text = "Stop / target:";
            movedLabel.Foreground = ColFaint;
            movedLabel.FontSize = 11;
            movedLabel.Margin = new Thickness(0, 8, 8, 0);
            movedRow.Children.Add(movedLabel);

            List<Button> movedButtons = new List<Button>();
            TextBlock noteHint = new TextBlock();

            for (int v = 0; v < BallastJournal.MovedOptions.Length; v++)
            {
                string key = BallastJournal.MovedOptions[v];

                Button mb = new Button();
                mb.Content = BallastJournal.MovedLabel(key);
                mb.Padding = new Thickness(11, 6, 11, 6);
                mb.Margin = new Thickness(0, 0, 6, 0);
                mb.FontSize = 11;
                mb.Click += delegate
                {
                    e.Moved = key;
                    StyleMoved(e, movedButtons);
                    SetNoteHint(e, noteHint);
                    journalDirty = true;
                };
                movedButtons.Add(mb);
                movedRow.Children.Add(mb);
            }

            sp.Children.Add(movedRow);
            StyleMoved(e, movedButtons);

            StackPanel detailRow = new StackPanel();
            detailRow.Orientation = Orientation.Horizontal;
            detailRow.Margin = new Thickness(0, 0, 0, 8);

            ComboBox feel = new ComboBox();
            feel.Width = 150;
            feel.FontSize = 12;
            feel.Margin = new Thickness(0, 0, 6, 0);
            feel.Items.Add("Feeling?");
            for (int i = 0; i < BallastJournal.Feelings.Length; i++) feel.Items.Add(BallastJournal.Feelings[i]);
            feel.SelectedIndex = 0;
            for (int i = 0; i < BallastJournal.Feelings.Length; i++)
                if (BallastJournal.Feelings[i] == e.Feeling) feel.SelectedIndex = i + 1;
            feel.SelectionChanged += delegate
            {
                e.Feeling = feel.SelectedIndex > 0 ? (feel.SelectedItem as string) : "";
                journalDirty = true;
            };
            detailRow.Children.Add(feel);

            Button done = new Button();
            done.Content = "Done";
            done.FontSize = 12;
            done.FontWeight = FontWeights.Bold;
            done.Padding = new Thickness(16, 7, 16, 7);
            done.Background = ColAccent;
            done.Foreground = ColBg;
            done.BorderBrush = ColAccent;
            done.Click += delegate { DismissTrade(e); };
            detailRow.Children.Add(done);

            sp.Children.Add(detailRow);

            TextBox note = new TextBox();
            note.Text = e.Note;
            note.Background = ColPanel;
            note.Foreground = ColInk;
            note.BorderBrush = ColLine;
            note.FontSize = 12;
            note.Padding = new Thickness(8, 6, 8, 6);
            note.TextWrapping = TextWrapping.Wrap;
            note.LostFocus += delegate { e.Note = note.Text == null ? "" : note.Text; journalDirty = true; };
            sp.Children.Add(note);

            noteHint.Foreground = ColFaint;
            noteHint.FontSize = 10;
            noteHint.Margin = new Thickness(0, 4, 0, 0);
            SetNoteHint(e, noteHint);
            sp.Children.Add(noteHint);

            b.Child = sp;
            return b;
        }

        /// <summary>
        /// Show which verdict is recorded. Green for the one you want more of,
        /// amber for a rule broken, red for the two that cost money - so the
        /// colour of the row is itself feedback.
        /// </summary>
        /// <summary>
        /// The note prompt follows the answer. Someone who has just said they
        /// moved their stop is being asked a specific question at the moment they
        /// still know the answer - which is worth far more than the same blank
        /// box labelled "anything you want to remember".
        /// </summary>
        private void SetNoteHint(BallastTrade e, TextBlock hint)
        {
            if (hint == null) return;

            hint.Text = (e != null && BallastJournal.DidMove(e.Moved))
                ? "Why did you move it? One line, while you still remember."
                : "Anything you want to remember. Optional - press Done when finished.";
        }

        private void StyleMoved(BallastTrade e, List<Button> buttons)
        {
            if (e == null || buttons == null) return;

            for (int i = 0; i < buttons.Count && i < BallastJournal.MovedOptions.Length; i++)
            {
                bool on = e.Moved == BallastJournal.MovedOptions[i];
                bool bad = BallastJournal.DidMove(BallastJournal.MovedOptions[i]);

                buttons[i].Background = on ? (bad ? ColAmber : ColGreen) : ColPanel;
                buttons[i].Foreground = on ? ColBg : ColInk;
                buttons[i].BorderBrush = on ? (bad ? ColAmber : ColGreen) : ColLine;
                buttons[i].FontWeight = on ? FontWeights.Bold : FontWeights.Normal;
            }
        }

        private void StyleVerdicts(BallastTrade e, List<Button> buttons)
        {
            for (int i = 0; i < buttons.Count && i < BallastJournal.PlannedOptions.Length; i++)
            {
                string key = BallastJournal.PlannedOptions[i];
                bool on = e.Planned == key;

                Brush tone = key == BallastJournal.Verdict_ByTheBook ? ColGreen
                           : key == BallastJournal.Verdict_Sloppy ? ColAmber
                           : ColRed;

                buttons[i].Background = on ? tone : ColPanel;
                buttons[i].Foreground = on ? ColBg : ColInk;
                buttons[i].BorderBrush = on ? tone : ColLine;
                buttons[i].FontWeight = on ? FontWeights.Bold : FontWeights.Normal;
            }
        }

        private void DismissTrade(BallastTrade e)
        {
            if (e.SessionPlan.Length == 0) e.SessionPlan = monitor.Journal.SessionPlan;
            e.Dismissed = true;
            journalDirty = true;
            lastPendingCount = -1;   // force the strip to rebuild
            monitor.Journal.Save(JournalPath());
            RenderJournal();
        }

        /// <summary>
        /// Open the entry and exit photographs in whatever the trader uses for
        /// images. Ballast does not try to be an image viewer.
        /// </summary>
        /// <summary>
        /// A small chart image for a journal row.
        ///
        /// DecodePixelWidth matters more than it looks: it decodes at thumbnail
        /// size rather than loading a full screenshot into memory and shrinking
        /// it. Twenty trades a day would otherwise be forty full-resolution
        /// bitmaps held live while the trader scrolls.
        ///
        /// OnLoad caching means the file is not locked, so pruning can still
        /// delete it later.
        /// </summary>
        private UIElement Thumbnail(string path, string caption, BallastTrade trade)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

                BitmapImage bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.DecodePixelWidth = 260;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();

                Image img = new Image();
                img.Source = bmp;
                img.Width = 260;
                img.Stretch = Stretch.Uniform;

                Border frame = new Border();
                frame.BorderBrush = ColLine;
                frame.BorderThickness = new Thickness(1);
                frame.CornerRadius = new CornerRadius(6);
                frame.Padding = new Thickness(2);
                frame.Child = img;

                // A 260px-wide picture of a trading chart is a record that
                // something happened, not something anyone can read. Clicking it
                // opens it as large as the window allows.
                string open = path;
                BallastTrade owner = trade;
                string which = caption;
                frame.Cursor = System.Windows.Input.Cursors.Hand;
                frame.MouseLeftButtonUp += delegate { ShowImage(open, which, owner); };

                TextBlock cap = new TextBlock();
                cap.Text = caption + "  -  click to enlarge";
                cap.Foreground = ColFaint;
                cap.FontSize = 9;
                cap.FontWeight = FontWeights.Bold;
                cap.Margin = new Thickness(2, 0, 0, 3);

                StackPanel sp = new StackPanel();
                sp.Margin = new Thickness(0, 0, 8, 0);
                sp.Children.Add(cap);
                sp.Children.Add(frame);
                return sp;
            }
            catch { return null; }
        }

        private void OpenTradeCard(BallastTrade e)
        {
            try
            {
                List<BallastTrade> one = new List<BallastTrade>();
                one.Add(e);

                string title = e.AccountName + "  "
                             + (e.SizeLabel.Length > 0 ? e.SizeLabel + " " : "")
                             + e.Instrument + "  " + BallastTrade.Money(e.Pnl);

                // Viewing: one reusable file, linked images, nothing accumulates.
                TradeReport.EmbedImages = false;
                string path = TradeReport.Write(ReportRoot(), TradeReport.ViewerName,
                    TradeReport.Page(title, one));

                if (path.Length > 0) System.Diagnostics.Process.Start(path);
            }
            catch { }
        }

        private void OpenDayReport()
        {
            try
            {
                List<string> watched = monitor.MonitoredNames;
                DateTime now = Core.Globals.Now;
                List<BallastTrade> trades = monitor.Journal.ForAccounts(watched, tradesTodayOnly, now);

                string title = tradesTodayOnly
                    ? now.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture)
                    : "Every recorded trade";

                TradeReport.EmbedImages = false;
                string path = TradeReport.Write(ReportRoot(), TradeReport.ViewerName,
                    TradeReport.Page(title, trades));

                if (path.Length > 0) System.Diagnostics.Process.Start(path);
            }
            catch { }
        }

        /// <summary>
        /// Save a copy worth keeping: dated file, charts embedded in it.
        ///
        /// This is the version that still works in a year, after image retention
        /// has deleted the PNGs, and the only version that can be sent to anyone.
        /// It is several megabytes, which is precisely why it is a deliberate act
        /// rather than something that happens every time a row is clicked.
        /// </summary>
        private void SaveReportCopy()
        {
            try
            {
                List<string> watched = monitor.MonitoredNames;
                DateTime now = Core.Globals.Now;
                List<BallastTrade> trades = monitor.Journal.ForAccounts(watched, tradesTodayOnly, now);

                string title = tradesTodayOnly
                    ? now.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture)
                    : "Every recorded trade";

                TradeReport.EmbedImages = true;
                string html = TradeReport.Page(title, trades);
                TradeReport.EmbedImages = false;

                string path = TradeReport.Write(ReportRoot(),
                    TradeReport.ReportName(tradesTodayOnly ? "day" : "all", now), html);

                if (path.Length > 0)
                {
                    journalPathNote.Foreground = ColMuted;
                    journalPathNote.Text = "Saved a self-contained copy to " + path
                                         + " - the charts are inside it, so it still works after "
                                         + "the images are cleaned up, and it can be sent to anyone.";
                    System.Diagnostics.Process.Start(path);
                }
            }
            catch { }
        }

        private string ReportRoot()
        {
            try { return Path.Combine(Core.Globals.UserDataDir, "ballast-reports"); }
            catch { return "ballast-reports"; }
        }

        private void OpenImages(BallastTrade e)
        {
            try
            {
                if (e.EntryImage.Length > 0 && File.Exists(e.EntryImage))
                    System.Diagnostics.Process.Start(e.EntryImage);
                if (e.ExitImage.Length > 0 && File.Exists(e.ExitImage))
                    System.Diagnostics.Process.Start(e.ExitImage);
            }
            catch { }
        }

        private static string Humanise(string action)
        {
            switch (action)
            {
                case "Lockout":      return "stop, the account is at risk";
                case "StopForDay":   return "stop for the day";
                case "ProtectGreen": return "protect the green";
                case "Cooldown":     return "wait out a cooldown";
                case "SizeDown":     return "size down";
                default:             return action;
            }
        }

        private static bool Contains(List<string> list, string value)
        {
            if (list == null || value == null) return false;
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private string SettingsPath()
        {
            try { return Path.Combine(Core.Globals.UserDataDir, "ballast-settings.txt"); }
            catch { return "ballast-settings.txt"; }
        }

        private void SaveSettings()
        {
            try
            {
                List<string> lines = new List<string>();
                lines.Add("*UI*|" + zoomIndex.ToString(CultureInfo.InvariantCulture)
                          + "|" + ((int)generation).ToString(CultureInfo.InvariantCulture));
                lines.Add("*TILT*|" + (tiltEnabled ? "1" : "0") + "|" + (tiltOnGiveBack ? "1" : "0"));
                lines.Add(SettingsCodec.Serialise("*DEFAULT*", monitor.DefaultConfig));

                // Which accounts are ticked. Kept as its own line so that an
                // account's RULES and whether it is being WATCHED are two
                // separate facts. They used to be the same fact - a settings line
                // existed only while an account was ticked - which is why
                // un-ticking silently deleted everything the trader had entered.
                List<string> watched = monitor.MonitoredNames;
                StringBuilder w = new StringBuilder("*WATCH*");
                for (int i = 0; i < watched.Count; i++) w.Append('|').Append(watched[i]);
                lines.Add(w.ToString());

                foreach (string n in watched)
                {
                    BallastTracker t = monitor.Get(n);
                    if (t != null) lines.Add(SettingsCodec.Serialise(n, t.Config));
                }

                // Un-ticked accounts keep their rules on disk too.
                foreach (string n in monitor.RememberedNames)
                {
                    TrackerConfig c = monitor.RememberedConfig(n);
                    if (c != null) lines.Add(SettingsCodec.Serialise(n, c));
                }
                File.WriteAllLines(SettingsPath(), lines.ToArray());
            }
            catch { /* settings are a convenience, never fatal */ }
        }

        private void LoadSettings()
        {
            try
            {
                string p = SettingsPath();
                if (!File.Exists(p)) { LoadConfigIntoFields(monitor.DefaultConfig); return; }

                string[] lines = File.ReadAllLines(p);

                // Read the watch list first, because every account line after it
                // has to know whether that account is ticked or merely on record.
                // A file written before this line existed has no watch list at
                // all - in that case every account in it was, by definition,
                // being watched, so an upgrade must not silently untick the lot.
                List<string> watch = null;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i] == null || !lines[i].StartsWith("*WATCH*")) continue;
                    watch = new List<string>();
                    string[] parts = lines[i].Split('|');
                    for (int k = 1; k < parts.Length; k++)
                        if (parts[k].Length > 0) watch.Add(parts[k]);
                    break;
                }

                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i] != null && lines[i].StartsWith("*WATCH*")) continue;

                    if (lines[i] != null && lines[i].StartsWith("*UI*|"))
                    {
                        string[] ui = lines[i].Split('|');
                        int z;
                        if (ui.Length > 1 && int.TryParse(ui[1], out z)
                            && z >= 0 && z < ZoomSteps.Length) zoomIndex = z;

                        int gi;
                        if (ui.Length > 2 && int.TryParse(ui[2], out gi) && gi >= 0 && gi <= 2)
                            generation = (AccountGeneration)gi;
                        continue;
                    }

                    if (lines[i] != null && lines[i].StartsWith("*TILT*|"))
                    {
                        string[] tf = lines[i].Split('|');
                        // Absent means on. A trader who has never seen this
                        // setting should still get the wall.
                        if (tf.Length > 1) tiltEnabled = tf[1] != "0";
                        if (tf.Length > 2) tiltOnGiveBack = tf[2] == "1";
                        continue;
                    }

                    string key;
                    TrackerConfig c = SettingsCodec.Deserialise(lines[i], out key);
                    if (c == null) continue;

                    if (key == "*DEFAULT*") monitor.DefaultConfig = c;
                    else
                    {
                        bool isWatched = watch == null || Contains(watch, key);

                        if (isWatched)
                        {
                            BallastTracker t = monitor.GetOrCreate(key);
                            if (t != null) t.Config = c;
                        }
                        else
                        {
                            // On record, not being watched. Its rules survive
                            // restarts exactly like a watched account's do.
                            monitor.RememberConfig(key, c);
                        }

                        // Either way it counts as configured, so re-ticking it
                        // later never triggers auto-detection over settings the
                        // trader entered by hand.
                        if (!configuredFromDisk.Contains(key)) configuredFromDisk.Add(key);
                    }
                }
            }
            catch { }

            LoadConfigIntoFields(monitor.DefaultConfig);
        }

        private void OnClosedCleanup(object sender, EventArgs e)
        {
            // Never lose a session's trades because the window was closed.
            try { CommitSessionPlan(); monitor.Journal.Save(JournalPath()); } catch { }

            // Closing the window at the end of the day is the most likely moment
            // for a session's final P&L to be lost, and that figure is the whole
            // point of the override record.
            try { tiltLog.Save(TiltPath()); } catch { }
            try { SaveTiltGate(); } catch { }
            try { SaveSettings(); } catch { }

            // Last thing written, so tomorrow morning - or five minutes from now -
            // Ballast knows exactly where today was being measured from.
            try { SaveSessionState(); } catch { }

            if (timer != null)
            {
                timer.Stop();
                timer.Tick -= OnTick;
                timer = null;
            }
            Closed -= OnClosedCleanup;
        }
    }
}
