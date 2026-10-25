using KuangYi.Core.Models;

namespace KuangYi.Core.Services.Translation;

public sealed class TranslationPipeline
{
    private readonly IReadOnlyList<ITranslationProvider> _providers;

    public TranslationPipeline(IEnumerable<ITranslationProvider> providers)
    {
        _providers = providers.ToList();
    }

    public IReadOnlyList<ITranslationProvider> Providers => _providers;

    public async Task<TranslationOutcome> TranslateAsync(TranslationRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return new TranslationOutcome { SummaryError = "没有可翻译的文字" };
        }

        if (string.Equals(request.SourceCode, request.TargetCode, StringComparison.OrdinalIgnoreCase))
        {
            return new TranslationOutcome
            {
                Result = new TranslationResult(request.Text, "none", "无需翻译", true),
                Attempts = Array.Empty<ProviderAttempt>(),
            };
        }

        var attempts = new List<ProviderAttempt>();

        foreach (var provider in _providers)
        {
            try
            {
                var result = await provider.TranslateAsync(request, ct).ConfigureAwait(false);
                if (result is not null && !string.IsNullOrWhiteSpace(result.Text))
                {
                    attempts.Add(new ProviderAttempt(provider.Id, provider.Name, true, null));
                    return new TranslationOutcome { Result = result, Attempts = attempts };
                }

                attempts.Add(new ProviderAttempt(provider.Id, provider.Name, false, "未返回结果"));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                attempts.Add(new ProviderAttempt(provider.Id, provider.Name, false, ex.Message));
            }
        }

        var summary = attempts.Count == 0
            ? "没有可用的翻译源"
            : string.Join("；", attempts.Select(a => $"{a.ProviderName}：{a.Error}"));

        return new TranslationOutcome { Attempts = attempts, SummaryError = summary };
    }
}
