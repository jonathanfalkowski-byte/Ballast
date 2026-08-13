// ─────────────────────────────────────────────────────────────────────────────
// SetupTests — the per-setup edge experiment.
//
// Covers the three things the experiment depends on being right:
//   * a setup label survives the CSV, and a journal written before the column
//     existed still loads (as untagged, never as a wrong setup);
//   * SetupSplit buckets by setup, excludes bot and untagged rows, worst first;
//   * EdgeRead nets out commission and routes each sample to the honest verdict -
//     too few, no edge, inside the noise, or a real edge - with the t-stat
//     checked against a hand-computed value.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.IO;
using Ballast;

public static class SetupTests
{
    public static void Run()
    {
        SetupSurvivesCsvRoundTrip();
        OldRowWithoutSetupLoadsUntagged();
        SetupSplitBucketsAndExcludes();
        EdgeReadRefusesBelowSample();
        EdgeReadCallsALoserALoser();
        EdgeReadNetsOutCommission();
        EdgeReadKnowsTheNoise();
        EdgeReadSeesAProbableEdge();
        EdgeReadSeesARealEdge();
        ASetupNameWithADashSurvives();
        ASimAccountKnowsWhatItWasSetUpAs();

        SetupBookAddsTrimsAndDedupes();
        SetupBookRefusesToSprawl();
        SetupBookRemoves();
        SetupBookRoundTripsText();
        SetupBookRoundTripsFile();
    }

    /// <summary>
    /// "i feel the journal page is still too much of a wall of text"
    ///
    /// Part of that mess was not layout at all - it was a name being taken
    /// apart. The setup name was glued onto the front of the verdict with " - "
    /// and split back off by the window, so a setup called "C - bollinger bands
    /// and dot forms going direction of the bar" rendered as a row labelled "C"
    /// with the rest of his own name running into the sentence beneath it.
    /// </summary>
    static void ASetupNameWithADashSurvives()
    {
        T.S("a setup name with a dash in it survives");

        BallastJournal j = new BallastJournal();
        List<BallastTrade> book = new List<BallastTrade>();

        string full = "C - bollinger bands and dot forms going direction of the bar";
        for (int i = 0; i < 10; i++)
        {
            BallastTrade e = new BallastTrade();
            e.AccountName = "Sim103";
            e.Instrument = "NQ SEP26";
            e.MaxContracts = 1;
            e.EntryTime = new DateTime(2026, 8, 3, 10, 0, 0).AddDays(i % 4);
            e.ExitTime = e.EntryTime.AddMinutes(3);
            e.Pnl = -51;
            e.Planned = BallastJournal.Verdict_ByTheBook;
            e.Setup = full;
            book.Add(e);
        }

        List<EdgeReadResult> edges = j.SetupEdges(book, 20);
        T.Eq(edges.Count, 1, "one setup");
        T.Eq(edges[0].Setup, full, "his whole name, exactly as he typed it");
        T.Ok(edges[0].Verdict.IndexOf("bollinger") < 0,
             "and none of it leaks into the verdict: " + edges[0].Verdict);
        T.Ok(edges[0].Short.IndexOf("10 of 20") >= 0,
             "the row says the sample is short, in a few words: " + edges[0].Short);
        T.Eq(edges[0].Confidence, EdgeConfidence.TooFew, "and the confidence agrees");
    }

    static BallastTrade Tr(double pnl)
    {
        BallastTrade e = new BallastTrade();
        e.Pnl = pnl;
        return e;
    }

    static BallastTrade Tr(double pnl, double commission)
    {
        BallastTrade e = new BallastTrade();
        e.Pnl = pnl;
        e.Commission = commission;
        return e;
    }

    static List<BallastTrade> Many(double pnl, int n)
    {
        List<BallastTrade> l = new List<BallastTrade>();
        for (int i = 0; i < n; i++) l.Add(Tr(pnl));
        return l;
    }

    static List<BallastTrade> Mix(double pnl, int count, double pnl2, int count2)
    {
        List<BallastTrade> l = new List<BallastTrade>();
        for (int i = 0; i < count; i++) l.Add(Tr(pnl));
        for (int i = 0; i < count2; i++) l.Add(Tr(pnl2));
        return l;
    }

