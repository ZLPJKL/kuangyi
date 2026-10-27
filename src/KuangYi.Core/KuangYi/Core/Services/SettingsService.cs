using System;
using System.IO;
using System.Text.Json;
using KuangYi.Core.Models;

namespace KuangYi.Core.Services;

public sealed class SettingsService
{
	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true
	};

	private readonly string _path;

	private readonly object _gate = new object();

	public SettingsService(string path)
	{
		_path = path;
		string directoryName = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
	}

	public AppSettings Load()
	{
		lock (_gate)
		{
			if (!File.Exists(_path))
			{
				return Normalize(new AppSettings());
			}
			try
			{
				AppSettings? obj = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions) ?? new AppSettings();
				obj.DeepLApiKey = SecretProtector.Unprotect(obj.DeepLApiKey);
				obj.LibreTranslateApiKey = SecretProtector.Unprotect(obj.LibreTranslateApiKey);
				return Normalize(obj);
			}
			catch (Exception ex)
			{
				TryBackupBrokenFile(ex);
				return Normalize(new AppSettings());
			}
		}
	}

	public void Save(AppSettings settings)
	{
		lock (_gate)
		{
			Normalize(settings);
			AppSettings appSettings = settings.Clone();
			appSettings.DeepLApiKey = SecretProtector.Protect(settings.DeepLApiKey);
			appSettings.LibreTranslateApiKey = SecretProtector.Protect(settings.LibreTranslateApiKey);
			string text = _path + ".tmp";
			File.WriteAllText(text, JsonSerializer.Serialize(appSettings, JsonOptions));
			File.Move(text, _path, overwrite: true);
		}
	}

	public static AppSettings Normalize(AppSettings s)
	{
		s.SchemaVersion = 1;
		s.Hotkey = (string.IsNullOrWhiteSpace(s.Hotkey) ? "F2" : s.Hotkey.Trim());
		string text = s.Theme?.Trim().ToLowerInvariant();
		string theme;
		if (text == "black")
		{
			theme = "Black";
		}
		else
		{
			theme = ((!(text == "auto")) ? "White" : "Auto");
		}
		s.Theme = theme;
		if (!string.Equals(s.SourceLanguage, "auto", StringComparison.OrdinalIgnoreCase) && !Languages.IsSupported(s.SourceLanguage))
		{
			s.SourceLanguage = "auto";
		}
		if (!string.IsNullOrEmpty(s.TargetLanguage) && !Languages.IsSupported(s.TargetLanguage))
		{
			s.TargetLanguage = string.Empty;
		}
		string text2 = s.OnlineProvider?.Trim().ToLowerInvariant();
		string onlineProvider;
		if (text2 == "libretranslate")
		{
			onlineProvider = "libretranslate";
		}
		else
		{
			onlineProvider = ((!(text2 == "deepl")) ? "mymemory" : "deepl");
		}
		s.OnlineProvider = onlineProvider;
		s.OnlineTimeoutSeconds = Math.Clamp(s.OnlineTimeoutSeconds, 3, 30);
		s.HistoryLimit = Math.Clamp(s.HistoryLimit, 10, 500);
		s.HistoryDays = Math.Clamp(s.HistoryDays, 1, 3650);
		s.FavoriteLimit = Math.Clamp(s.FavoriteLimit, 1, 50);
		s.ResultFontSize = Math.Clamp(s.ResultFontSize, 10.0, 28.0);
		return s;
	}

	private void TryBackupBrokenFile(Exception ex)
	{
		try
		{
			string destFileName = _path + ".broken";
			File.Copy(_path, destFileName, overwrite: true);
		}
		catch
		{
		}
	}
}
