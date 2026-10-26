using System;
using System.IO;

namespace Intonaco.Core
{
    /// <summary>
    /// Minimal append-only diagnostic log at %LocalAppData%\Intonaco\debug.log.
    /// Desktop integration depends on undocumented Explorer window layouts that
    /// vary between Windows builds, so attach attempts record exactly what was
    /// found on the machine — that file is what users attach to bug reports.
    /// Logging must never fail the app: every error is swallowed.
    /// </summary>
    internal static class DebugLog
    {
        private static readonly object Lock = new object();

        public static string LogFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Intonaco", "debug.log");

        public static void Write(string message)
        {
            try
            {
                lock (Lock)
                {
                    string? dir = Path.GetDirectoryName(LogFilePath);
                    if (dir != null)
                        Directory.CreateDirectory(dir);
                    File.AppendAllText(LogFilePath,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\r\n");
                }
            }
            catch
            {
                // Logging must never throw.
            }
        }
    }
}