    /// <summary>
    /// "when you go into setup it defaults to the top of the list of what type
    /// of account this is and i was going to reset my trades, loss and target
    /// on my sim account forgetting i had already set the account to eval 150k
    /// account and so i thought i had not done it yet and reset it to 250"
    ///
    /// The dropdown was resting on the first row in the rule book, because
    /// nothing could tell it what a Sim account was. That reads exactly like an
    /// account nobody has configured, and the button beside it writes whatever
    /// is showing over an account that was already right. It cost him a 150K
    /// evaluation's settings.
    ///
    /// A sim account's NAME belongs to no firm. Its FIGURES do.
    /// </summary>
    static void ASimAccountKnowsWhatItWasSetUpAs()
    {
        T.S("a sim account knows what it was set up as");

        RuleBook rb = new RuleBook();
        T.Ok(rb.Load("Ballast/ballast-rules.txt"), "the shipped rule book loads");

        // A sim set up to mirror a 150K legacy evaluation - the one he lost.
        TrackerConfig c = new TrackerConfig();
        c.StartingBalance = 150000;
        c.TrailingDrawdown = 5000;
        c.DrawdownType = DrawdownType.Intraday;
        c.LockFloorAt = 159000;

        FirmAccountSpec s = rb.MatchSpecForAccount("Sim103", c);
        T.Ok(s != null, "the figures name the type even though the account is called Sim103");
        T.Near(s.Size, 150000, 1, "150K");
        T.Ok(s.Label.IndexOf("150K", StringComparison.Ordinal) >= 0,
             "with a label he would recognise: " + s.Label);
        T.Ok(s.Plan.IndexOf("valuation", StringComparison.Ordinal) >= 0,
             "and it is the evaluation row, not the funded one");

        // Playback and Backtest accounts get the same treatment - they are the
        // other two names FirmFromAccountName deliberately refuses.
        T.Ok(rb.MatchSpecForAccount("Playback101", c) != null, "so does a playback account");
        T.Ok(rb.MatchSpecForAccount("Backtest", c) != null, "and a backtest account");

        // A named account still goes through its own firm, unchanged. Widening
        // the search must not let another firm's identical row answer for an
        // account whose name already said who it belongs to.
        FirmAccountSpec named = rb.MatchSpecForAccount("APEX-11325-105", c);
        T.Ok(named != null && named.Firm.IndexOf("Apex", StringComparison.OrdinalIgnoreCase) >= 0,
             "a named Apex account is still answered by Apex's rows");

        // And the safety rule survives the widening: an unconfigured account
        // has no figures to match, so it gets no type rather than the first one.
        T.Ok(rb.MatchSpecForAccount("Sim103", new TrackerConfig()) == null,
             "an account with no figures is still not guessed at");

        TrackerConfig odd = new TrackerConfig();
        odd.StartingBalance = 187000; odd.TrailingDrawdown = 5000;
        odd.DrawdownType = DrawdownType.Intraday; odd.LockFloorAt = 159000;
        T.Ok(rb.MatchSpecForAccount("Sim103", odd) == null,
             "and a size no firm publishes still gets silence rather than the nearest row");

        // Two rows fitting is still no answer, across the whole book as well as
        // within one firm. A confident wrong type is worse than an empty one.
        RuleBook twins = new RuleBook();
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ballast-twins.txt");
        System.IO.File.WriteAllText(path,
            "VERSION|1\n"
          + "Firm One|Evaluation|150000|5000|INTRADAY|0|9000|note|159000|17\n"
          + "Firm Two|Evaluation|150000|5000|INTRADAY|0|9000|note|159000|17\n");
        T.Ok(twins.Load(path), "a book with two identical rows loads");
        T.Ok(twins.MatchSpecForAccount("Sim103", c) == null,
             "and two firms describing the same account is not an answer");
    }

    static void SetupSurvivesCsvRoundTrip()
    {
        T.S("a setup label survives the CSV round trip");

        T.Ok(BallastJournal.CsvHeader.EndsWith(",Setup"), "the header carries the setup column last");

        BallastTrade a = new BallastTrade();
        a.AccountName = "APEX-11325-107";
        a.Instrument = "MNQ SEP26";
        a.IsLong = true;
        a.MaxContracts = 1;
        a.EntryTime = new DateTime(2026, 8, 4, 9, 45, 0);
        a.ExitTime = new DateTime(2026, 8, 4, 9, 52, 0);
        a.Pnl = 70.5;
        a.Commission = 1.04;
        a.Planned = BallastJournal.Verdict_ByTheBook;
        a.Feeling = "Focused";
        a.Setup = "A — EMA cross + dot";

        BallastTrade b = BallastJournal.FromCsvLine(BallastJournal.ToCsvLine(a));
        T.Ok(b != null, "the line parses back");
        T.Eq(b.Setup, a.Setup, "the setup comes back intact");
        T.Eq(b.Instrument, a.Instrument, "the instrument is unaffected by the new column");
        T.Near(b.Commission, a.Commission, 0.001, "commission still reads from its own field");
    }

