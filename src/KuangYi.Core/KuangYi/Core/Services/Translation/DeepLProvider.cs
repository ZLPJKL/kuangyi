using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KuangYi.Core.Models;

namespace KuangYi.Core.Services.Translation;

public sealed class DeepLProvider : ITranslationProvider
{
	private const string FreeEndpoint = "https://api-free.deepl.com/v2/translate";

	private const string PaidEndpoint = "https://api.deepl.com/v2/translate";

	private readonly HttpClient _http;

	private readonly string _apiKey;

	private readonly TimeSpan _timeout;

	private string Endpoint
	{
		get
		{
			if (!_apiKey.EndsWith(":fx", StringComparison.OrdinalIgnoreCase))
			{
				return "https://api.deepl.com/v2/translate";
			}
			return "https://api-free.deepl.com/v2/translate";
		}
	}

	public string Id => "deepl";

	public string Name => "DeepL 在线";

	public DeepLProvider(HttpClient http, string apiKey, TimeSpan timeout)
	{
		_http = http;
		_apiKey = apiKey;
		_timeout = timeout;
	}

	public async Task<TranslationResult?> TranslateAsync(TranslationRequest request, CancellationToken ct)
	{
		string text = MapCode(request.SourceCode);
		string text2 = MapTarget(request.TargetCode);
		if (string.Equals(text, text2, StringComparison.OrdinalIgnoreCase))
		{
			return new TranslationResult(request.Text, Id, Name, IsOffline: false);
		}
		using FormUrlEncodedContent content = new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["text"] = request.Text,
			["source_lang"] = text,
			["target_lang"] = text2
		});
		using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
		linked.CancelAfter(_timeout);
		using HttpRequestMessage httpRequest = new HttpRequestMessage(HttpMethod.Post, Endpoint)
		{
			Content = content
		};
		httpRequest.Headers.TryAddWithoutValidation("Authorization", "DeepL-Auth-Key " + _apiKey);
		using HttpResponseMessage response = await _http.SendAsync(httpRequest, linked.Token).ConfigureAwait(continueOnCapturedContext: false);
		string text3 = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(continueOnCapturedContext: false);
		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"DeepL 返回 {(int)response.StatusCode}：{Trim(text3)}");
		}
		using JsonDocument jsonDocument = JsonDocument.Parse(text3);
		JsonElement property = jsonDocument.RootElement.GetProperty("translations");
		if (property.GetArrayLength() == 0)
		{
			throw new InvalidOperationException("DeepL 返回空结果");
		}
		string? text4 = property[0].GetProperty("text").GetString();
		if (string.IsNullOrWhiteSpace(text4))
		{
			throw new InvalidOperationException("DeepL 返回空文本");
		}
		return new TranslationResult(text4, Id, Name, IsOffline: false);
	}

	private static string Trim(string value)
	{
		if (value.Length > 180)
		{
			return value.Substring(0, 180);
		}
		return value;
	}

	private static string MapCode(string code)
	{
		return code switch
		{
			"zh" => "ZH", 
			"en" => "EN", 
			"ja" => "JA", 
			"ko" => "KO", 
			"ru" => "RU", 
			_ => code.ToUpperInvariant(), 
		};
	}

	private static string MapTarget(string code)
	{
		return code switch
		{
			"zh" => "ZH", 
			"en" => "EN-US", 
			"ja" => "JA", 
			"ko" => "KO", 
			"ru" => "RU", 
			_ => code.ToUpperInvariant(), 
		};
	}
}
