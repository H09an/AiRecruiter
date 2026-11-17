using System.IO;
using System.Threading;

namespace AiRecruiter;

public static class Logger
{
    private const string _logFile = "app.log";
    private const long MaxLogSizeBytes = 5 * 1024 * 1024; // 5 MB
    private static readonly object _lock = new object();

    public static void Log(string message)
    {
        try
        {
            lock (_lock)
            {
                string logLine = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}";
                long newLineSize = System.Text.Encoding.UTF8.GetByteCount(logLine + Environment.NewLine);

                if (File.Exists(_logFile) && (new FileInfo(_logFile).Length + newLineSize) > MaxLogSizeBytes)
                {
                    File.Delete(_logFile);
                }

                using var writer = new StreamWriter(_logFile, append: true);
                writer.WriteLine(logLine);

                Console.WriteLine(logLine);
            }
        }
        catch
        {
            // File access errors are swallowed so logging never crashes the application.
        }
    }
}
