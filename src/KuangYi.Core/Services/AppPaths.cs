namespace KuangYi.Core.Services;

public static class AppPaths
{
    public static string Root { get; } = ResolveRoot();

    private static string ResolveRoot()
    {
        var candidates = new List<string>();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(local))
        {
            candidates.Add(Path.Combine(local, "KuangYi"));
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile))
        {
            candidates.Add(Path.Combine(profile, "AppData", "Local", "KuangYi"));
        }

        // Fallback for restricted/low-integrity launches: the profile may be readable
        // but not writable, while the program directory usually is.
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "KuangYiData"));
        candidates.Add(Path.Combine(Path.GetTempPath(), "KuangYi"));

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                Directory.CreateDirectory(candidate);
                var probe = Path.Combine(candidate, ".write-test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return candidate;
            }
            catch
            {
                // try the next candidate
            }
        }

        return Path.Combine(Path.GetTempPath(), "KuangYi");
    }

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static string HistoryDb => Path.Combine(Root, "history.db");

    public static string ThumbnailDir => Path.Combine(Root, "thumbs");

    public static string OriginalDir => Path.Combine(Root, "originals");

    public static string DictionaryDb => Path.Combine(Root, "dict", "dict.db");

    public static string ModelDir => Path.Combine(Root, "models");

    public static string LogDir => Path.Combine(Root, "logs");

    public static string LogFile => Path.Combine(LogDir, "app.log");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(ThumbnailDir);
        Directory.CreateDirectory(LogDir);
        Directory.CreateDirectory(Path.GetDirectoryName(DictionaryDb)!);
        Directory.CreateDirectory(ModelDir);
    }
}
