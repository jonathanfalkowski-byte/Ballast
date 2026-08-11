using System;
using System.Collections.Generic;
using Ballast;

/// <summary>
/// "also did we think about the amount of space the journal and the chart
/// pictures will eat up?"
///
/// Partly. There was a sweep, and it worked - but it counted DAYS, and it was
/// sized for a much lighter user.
///
/// His real figures, off his own disk: 64 MB across 8 trading days, 194 images,
/// median 272 KB each. At sixty days that is somewhere between half a gigabyte
/// and well over one, and nobody can budget against "somewhere between" -
/// because his own days range from 24 KB to 21 MB. A nine-hundred-fold spread,
/// same trader, same week.
///
/// The journal itself is a rounding error: 55 KB for 111 trades, about 500 bytes
/// each. A decade of it is a few megabytes and it needs no housekeeping at all.
/// </summary>
public static class DiskTests
{
    public static void Run()
    {
        OldestGoesFirstUntilItFits();
        TodayIsNeverDeleted();
        AFolderThatFitsIsLeftAlone();
        AgeAndSizeAreTwoSeparateLimits();
        HisOwnEightDays();
    }

    static List<string> Names(params string[] n) { return new List<string>(n); }

    static List<long> Mb(params double[] mb)
    {
        List<long> l = new List<long>();
        for (int i = 0; i < mb.Length; i++) l.Add((long)(mb[i] * 1024 * 1024));
        return l;
    }

    static void OldestGoesFirstUntilItFits()
    {
        T.S("the oldest go first, and only until it fits");

        List<string> days = Names("2026-08-03", "2026-08-04", "2026-08-05", "2026-08-06");
        List<long> size = Mb(40, 40, 40, 40);            // 160 MB

        List<string> doomed = ChartSnapshot.FoldersOverBudget(days, size, 100);
        T.Eq(doomed.Count, 2, "two of the four have to go to get under 100 MB");
        T.Eq(doomed[0], "2026-08-03", "the oldest first");
        T.Eq(doomed[1], "2026-08-04", "then the next oldest");

        // And it stops as soon as it fits, rather than emptying the folder.
        T.Eq(ChartSnapshot.FoldersOverBudget(days, size, 150).Count, 1,
             "a budget that only needs one day removed removes one day");

        // Order in is not order on disk. The arithmetic must not depend on it.
        List<string> jumbled = Names("2026-08-06", "2026-08-03", "2026-08-05", "2026-08-04");
        List<long> jsize = Mb(40, 40, 40, 40);
        List<string> j = ChartSnapshot.FoldersOverBudget(jumbled, jsize, 100);
        T.Eq(j[0], "2026-08-03", "still the oldest first whatever order they arrive in");
        T.Eq(j[1], "2026-08-04", "and still by date");
    }

    /// <summary>
    /// A cap set too low should degrade to "keep today", never to "keep
    /// nothing". An empty folder on a day he traded looks exactly like a broken
    /// feature, and he would be right to think so.
    /// </summary>
    static void TodayIsNeverDeleted()
    {
        T.S("the most recent day survives any budget");

        List<string> days = Names("2026-08-09", "2026-08-10");
        List<long> size = Mb(21, 21);

        List<string> doomed = ChartSnapshot.FoldersOverBudget(days, size, 1);
        T.Eq(doomed.Count, 1, "a 1 MB budget against 42 MB still keeps one day");
        T.Eq(doomed[0], "2026-08-09", "and it is the older one that goes");

        List<string> one = Names("2026-08-10");
        T.Eq(ChartSnapshot.FoldersOverBudget(one, Mb(500), 1).Count, 0,
             "a single day is never deleted, however far over it is");
    }

    static void AFolderThatFitsIsLeftAlone()
    {
        T.S("a folder inside its budget is left alone");

        List<string> days = Names("2026-08-05", "2026-08-06", "2026-08-07");
        T.Eq(ChartSnapshot.FoldersOverBudget(days, Mb(13, 7.8, 9.4), 500).Count, 0,
             "30 MB against a 500 MB budget deletes nothing");

        T.Eq(ChartSnapshot.FoldersOverBudget(days, Mb(13, 7.8, 9.4), 0).Count, 0,
             "and a budget of zero means no ceiling, not delete everything");

        T.Eq(ChartSnapshot.FoldersOverBudget(null, null, 500).Count, 0, "nothing in, nothing out");
        T.Eq(ChartSnapshot.FoldersOverBudget(days, Mb(1, 1), 1).Count, 0,
             "mismatched lists are refused rather than half-applied");
    }

