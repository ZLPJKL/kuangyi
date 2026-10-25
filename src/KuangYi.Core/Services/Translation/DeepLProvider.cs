using System.Net.Http;
using System.Text.Json;
using KuangYi.Core.Models;

namespace KuangYi.Core.Services.Translation;

/// <summary>
/// DeepL API Free (500k characters/month). The key is supplied by the user and
/// usually ends with ":fx".
/// </summary>
public sealed class DeepLProvider : ITranslationProvider
{
    private const string FreeEndpoint = "https://api-free.deepl.com/v2/translate";
    private const string PaidEndpoint = "https://api.deepl.com/v2/translate";

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly TimeSpan _timeout;

    public DeepLProvider(HttpClient http, string apiKey, TimeSpan timeout)
    {
        _http = http;
        _apiKey = apiKey;
        _timeout = timeout;
    }

    private string Endpoint => _apiKey.EndsWith(":fx", StringComparison.OrdinalIgnoreCase)
        ? FreeEndpoint
        : PaidEndpoint;

    public string Id => "deepl";

    public string Name => "DeepL 在线";

    public async Task<TranslationResult?> TranslateAsync(TranslationRequest request, CancellationToken ct)
    {
        var source = MapCode(request.SourceCode);
        var target = MapTarget(request.TargetCode);
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            return new TranslationResult(request.Text, Id, Name, false);
        }

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["text"] = request.Text,
            ["source_lang"] = source,
            ["target_lang"] = target,
        });

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(_timeout);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = content,
        };
        httpRequest.Headers.TryAddWithoutValidation("Authorization", "DeepL-Auth-Key " + _apiKey);

        using var response = await _http.SendAsync(httpRequest, linked.Token).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"DeepL 返回 {(int)response.StatusCode}：{Trim(body)}");
        }

        using var document = JsonDocument.Parse(body);
        var translations = document.RootElement.GetProperty("translations");
        if (translations.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("DeepL 返回空结果");
        }

        var text = translations[0].GetProperty("text").GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("DeepL 返回空文本");
        }

        return new TranslationResult(text, Id, Name, IsOffline: false);
    }

    private static string Trim(string value)
        => value.Length <= 180 ? value : value[..180];

    private static string MapCode(string code) => code switch
    {
        "zh" => "ZH",
        "en" => "EN",
        "ja" => "JA",
        "ko" => "KO",
        "ru" => "RU",
        _ => code.ToUpperInvariant(),
    };

    private static string MapTarget(string code) => code switch
    {
        "zh" => "ZH",
        "en" => "EN-US",
        "ja" => "JA",
        "ko" => "KO",
        "ru" => "RU",
        _ => code.ToUpperInvariant(),
    };
}
