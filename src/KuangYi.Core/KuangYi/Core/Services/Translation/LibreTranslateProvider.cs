using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KuangYi.Core.Models;

namespace KuangYi.Core.Services.Translation;

public sealed class LibreTranslateProvider : ITranslationProvider
{
	private readonly HttpClient _http;

	private readonly string _endpoint;

	private readonly string _apiKey;

	private readonly TimeSpan _timeout;

	public string Id => "libretranslate";

	public string Name => "LibreTranslate";

	public LibreTranslateProvider(HttpClient http, string endpoint, string apiKey, TimeSpan timeout)
	{
		_http = http;
		_endpoint = (endpoint ?? string.Empty).TrimEnd('/');
		_apiKey = apiKey ?? string.Empty;
		_timeout = timeout;
	}

	public async Task<TranslationResult?> TranslateAsync(TranslationRequest request, CancellationToken ct)
	{
		if (string.IsNullOrWhiteSpace(_endpoint))
		{
			return null;
		}
		Dictionary<string, object> dictionary = new Dictionary<string, object>
		{
			["q"] = request.Text,
			["source"] = request.SourceCode,
			["target"] = request.TargetCode,
			["format"] = "text"
		};
		if (!string.IsNullOrEmpty(_apiKey))
		{
			dictionary["api_key"] = _apiKey;
		}
		using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
		linked.CancelAfter(_timeout);
		using HttpResponseMessage response = await _http.PostAsJsonAsync(_endpoint + "/translate", dictionary, linked.Token).ConfigureAwait(continueOnCapturedContext: false);
		string text = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(continueOnCapturedContext: false);
		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"LibreTranslate 返回 {(int)response.StatusCode}：{Trim(text)}");
		}
		using JsonDocument jsonDocument = JsonDocument.Parse(text);
		if (jsonDocument.RootElement.TryGetProperty("error", out var value))
		{
			throw new InvalidOperationException("LibreTranslate 错误：" + value);
		}
		string? obj = (jsonDocument.RootElement.TryGetProperty("translatedText", out var value2) ? value2.GetString() : null);
		if (string.IsNullOrWhiteSpace(obj))
		{
			throw new InvalidOperationException("LibreTranslate 返回空结果");
		}
		return new TranslationResult(obj, Id, Name, IsOffline: false);
	}

	private static string Trim(string value)
	{
		if (value.Length > 160)
		{
			return value.Substring(0, 160) + "…";
		}
		return value;
	}
}
