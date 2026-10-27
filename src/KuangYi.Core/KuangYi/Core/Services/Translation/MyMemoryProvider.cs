using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KuangYi.Core.Models;

namespace KuangYi.Core.Services.Translation;

public sealed class MyMemoryProvider : ITranslationProvider
{
	private const int MaxChunkBytes = 450;

	private readonly HttpClient _http;

	private readonly TimeSpan _timeout;

	public string Id => "mymemory";

	public string Name => "MyMemory 在线";

	public MyMemoryProvider(HttpClient http, TimeSpan timeout)
	{
		_http = http;
		_timeout = timeout;
	}

	public async Task<TranslationResult?> TranslateAsync(TranslationRequest request, CancellationToken ct)
	{
		string source = MapCode(request.SourceCode);
		string target = MapCode(request.TargetCode);
		if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
		{
			return new TranslationResult(request.Text, Id, Name, IsOffline: false);
		}
		List<string> parts = new List<string>();
		foreach (string item in TextChunker.SplitByUtf8Bytes(request.Text))
		{
			List<string> list = parts;
			list.Add(await TranslateChunkAsync(item, source, target, ct).ConfigureAwait(continueOnCapturedContext: false));
		}
		return new TranslationResult(string.Concat(parts), Id, Name, IsOffline: false);
	}

	private async Task<string> TranslateChunkAsync(string text, string source, string target, CancellationToken ct)
	{
		string requestUri = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(text)}&langpair={Uri.EscapeDataString(source)}%7C{Uri.EscapeDataString(target)}";
		using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
		linked.CancelAfter(_timeout);
		using HttpResponseMessage response = await _http.GetAsync(requestUri, linked.Token).ConfigureAwait(continueOnCapturedContext: false);
		response.EnsureSuccessStatusCode();
		using JsonDocument jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(continueOnCapturedContext: false));
		JsonElement rootElement = jsonDocument.RootElement;
		int num = ((rootElement.TryGetProperty("responseStatus", out var value) && value.TryGetInt32(out var value2)) ? value2 : 0);
		if (num != 200)
		{
			string text2 = (rootElement.TryGetProperty("responseDetails", out var value3) ? value3.ToString() : $"HTTP {num}");
			throw new InvalidOperationException("在线翻译返回错误：" + text2);
		}
		string? text3 = rootElement.GetProperty("responseData").GetProperty("translatedText").GetString();
		if (string.IsNullOrWhiteSpace(text3))
		{
			throw new InvalidOperationException("在线翻译返回空结果");
		}
		if (text3.Contains("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("公共翻译接口额度已用尽，请稍后再试或改用离线词典");
		}
		return WebUtility.HtmlDecode(text3);
	}

	private static string MapCode(string code)
	{
		return code switch
		{
			"zh" => "zh-CN", 
			"en" => "en-GB", 
			"ja" => "ja-JP", 
			"ko" => "ko-KR", 
			"ru" => "ru-RU", 
			_ => code, 
		};
	}
}
