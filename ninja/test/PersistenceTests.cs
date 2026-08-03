using System;
using System.IO;
using Ballast;

public static class PersistenceTests
{
    public static void Run()
    {
        ReplacementKeepsACompleteBackup();
        MissingPrimaryRecoversFromBackup();
    }

    static string TempPath(string name)
    {
        string dir = Path.Combine(Path.GetTempPath(), "ballast-atomic-tests");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name + "-" + Guid.NewGuid().ToString("N") + ".txt");
    }

    static void ReplacementKeepsACompleteBackup()
    {
        T.S("atomic replacement keeps the previous complete file");
        string path = TempPath("replace");
        try
        {
            AtomicFile.WriteAllText(path, "version one");
            AtomicFile.WriteAllText(path, "version two");

            T.Eq(File.ReadAllText(path), "version two", "the new complete state is primary");
            T.Eq(File.ReadAllText(path + ".bak"), "version one", "the previous complete state is recoverable");
            T.Eq(Directory.GetFiles(Path.GetDirectoryName(path), Path.GetFileName(path) + ".tmp.*").Length,
                 0, "no temporary file is left behind");
        }
        finally
        {
            try { File.Delete(path); } catch { }
            try { File.Delete(path + ".bak"); } catch { }
        }
    }

    static void MissingPrimaryRecoversFromBackup()
    {
        T.S("a missing primary recovers from its backup");
        string path = TempPath("recover");
        try
        {
            AtomicFile.WriteAllText(path, "safe old state");
            AtomicFile.WriteAllText(path, "new state");
            File.Delete(path);

            T.Ok(AtomicFile.RecoverBackup(path), "recovery reports that it restored a file");
            T.Eq(File.ReadAllText(path), "safe old state", "the backup is copied back intact");
        }
        finally
        {
            try { File.Delete(path); } catch { }
            try { File.Delete(path + ".bak"); } catch { }
        }
    }
}
