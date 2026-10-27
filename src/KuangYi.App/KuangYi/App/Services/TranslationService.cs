using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using KuangYi.Core.Models;
using KuangYi.Core.Offline;
using KuangYi.Core.Services.Translation;

namespace KuangYi.App.Services;

public sealed class TranslationService
{
	private readonly HttpClient _http = new HttpClient
	{
		Timeout = TimeSpan.FromSeconds(40L)
	};

	private readonly DictionaryProvider _dictionary;

	private readonly OfflineModelProvider _offline;

	public bool DictionaryAvailable => _dictionary.IsAvailable;

	public bool OfflineAvailable => _offline.IsAvailable;

	public string OfflineModelRoot => _offline.ModelRoot;

	public IReadOnlyList<string> OfflineDirections => _offline.InstalledDirections;

	public TranslationService(string dictionaryPath, string? modelRoot = null)
	{
		_dictionary = new DictionaryProvider(dictionaryPath);
		_offline = new OfflineModelProvider(modelRoot ?? OfflineModelProvider.ResolveModelRoot());
	}

	public void TrimMemory()
	{
		_offline.TrimNow();
	}

	public TranslationPipeline CreatePipeline(AppSettings settings)
	{
		List<ITranslationProvider> list = new List<ITranslationProvider>();
		if (!settings.LocalOnly && settings.OnlineEnabled)
		{
			TimeSpan timeout = TimeSpan.FromSeconds(settings.OnlineTimeoutSeconds);
			if (string.Equals(settings.OnlineProvider, "deepl", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(settings.DeepLApiKey))
			{
				list.Add(new DeepLProvider(_http, settings.DeepLApiKey.Trim(), timeout));
			}
			else if (string.Equals(settings.OnlineProvider, "libretranslate", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(settings.LibreTranslateEndpoint))
			{
				list.Add(new LibreTranslateProvider(_http, settings.LibreTranslateEndpoint, settings.LibreTranslateApiKey, timeout));
			}
			else if (string.Equals(settings.OnlineProvider, "mymemory", StringComparison.OrdinalIgnoreCase))
			{
				list.Add(new MyMemoryProvider(_http, timeout));
			}
		}
		list.Add(_offline);
		list.Add(_dictionary);
		return new TranslationPipeline(list);
	}

	public async Task<string> TestOnlineAsync(string provider, string deepLKey, string libreEndpoint, string libreKey, int timeoutSeconds)
	{
		TimeSpan timeSpan = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 3, 30));
		using HttpClient http = new HttpClient
		{
			Timeout = timeSpan + TimeSpan.FromSeconds(5L)
		};
		ITranslationProvider translationProvider;
		if (string.Equals(provider, "deepl", StringComparison.OrdinalIgnoreCase))
		{
			if (string.IsNullOrWhiteSpace(deepLKey))
			{
				throw new InvalidOperationException("请先填写 DeepL API Key");
			}
			translationProvider = new DeepLProvider(http, deepLKey.Trim(), timeSpan);
		}
		else if (string.Equals(provider, "libretranslate", StringComparison.OrdinalIgnoreCase))
		{
			if (string.IsNullOrWhiteSpace(libreEndpoint))
			{
				throw new InvalidOperationException("请先填写 LibreTranslate 地址");
			}
			translationProvider = new LibreTranslateProvider(http, libreEndpoint, libreKey, timeSpan);
		}
		else
		{
			translationProvider = new MyMemoryProvider(http, timeSpan);
		}
		TranslationResult translationResult = await translationProvider.TranslateAsync(new TranslationRequest("Hello", "en", "zh"), CancellationToken.None);
		if (translationResult == null || string.IsNullOrWhiteSpace(translationResult.Text))
		{
			throw new InvalidOperationException("服务返回空结果");
		}
		return translationResult.Text;
	}
}
