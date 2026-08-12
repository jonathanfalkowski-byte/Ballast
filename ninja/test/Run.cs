using System;

public static class T
{
    public static int Pass, Fail;
    public static string Suite = "";
    public static void S(string name) { Suite = name; Console.WriteLine("\n== " + name); }
    public static void Ok(bool cond, string what)
    {
        if (cond) Pass++;
        else { Fail++; Console.WriteLine("  FAIL [" + Suite + "] " + what); }
    }
    public static void Eq(object a, object b, string what)
    {
        bool same = (a == null && b == null) || (a != null && a.Equals(b));
        if (same) Pass++;
        else { Fail++; Console.WriteLine("  FAIL [" + Suite + "] " + what + "  expected <" + b + "> got <" + a + ">"); }
    }
    public static void Near(double a, double b, double tol, string what)
    {
        if (Math.Abs(a - b) <= tol) Pass++;
        else { Fail++; Console.WriteLine("  FAIL [" + Suite + "] " + what + "  expected ~" + b + " got " + a); }
    }
}

public static class Program
{
    public static int Main()
    {
        CountTests.Run();
        TargetTests.Run();
        ApexFloorTests.Run();
        SanityTests.Run();
        LimitTests.Run();
        WindowTests.Run();
        LatchTests.Run();
        GapTests.Run();
        ExecutionTests.Run();
        SetupTests.Run();
        PersistenceTests.Run();
        RuleUpdateTests.Run();
        ProviderFloorTests.Run();
        MismatchTests.Run();
        ResetTests.Run();
        LessonTests.Run();
        PressureTests.Run();
        SuggestTests.Run();
        PracticeTests.Run();
        MonthTests.Run();
        LookBackTests.Run();
        DiskTests.Run();
        RestartTests.Run();
        Console.WriteLine("\n" + T.Pass + " passed, " + T.Fail + " failed");
        return T.Fail == 0 ? 0 : 1;
    }
}
