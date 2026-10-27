using System.Threading;
using System.Threading.Tasks;
using KuangYi.Core.Models;

namespace KuangYi.Core.Services.Translation;

public interface ITranslationProvider
{
	string Id { get; }

	string Name { get; }

	Task<TranslationResult?> TranslateAsync(TranslationRequest request, CancellationToken ct);
}
