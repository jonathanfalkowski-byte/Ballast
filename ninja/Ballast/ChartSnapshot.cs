// ─────────────────────────────────────────────────────────────────────────────
// Ballast — ChartSnapshot.cs
//
// Photographs the chart at the moment a trade opens, and again when it closes.
//
// WHY THIS IS THE MOST VALUABLE THING IN THE JOURNAL.
//
// Everything else Ballast records is a number, and numbers are easy to argue
// with after the fact. "The setup was there." "It looked different at the time."
// Memory of one's own reasoning is not retrieved, it is reconstructed, and it
// gets reconstructed to fit the outcome - which is precisely why traders review
// a losing trade and conclude they knew all along.
//
// A picture of what was actually on the screen ends that argument. It is the
// only journal field that cannot be rewritten by hindsight, and it costs the
// trader nothing: no screenshot key, no cropping, no filing.
//
// HOW IT IS BUILT, AND WHY IT LOOKS PARANOID.
//
// This file talks to NinjaTrader's chart internals, which are not part of the
// documented add-on surface and differ between versions. A direct compile-time
// reference to a type that moved would take the WHOLE add-on down - not just
// screenshots, but the cushion figure a trader is relying on mid-session.
//
// So every NinjaTrader touch here goes through reflection. It cannot fail to
// compile, it cannot throw into the UI thread, and if the internals are not what
// this expects it records why and captures nothing. A missing screenshot is a
// disappointment; an add-on that will not load is a blown account.
//
// The WPF rendering itself is ordinary and certain, so that part is direct.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace Ballast
{
    public class SnapshotResult
    {
        public bool Ok;
        public string Path = "";
        /// <summary>Why nothing was captured. Shown to the trader, never swallowed.</summary>
        public string Problem = "";
    }

    public static class ChartSnapshot
    {
        /// <summary>Turned off by a trader who does not want images on disk.</summary>
        public static bool Enabled = true;

        /// <summary>Keep this many days of images, then delete. 0 = keep everything.</summary>
        public static int RetentionDays = 60;

        /// <summary>
        /// Hard ceiling on the whole image folder, in megabytes. 0 = no ceiling.
        ///
        /// A cap counted in DAYS cannot give a predictable answer here. One
        /// trader's day is 24 KB and the next is 21 MB - a nine-hundred-fold
        /// spread on the same screen, from the same person, inside one week - so
        /// "sixty days" is somewhere between forty megabytes and one and a half
        /// gigabytes depending on how busy the month was. Nobody can budget
        /// against that.
        ///
        /// Megabytes are the thing he actually cares about, so megabytes are the
        /// thing to cap. Days stay as a second limit: old pictures are worth
        /// little even when there is room for them.
        /// </summary>
        public static int MaxTotalMb = 500;

        /// <summary>
        /// Widest a saved screenshot gets - ON THE FALLBACK RENDER PATH ONLY.
        ///
        /// This does NOT bound a real screenshot, and the comment that used to
        /// sit here said it did. Only the RenderTargetBitmap fallback below is
        /// scaled by it; the screen capture that produces every actual picture
        /// takes the region at the resolution it is on screen and saves it
        /// whole. His are 2213 x 892 and about 780 KB each, not the 1000-wide,
        /// 130 KB files this constant implies.
        ///
        /// Left alone deliberately after finding it. "if i cant read the chart
        /// what is the point of having it" - the full resolution IS the point,
        /// and it is what makes the picture worth opening. The disk budget in
        /// Prune() is the right place to control the cost, and it does: about
        /// 1.5 MB a trade, so the 500 MB cap begins retiring the oldest days
        /// after roughly six weeks of his volume.
        ///
        /// Anyone who wants smaller files should scale the screen capture
        /// explicitly rather than assume this figure is doing it.
        /// </summary>
        public static int MaxWidth = 1000;

        /// <summary>Last problem encountered, surfaced in the window rather than hidden.</summary>
        public static string LastProblem = "";

        // ── Paths ────────────────────────────────────────────────────────────

        /// <summary>
        /// Strip anything a filesystem will reject. Instrument names contain
        /// spaces and hyphens, account names can contain almost anything, and an
        /// unsanitised name is an unhandled exception on the trading thread.
        /// </summary>
        public static string SafeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "unknown";

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                       || (c >= '0' && c <= '9') || c == '-' || c == '_';
                sb.Append(ok ? c : '-');
            }

            string outp = sb.ToString().Trim('-');
            while (outp.IndexOf("--") >= 0) outp = outp.Replace("--", "-");
            if (outp.Length == 0) return "unknown";
            if (outp.Length > 40) outp = outp.Substring(0, 40);
            return outp;
        }

        /// <summary>Folder for one day's images. Dated so retention is a directory delete.</summary>
        public static string DayFolder(string root, DateTime when)
        {
            return Path.Combine(root, when.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Deterministic file name. Includes account, instrument, time and which
        /// end of the trade it is, so the folder is browsable without the app.
        /// </summary>
        public static string FileName(string account, string instrument, DateTime when, bool isEntry)
        {
            return SafeName(account) + "_" + SafeName(instrument) + "_"
                 + when.ToString("HHmmss", CultureInfo.InvariantCulture) + "_"
                 + (isEntry ? "entry" : "exit") + ".png";
        }

        public static string FullPath(string root, string account, string instrument,
                                      DateTime when, bool isEntry)
        {
            return Path.Combine(DayFolder(root, when), FileName(account, instrument, when, isEntry));
        }

        // ── Retention ────────────────────────────────────────────────────────

        /// <summary>
        /// Which dated folders are past the retention window. Pure so it can be
        /// tested; the caller does the deleting. Images are the only thing here
        /// that grows without bound, so this is not optional housekeeping.
        /// </summary>
        public static List<string> FoldersToDelete(List<string> folderNames, DateTime today, int retentionDays)
        {
            List<string> doomed = new List<string>();
            if (retentionDays <= 0 || folderNames == null) return doomed;

            for (int i = 0; i < folderNames.Count; i++)
            {
                string name = folderNames[i];
                DateTime d;
                if (!DateTime.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                            System.Globalization.DateTimeStyles.None, out d))
                    continue;   // not ours - never delete something we did not create

                if ((today.Date - d.Date).TotalDays > retentionDays) doomed.Add(name);
            }
            return doomed;
        }

        /// <summary>
        /// Folders to delete, oldest first, until the total fits the budget.
        /// Pure, so the arithmetic can be tested without a disk.
        ///
        /// Oldest first because that is the order they stop being worth
        /// anything. The most recent day is never deleted whatever the budget
        /// says - a cap set too low should degrade to "keep today" rather than
        /// to "keep nothing", and an empty folder on a day he traded would look
        /// exactly like a broken feature.
        /// </summary>
        public static List<string> FoldersOverBudget(List<string> folderNames,
                                                     List<long> bytes, int maxTotalMb)
        {
            List<string> doomed = new List<string>();
            if (maxTotalMb <= 0 || folderNames == null || bytes == null) return doomed;
            if (folderNames.Count != bytes.Count) return doomed;

            // Sort a copy, oldest first. The names are yyyy-MM-dd, so ordinal
            // order is date order.
            List<string> names = new List<string>(folderNames);
            List<long> size = new List<long>(bytes);
            for (int a = 0; a < names.Count; a++)
                for (int b = a + 1; b < names.Count; b++)
                    if (string.CompareOrdinal(names[b], names[a]) < 0)
                    {
                        string n = names[a]; names[a] = names[b]; names[b] = n;
                        long z = size[a]; size[a] = size[b]; size[b] = z;
                    }

            long total = 0;
            for (int i = 0; i < size.Count; i++) total += size[i];

            long budget = (long)maxTotalMb * 1024L * 1024L;

            for (int i = 0; i < names.Count - 1 && total > budget; i++)
            {
                doomed.Add(names[i]);
                total -= size[i];
            }

            return doomed;
        }

        /// <summary>Bytes held by one folder. 0 if it cannot be read.</summary>
        public static long FolderBytes(string path)
        {
            long n = 0;
            try
            {
                string[] files = Directory.GetFiles(path);
                for (int i = 0; i < files.Length; i++)
                {
                    try { n += new FileInfo(files[i]).Length; }
                    catch { }
                }
            }
            catch { }
            return n;
        }

        /// <summary>Everything the image folder is using, in bytes.</summary>
        public static long TotalBytes(string root)
        {
            long n = 0;
            try
            {
                if (!Directory.Exists(root)) return 0;
                string[] dirs = Directory.GetDirectories(root);
                for (int i = 0; i < dirs.Length; i++) n += FolderBytes(dirs[i]);
            }
            catch { }
            return n;
        }

        public static void Prune(string root, DateTime today)
        {
            try
            {
                if (!Directory.Exists(root)) return;

                string[] dirs = Directory.GetDirectories(root);
                List<string> names = new List<string>();
                for (int i = 0; i < dirs.Length; i++) names.Add(Path.GetFileName(dirs[i]));

                // Age first: a picture from March is worth nothing whatever the
                // budget says.
                if (RetentionDays > 0)
                {
                    List<string> old = FoldersToDelete(names, today, RetentionDays);
                    for (int i = 0; i < old.Count; i++)
                    {
                        try { Directory.Delete(Path.Combine(root, old[i]), true); }
                        catch { }
                        names.Remove(old[i]);
                    }
                }

                // Then size, on whatever is left.
                if (MaxTotalMb > 0 && names.Count > 1)
                {
                    List<long> sizes = new List<long>();
                    for (int i = 0; i < names.Count; i++)
                        sizes.Add(FolderBytes(Path.Combine(root, names[i])));

                    List<string> over = FoldersOverBudget(names, sizes, MaxTotalMb);
                    for (int i = 0; i < over.Count; i++)
                    {
                        try { Directory.Delete(Path.Combine(root, over[i]), true); }
                        catch { }
                    }
                }
            }
            catch { }
        }

        // ── Choosing which chart to photograph ───────────────────────────────

        /// <summary>
        /// Score how well a chart's instrument matches the traded one. Higher is
        /// better, 0 means no match.
        ///
        /// NinjaTrader instrument names are inconsistent between contexts - a
        /// position may report "ES 09-26" while a chart reports "ES 09-26" or
        /// "ES" or "ES DEC26". Exact match wins; a shared root is accepted; and
        /// anything else is refused, because photographing the WRONG chart is far
        /// worse than photographing none. A journal that quietly shows you the
        /// wrong instrument teaches you the wrong lesson.
        /// </summary>
        public static int MatchScore(string chartInstrument, string tradedInstrument)
        {
            if (string.IsNullOrEmpty(chartInstrument) || string.IsNullOrEmpty(tradedInstrument))
                return 0;

            string a = chartInstrument.Trim().ToUpperInvariant();
            string b = tradedInstrument.Trim().ToUpperInvariant();

            if (a == b) return 100;

            string ra = Root(a), rb = Root(b);
            if (ra.Length > 0 && ra == rb) return 50;

            return 0;
        }

        /// <summary>Ticker root: everything before the first space or hyphen.</summary>
        public static string Root(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string t = s.Trim().ToUpperInvariant();

            int cut = t.Length;
            int sp = t.IndexOf(' ');
            if (sp >= 0 && sp < cut) cut = sp;
            int hy = t.IndexOf('-');
            if (hy >= 0 && hy < cut) cut = hy;

            return t.Substring(0, cut);
        }

        /// <summary>
        /// Pick the best-matching chart from a list of (instrument, visual) pairs.
        /// Returns -1 when nothing matches well enough.
        /// </summary>
        public static int BestChartIndex(List<string> chartInstruments, string tradedInstrument)
        {
            return BestChartIndex(chartInstruments, tradedInstrument, null);
        }

        public static int BestChartIndex(List<string> chartInstruments, string tradedInstrument,
                                         List<bool> isActive)
        {
            return BestChartIndex(chartInstruments, tradedInstrument, isActive, null);
        }

        /// <summary>
        /// Pick the chart to photograph.
        ///
        /// Instrument match decides it. Where several charts show the same
        /// instrument, the chart's BAR PERIOD decides next, and only then does
        /// being the active window break what is left.
        ///
        /// FOCUS USED TO BE THE TIEBREAK, AND IT WAS WRONG.
        ///
        /// "if you notice this states a daily trade but no trade and the funny
        /// part that chart should have 2 tabs which it doesnt so that is very
        /// weird"
        ///
        /// He runs a Daily NQ chart for context beside the range chart he trades
        /// from. Both say NQ SEP26, both have Chart Trader on the same account,
        /// so every earlier test tied and focus settled it - and a context chart
        /// is very often the one just clicked. On 11 August the daily was in
        /// front at the moment of entry and a fifty-five second scalp was filed
        /// with two months of daily candles. The files gave it away: 23 KB,
        /// against 850 KB for the chart he actually traded.
        ///
        /// A daily chart cannot show what an intraday entry looked like, whether
        /// or not it was in front. So the period outranks focus - +20 against
        /// +10 - while both stay far below the fifty points between a shared
        /// ticker root and an exact instrument match, because photographing the
        /// WRONG INSTRUMENT is still the worst outcome of all and nothing here
        /// may reorder that.
        /// </summary>
        public static int BestChartIndex(List<string> chartInstruments, string tradedInstrument,
                                         List<bool> isActive, List<bool> isHigherTimeframe)
        {
            int best = -1, bestScore = 0;
            if (chartInstruments == null) return -1;

            for (int i = 0; i < chartInstruments.Count; i++)
            {
                int score = MatchScore(chartInstruments[i], tradedInstrument);
                if (score <= 0) continue;

                // An intraday chart outranks a daily/weekly/monthly one. A chart
                // whose period could not be read is treated as intraday, so an
                // unreadable period can never cost a chart the picture.
                bool higher = isHigherTimeframe != null
                           && i < isHigherTimeframe.Count
                           && isHigherTimeframe[i];
                if (!higher) score += 20;

                // Active charts get a bump, but never enough to beat a real
                // instrument match with a non-matching one - nor, now, to put a
                // daily chart ahead of the chart he traded from.
                if (isActive != null && i < isActive.Count && isActive[i]) score += 10;

                if (score > bestScore) { bestScore = score; best = i; }
            }
            return best;
        }

        /// <summary>
        /// The chart's bar period as NinjaTrader names it - "Minute", "Range",
        /// "Tick", "Day" - or "" when it cannot be read.
        ///
        /// Reflection, like every other NinjaTrader touch in this file: a direct
        /// reference to a type that moved between versions would take the whole
        /// add-on down, and the cushion figure a trader is relying on mid-session
        /// is worth more than a screenshot.
        /// </summary>
        public static string PeriodOf(object chartWindow)
        {
            string[] paths = new string[]
            {
                "ActiveChartControl.BarsPeriod.BarsPeriodType",
                "ChartControl.BarsPeriod.BarsPeriodType",
                "ActiveChartControl.Bars.BarsPeriod.BarsPeriodType",
                "ChartControl.Bars.BarsPeriod.BarsPeriodType",
                "BarsPeriod.BarsPeriodType"
            };

            for (int i = 0; i < paths.Length; i++)
            {
                string v = ReadPath(chartWindow, paths[i]);
                if (!string.IsNullOrEmpty(v)) return v;
            }
            return "";
        }

        /// <summary>
        /// Whether a bar period is too coarse to show what an intraday entry
        /// looked like.
        ///
        /// An unknown or unreadable period answers FALSE. Everything a trader
        /// actually scalps from - minute, second, tick, volume, range, renko -
        /// is intraday, so "not one of the four coarse ones" is the safe
        /// default, and a chart Ballast could not interrogate is left exactly
        /// where it was rather than quietly demoted.
        /// </summary>
        public static bool IsHigherTimeframe(string periodName)
        {
            if (string.IsNullOrEmpty(periodName)) return false;

            string p = periodName.Trim();
            return string.Equals(p, "Day", StringComparison.OrdinalIgnoreCase)
                || string.Equals(p, "Week", StringComparison.OrdinalIgnoreCase)
                || string.Equals(p, "Month", StringComparison.OrdinalIgnoreCase)
                || string.Equals(p, "Year", StringComparison.OrdinalIgnoreCase);
        }

        // ── Rendering ────────────────────────────────────────────────────────

        /// <summary>
        /// Render a WPF visual to a PNG. This part is ordinary WPF and is the only
        /// piece here that touches NinjaTrader not at all.
        /// </summary>
        public static SnapshotResult RenderToPng(FrameworkElement element, string path)
        {
            SnapshotResult r = new SnapshotResult();

            try
            {
                if (element == null) { r.Problem = "No chart element to capture."; return r; }

                // NinjaTrader charts live on their own UI threads. Touching one
                // from Ballast's thread throws, so the whole render is marshalled
                // onto the chart's dispatcher and the result handed back.
                if (element.Dispatcher != null && !element.Dispatcher.CheckAccess())
                {
                    SnapshotResult inner = null;
                    element.Dispatcher.Invoke(new Action(delegate
                    {
                        inner = RenderOnOwningThread(element, path);
                    }));
                    return inner ?? r;
                }

                return RenderOnOwningThread(element, path);
            }
            catch (Exception ex)
            {
                r.Problem = "Could not save the chart image: " + ex.Message;
                return r;
            }
        }

        // ── Screen capture ───────────────────────────────────────────────────
        //
        // WHY NOT RenderTargetBitmap.
        //
        // The first version rendered the chart's WPF visual tree, and produced a
        // picture of an EMPTY chart: correct toolbar, correct instrument in the
        // dropdown, no bars. NinjaTrader does not draw chart bars with WPF - it
        // draws them onto a DirectX surface hosted inside the window. That
        // surface is invisible to RenderTargetBitmap, which walks the WPF tree
        // only, so the one part of the picture that mattered was the one part it
        // could never capture.
        //
        // The fix is to photograph the screen region the window occupies, which
        // captures whatever is actually displayed there regardless of how it was
        // drawn. Done through GDI directly rather than System.Drawing, so no
        // assembly NinjaTrader might not reference is required.
        //
        // The trade-off, stated plainly: this captures what is ON SCREEN. If
        // another window is covering the chart, that is what ends up in the
        // picture. A minimised chart cannot be captured at all.

        [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
        [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest, int dx, int dy, int w, int h,
                                                                   IntPtr src, int sx, int sy, int rop);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);

        private const int SRCCOPY = 0x00CC0020;
        private const int CAPTUREBLT = 0x40000000;

        /// <summary>
        /// Photograph a rectangle of the screen. Returns null on any failure -
        /// this runs on the trading thread and a screenshot is never worth an
        /// exception there.
        /// </summary>
        private static BitmapSource CaptureScreenRegion(int x, int y, int w, int h)
        {
            IntPtr desktop = IntPtr.Zero, srcDc = IntPtr.Zero, memDc = IntPtr.Zero, bmp = IntPtr.Zero;

            try
            {
                desktop = GetDesktopWindow();
                srcDc = GetWindowDC(desktop);
                if (srcDc == IntPtr.Zero) return null;

                memDc = CreateCompatibleDC(srcDc);
                bmp = CreateCompatibleBitmap(srcDc, w, h);
                if (memDc == IntPtr.Zero || bmp == IntPtr.Zero) return null;

                IntPtr old = SelectObject(memDc, bmp);
                bool ok = BitBlt(memDc, 0, 0, w, h, srcDc, x, y, SRCCOPY | CAPTUREBLT);
                SelectObject(memDc, old);
                if (!ok) return null;

                BitmapSource src = Imaging.CreateBitmapSourceFromHBitmap(
                    bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            catch { return null; }
            finally
            {
                if (bmp != IntPtr.Zero) DeleteObject(bmp);
                if (memDc != IntPtr.Zero) DeleteDC(memDc);
                if (srcDc != IntPtr.Zero && desktop != IntPtr.Zero) ReleaseDC(desktop, srcDc);
            }
        }

        private static SnapshotResult RenderOnOwningThread(FrameworkElement element, string path)
        {
            SnapshotResult r = new SnapshotResult();

            try
            {
                double w = element.ActualWidth, h = element.ActualHeight;
                if (w < 50 || h < 50)
                {
                    r.Problem = "Chart is not visible (minimised or zero-sized).";
                    return r;
                }

                double scale = 1.0;
                if (MaxWidth > 0 && w > MaxWidth) scale = MaxWidth / w;

                int pw = (int)Math.Round(w * scale);
                int ph = (int)Math.Round(h * scale);
                if (pw < 1 || ph < 1) { r.Problem = "Chart too small to capture."; return r; }

                BitmapSource shot = null;

                // Screen capture first - it is the only way to get the bars.
                try
                {
                    Point tl = element.PointToScreen(new Point(0, 0));
                    Point br = element.PointToScreen(new Point(w, h));

                    int sx = (int)Math.Round(tl.X), sy = (int)Math.Round(tl.Y);
                    int sw = (int)Math.Round(br.X - tl.X), sh = (int)Math.Round(br.Y - tl.Y);

                    if (sw > 50 && sh > 50) shot = CaptureScreenRegion(sx, sy, sw, sh);
                }
                catch { shot = null; }

                if (shot == null)
                {
                    // Falls back to the WPF render. It will miss the bars, but a
                    // picture of the toolbar and the instrument still beats none.
                    RenderTargetBitmap bmp = new RenderTargetBitmap(
                        pw, ph, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                    bmp.Render(element);
                    shot = bmp;
                    LastProblem = "Screen capture unavailable - the chart image may be missing its bars.";
                }

                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                PngBitmapEncoder enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(shot));

                using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                    enc.Save(fs);

                r.Ok = true;
                r.Path = path;
                return r;
            }
            catch (Exception ex)
            {
                r.Problem = "Could not save the chart image: " + ex.Message;
                return r;
            }
        }

        // ── Finding charts, entirely by reflection ───────────────────────────

        /// <summary>
        /// Walk the open windows and return every chart we can identify, as
        /// (instrument name, visual) pairs.
        ///
        /// All reflection, deliberately. See the file header: a compile-time
        /// reference to a chart internal that moved between NinjaTrader versions
        /// would stop the entire add-on from loading, and the cushion figure
        /// matters far more than the screenshots do.
        /// </summary>
        public static void FindCharts(out List<string> instruments, out List<FrameworkElement> visuals)
        {
            List<bool> ignored;
            FindCharts(out instruments, out visuals, out ignored);
        }

        public static void FindCharts(out List<string> instruments, out List<FrameworkElement> visuals,
                                      out List<bool> active)
        {
            List<string> ignoredAccounts;
            FindCharts(out instruments, out visuals, out active, out ignoredAccounts);
        }

        public static void FindCharts(out List<string> instruments, out List<FrameworkElement> visuals,
                                      out List<bool> active, out List<string> accounts)
        {
            List<bool> ignoredPeriods;
            FindCharts(out instruments, out visuals, out active, out accounts, out ignoredPeriods);
        }

        /// <summary>
        /// As above, and which of them are daily/weekly/monthly charts - the one
        /// fact that stops a context chart being photographed instead of the
        /// chart he traded from.
        /// </summary>
        public static void FindCharts(out List<string> instruments, out List<FrameworkElement> visuals,
                                      out List<bool> active, out List<string> accounts,
                                      out List<bool> higherTimeframe)
        {
            instruments = new List<string>();
            visuals = new List<FrameworkElement>();
            active = new List<bool>();
            accounts = new List<string>();
            higherTimeframe = new List<bool>();

            try
            {
                List<object> windows = AllWindows();

                for (int i = 0; i < windows.Count; i++)
                {
                    try
                    {
                        FrameworkElement w = windows[i] as FrameworkElement;
                        if (w == null) continue;

                        string typeName = w.GetType().Name ?? "";
                        if (typeName.IndexOf("Chart", StringComparison.OrdinalIgnoreCase) < 0) continue;

                        // A chart whose instrument we cannot read is STILL a chart.
                        // Skipping those is why a screen full of charts once
                        // reported "no chart window found".
                        instruments.Add(InstrumentOf(w));
                        visuals.Add(w);
                        active.Add(IsActiveWindow(w));
                        accounts.Add(ChartTraderAccountOf(w));
                        higherTimeframe.Add(IsHigherTimeframe(PeriodOf(w)));
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                LastProblem = "Could not enumerate chart windows: " + ex.Message;
            }
        }

        /// <summary>
        /// Which account this chart's Chart Trader is pointed at, or "" when it
        /// has none or cannot be read.
        ///
        /// This is what tells three charts of the same instrument apart. Matching
        /// on the instrument alone cannot: with NQ open on a 50-tick, an 80-tick
        /// and a minute chart, every one of them "matches", so the picture came
        /// from whichever chart happened to be focused - which is how a trade on
        /// one account was filed with a photograph of a different chart. A
        /// journal that shows you the wrong chart is worse than one that shows
        /// you none, because you will draw a conclusion from it.
        /// </summary>
        public static string ChartTraderAccountOf(object chartWindow)
        {
            try
            {
                if (chartWindow == null) return "";

                PropertyInfo traderProp = chartWindow.GetType().GetProperty("ChartTrader");
                if (traderProp == null) return "";

                object trader = traderProp.GetValue(chartWindow, null);
                if (trader == null) return "";

                PropertyInfo accountProp = trader.GetType().GetProperty("Account");
                if (accountProp == null) return "";

                object account = accountProp.GetValue(trader, null);
                if (account == null) return "";

                PropertyInfo nameProp = account.GetType().GetProperty("Name");
                if (nameProp == null) return "";

                return (nameProp.GetValue(account, null) as string) ?? "";
            }
            catch { return ""; }
        }

        /// <summary>
        /// Every top-level window, from wherever they can be found.
        ///
        /// Application.Current.Windows returns ZERO under NinjaTrader, which is
        /// what the first version relied on. NinjaTrader runs its windows on their
        /// own UI threads, and that collection only ever contains windows created
        /// on the Application's own thread - so charts were invisible to it no
        /// matter how many were open.
        ///
        /// NinjaTrader keeps its own list on Core.Globals. It is reached by
        /// reflection here for the same reason as everything else in this file:
        /// a hard reference that moved between versions would take the add-on
        /// down, and screenshots are not worth that.
        /// </summary>
        public static List<object> AllWindows()
        {
            List<object> found = new List<object>();

            // 1. NinjaTrader's own registry of windows - the one that works.
            string[] typeNames = new string[]
            {
                "NinjaTrader.Core.Globals, NinjaTrader.Core",
                "NinjaTrader.Core.Globals"
            };
            string[] memberNames = new string[] { "AllWindows", "AllToolWindows", "AllNTWindows" };

            for (int t = 0; t < typeNames.Length; t++)
            {
                Type gt = null;
                try { gt = Type.GetType(typeNames[t], false); } catch { }
                if (gt == null) continue;

                for (int m = 0; m < memberNames.Length; m++)
                {
                    try
                    {
                        object coll = null;

                        PropertyInfo pi = gt.GetProperty(memberNames[m],
                            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                        if (pi != null) coll = pi.GetValue(null, null);

                        if (coll == null)
                        {
                            FieldInfo fi = gt.GetField(memberNames[m],
                                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                            if (fi != null) coll = fi.GetValue(null);
                        }

                        System.Collections.IEnumerable seq = coll as System.Collections.IEnumerable;
                        if (seq == null) continue;

                        foreach (object o in seq)
                            if (o != null && !found.Contains(o)) found.Add(o);
                    }
                    catch { }
                }

                if (found.Count > 0) break;
            }

            // 2. Fall back to WPF's own list. Empty under NinjaTrader, but correct
            //    anywhere else and free to try.
            try
            {
                if (Application.Current != null)
                    foreach (Window w in Application.Current.Windows)
                        if (w != null && !found.Contains(w)) found.Add(w);
            }
            catch { }

            return found;
        }

        /// <summary>
        /// Is this the window the trader is actually looking at? Read by
        /// reflection, and false rather than a guess when it cannot be told.
        /// </summary>
        public static bool IsActiveWindow(object window)
        {
            try
            {
                string v = ReadPath(window, "IsActive");
                if (v == "True") return true;
                if (v == "False") return false;

                v = ReadPath(window, "IsKeyboardFocusWithin");
                return v == "True";
            }
            catch { return false; }
        }

        /// <summary>
        /// Dig an instrument name out of a chart window without referencing any
        /// NinjaTrader type. Tries the property paths NinjaTrader 8 is known to
        /// use, and gives up quietly rather than guessing.
        /// </summary>
        public static string InstrumentOf(object chartWindow)
        {
            string[] paths = new string[]
            {
                "ActiveChartControl.Instrument.FullName",
                "ChartControl.Instrument.FullName",
                "ActiveChartControl.Instrument.MasterInstrument.Name",
                "ChartControl.Instrument.MasterInstrument.Name",
                "Instrument.FullName",
                "Instrument.MasterInstrument.Name",
                "ActiveChartControl.Instruments[0].FullName",
                "ChartControl.Instruments[0].FullName",
                "MainTabControl.SelectedContent.ChartControl.Instrument.FullName",
                "SelectedChartControl.Instrument.FullName",
                "Caption",
                "Title"
            };

            for (int i = 0; i < paths.Length; i++)
            {
                string v = ReadPath(chartWindow, paths[i]);
                if (!string.IsNullOrEmpty(v)) return v;
            }
            return "";
        }

        /// <summary>Follow a dotted property path, returning "" on any hiccup.</summary>
        public static string ReadPath(object root, string path)
        {
            try
            {
                object cur = root;
                string[] parts = path.Split('.');

                for (int i = 0; i < parts.Length; i++)
                {
                    if (cur == null) return "";

                    string part = parts[i];
                    int idx = -1;

                    int br = part.IndexOf('[');
                    if (br > 0 && part.EndsWith("]"))
                    {
                        string num = part.Substring(br + 1, part.Length - br - 2);
                        if (!int.TryParse(num, out idx)) idx = -1;
                        part = part.Substring(0, br);
                    }

                    PropertyInfo pi = cur.GetType().GetProperty(part,
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

                    if (pi == null)
                    {
                        FieldInfo fi = cur.GetType().GetField(part,
                            BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                        if (fi == null) return "";
                        cur = fi.GetValue(cur);
                    }
                    else
                    {
                        cur = pi.GetValue(cur, null);
                    }

                    if (idx >= 0 && cur != null)
                    {
                        System.Collections.IEnumerable seq = cur as System.Collections.IEnumerable;
                        if (seq == null) return "";
                        int n = 0; object picked = null;
                        foreach (object o in seq) { if (n++ == idx) { picked = o; break; } }
                        cur = picked;
                    }
                }

                return cur == null ? "" : cur.ToString();
            }
            catch { return ""; }
        }

        // ── The one call the rest of Ballast makes ───────────────────────────

        /// <summary>
        /// Photograph the chart showing <paramref name="instrument"/>. Returns the
        /// saved path, or "" with LastProblem set. Never throws: it is called from
        /// the trading loop, and no screenshot is worth an exception there.
        /// </summary>
        /// <summary>
        /// What Ballast can currently see. Shown in the window so a capture
        /// problem can be diagnosed from the trader's own screen rather than
        /// guessed at from here.
        /// </summary>
        public static string Diagnose()
        {
            try
            {
                List<string> names;
                List<FrameworkElement> visuals;
                FindCharts(out names, out visuals);

                if (visuals.Count == 0)
                {
                    int windows = 0;
                    List<string> types = new List<string>();
                    try
                    {
                        List<object> all = AllWindows();
                        for (int i = 0; i < all.Count; i++)
                        {
                            windows++;
                            string tn = all[i].GetType().Name;
                            if (!types.Contains(tn) && types.Count < 14) types.Add(tn);
                        }
                    }
                    catch { }

                    return "No chart windows recognised. Ballast can see " + windows
                         + " windows: " + string.Join(", ", types.ToArray())
                         + ". Send this line over and it can be taught the right one.";
                }

                StringBuilder sb = new StringBuilder();
                sb.Append("Found ").Append(visuals.Count).Append(" chart window");
                sb.Append(visuals.Count == 1 ? ": " : "s: ");
                for (int i = 0; i < names.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(names[i].Length > 0 ? names[i] : "(instrument unreadable)");
                }
                return sb.ToString();
            }
            catch (Exception ex) { return "Diagnosis failed: " + ex.Message; }
        }

        /// <summary>
        /// The chart each account's OPEN trade was photographed on.
        ///
        /// Which chart to photograph is decided partly by which window is
        /// active, because that is genuinely the best signal at the moment an
        /// order goes in - the trader is looking at the chart they just clicked.
        /// At the EXIT it is the worst possible signal: minutes have passed, the
        /// trader has been looking at other charts, and the exit was very often
        /// filled by a bracket while their attention was somewhere else
        /// entirely. The result was an entry from one chart and an exit from
        /// another, which is worse than no picture, because the pair reads as a
        /// record of a trade that never happened.
        ///
        /// So the exit follows the entry. Whatever chart was photographed when
        /// the position opened is the one photographed when it closes, whatever
        /// happens to be focused at the time.
        ///
        /// Weak references: a chart the trader has since closed must not be kept
        /// alive by Ballast remembering it.
        /// </summary>
        private static readonly Dictionary<string, WeakReference> openTradeChart =
            new Dictionary<string, WeakReference>(StringComparer.OrdinalIgnoreCase);

        private static readonly object chartGate = new object();

        private static void RememberChart(string account, FrameworkElement chart)
        {
            if (string.IsNullOrEmpty(account) || chart == null) return;
            lock (chartGate) { openTradeChart[account] = new WeakReference(chart); }
        }

        /// <summary>
        /// Index of this account's remembered chart within the current list, or
        /// -1 if it was never recorded or has since been closed.
        /// </summary>
        private static int RememberedIndex(string account, List<FrameworkElement> visuals)
        {
            if (string.IsNullOrEmpty(account) || visuals == null) return -1;

            object target = null;
            lock (chartGate)
            {
                WeakReference wr;
                if (!openTradeChart.TryGetValue(account, out wr)) return -1;
                if (wr != null && wr.IsAlive) target = wr.Target;
            }

            if (target == null) return -1;

            for (int i = 0; i < visuals.Count; i++)
                if (ReferenceEquals(visuals[i], target)) return i;

            return -1;
        }

        /// <summary>
        /// The chart whose Chart Trader is set to this account. Prefers one that
        /// also shows the traded instrument; falls back to the account alone,
        /// because a chart bound to the right account is still a better guess
        /// than a chart that merely happens to be in front.
        ///
        /// Returns -1 when the account appears on no chart, or on more than one
        /// with no instrument to separate them - an ambiguous answer is not an
        /// answer, and the caller's existing search is a better one.
        /// </summary>
        public static int AccountChartIndex(List<string> chartAccounts, List<string> instruments,
                                            string account, string instrument)
        {
            return AccountChartIndex(chartAccounts, instruments, account, instrument, null);
        }

        /// <summary>
        /// As above, but breaking a tie with the chart that is actually in
        /// front of him.
        ///
        /// "if you notice this states a daily trade but no trade and the funny
        /// part that chart should have 2 tabs which it doesnt so that is very
        /// weird"
        ///
        /// He runs a Daily NQ chart for context beside the 60-Range NQ chart he
        /// trades from. Both say NQ SEP26, and both have Chart Trader pointed at
        /// the same account, so every test above them ties - and the tie was
        /// broken by whichever window happened to enumerate first. That is how a
        /// daily chart with no trade on it and one tab ended up in a card asking
        /// him whether he would take the entry again.
        ///
        /// Focus was made the tiebreak here, because it looked like the only one
        /// available that meant anything. It was not enough, and the same daily
        /// chart came back on 11 August: a context chart is very often the one
        /// just clicked, so on that entry the daily WAS the focused window and
        /// won the tie fairly. A fifty-five second scalp was filed with two
        /// months of daily candles.
        ///
        /// So the bar period now sits above focus. A daily chart cannot show what
        /// an intraday entry looked like whether or not it is in front, and
        /// focus decides only between charts of the same kind.
        /// </summary>
        public static int AccountChartIndex(List<string> chartAccounts, List<string> instruments,
                                            string account, string instrument, List<bool> active)
        {
            return AccountChartIndex(chartAccounts, instruments, account, instrument, active, null);
        }

        /// <summary>
        /// As above, knowing which charts are daily/weekly/monthly.
        ///
        /// With no period information every chart counts as intraday, so this
        /// behaves exactly as the five-argument form always did.
        /// </summary>
        public static int AccountChartIndex(List<string> chartAccounts, List<string> instruments,
                                            string account, string instrument, List<bool> active,
                                            List<bool> isHigherTimeframe)
        {
            if (chartAccounts == null || string.IsNullOrEmpty(account)) return -1;

            // Per group: the first intraday-and-focused, the first intraday, the
            // first focused, and the first of any kind. Read in that order, which
            // is the whole rule.
            int withCount = 0, withIntradayActive = -1, withIntraday = -1, withActive = -1, withAny = -1;
            int onlyCount = 0, onlyIntradayActive = -1, onlyIntraday = -1, onlyActive = -1, onlyAny = -1;

            for (int i = 0; i < chartAccounts.Count; i++)
            {
                if (!string.Equals(chartAccounts[i], account, StringComparison.OrdinalIgnoreCase)) continue;

                bool focused = active != null && i < active.Count && active[i];

                // A chart whose period could not be read counts as intraday, so
                // an unreadable period never costs a chart the picture.
                bool higher = isHigherTimeframe != null
                           && i < isHigherTimeframe.Count
                           && isHigherTimeframe[i];

                onlyCount++;
                if (onlyAny < 0) onlyAny = i;
                if (focused && onlyActive < 0) onlyActive = i;
                if (!higher && onlyIntraday < 0) onlyIntraday = i;
                if (!higher && focused && onlyIntradayActive < 0) onlyIntradayActive = i;

                if (instruments != null && i < instruments.Count
                    && MatchScore(instruments[i], instrument) > 0)
                {
                    withCount++;
                    if (withAny < 0) withAny = i;
                    if (focused && withActive < 0) withActive = i;
                    if (!higher && withIntraday < 0) withIntraday = i;
                    if (!higher && focused && withIntradayActive < 0) withIntradayActive = i;
                }
            }

            // One chart of this account showing this instrument is not a choice.
            // Even a daily one beats refusing, because there is nothing else of
            // his to photograph.
            if (withCount == 1) return withAny;

            if (withCount > 1)
            {
                if (withIntradayActive >= 0) return withIntradayActive;
                if (withIntraday >= 0) return withIntraday;
                if (withActive >= 0) return withActive;
                return withAny;
            }

            if (onlyCount == 1) return onlyAny;

            if (onlyCount > 1)
            {
                if (onlyIntradayActive >= 0) return onlyIntradayActive;

                // The focused one is a daily and he has an intraday chart on this
                // account: take the intraday. Neither shows the instrument, so
                // this is already a fallback - but it is still no reason to hand
                // back the coarsest chart on screen.
                if (onlyActive >= 0 && onlyIntraday >= 0) return onlyIntraday;

                if (onlyActive >= 0) return onlyActive;

                // Unchanged: several of his charts, nothing focused, none showing
                // the instrument. Silence beats a guess.
                return -1;
            }

            return -1;
        }

        private static void ForgetChart(string account)
        {
            if (string.IsNullOrEmpty(account)) return;
            lock (chartGate) { openTradeChart.Remove(account); }
        }

        public static string Capture(string root, string account, string instrument,
                                     DateTime when, bool isEntry)
        {
            if (!Enabled) return "";

            try
            {
                List<string> names;
                List<FrameworkElement> visuals;
                List<bool> active;
                List<string> chartAccounts;
                List<bool> higher;
                FindCharts(out names, out visuals, out active, out chartAccounts, out higher);

                if (names.Count == 0)
                {
                    LastProblem = "No chart window found to photograph.";
                    return "";
                }

                // On the way out, use the chart the entry was taken from. Only
                // fall through to the usual search if that chart has since been
                // closed, or the entry was never photographed.
                int idx = -1;
                if (!isEntry) idx = RememberedIndex(account, visuals);

                // The account this trade belongs to, matched against what each
                // chart's Chart Trader is pointed at. This outranks everything
                // else, because it is the only signal that can tell three charts
                // of the same instrument apart - and being focused is not it.
                if (idx < 0) idx = AccountChartIndex(chartAccounts, names, account, instrument,
                                                     active, higher);

                if (idx < 0) idx = BestChartIndex(names, instrument, active, higher);

                // Nothing matched by instrument, but exactly one chart is active -
                // that is the one being traded on.
                if (idx < 0)
                {
                    for (int i = 0; i < active.Count; i++)
                        if (active[i]) { idx = i; break; }
                }

                if (idx < 0 && names.Count == 1)
                {
                    // Only one chart open, so there is nothing to confuse it with.
                    idx = 0;
                }

                if (idx < 0)
                {
                    // With several charts open and none matching, refusing is
                    // right: a journal showing the wrong instrument teaches the
                    // wrong lesson, which is worse than no picture at all.
                    LastProblem = "Found " + names.Count + " charts but none matching "
                                + instrument + " - nothing captured.";
                    return "";
                }

                string path = FullPath(root, account, instrument, when, isEntry);
                SnapshotResult r = RenderToPng(visuals[idx], path);

                if (!r.Ok) { LastProblem = r.Problem; return ""; }

                // Opening a position pins the chart for this account; closing it
                // releases it, so the next trade starts the choice again rather
                // than inheriting a chart from an hour ago.
                if (isEntry) RememberChart(account, visuals[idx]);
                else ForgetChart(account);

                LastProblem = "";
                return r.Path;
            }
            catch (Exception ex)
            {
                LastProblem = "Chart capture failed: " + ex.Message;
                return "";
            }
        }
    }
}
