using System;
using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Win32;

namespace KuangYi.App.Services;

public static class ThemeService
{
	private static string _current = string.Empty;

	public static string Resolve(string theme)
	{
		if (string.Equals(theme, "Auto", StringComparison.OrdinalIgnoreCase))
		{
			if (!IsSystemLight())
			{
				return "Black";
			}
			return "White";
		}
		if (!string.Equals(theme, "Black", StringComparison.OrdinalIgnoreCase))
		{
			return "White";
		}
		return "Black";
	}

	private static bool IsSystemLight()
	{
		try
		{
			return !(Registry.GetValue("HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", "AppsUseLightTheme", 1) is int num) || num != 0;
		}
		catch
		{
			return true;
		}
	}

	public static void Apply(string theme)
	{
		Application current = Application.Current;
		if (current == null)
		{
			return;
		}
		string text = Resolve(theme);
		if (string.Equals(text, _current, StringComparison.Ordinal))
		{
			return;
		}
		ResourceDictionary resourceDictionary = new ResourceDictionary
		{
			Source = new Uri("Themes/" + text + ".xaml", UriKind.Relative)
		};
		Collection<ResourceDictionary> mergedDictionaries = current.Resources.MergedDictionaries;
		for (int i = 0; i < mergedDictionaries.Count; i++)
		{
			string text2 = mergedDictionaries[i].Source?.OriginalString ?? string.Empty;
			if (text2.Contains("Themes/White.xaml", StringComparison.OrdinalIgnoreCase) || text2.Contains("Themes/Black.xaml", StringComparison.OrdinalIgnoreCase))
			{
				mergedDictionaries[i] = resourceDictionary;
				_current = text;
				return;
			}
		}
		mergedDictionaries.Add(resourceDictionary);
		_current = text;
	}
}
