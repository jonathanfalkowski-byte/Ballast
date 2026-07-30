// ─────────────────────────────────────────────────────────────────────────────
// Ballast — TradeReport.cs
//
// Builds a self-contained HTML page for a trade, or for a whole day of them,
// and opens it in the browser.
//
// WHY HTML RATHER THAN A WPF WINDOW.
//
// The journal shows thumbnails inline, which is enough to glance at. But a
// proper review wants two large charts side by side with every recorded figure
// underneath, and building that as a WPF window means image panning, zoom,
// scroll and layout code - a lot of surface area, none of it interesting, all of
// it untestable inside NinjaTrader.
//
// A generated page gets all of that free from the browser, opens on a second
// monitor, prints, and can be sent to somebody. And because the generation is
// pure string work with no NinjaTrader or WPF types anywhere in this file, every
// line of it is unit tested - which is not true of a single pixel of WPF.
//
// TWO MODES, because there are two different jobs here and one file cannot do
// both well.
//
//   VIEWING  - images referenced by file:// path. Tiny, instant, and written to
//              ONE reusable file that is overwritten every time. It is a viewer,
//              not an artifact, so nothing accumulates.
//
//   KEEPING  - images embedded as base64. Several megabytes, but genuinely
//              self-contained: it can be emailed, and it still works after the
//              chart images have been pruned off disk.
//
// The first version only had the middle of these - a permanent-looking file with
// transient content. Every click wrote another timestamped page into a folder
// that grew forever, and each one silently broke the day image retention deleted
// the PNGs it pointed at. It looked like an archive and behaved like a cache.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Ballast
{
    public static class TradeReport
    {
        /// <summary>
        /// Escape text for HTML. Notes are free text a trader typed - one
        /// unescaped angle bracket and the rest of the page silently disappears.
        /// </summary>
        public static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '&') sb.Append("&amp;");
                else if (c == '<') sb.Append("&lt;");
                else if (c == '>') sb.Append("&gt;");
                else if (c == '"') sb.Append("&quot;");
                else if (c == '\'') sb.Append("&#39;");
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Read an image and return it as a data: URI, or "" if that is not
        /// possible. Used only when SAVING a copy - embedding is what makes a
        /// report survive the image pruning that would otherwise gut it.
        /// </summary>
        public static string DataUri(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "";

                FileInfo fi = new FileInfo(path);
                // A single absurd file should not turn a report into a 200MB page.
                if (fi.Length > 12 * 1024 * 1024) return "";

                byte[] bytes = File.ReadAllBytes(path);
                return "data:image/png;base64," + Convert.ToBase64String(bytes);
            }
            catch { return ""; }
        }

        /// <summary>
        /// How an image is referenced. Embedded survives image pruning and can be
        /// sent to someone; linked is small and instant.
        /// </summary>
        public static bool EmbedImages = false;

        /// <summary>The src attribute for an image, honouring the current mode.</summary>
        public static string ImageSrc(string path)
        {
            if (EmbedImages)
            {
                string data = DataUri(path);
                if (data.Length > 0) return data;
                // Embedding failed - a link is better than a broken image tag.
            }
            return FileUri(path);
        }

        /// <summary>A Windows path as a browser-usable file URI.</summary>
        public static string FileUri(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string p = path.Replace('\\', '/');
            if (!p.StartsWith("/")) p = "/" + p;
            return "file://" + Uri.EscapeUriString(p);
        }

        public static string Money(double v)
        {
            double r = Math.Round(v);
            return (r < 0 ? "-$" : "$") + Math.Abs(r).ToString("N0", CultureInfo.InvariantCulture);
        }

        private static string Row(string label, string value)
        {
            return "<div class=\"f\"><span class=\"k\">" + Esc(label)
                 + "</span><span class=\"v\">" + Esc(value) + "</span></div>";
        }

        /// <summary>One trade: both charts, and every figure Ballast recorded.</summary>
        public static string TradeCard(BallastTrade e)
        {
            if (e == null) return "";

            StringBuilder sb = new StringBuilder();

            string cls = e.Pnl >= 0 ? "win" : "loss";
            sb.Append("<section class=\"card\">");

            sb.Append("<h2><span class=\"acct\">").Append(Esc(e.AccountName)).Append("</span> ")
              .Append(Esc(e.DirectionLabel)).Append(" ").Append(e.MaxContracts).Append(" ")
              .Append(Esc(e.Instrument.Length > 0 ? e.Instrument : "position"))
              .Append(" <span class=\"pnl ").Append(cls).Append("\">").Append(Money(e.Pnl))
              .Append("</span></h2>");

            sb.Append("<div class=\"when\">")
              .Append(e.EntryTime.ToString("ddd d MMM yyyy  HH:mm:ss", CultureInfo.InvariantCulture))
              .Append("  &rarr;  ")
              .Append(e.ExitTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture))
              .Append("   (").Append(Math.Round(e.DurationMinutes)).Append(" min held)</div>");

            // Charts.
            sb.Append("<div class=\"shots\">");
            if (e.EntryImage.Length > 0)
                sb.Append("<figure><figcaption>At entry</figcaption><img src=\"")
                  .Append(ImageSrc(e.EntryImage)).Append("\" alt=\"chart at entry\"></figure>");
            if (e.ExitImage.Length > 0)
                sb.Append("<figure><figcaption>At exit</figcaption><img src=\"")
                  .Append(ImageSrc(e.ExitImage)).Append("\" alt=\"chart at exit\"></figure>");
            if (e.EntryImage.Length == 0 && e.ExitImage.Length == 0)
                sb.Append("<p class=\"none\">No chart photographs were captured for this trade.</p>");
            sb.Append("</div>");

            // What the machine saw, which is the half worth reviewing.
            sb.Append("<div class=\"facts\">");
            sb.Append(Row("Trade of the day", e.TradeNumberToday.ToString(CultureInfo.InvariantCulture)));
            sb.Append(Row("Ballast advised at entry", Humanise(e.AdviceAtEntry)));
            if (e.TakenAgainstAdvice)
                sb.Append("<div class=\"warn\">Opened after Ballast advised against it.</div>");
            sb.Append(Row("Room left at entry", Money(e.CushionAtEntry)));
            sb.Append(Row("Account floor at entry", Money(e.FloorAtEntry)));
            sb.Append(Row("Day P&L before this trade", Money(e.DailyPnlBefore)));
            if (e.PreviousTradeWasLoss && e.MinutesSincePreviousLoss >= 0)
                sb.Append(Row("Minutes since the last loss",
                    e.MinutesSincePreviousLoss.ToString(CultureInfo.InvariantCulture)));
            sb.Append(Row("Inside your session window", e.InsideSessionWindow ? "yes" : "no"));
            if (e.Automated) sb.Append(Row("Taken by", "a strategy, not by hand"));
            sb.Append("</div>");

            // What the trader said.
            sb.Append("<div class=\"facts\">");
            sb.Append(Row("Planned?", e.Planned.Length > 0 ? e.Planned : "not tagged"));
            if (e.Feeling.Length > 0) sb.Append(Row("Feeling", e.Feeling));
            if (e.SessionPlan.Length > 0) sb.Append(Row("Plan that day", e.SessionPlan));
            sb.Append("</div>");

            if (e.Note.Length > 0)
                sb.Append("<blockquote>").Append(Esc(e.Note)).Append("</blockquote>");

            sb.Append("</section>");
            return sb.ToString();
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
                case "Trade":        return "clear to trade";
                case "None":         return "nothing to act on";
                default:             return action.Length > 0 ? action : "unknown";
            }
        }

        /// <summary>A whole page: title, summary line, then a card per trade.</summary>
        public static string Page(string title, List<BallastTrade> trades)
        {
            StringBuilder sb = new StringBuilder();

            int n = trades == null ? 0 : trades.Count;
            double net = 0, wins = 0;
            if (trades != null)
                for (int i = 0; i < trades.Count; i++)
                {
                    net += trades[i].Pnl;
                    if (trades[i].Pnl > 0) wins++;
                }

            sb.Append("<!doctype html><html><head><meta charset=\"utf-8\">");
            sb.Append("<title>").Append(Esc(title)).Append(" - Ballast</title>");
            sb.Append("<style>");
            sb.Append("body{background:#0e1116;color:#e8edf3;font:15px/1.5 -apple-system,Segoe UI,Arial,sans-serif;margin:0;padding:28px}");
            sb.Append("h1{font-size:22px;margin:0 0 4px}");
            sb.Append(".sum{color:#8b97a5;font-size:13px;margin-bottom:24px}");
            sb.Append(".card{background:#161b22;border-radius:12px;padding:20px;margin:0 0 22px}");
            sb.Append("h2{font-size:17px;margin:0 0 4px;font-weight:600}");
            sb.Append(".acct{color:#8b97a5;font-weight:400}");
            sb.Append(".pnl{font-weight:700}.win{color:#3fb950}.loss{color:#f4523b}");
            sb.Append(".when{color:#8b97a5;font-size:12px;margin-bottom:16px}");
            sb.Append(".shots{display:flex;gap:14px;flex-wrap:wrap;margin-bottom:18px}");
            sb.Append("figure{margin:0;flex:1 1 420px}");
            sb.Append("figcaption{color:#8b97a5;font-size:11px;text-transform:uppercase;letter-spacing:.05em;margin-bottom:6px}");
            sb.Append("img{width:100%;border-radius:8px;border:1px solid #252c36;display:block}");
            sb.Append(".none{color:#636e7b;font-size:13px}");
            sb.Append(".facts{display:flex;flex-wrap:wrap;gap:6px 28px;margin-bottom:12px}");
            sb.Append(".f{display:flex;gap:8px;font-size:13px}");
            sb.Append(".k{color:#8b97a5}.v{color:#e8edf3}");
            sb.Append(".warn{color:#e3b341;font-size:13px;width:100%;margin-top:4px}");
            sb.Append("blockquote{margin:0;padding:12px 16px;background:#0e1116;border-left:3px solid #4da3ff;color:#b4c0cd;font-size:14px;border-radius:0 8px 8px 0}");
            sb.Append(".foot{color:#636e7b;font-size:11px;margin-top:24px}");
            sb.Append("</style></head><body>");

            sb.Append("<h1>").Append(Esc(title)).Append("</h1>");
            sb.Append("<div class=\"sum\">").Append(n).Append(n == 1 ? " trade" : " trades")
              .Append("  &middot;  net ").Append(Money(net));
            if (n > 0) sb.Append("  &middot;  ").Append(Math.Round(wins / n * 100)).Append("% won");
            sb.Append("</div>");

            if (n == 0) sb.Append("<p class=\"none\">Nothing recorded for this selection.</p>");
            else for (int i = 0; i < trades.Count; i++) sb.Append(TradeCard(trades[i]));

            sb.Append("<div class=\"foot\">Generated by Ballast. ");
            sb.Append(EmbedImages
                ? "The charts are embedded in this file, so it works anywhere and survives image cleanup."
                : "The charts are read from this computer, so this page only displays them here. Use \u201cSave a copy\u201d for a version that travels.");
            sb.Append("</div>");
            sb.Append("</body></html>");
            return sb.ToString();
        }

        /// <summary>Write the page and hand back the path, or "" on failure.</summary>
        public static string Write(string folder, string fileName, string html)
        {
            try
            {
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, fileName);
                File.WriteAllText(path, html, new UTF8Encoding(false));
                return path;
            }
            catch { return ""; }
        }

        /// <summary>
        /// Filesystem-safe name. Deliberately a local copy rather than a call into
        /// ChartSnapshot: that file references WPF, and borrowing one helper from
        /// it would drag the whole of WPF into this one - which is exactly what
        /// makes a file untestable outside NinjaTrader. This file has no
        /// dependencies at all, and every line of it is covered.
        /// </summary>
        public static string SafeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "report";

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
            if (outp.Length == 0) return "report";
            if (outp.Length > 40) outp = outp.Substring(0, 40);
            return outp;
        }

        /// <summary>Dated name, for a copy the trader chose to keep.</summary>
        public static string ReportName(string prefix, DateTime when)
        {
            return SafeName(prefix) + "-"
                 + when.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".html";
        }

        /// <summary>
        /// The single file used for looking at something. Reused and overwritten,
        /// so browsing the journal cannot silently fill a folder with pages nobody
        /// will open twice.
        /// </summary>
        public const string ViewerName = "ballast-view.html";
    }
}
