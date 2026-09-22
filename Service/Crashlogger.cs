using System.Text;

namespace Arthiva.Services;

/// <summary>
/// Writes unhandled exceptions to a plain text file in the app's private
/// storage, so a crash can be diagnosed later without needing adb/USB
/// debugging attached at the time it happened. Hooked up in MauiProgram.cs
/// (AppDomain / TaskScheduler) and in Platforms/Android/MainApplication.cs
/// (Android native-level crashes).
/// </summary>
public static class CrashLogger
{
    private static readonly string LogFilePath =
        Path.Combine(FileSystem.AppDataDirectory, "crash_log.txt");

    public static string FilePath => LogFilePath;

    public static void Log(Exception? ex, string source)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("=====================================");
            sb.AppendLine($"Time   : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Source : {source}");
            sb.AppendLine($"Device : {DeviceInfo.Manufacturer} {DeviceInfo.Model}, Android {DeviceInfo.VersionString}");

            var current = ex;
            var depth = 0;
            while (current is not null && depth < 5)
            {
                sb.AppendLine(depth == 0 ? "Exception:" : $"Inner Exception ({depth}):");
                sb.AppendLine(current.GetType().FullName);
                sb.AppendLine(current.Message);
                sb.AppendLine(current.StackTrace);
                current = current.InnerException;
                depth++;
            }

            sb.AppendLine("=====================================");
            sb.AppendLine();

            // Append, don't overwrite, so multiple crashes accumulate for comparison.
            File.AppendAllText(LogFilePath, sb.ToString());
        }
        catch
        {
            // Logging must never itself throw during a crash handler.
        }
    }

    public static bool HasLog() => File.Exists(LogFilePath);

    public static string ReadLog() => File.Exists(LogFilePath) ? File.ReadAllText(LogFilePath) : string.Empty;

    public static void ClearLog()
    {
        try
        {
            if (File.Exists(LogFilePath))
                File.Delete(LogFilePath);
        }
        catch
        {
            // ignore
        }
    }
}