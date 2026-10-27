using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KuangYi.Core.Models;

namespace KuangYi.Core.Services.Translation;

public sealed class TranslationPipeline
{
	private readonly IReadOnlyList<ITranslationProvider> _providers;

	public IReadOnlyList<ITranslationProvider> Providers => _providers;

	public TranslationPipeline(IEnumerable<ITranslationProvider> providers)
	{
		_providers = providers.ToList();
	}

	public async Task<TranslationOutcome> TranslateAsync(TranslationRequest request, CancellationToken ct = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(request.Text))
		{
			return new TranslationOutcome
			{
				SummaryError = "没有可翻译的文字"
			};
		}
		if (string.Equals(request.SourceCode, request.TargetCode, StringComparison.OrdinalIgnoreCase))
		{
			return new TranslationOutcome
			{
				Result = new TranslationResult(request.Text, "none", "无需翻译", IsOffline: true),
				Attempts = Array.Empty<ProviderAttempt>()
			};
		}
		List<ProviderAttempt> attempts = new List<ProviderAttempt>();
		foreach (ITranslationProvider provider in _providers)
		{
			try
			{
				TranslationResult translationResult = await provider.TranslateAsync(request, ct).ConfigureAwait(continueOnCapturedContext: false);
				if ((object)translationResult != null && !string.IsNullOrWhiteSpace(translationResult.Text))
				{
					attempts.Add(new ProviderAttempt(provider.Id, provider.Name, Success: true, null));
					return new TranslationOutcome
					{
						Result = translationResult,
						Attempts = attempts
					};
				}
				attempts.Add(new ProviderAttempt(provider.Id, provider.Name, Success: false, "未返回结果"));
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex2)
			{
				attempts.Add(new ProviderAttempt(provider.Id, provider.Name, Success: false, ex2.Message));
			}
		}
		string summaryError = ((attempts.Count == 0) ? "没有可用的翻译源" : string.Join("；", attempts.Select((ProviderAttempt a) => a.ProviderName + "：" + a.Error)));
		return new TranslationOutcome
		{
			Attempts = attempts,
			SummaryError = summaryError
		};
	}
}
