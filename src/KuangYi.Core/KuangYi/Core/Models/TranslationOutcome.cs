using System;
using System.Collections.Generic;

namespace KuangYi.Core.Models;

public sealed class TranslationOutcome
{
	public TranslationResult? Result { get; init; }

	public IReadOnlyList<ProviderAttempt> Attempts { get; init; } = Array.Empty<ProviderAttempt>();

	public string? SummaryError { get; init; }

	public bool Success => (object)Result != null;
}
