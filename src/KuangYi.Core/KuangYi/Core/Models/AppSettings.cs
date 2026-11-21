namespace KuangYi.Core.Models;

public sealed class AppSettings
{
	public int SchemaVersion { get; set; } = 1;

	public string Hotkey { get; set; } = "F2";

	public string Theme { get; set; } = "White";

	public string SourceLanguage { get; set; } = "auto";

	public string TargetLanguage { get; set; } = string.Empty;

	public bool OnlineEnabled { get; set; }

	public string OnlineProvider { get; set; } = "mymemory";

	public bool CacheEnabled { get; set; } = true;

	public int CacheMaxEntries { get; set; } = 1000;

	public string OcrQuality { get; set; } = "Balanced";

	public string LibreTranslateEndpoint { get; set; } = string.Empty;

	public string LibreTranslateApiKey { get; set; } = string.Empty;

	public string DeepLApiKey { get; set; } = string.Empty;

	public int OnlineTimeoutSeconds { get; set; } = 8;

	public bool LocalOnly { get; set; }

	public bool HistoryEnabled { get; set; } = true;

	public int HistoryLimit { get; set; } = 50;

	public int HistoryDays { get; set; } = 30;

	public int FavoriteLimit { get; set; } = 50;

	public bool ThumbnailsEnabled { get; set; } = true;

	public bool KeepOriginalImage { get; set; }

	public bool StartWithWindows { get; set; }

	public bool FirstRunCompleted { get; set; }

	public bool DisclaimerAccepted { get; set; }

	public bool PinResultWindow { get; set; }

	public double ResultFontSize { get; set; } = 14.0;

	public double ResultOpacity { get; set; } = 1.0;

	public double? MainWindowLeft { get; set; }

	public double? MainWindowTop { get; set; }

	public double? ResultWindowLeft { get; set; }

	public double? ResultWindowTop { get; set; }

	public AppSettings Clone()
	{
		return (AppSettings)MemberwiseClone();
	}
}
