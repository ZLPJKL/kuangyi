using System.Net.Http;
using KuangYi.Core.Models;
using KuangYi.Core.Offline;
using KuangYi.Core.Services.Translation;

namespace KuangYi.App.Services;

public sealed class TranslationService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(40) };
    private readonly DictionaryProvider _dictionary;
    private readonly OfflineModelProvider _offline;

    public TranslationService(string dictionaryPath, string? modelRoot = null)
    {
        _dictionary = new DictionaryProvider(dictionaryPath);
        _offline = new OfflineModelProvider(modelRoot ?? OfflineModelProvider.ResolveModelRoot());
    }

    public bool DictionaryAvailable => _dictionary.IsAvailable;

    public bool OfflineAvailable => _offline.IsAvailable;

    public string OfflineModelRoot => _offline.ModelRoot;

    public IReadOnlyList<string> OfflineDirections => _offline.InstalledDirections;

    public void TrimMemory() => _offline.TrimNow();

    public TranslationPipeline CreatePipeline(AppSettings settings)
    {
        var providers = new List<ITranslationProvider>();

        // When the user explicitly enables online translation, it should actually be
        // tried first; offline models remain as a fallback instead of silently winning.
        if (!settings.LocalOnly && settings.OnlineEnabled)
        {
            var timeout = TimeSpan.FromSeconds(settings.OnlineTimeoutSeconds);
            if (string.Equals(settings.OnlineProvider, "deepl", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(settings.DeepLApiKey))
            {
                providers.Add(new DeepLProvider(_http, settings.DeepLApiKey.Trim(), timeout));
            }
            else if (string.Equals(settings.OnlineProvider, "libretranslate", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(settings.LibreTranslateEndpoint))
            {
                providers.Add(new LibreTranslateProvider(
                    _http,
                    settings.LibreTranslateEndpoint,
                    settings.LibreTranslateApiKey,
                    timeout));
            }
            else if (string.Equals(settings.OnlineProvider, "mymemory", StringComparison.OrdinalIgnoreCase))
            {
                providers.Add(new MyMemoryProvider(_http, timeout));
            }
        }

        providers.Add(_offline);
        providers.Add(_dictionary);

        return new TranslationPipeline(providers);
    }
}
