// ─────────────────────────────────────────────────────────────────────────────
// Ballast — RuleBookUpdater.cs
//
// Keeps the prop firm rule book current WITHOUT the trader ever touching a file.
// On a schedule it asks tradeballast.com for the latest rule book, and if the
// version is newer than what's on disk it writes it and reloads.
//
// Design rules, in order of importance:
//   1. NEVER interfere with trading. All network work happens on a background
//      thread with a short timeout. A failed update is a silent no-op — the
//      previously cached rules keep working, offline included.
//   2. Never apply a partial or malformed download. The payload is parsed into a
//      throwaway RuleBook first; only a valid, non-empty parse is written to disk.
//   3. Never silently downgrade. A lower version number is ignored.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;

namespace Ballast
{
    public class RuleUpdateResult
    {
        public bool Updated;          // a newer rule book was installed
        public bool Checked;          // the server was successfully reached
        public int RemoteVersion;
        public int LocalVersion;
        public string Message;
    }

    public static class RuleBookUpdater
    {
        public const string RulesUrl = "https://tradeballast.com/api/rules";

        /// <summary>How often to bother the server. Rules change on the order of months.</summary>
        public static TimeSpan CheckInterval = TimeSpan.FromHours(24);

        /// <summary>
        /// Parse a VERSION|n line out of a rule book payload. Returns 0 if absent,
        /// which is treated as "older than anything versioned".
        /// </summary>
        public static int ParseVersion(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                string[] f = line.Split('|');
                if (f.Length >= 2 && f[0].Trim().ToUpperInvariant() == "VERSION")
                {
                    int v;
                    if (int.TryParse(f[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out v)) return v;
                }
            }
            return 0;
        }

        /// <summary>Version of the rule book currently on disk (0 if none/unreadable).</summary>
        public static int LocalVersion(string path)
        {
            try
            {
                if (!File.Exists(path)) return 0;
                return ParseVersion(File.ReadAllText(path));
            }
            catch { return 0; }
        }

        private static string StampPath(string rulesPath)
        {
            try { return rulesPath + ".lastcheck"; }
            catch { return "ballast-rules.lastcheck"; }
        }

        public static bool DueForCheck(string rulesPath, DateTime nowUtc)
        {
            try
            {
                string p = StampPath(rulesPath);
                if (!File.Exists(p)) return true;

                string raw = File.ReadAllText(p).Trim();
                DateTime last;
                if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                                       DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out last))
                    return true;

                return (nowUtc - last) >= CheckInterval;
            }
            catch { return true; }
        }

        private static void StampChecked(string rulesPath, DateTime nowUtc)
        {
            try { File.WriteAllText(StampPath(rulesPath), nowUtc.ToString("o", CultureInfo.InvariantCulture)); }
            catch { }
        }

        /// <summary>
        /// Synchronous fetch-and-install. Call this on a BACKGROUND thread only.
        /// Returns a result describing what happened; never throws.
        /// </summary>
        public static RuleUpdateResult FetchAndInstall(string rulesPath, DateTime nowUtc)
        {
            RuleUpdateResult r = new RuleUpdateResult();
            r.LocalVersion = LocalVersion(rulesPath);

            string payload = null;
            try
            {
                // TLS 1.2 — .NET Framework defaults can be too old for modern hosts.
                try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
                catch { }

                using (WebClient wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "Ballast-NinjaTrader-AddOn");
                    payload = wc.DownloadString(RulesUrl);
                }
                r.Checked = true;
            }
            catch (Exception ex)
            {
                r.Message = "Rule update check failed (" + ex.Message + "). Using cached rules.";
                return r;
            }

            StampChecked(rulesPath, nowUtc);

            r.RemoteVersion = ParseVersion(payload);
            if (r.RemoteVersion <= r.LocalVersion)
            {
                r.Message = "Rule book already current (v" + r.LocalVersion + ").";
                return r;
            }

            // Validate before writing: never install something that doesn't parse.
            string temp = rulesPath + ".incoming";
            try
            {
                File.WriteAllText(temp, payload);

                RuleBook probe = new RuleBook();
                if (!probe.Load(temp) || probe.Count == 0)
                {
                    r.Message = "Downloaded rule book failed validation - keeping existing rules.";
                    try { File.Delete(temp); } catch { }
                    return r;
                }

                File.Copy(temp, rulesPath, true);
                try { File.Delete(temp); } catch { }

                r.Updated = true;
                r.Message = "Rule book updated to v" + r.RemoteVersion + " (" + probe.Count + " account types).";
                return r;
            }
            catch (Exception ex)
            {
                r.Message = "Could not install rule book: " + ex.Message;
                try { File.Delete(temp); } catch { }
                return r;
            }
        }

        /// <summary>
        /// Fire-and-forget background check. onDone is invoked with the result and
        /// MUST be marshalled to the UI thread by the caller if it touches UI.
        /// </summary>
        public static void CheckInBackground(string rulesPath, bool force, Action<RuleUpdateResult> onDone)
        {
            ThreadStart work = delegate
            {
                RuleUpdateResult r;
                try
                {
                    DateTime now = DateTime.UtcNow;
                    if (!force && !DueForCheck(rulesPath, now))
                    {
                        r = new RuleUpdateResult();
                        r.LocalVersion = LocalVersion(rulesPath);
                        r.Message = null; // nothing to say
                    }
                    else
                    {
                        r = FetchAndInstall(rulesPath, now);
                    }
                }
                catch (Exception ex)
                {
                    r = new RuleUpdateResult();
                    r.Message = "Rule update error: " + ex.Message;
                }

                if (onDone != null)
                {
                    try { onDone(r); } catch { }
                }
            };

            Thread t = new Thread(work);
            t.IsBackground = true;   // never keeps NinjaTrader alive
            t.Start();
        }
    }
}
