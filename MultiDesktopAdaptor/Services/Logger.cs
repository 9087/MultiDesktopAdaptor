using System.Diagnostics;
using System.IO;

namespace MultiDesktopAdaptor.Services;

/// <summary>
/// Simple static logger that writes to Debug output and a rolling file.
/// All project code can use Logger.Info(...) from anywhere.
/// </summary>
public static class Logger
{
    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MultiDesktopAdaptor", "app.log");

    static Logger()
    {
        // Ensure directory exists
        var dir = Path.GetDirectoryName(LogFilePath);
        if (dir != null)
            Directory.CreateDirectory(dir);
    }

    public static void Info(string message)
    {
        Write("INFO", message);
    }

    public static void Warn(string message)
    {
        Write("WARN", message);
    }

    public static void Error(string message)
    {
        Write("ERROR", message);
    }

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}";
        Debug.WriteLine(line);
        try { File.AppendAllText(LogFilePath, line + Environment.NewLine); }
        catch { }
    }
}