    static void OldRowWithoutSetupLoadsUntagged()
    {
        T.S("a journal row written before the setup column loads as untagged");

        BallastTrade a = new BallastTrade();
        a.AccountName = "APEX-11325-107";
        a.Instrument = "MNQ SEP26";
        a.IsLong = true;
        a.MaxContracts = 1;
        a.EntryTime = new DateTime(2026, 8, 4, 9, 45, 0);
        a.ExitTime = new DateTime(2026, 8, 4, 9, 52, 0);
        a.Pnl = 70.5;
        a.Commission = 1.04;
        a.Setup = "A — EMA cross + dot";

        // Simulate an older file: take the current line and drop the trailing
        // setup field, leaving the 26-column shape earlier builds wrote. The
        // fields here contain no commas, so a plain re-join is faithful.
        List<string> fields = BallastJournal.SplitCsvLine(BallastJournal.ToCsvLine(a));
        fields.RemoveAt(fields.Count - 1);
        string oldLine = string.Join(",", fields.ToArray());

        BallastTrade b = BallastJournal.FromCsvLine(oldLine);
        T.Ok(b != null, "the older line still parses");
        T.Eq(b.Setup, "", "a missing setup column reads as untagged, not as a wrong setup");
        T.Near(b.Commission, 1.04, 0.001, "the last real column still reads correctly");
    }

    static void SetupSplitBucketsAndExcludes()
    {
        T.S("SetupSplit buckets by setup, worst first, excluding bot and untagged rows");

        string A = "A — EMA cross + dot";
        string B = "B — pivot, first dot";

        List<BallastTrade> all = new List<BallastTrade>();

        BallastTrade a1 = Tr(10);  a1.Setup = A; all.Add(a1);
        BallastTrade a2 = Tr(-50); a2.Setup = A; all.Add(a2);   // A nets -40 over 2
        BallastTrade b1 = Tr(30);  b1.Setup = B; all.Add(b1);
        BallastTrade b2 = Tr(20);  b2.Setup = B; all.Add(b2);   // B nets +50 over 2

        BallastTrade untagged = Tr(9999); all.Add(untagged);    // no setup -> excluded

        BallastTrade bot = Tr(9999); bot.Setup = A; bot.Automated = true; all.Add(bot); // bot -> excluded

        BallastJournal j = new BallastJournal();
        List<JournalBucket> split = j.SetupSplit(all);

        T.Eq(split.Count, 2, "only the two tagged, hand-taken setups appear");
        T.Eq(split[0].Label, A, "the setup losing money is listed first");
        T.Near(split[0].Net, -40, 0.001, "setup A's net is its two trades, and nothing else");
        T.Eq(split[0].Count, 2, "the bot row did not leak into setup A");
        T.Eq(split[1].Label, B, "the profitable setup is second");
        T.Near(split[1].Net, 50, 0.001, "setup B's net is correct");
    }

    static void EdgeReadRefusesBelowSample()
    {
        T.S("EdgeRead refuses to judge below the minimum sample");

        EdgeReadResult r = BallastJournal.EdgeRead(Many(50, 5), 15);
        T.Eq(r.Count, 5, "it still counts the trades");
        T.Ok(r.Confidence == EdgeConfidence.TooFew, "five trades earns no verdict");
    }

    static void EdgeReadCallsALoserALoser()
    {
        T.S("EdgeRead names a negative-expectancy setup as no edge");

        EdgeReadResult r = BallastJournal.EdgeRead(Many(-10, 20), 15);
        T.Ok(r.Confidence == EdgeConfidence.NoEdge, "twenty losers is not an edge");
        T.Near(r.Expectancy, -10, 0.001, "expectancy is the per-trade loss");
    }

    static void EdgeReadNetsOutCommission()
    {
        T.S("EdgeRead measures after commission, not before");

        // Gross +$3 a trade, but $5 a trade in commission: a real -$2 leak that a
        // gross number would have shown as a winner.
        List<BallastTrade> l = new List<BallastTrade>();
        for (int i = 0; i < 20; i++) l.Add(Tr(3, 5));

        EdgeReadResult r = BallastJournal.EdgeRead(l, 15);
        T.Near(r.Expectancy, -2, 0.001, "the $5 commission turns a gross winner into a net loss");
        T.Ok(r.Confidence == EdgeConfidence.NoEdge, "and it is judged on the net figure");
    }

    static void EdgeReadKnowsTheNoise()
    {
        T.S("EdgeRead calls a small edge on wild trades what it is: noise");

        // 8 x +70, 8 x -60. Mean +5, but a huge spread: t works out near 0.30.
        EdgeReadResult r = BallastJournal.EdgeRead(Mix(70, 8, -60, 8), 15);
        T.Near(r.Expectancy, 5, 0.001, "the average really is +$5 a trade");
        T.Near(r.WinRate, 0.5, 0.001, "half of them won");
        T.Near(r.TStat, 0.298, 0.02, "the t-stat matches the hand-computed value");
        T.Ok(r.Confidence == EdgeConfidence.InTheNoise, "positive, but indistinguishable from luck");
    }

