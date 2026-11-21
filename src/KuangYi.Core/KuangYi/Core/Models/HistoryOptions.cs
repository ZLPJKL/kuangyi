namespace KuangYi.Core.Models;

public sealed record HistoryOptions(int NonFavoriteLimit = 50, int Days = 30, int FavoriteLimit = 50);
