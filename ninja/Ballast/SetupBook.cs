// ─────────────────────────────────────────────────────────────────────────────
// Ballast — SetupBook.cs
//
// A trader's own list of setups — their playbook. Ballast can capture a fill
// automatically, but it cannot know which of the trader's setups the trade was;
// only the trader can say. So each trader defines their setups here, and every
// trade gets tagged with one, which is what lets Ballast tell setup A's edge from
// setup B's instead of blending them into one meaningless number.
//
// Stored one-per-line in a plain text file (ballast-setups.txt) next to the
// journal, so it is per-trader rather than per-account — a playbook is the same
// whether the trader is on a 50K or a 250K — and so it can be edited in Notepad
// as easily as in Ballast.
//
// DELIBERATELY CAPPED. A setup list that keeps growing is the exact pattern that
// empties accounts: when a setup struggles, the temptation is to go find another
// one, which resets the evidence to zero every time. Ballast is a discipline
// layer; it resists that sprawl rather than enabling it. A trader who genuinely
// retires a setup removes it — and the old trades keep their label regardless,
// because the journal stores the text, not an index into this list.
//
// Pure C# — no NinjaTrader or WPF — so every rule here is unit tested.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.IO;

namespace Ballast
{
    public class SetupBook
    {
        /// <summary>
        /// The most setups a book will hold. Short on purpose — see the file
        /// header. Additions past this are refused rather than silently dropped,
        /// so the editor can say why.
        /// </summary>
        public const int MaxSetups = 6;

        private readonly List<string> names = new List<string>();

        /// <summary>A copy of the current setups, in the order they were added.</summary>
        public List<string> Names { get { return new List<string>(names); } }

        public int Count { get { return names.Count; } }
        public bool IsFull { get { return names.Count >= MaxSetups; } }

        /// <summary>
        /// Add one setup. Returns true if it was added, false if it was blank, a
        /// duplicate (case-insensitive), or would exceed the cap — so the caller
        /// can report a refusal rather than pretend it worked.
        /// </summary>
        public bool Add(string name)
        {
            string clean = Normalise(name);
            if (clean.Length == 0) return false;
            if (names.Count >= MaxSetups) return false;
            if (Contains(clean)) return false;
            names.Add(clean);
            return true;
        }

        /// <summary>Remove a setup by name (case-insensitive). Returns true if one went.</summary>
        public bool Remove(string name)
        {
            string clean = Normalise(name);
            for (int i = 0; i < names.Count; i++)
                if (string.Equals(names[i], clean, StringComparison.OrdinalIgnoreCase))
                {
                    names.RemoveAt(i);
                    return true;
                }
            return false;
        }

        public bool Contains(string name)
        {
            string clean = Normalise(name);
            for (int i = 0; i < names.Count; i++)
                if (string.Equals(names[i], clean, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        public void Clear() { names.Clear(); }

        /// <summary>
        /// Replace the whole list from raw editor text — one setup per line.
        /// Applies every rule (trim, drop blanks, drop duplicates, stop at the
        /// cap) and returns how many non-blank lines were dropped, so a
        /// silent-looking truncation can be explained to the trader.
        /// </summary>
        public int SetFromText(string text)
        {
            names.Clear();
            if (text == null) return 0;

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int dropped = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (Normalise(lines[i]).Length == 0) continue;   // a blank line is not a drop
                if (!Add(lines[i])) dropped++;                   // duplicate or over the cap
            }
            return dropped;
        }

        /// <summary>The book as editor text — one setup per line.</summary>
        public string ToText()
        {
            return string.Join("\n", names.ToArray());
        }

        // ── Persistence ──────────────────────────────────────────────────────

        public bool Load(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
                SetFromText(File.ReadAllText(path));
                return true;
            }
            catch { return false; }
        }

        public bool Save(string path)
        {
            try
            {
                AtomicFile.WriteAllText(path, ToText());
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Trim, and keep the pipe out. The journal CSV quotes commas so those are
        /// fine in a setup name, but the settings file is pipe-delimited and a pipe
        /// in a name would shift every later field along by one — so it is replaced
        /// here for safety, the same rule SettingsCodec applies to account names.
        /// </summary>
        private static string Normalise(string s)
        {
            if (s == null) return "";
            return s.Trim().Replace("|", "/");
        }
    }
}