    static void EdgeReadSeesAProbableEdge()
    {
        T.S("EdgeRead flags a promising-but-unfinished edge");

        // 15 x +75, 10 x -55. Mean +23, t ~ 1.77 -> in the [1.7, 2.5) band.
        EdgeReadResult r = BallastJournal.EdgeRead(Mix(75, 15, -55, 10), 15);
        T.Near(r.Expectancy, 23, 0.001, "expectancy is +$23 a trade");
        T.Near(r.TStat, 1.77, 0.03, "the t-stat lands in the probable band");
        T.Ok(r.Confidence == EdgeConfidence.ProbablyReal, "probably real, but finish the sample");
    }

    static void EdgeReadSeesARealEdge()
    {
        T.S("EdgeRead recognises a genuine, well-evidenced edge");

        // 20 x +100, 10 x -50. Mean +50, t ~ 3.8.
        EdgeReadResult r = BallastJournal.EdgeRead(Mix(100, 20, -50, 10), 15);
        T.Near(r.Expectancy, 50, 0.001, "expectancy is +$50 a trade");
        T.Ok(r.TStat >= 2.5, "the t-stat clears the real-edge bar");
        T.Ok(r.Confidence == EdgeConfidence.LikelyReal, "unlikely to be luck");
    }

    static void SetupBookAddsTrimsAndDedupes()
    {
        T.S("a setup book trims, dedupes, and refuses blanks");

        SetupBook bk = new SetupBook();
        T.Ok(bk.Add("  A — EMA cross  "), "a setup is added");
        T.Eq(bk.Names[0], "A — EMA cross", "and stored trimmed");
        T.Ok(!bk.Add("a — EMA CROSS"), "the same setup in a different case is refused as a duplicate");
        T.Ok(!bk.Add("   "), "a blank setup is refused");
        T.Eq(bk.Count, 1, "so the book still holds exactly one");
    }

    static void SetupBookRefusesToSprawl()
    {
        T.S("a setup book will not grow past its cap");

        SetupBook bk = new SetupBook();
        for (int i = 0; i < SetupBook.MaxSetups; i++)
            T.Ok(bk.Add("setup " + i), "setup " + i + " fits under the cap");

        T.Ok(bk.IsFull, "the book reports itself full at the cap");
        T.Ok(!bk.Add("one too many"), "and refuses the next one");
        T.Eq(bk.Count, SetupBook.MaxSetups, "the count never exceeds the cap");

        // SetFromText reports the overflow rather than swallowing it.
        SetupBook bk2 = new SetupBook();
        string text = "a\nb\nc\nd\ne\nf\ng\nh";   // 8 lines, cap is 6
        int dropped = bk2.SetFromText(text);
        T.Eq(bk2.Count, SetupBook.MaxSetups, "only the cap's worth are kept");
        T.Eq(dropped, 8 - SetupBook.MaxSetups, "and the overflow is counted, not hidden");
    }

    static void SetupBookRemoves()
    {
        T.S("a setup book removes a retired setup, case-insensitively");

        SetupBook bk = new SetupBook();
        bk.Add("A — EMA cross");
        bk.Add("B — pivot");
        T.Ok(bk.Remove("b — PIVOT"), "remove matches regardless of case");
        T.Eq(bk.Count, 1, "one setup is gone");
        T.Ok(bk.Contains("A — EMA cross"), "and the other remains");
        T.Ok(!bk.Remove("never added"), "removing something absent reports false");
    }

    static void SetupBookRoundTripsText()
    {
        T.S("a setup book round-trips through editor text, dropping blanks and dupes");

        SetupBook bk = new SetupBook();
        int dropped = bk.SetFromText("A — EMA cross\n\n  B — pivot \nA — EMA cross\n");
        T.Eq(bk.Count, 2, "two real setups survive the blanks and the duplicate");
        T.Eq(dropped, 1, "the duplicate line is reported as dropped, the blank is not");
        T.Eq(bk.ToText(), "A — EMA cross\nB — pivot", "the text comes back clean, one per line");
    }

    static void SetupBookRoundTripsFile()
    {
        T.S("a setup book saves and loads from disk");

        string dir = Path.Combine(Path.GetTempPath(), "ballast-setup-tests");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "setups-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            SetupBook a = new SetupBook();
            a.Add("A — EMA cross + dot");
            a.Add("B — pivot, first dot");
            T.Ok(a.Save(path), "the book saves");

            SetupBook b = new SetupBook();
            T.Ok(b.Load(path), "and loads back");
            T.Eq(b.Count, 2, "with both setups");
            T.Eq(b.Names[1], "B — pivot, first dot", "in order and intact");

            SetupBook missing = new SetupBook();
            T.Ok(!missing.Load(Path.Combine(dir, "does-not-exist.txt")), "a missing file loads to an empty book, not a crash");
            T.Eq(missing.Count, 0, "which is simply empty");
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }
}
