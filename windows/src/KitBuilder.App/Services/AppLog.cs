namespace KitBuilder.App.Services;

/// <summary>Local rotating log (no personal data): %LOCALAPPDATA%\KitBuilder\logs\app.log.</summary>
public static class AppLog
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();

    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KitBuilder", "logs");

    public static string FilePath => Path.Combine(Directory, "app.log");

    public static void Write(string message, Exception? exception = null)
    {
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                {
                    File.Move(FilePath, Path.Combine(Directory, "app.1.log"), overwrite: true);
                }

                File.AppendAllText(FilePath, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} {message}"
                    + (exception is null ? "" : Environment.NewLine + exception) + Environment.NewLine);
            }
        }
        catch (IOException)
        {
            // Logging must never crash the app.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
