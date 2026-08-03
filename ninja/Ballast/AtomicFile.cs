using System;
using System.IO;
using System.Text;

namespace Ballast
{
    /// <summary>
    /// Crash-safe text persistence for Ballast's local state. The temporary file
    /// is created beside the destination so File.Replace stays on one volume.
    /// A successful replacement leaves the previous complete file at .bak.
    /// </summary>
    public static class AtomicFile
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        public static void WriteAllText(string path, string text)
        {
            WriteAllText(path, text, Utf8);
        }

        public static void WriteAllText(string path, string text, Encoding encoding)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("A destination path is required.", "path");
            if (encoding == null) encoding = Utf8;

            string full = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(full);
            if (string.IsNullOrEmpty(directory)) directory = Directory.GetCurrentDirectory();
            Directory.CreateDirectory(directory);

            string temp = Path.Combine(directory, Path.GetFileName(full) + ".tmp." + Guid.NewGuid().ToString("N"));
            string backup = full + ".bak";

            try
            {
                using (FileStream stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write,
                                                          FileShare.None, 4096, FileOptions.WriteThrough))
                using (StreamWriter writer = new StreamWriter(stream, encoding))
                {
                    writer.Write(text ?? "");
                    writer.Flush();
                    stream.Flush(true);
                }

                if (File.Exists(full))
                    File.Replace(temp, full, backup, true);
                else
                    File.Move(temp, full);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        public static void WriteAllLines(string path, string[] lines)
        {
            StringBuilder text = new StringBuilder();
            if (lines != null)
            {
                for (int i = 0; i < lines.Length; i++)
                    text.Append(lines[i] ?? "").Append(Environment.NewLine);
            }
            WriteAllText(path, text.ToString(), Utf8);
        }

        /// <summary>Restore the last complete file only when the primary is absent.</summary>
        public static bool RecoverBackup(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string full = Path.GetFullPath(path);
            string backup = full + ".bak";
            if (File.Exists(full) || !File.Exists(backup)) return false;
            File.Copy(backup, full, false);
            return true;
        }
    }
}
