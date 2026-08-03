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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
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
        public const string SignatureHeader = "X-Ballast-Signature";
        public const int MaximumPayloadBytes = 1024 * 1024;
        public const int MinimumRuleRows = 50;
        public const int RequestTimeoutMs = 5000;

        /// <summary>
        /// Production must assign a pinned-public-key verifier before remote
        /// updates are enabled. Null deliberately means "do not trust remote
        /// rules", not "skip verification". The byte array is the exact UTF-8
        /// response body and the string is the detached base64 signature header.
        /// </summary>
        public static Func<byte[], string, bool> SignatureVerifier;

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
            try { AtomicFile.WriteAllText(StampPath(rulesPath), nowUtc.ToString("o", CultureInfo.InvariantCulture)); }
            catch { }
        }

        private sealed class TimeoutWebClient : WebClient
        {
            protected override WebRequest GetWebRequest(Uri address)
            {
                WebRequest request = base.GetWebRequest(address);
                request.Timeout = RequestTimeoutMs;
                HttpWebRequest http = request as HttpWebRequest;
                if (http != null) http.ReadWriteTimeout = RequestTimeoutMs;
                return request;
            }
        }

        private static bool RequiredFirmsPresent(RuleBook book)
        {
            return book.ForFirm("Apex Trader Funding").Count > 0
                && book.ForFirm("Topstep").Count > 0;
        }

        /// <summary>Validate integrity, metadata, completeness, and row invariants.</summary>
        public static bool ValidateDownloadedPayload(byte[] bytes, string signature,
                                                     DateTime nowUtc, out RuleBook book,
                                                     out string error)
        {
            book = null;
            error = null;

            if (bytes == null || bytes.Length == 0)
            {
                error = "The server returned an empty rule book.";
                return false;
            }
            if (bytes.Length > MaximumPayloadBytes)
            {
                error = "The downloaded rule book exceeded the size limit.";
                return false;
            }
            if (SignatureVerifier == null || string.IsNullOrEmpty(signature)
                || !SignatureVerifier(bytes, signature))
            {
                error = "The downloaded rule book did not have a valid pinned signature.";
                return false;
            }

            string payload;
            try { payload = new UTF8Encoding(false, true).GetString(bytes); }
            catch
            {
                error = "The downloaded rule book was not valid UTF-8.";
                return false;
            }

            int version = ParseVersion(payload);
            if (version <= 0)
            {
                error = "The downloaded rule book had no valid version.";
                return false;
            }

            string temp = Path.Combine(Path.GetTempPath(),
                "ballast-rules-validate-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                File.WriteAllBytes(temp, bytes);
                RuleBook probe = new RuleBook();
                if (!probe.Load(temp) || probe.Count < MinimumRuleRows)
                {
                    error = "The downloaded rule book was incomplete or malformed.";
                    return false;
                }
                if (!RequiredFirmsPresent(probe))
                {
                    error = "The downloaded rule book omitted a required firm.";
                    return false;
                }

                DateTime verified;
                if (!DateTime.TryParseExact(probe.VerifiedDate, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out verified)
                    || verified.Date > nowUtc.Date.AddDays(1))
                {
                    error = "The downloaded rule book had an invalid verification date.";
                    return false;
                }

                List<string> firms = probe.Firms();
                for (int f = 0; f < firms.Count; f++)
                {
                    System.Collections.Generic.List<FirmAccountSpec> rows = probe.ForFirm(firms[f]);
                    for (int i = 0; i < rows.Count; i++)
                    {
                        FirmAccountSpec row = rows[i];
                        if (row.Size <= 0 || row.Drawdown <= 0 || row.Drawdown >= row.Size
                            || row.DailyLossLimit < 0 || row.ProfitTarget < 0
                            || row.FirmMaxContracts < 0)
                        {
                            error = "The downloaded rule book contained an unsafe account row.";
                            return false;
                        }
                    }
                }

                book = probe;
                return true;
            }
            finally
            {
                try { File.Delete(temp); } catch { }
            }
        }

        /// <summary>
        /// Synchronous fetch-and-install. Call this on a BACKGROUND thread only.
        /// Returns a result describing what happened; never throws.
        /// </summary>
        public static RuleUpdateResult FetchAndInstall(string rulesPath, DateTime nowUtc)
        {
            RuleUpdateResult r = new RuleUpdateResult();
            r.LocalVersion = LocalVersion(rulesPath);

            byte[] payloadBytes = null;
            string signature = null;
            try
            {
                // TLS 1.2 — .NET Framework defaults can be too old for modern hosts.
                try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
                catch { }

                using (TimeoutWebClient wc = new TimeoutWebClient())
                {
                    wc.Headers.Add("User-Agent", "Ballast-NinjaTrader-AddOn");
                    payloadBytes = wc.DownloadData(RulesUrl);
                    signature = wc.ResponseHeaders == null ? null : wc.ResponseHeaders[SignatureHeader];
                }
                r.Checked = true;
            }
            catch (Exception ex)
            {
                r.Message = "Rule update check failed (" + ex.Message + "). Using cached rules.";
                return r;
            }

            StampChecked(rulesPath, nowUtc);

            string payload;
            try { payload = new UTF8Encoding(false, true).GetString(payloadBytes); }
            catch
            {
                r.Message = "Rule update was not valid UTF-8. Using cached rules.";
                return r;
            }

            r.RemoteVersion = ParseVersion(payload);
            if (r.RemoteVersion <= r.LocalVersion)
            {
                r.Message = "Rule book already current (v" + r.LocalVersion + ").";
                return r;
            }

            try
            {
                RuleBook probe;
                string validationError;
                if (!ValidateDownloadedPayload(payloadBytes, signature, nowUtc,
                                               out probe, out validationError))
                {
                    r.Message = validationError + " Keeping existing rules.";
                    return r;
                }

                AtomicFile.WriteAllText(rulesPath, payload, new UTF8Encoding(false));

                r.Updated = true;
                r.Message = "Rule book updated to v" + r.RemoteVersion + " (" + probe.Count + " account types).";
                return r;
            }
            catch (Exception ex)
            {
                r.Message = "Could not install rule book: " + ex.Message;
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
