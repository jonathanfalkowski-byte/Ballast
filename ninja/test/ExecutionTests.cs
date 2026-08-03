using System;
using Ballast;

/// <summary>Regression coverage for the event-driven per-instrument ledger.</summary>
public static class ExecutionTests
{
    public static void Run()
    {
        ACompleteTradeBetweenTimerTicksIsRecorded();
        SimultaneousInstrumentsStayIndependent();
        DuplicateExecutionsAreIdempotent();
        PartialExitsAndReversalsAreAccountedFor();
        APositionMismatchFailsClosed();
    }

    static BallastTracker Fresh()
    {
        BallastTracker t = new BallastTracker();
        t.Config = new TrackerConfig();
        t.Config.StartingBalance = 100000;
        t.Config.TrailingDrawdown = 6500;
        t.Config.MaxContracts = 10;
        t.Config.MaxTrades = 20;
        t.Config.MaxLossesBeforeStop = 10;
        DateTime now = new DateTime(2026, 8, 3, 9, 30, 0);
        t.EnsureSession(now, 0, 100000);
        t.OnEquity(100000, 0, 100000);
        return t;
    }

    static void ACompleteTradeBetweenTimerTicksIsRecorded()
    {
        T.S("an execution round trip does not depend on the timer");
        BallastTracker t = Fresh();
        DateTime now = new DateTime(2026, 8, 3, 9, 31, 0);

        T.Ok(t.OnExecution("e1", "NQ SEP26", 1, 20000, 20, 2.50, now, "A") == null,
             "the entry opens a round trip");
        BallastTrade closed = t.OnExecution("e2", "NQ SEP26", -1, 20010, 20, 2.50,
                                             now.AddMilliseconds(200), "A");

        T.Ok(closed != null, "the sub-second exit closes it without a poll");
        T.Near(closed.Pnl, 200, 0.01, "fill prices and point value produce exact gross P&L");
        T.Near(closed.Commission, 5, 0.01, "execution commissions are retained");
        T.Eq(t.TradesToday, 1, "one round trip is one trade");
    }

    static void SimultaneousInstrumentsStayIndependent()
    {
        T.S("simultaneous instruments do not collapse into one trade");
        BallastTracker t = Fresh();
        DateTime now = new DateTime(2026, 8, 3, 10, 0, 0);

        t.OnExecution("nq-in", "NQ SEP26", 2, 20000, 20, 0, now, "A");
        t.OnExecution("es-in", "ES SEP26", -1, 6000, 50, 0, now.AddSeconds(1), "A");
        T.Eq(t.OpenContracts, 3, "open size is summed across instruments");

        BallastTrade es = t.OnExecution("es-out", "ES SEP26", 1, 5998, 50, 0,
                                         now.AddSeconds(2), "A");
        T.Eq(es.Instrument, "ES SEP26", "the ES exit closes only ES");
        T.Near(es.Pnl, 100, 0.01, "the short ES P&L is independent");
        T.Eq(t.ExecutionPosition("NQ SEP26"), 2, "NQ remains open");

        BallastTrade nq = t.OnExecution("nq-out", "NQ SEP26", -2, 20005, 20, 0,
                                         now.AddSeconds(3), "A");
        T.Eq(nq.Instrument, "NQ SEP26", "NQ closes as its own trade");
        T.Near(nq.Pnl, 200, 0.01, "its P&L uses only NQ fills");
        T.Eq(t.TradesToday, 2, "two instruments produce two round trips");
    }

    static void DuplicateExecutionsAreIdempotent()
    {
        T.S("duplicate execution IDs are ignored");
        BallastTracker t = Fresh();
        DateTime now = new DateTime(2026, 8, 3, 10, 30, 0);

        t.OnExecution("same", "MNQ SEP26", 1, 20000, 2, 0, now, "A");
        t.OnExecution("same", "MNQ SEP26", 1, 20000, 2, 0, now, "A");
        T.Eq(t.ExecutionPosition("MNQ SEP26"), 1, "a replay does not double the position");

        BallastTrade closed = t.OnExecution("exit", "MNQ SEP26", -1, 20001, 2, 0,
                                             now.AddMinutes(1), "A");
        T.Ok(closed != null, "one exit closes the one real entry");
        T.Eq(t.TradesToday, 1, "the replay does not create a second trade");
    }

    static void PartialExitsAndReversalsAreAccountedFor()
    {
        T.S("partial exits and reversals preserve fill accounting");
        BallastTracker t = Fresh();
        DateTime now = new DateTime(2026, 8, 3, 11, 0, 0);

        t.OnExecution("p1", "ES SEP26", 2, 6000, 50, 0, now, "A");
        T.Ok(t.OnExecution("p2", "ES SEP26", -1, 6001, 50, 0,
                           now.AddMinutes(1), "A") == null, "a partial exit stays open");
        BallastTrade reversed = t.OnExecution("p3", "ES SEP26", -2, 6002, 50, 2,
                                               now.AddMinutes(2), "A");

        T.Near(reversed.Pnl, 150, 0.01, "both closing fills are included");
        T.Near(reversed.Commission, 1, 0.001,
               "a reversal fill assigns only the closing share to the old trade");
        T.Eq(t.ExecutionPosition("ES SEP26"), -1, "the excess fill opens the reversal");

        BallastTrade shortSide = t.OnExecution("p4", "ES SEP26", 1, 6000, 50, 1,
                                                now.AddMinutes(3), "A");
        T.Near(shortSide.Pnl, 100, 0.01, "the reversed short is tracked separately");
        T.Near(shortSide.Commission, 2, 0.001,
               "the opening share follows the new trade and combines with its exit");
        T.Eq(t.TradesToday, 2, "the reversal yields two completed round trips");
    }

    static void APositionMismatchFailsClosed()
    {
        T.S("an execution telemetry mismatch fails closed");
        BallastTracker t = Fresh();
        t.MarkExecutionTelemetryGap("NinjaTrader and Ballast disagree.");

        DisciplineDecision d = DisciplineEngine.Evaluate(t.BuildInput(
            new DateTime(2026, 8, 3, 12, 0, 0)));
        T.Eq(d.Action, DisciplineAction.Lockout, "no new trade is cleared on incomplete telemetry");
        T.Ok(DisciplineEngine.RowWarning(t.BuildInput(new DateTime(2026, 8, 3, 12, 0, 0)), d)
             .IndexOf("mismatch") >= 0, "the row tells the trader what failed");
    }
}
