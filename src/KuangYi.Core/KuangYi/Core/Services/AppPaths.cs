using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KuangYi.Core.Services;

public static class AppPaths
{
	public static string Root { get; } = ResolveRoot();

	public static string SettingsFile => Path.Combine(Root, "settings.json");

	public static string HistoryDb => Path.Combine(Root, "history.db");

	public static string ThumbnailDir => Path.Combine(Root, "thumbs");

	public static string OriginalDir => Path.Combine(Root, "originals");

	public static string DictionaryDb => Path.Combine(Root, "dict", "dict.db");

	public static string ModelDir => Path.Combine(Root, "models");

	public static string LogDir => Path.Combine(Root, "logs");

	public static string LogFile => Path.Combine(LogDir, "app.log");

	private static string ResolveRoot()
	{
		List<string> list = new List<string>();
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		if (!string.IsNullOrWhiteSpace(folderPath))
		{
			list.Add(Path.Combine(folderPath, "KuangYi"));
		}
		string folderPath2 = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		if (!string.IsNullOrWhiteSpace(folderPath2))
		{
			list.Add(Path.Combine(folderPath2, "AppData", "Local", "KuangYi"));
		}
		list.Add(Path.Combine(AppContext.BaseDirectory, "KuangYiData"));
		list.Add(Path.Combine(Path.GetTempPath(), "KuangYi"));
		foreach (string item in list.Distinct(StringComparer.OrdinalIgnoreCase))
		{
			try
			{
				Directory.CreateDirectory(item);
				string path = Path.Combine(item, ".write-test");
				File.WriteAllText(path, "ok");
				File.Delete(path);
				return item;
			}
			catch
			{
			}
		}
		return Path.Combine(Path.GetTempPath(), "KuangYi");
	}

	public static void EnsureCreated()
	{
		Directory.CreateDirectory(Root);
		Directory.CreateDirectory(ThumbnailDir);
		Directory.CreateDirectory(LogDir);
		Directory.CreateDirectory(Path.GetDirectoryName(DictionaryDb));
		Directory.CreateDirectory(ModelDir);
	}
}
