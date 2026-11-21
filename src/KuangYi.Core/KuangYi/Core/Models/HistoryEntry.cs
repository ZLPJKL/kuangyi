using System;

namespace KuangYi.Core.Models;

public sealed class HistoryEntry
{
	public long Id { get; set; }

	public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

	public string SourceLang { get; set; } = string.Empty;

	public string TargetLang { get; set; } = string.Empty;

	public string SourceText { get; set; } = string.Empty;

	public string TargetText { get; set; } = string.Empty;

	public string Provider { get; set; } = string.Empty;

	public bool IsFavorite { get; set; }

	public string Tags { get; set; } = string.Empty;

	public string? ThumbPath { get; set; }

	public long ElapsedMs { get; set; }
}
