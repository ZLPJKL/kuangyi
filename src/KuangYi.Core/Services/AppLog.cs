namespace KuangYi.Core.Services;

public static class AppLog
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message) => Write("WARN", message, null);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.LogDir);
                var file = AppPaths.LogFile;
                if (File.Exists(file) && new FileInfo(file).Length > MaxBytes)
                {
                    var old = file + ".1";
                    File.Move(file, old, overwrite: true);
                }

                var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                if (ex is not null)
                {
                    line += Environment.NewLine + ex;
                }

                File.AppendAllText(file, line + Environment.NewLine);
            }
        }
        catch (Exception logEx)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(AppContext.BaseDirectory, "applog-error.txt"),
                    $"{DateTimeOffset.Now:HH:mm:ss.fff} {logEx.GetType().Name}: {logEx.Message}{Environment.NewLine}file={AppPaths.LogFile}{Environment.NewLine}{logEx}{Environment.NewLine}");
            }
            catch
            {
                // logging must never throw into the app
            }
        }
    }
}
