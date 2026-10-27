using System.Windows.Media;
using KuangYi.Core.Models;

namespace KuangYi.App.Windows;

public sealed class HistoryItem
{
	public HistoryEntry Entry { get; }

	public ImageSource? Thumbnail { get; }

	public string Title => OneLine(Entry.SourceText);

	public string Subtitle => OneLine(Entry.TargetText);

	public string Meta => $"{Languages.Resolve(Entry.SourceLang).NameZh} → {Languages.Resolve(Entry.TargetLang).NameZh} · {Entry.Provider} · {Entry.CreatedAt.ToLocalTime():MM-dd HH:mm}";

	public string FavoriteMark
	{
		get
		{
			if (!Entry.IsFavorite)
			{
				return string.Empty;
			}
			return "★";
		}
	}

	public HistoryItem(HistoryEntry entry, ImageSource? thumbnail)
	{
		Entry = entry;
		Thumbnail = thumbnail;
	}

	private static string OneLine(string text)
	{
		return text.Replace("\r", " ").Replace("\n", " ").Trim();
	}
}
