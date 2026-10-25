using System.Net;
using System.Text.Json;
using KuangYi.Core.Models;

namespace KuangYi.Core.Services.Translation;

/// <summary>
/// Free public MyMemory endpoint. No key required, but it has a daily anonymous quota
/// and a request size limit, so the text is chunked and failures surface as errors.
/// </summary>
public sealed class MyMemoryProvider : ITranslationProvider
{
    private const int MaxChunkBytes = 450;

    private readonly HttpClient _http;
    private readonly TimeSpan _timeout;

    public MyMemoryProvider(HttpClient http, TimeSpan timeout)
    {
        _http = http;
        _timeout = timeout;
    }

    public string Id => "mymemory";

    public string Name => "MyMemory 在线";

    public async Task<TranslationResult?> TranslateAsync(TranslationRequest request, CancellationToken ct)
    {
        var source = MapCode(request.SourceCode);
        var target = MapCode(request.TargetCode);
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            return new TranslationResult(request.Text, Id, Name, false);
        }

        var parts = new List<string>();
        foreach (var chunk in TextChunker.SplitByUtf8Bytes(request.Text, MaxChunkBytes))
        {
            parts.Add(await TranslateChunkAsync(chunk, source, target, ct).ConfigureAwait(false));
        }

        return new TranslationResult(string.Concat(parts), Id, Name, false);
    }

    private async Task<string> TranslateChunkAsync(string text, string source, string target, CancellationToken ct)
    {
        var url =
            $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(text)}&langpair={Uri.EscapeDataString(source)}%7C{Uri.EscapeDataString(target)}";

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(_timeout);

        using var response = await _http.GetAsync(url, linked.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var status = root.TryGetProperty("responseStatus", out var statusElement) && statusElement.TryGetInt32(out var code)
            ? code
            : 0;

        if (status != 200)
        {
            var details = root.TryGetProperty("responseDetails", out var detailElement)
                ? detailElement.ToString()
                : $"HTTP {status}";
            throw new InvalidOperationException($"在线翻译返回错误：{details}");
        }

        var translated = root.GetProperty("responseData").GetProperty("translatedText").GetString();
        if (string.IsNullOrWhiteSpace(translated))
        {
            throw new InvalidOperationException("在线翻译返回空结果");
        }

        if (translated.Contains("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("公共翻译接口额度已用尽，请稍后再试或改用离线词典");
        }

        return WebUtility.HtmlDecode(translated);
    }

    private static string MapCode(string code) => code switch
    {
        "zh" => "zh-CN",
        "en" => "en-GB",
        "ja" => "ja-JP",
        "ko" => "ko-KR",
        "ru" => "ru-RU",
        _ => code,
    };
}
