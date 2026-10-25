using System.Text.Json;
using KuangYi.Core.Models;

namespace KuangYi.Core.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _path;
    private readonly object _gate = new();

    public SettingsService(string path)
    {
        _path = path;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public AppSettings Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
            {
                return Normalize(new AppSettings());
            }

            try
            {
                var json = File.ReadAllText(_path);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                return Normalize(settings);
            }
            catch (Exception ex)
            {
                TryBackupBrokenFile(ex);
                return Normalize(new AppSettings());
            }
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_gate)
        {
            Normalize(settings);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(tmp, _path, overwrite: true);
        }
    }

    public static AppSettings Normalize(AppSettings s)
    {
        s.SchemaVersion = 1;
        s.Hotkey = string.IsNullOrWhiteSpace(s.Hotkey) ? "F2" : s.Hotkey.Trim();
        s.Theme = string.Equals(s.Theme, "Black", StringComparison.OrdinalIgnoreCase) ? "Black" : "White";

        if (!string.Equals(s.SourceLanguage, Languages.Auto, StringComparison.OrdinalIgnoreCase)
            && !Languages.IsSupported(s.SourceLanguage))
        {
            s.SourceLanguage = Languages.Auto;
        }

        if (!string.IsNullOrEmpty(s.TargetLanguage) && !Languages.IsSupported(s.TargetLanguage))
        {
            s.TargetLanguage = string.Empty;
        }

        s.OnlineProvider = s.OnlineProvider?.Trim().ToLowerInvariant() switch
        {
            "libretranslate" => "libretranslate",
            "deepl" => "deepl",
            _ => "mymemory",
        };

        s.OnlineTimeoutSeconds = Math.Clamp(s.OnlineTimeoutSeconds, 3, 30);
        s.HistoryLimit = Math.Clamp(s.HistoryLimit, 10, 500);
        s.HistoryDays = Math.Clamp(s.HistoryDays, 1, 3650);
        s.FavoriteLimit = Math.Clamp(s.FavoriteLimit, 1, 50);
        s.ResultFontSize = Math.Clamp(s.ResultFontSize, 10, 28);
        return s;
    }

    private void TryBackupBrokenFile(Exception ex)
    {
        try
        {
            var backup = _path + ".broken";
            File.Copy(_path, backup, overwrite: true);
        }
        catch
        {
            // ignored: a broken settings file must never block startup
        }
    }
}
