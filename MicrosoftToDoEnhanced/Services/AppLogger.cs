using System;
using System.IO;

namespace MicrosoftToDoEnhanced.Services;

public static class AppLogger
{
    private static readonly object Gate = new();

    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MicrosoftToDoEnhanced",
        "logs");

    private static readonly string LogFile = Path.Combine(LogDirectory, "app.log");

    public static event EventHandler<string>? ErrorReported;

    public static void Log(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }

    public static void LogError(string context, Exception exception) =>
        Log($"[ERROR] {context}: {exception}");

    public static void ReportError(string message)
    {
        Log(message);
        ErrorReported?.Invoke(null, message);
    }
}
