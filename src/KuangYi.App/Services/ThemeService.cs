using System.Windows;

namespace KuangYi.App.Services;

public static class ThemeService
{
    public static void Apply(string theme)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        var name = string.Equals(theme, "Black", StringComparison.OrdinalIgnoreCase) ? "Black" : "White";
        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"Themes/{name}.xaml", UriKind.Relative),
        };

        var merged = app.Resources.MergedDictionaries;
        for (var i = 0; i < merged.Count; i++)
        {
            var source = merged[i].Source?.OriginalString ?? string.Empty;
            if (source.Contains("Themes/White.xaml", StringComparison.OrdinalIgnoreCase)
                || source.Contains("Themes/Black.xaml", StringComparison.OrdinalIgnoreCase))
            {
                merged[i] = dictionary;
                return;
            }
        }

        merged.Add(dictionary);
    }
}
