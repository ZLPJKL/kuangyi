using System.Net.Http.Json;
using System.Text.Json;
using KuangYi.Core.Models;

namespace KuangYi.Core.Services.Translation;

/// <summary>
/// LibreTranslate compatible endpoint (self-hosted instance or a user supplied public one).
/// Kept optional: the product never assumes a public instance is available.
/// </summary>
public sealed class LibreTranslateProvider : ITranslationProvider
{
    private readonly HttpClient _http;
    private readonly string _endpoint;
    private readonly string _apiKey;
    private readonly TimeSpan _timeout;

    public LibreTranslateProvider(HttpClient http, string endpoint, string apiKey, TimeSpan timeout)
    {
        _http = http;
        _endpoint = (endpoint ?? string.Empty).TrimEnd('/');
        _apiKey = apiKey ?? string.Empty;
        _timeout = timeout;
    }

    public string Id => "libretranslate";

    public string Name => "LibreTranslate";

    public async Task<TranslationResult?> TranslateAsync(TranslationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_endpoint))
        {
            return null;
        }

        var payload = new Dictionary<string, object?>
        {
            ["q"] = request.Text,
            ["source"] = request.SourceCode,
            ["target"] = request.TargetCode,
            ["format"] = "text",
        };

        if (!string.IsNullOrEmpty(_apiKey))
        {
            payload["api_key"] = _apiKey;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(_timeout);

        using var response = await _http
            .PostAsJsonAsync(_endpoint + "/translate", payload, linked.Token)
            .ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"LibreTranslate 返回 {(int)response.StatusCode}：{Trim(body)}");
        }

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException("LibreTranslate 错误：" + error.ToString());
        }

        var text = doc.RootElement.TryGetProperty("translatedText", out var translated)
            ? translated.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("LibreTranslate 返回空结果");
        }

        return new TranslationResult(text, Id, Name, false);
    }

    private static string Trim(string value)
        => value.Length <= 160 ? value : value[..160] + "…";
}
