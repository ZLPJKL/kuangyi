namespace KuangYi.Core.Models;

public sealed record ProviderAttempt(string ProviderId, string ProviderName, bool Success, string? Error);