    /// <summary>
    /// Age and size answer different questions. A picture from March is worth
    /// nothing even when there is room for it; a fortnight of heavy trading can
    /// blow a budget while every day in it is recent.
    /// </summary>
    static void AgeAndSizeAreTwoSeparateLimits()
    {
        T.S("age and size are two separate limits");

        DateTime today = new DateTime(2026, 8, 10);
        List<string> days = Names("2026-01-04", "2026-08-09", "2026-08-10");

        List<string> old = ChartSnapshot.FoldersToDelete(days, today, 60);
        T.Eq(old.Count, 1, "January is past sixty days");
        T.Eq(old[0], "2026-01-04", "and it goes on age alone, whatever it weighs");

        // Recent but heavy: age keeps all of it, size does not.
        List<string> recent = Names("2026-08-09", "2026-08-10");
        T.Eq(ChartSnapshot.FoldersToDelete(recent, today, 60).Count, 0, "both are recent");
        T.Eq(ChartSnapshot.FoldersOverBudget(recent, Mb(600, 600), 500).Count, 1,
             "but 1.2 GB of recent is still 1.2 GB");

        // Anything not ours is never touched, on either limit.
        List<string> foreign = Names("screenshots", "2026-01-04");
        List<string> f = ChartSnapshot.FoldersToDelete(foreign, today, 60);
        T.Eq(f.Count, 1, "a folder Ballast did not create is never deleted");
        T.Eq(f[0], "2026-01-04", "only the dated one it made itself");
    }

    /// <summary>His eight days, exactly as they sat on disk.</summary>
    static void HisOwnEightDays()
    {
        T.S("his own eight days, against the default budget");

        List<string> days = Names("2026-07-30", "2026-07-31", "2026-08-03", "2026-08-04",
                                  "2026-08-05", "2026-08-06", "2026-08-07", "2026-08-10");
        List<long> size = Mb(0.024, 3.6, 5.7, 4.0, 13, 7.8, 9.4, 21);   // 64 MB

        T.Eq(ChartSnapshot.FoldersOverBudget(days, size, 500).Count, 0,
             "64 MB is nowhere near the 500 MB default - nothing is deleted today");

        // Sixty days at his AVERAGE - 8 MB a day - is 480 MB, and the first
        // version of this test asserted that would bite. It does not: 480 is
        // under 500, so nothing is deleted and every day is kept. Worth writing
        // down, because it is the honest answer for a normal two months and it
        // says the default is set about right.
        List<string> average = new List<string>();
        List<long> avgSize = new List<long>();
        for (int i = 1; i <= 60; i++)
        {
            average.Add(new DateTime(2026, 6, 1).AddDays(i).ToString("yyyy-MM-dd"));
            avgSize.Add((long)(8.0 * 1024 * 1024));
        }
        T.Eq(ChartSnapshot.FoldersOverBudget(average, avgSize, 500).Count, 0,
             "two ordinary months come to 480 MB and are kept whole");

        // Sixty days at the rate of his BUSIEST day - 21 MB - is 1.26 GB, and
        // that is the case the ceiling exists for. A day count cannot tell these
        // two apart; they are the same sixty days.
        List<string> busy = new List<string>();
        List<long> busySize = new List<long>();
        for (int i = 1; i <= 60; i++)
        {
            busy.Add(new DateTime(2026, 6, 1).AddDays(i).ToString("yyyy-MM-dd"));
            busySize.Add((long)(21.0 * 1024 * 1024));
        }

        List<string> doomed = ChartSnapshot.FoldersOverBudget(busy, busySize, 500);
        T.Ok(doomed.Count > 0, "sixty busy days would be 1.26 GB, and that is trimmed");
        T.Eq(busy.Count - doomed.Count, 23,
             "back to 23 days - 483 MB, the most whole days that fit under 500");
    }
}
