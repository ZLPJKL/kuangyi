namespace KuangYi.Core.Models;

public sealed record HistoryStats(int RecentCount, int FavoriteCount)
{
	public int Total => RecentCount + FavoriteCount;
}
