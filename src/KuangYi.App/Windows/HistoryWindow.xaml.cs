using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KuangYi.Core.Models;
using KuangYi.Core.Services;
using Microsoft.Win32;

namespace KuangYi.App.Windows;

public partial class HistoryWindow : Window
{
    private bool _forceClose;
    private bool _favoritesOnly;
    private int _version;

    public HistoryWindow()
    {
        InitializeComponent();

        Closing += OnClosing;
        Closed += (_, _) => ForceClosed?.Invoke(this, EventArgs.Empty);
        Activated += (_, _) =>
        {
            if (IsVisible)
            {
                Reload();
            }
        };

        UpdateTabs();
        Reload();
    }

    public event EventHandler? ForceClosed;

    public void Reload() => _ = ReloadAsync();

    private async Task ReloadAsync()
    {
        var version = ++_version;

        try
        {
            var settings = App.Current.Settings;
            var entries = await App.Current.HistoryStore.SearchAsync(SearchBox.Text, _favoritesOnly);
            var stats = await App.Current.HistoryStore.GetStatsAsync();

            if (version != _version)
            {
                return;
            }

            var items = entries
                .Select(e => new HistoryItem(e, LoadThumbnail(e.ThumbPath)))
                .ToList();

            EntriesList.ItemsSource = items;
            EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            CountText.Text =
                $"最近 {stats.RecentCount}/{settings.HistoryLimit} · 收藏 {stats.FavoriteCount}/{settings.FavoriteLimit}";

            UpdateSelectionState();
        }
        catch (Exception ex)
        {
            AppLog.Error("history reload failed", ex);
        }
    }

    private void UpdateTabs()
    {
        RecentTab.Style = (Style)FindResource(_favoritesOnly ? "GhostButton" : "PrimaryButton");
        FavoriteTab.Style = (Style)FindResource(_favoritesOnly ? "PrimaryButton" : "GhostButton");
        RecentTab.Padding = new Thickness(12, 4, 12, 4);
        FavoriteTab.Padding = new Thickness(12, 4, 12, 4);
    }

    private void UpdateSelectionState()
    {
        var item = EntriesList.SelectedItem as HistoryItem;
        FavoriteButton.Content = item?.Entry.IsFavorite == true ? "取消收藏" : "收藏";

        var enabled = item is not null;
        FavoriteButton.IsEnabled = enabled;
        DeleteButton.IsEnabled = enabled;
        OpenButton.IsEnabled = enabled;
    }

    private void RecentTab_Click(object sender, RoutedEventArgs e)
    {
        if (!_favoritesOnly)
        {
            return;
        }

        _favoritesOnly = false;
        UpdateTabs();
        Reload();
    }

    private void FavoriteTab_Click(object sender, RoutedEventArgs e)
    {
        if (_favoritesOnly)
        {
            return;
        }

        _favoritesOnly = true;
        UpdateTabs();
        Reload();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => Reload();

    private void EntriesList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelectionState();

    private void EntriesList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelected();

    private async void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (EntriesList.SelectedItem is not HistoryItem item)
        {
            return;
        }

        try
        {
            var result = await App.Current.HistoryStore.ToggleFavoriteAsync(item.Entry.Id, Options());
            if (result == FavoriteToggleResult.LimitReached)
            {
                MessageBox.Show(
                    "收藏已达上限（50 条）。请先取消收藏部分条目，或使用“替换最旧收藏”的思路手动整理。",
                    "框译",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else if (result == FavoriteToggleResult.NotFound)
            {
                MessageBox.Show("该记录已不存在。", "框译", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("toggle favorite failed", ex);
        }

        Reload();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (EntriesList.SelectedItem is not HistoryItem item)
        {
            return;
        }

        var confirm = MessageBox.Show(
            "删除这条记录？\n\n" + Shorten(item.Entry.SourceText, 80),
            "框译",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        await App.Current.HistoryStore.DeleteAsync(item.Entry.Id);
        Reload();
    }

    private async void ClearRecent_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            $"清空最近记录？收藏的条目会保留。\n\n最近记录上限：{App.Current.Settings.HistoryLimit} 条。",
            "框译",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        await App.Current.HistoryStore.ClearAsync(includeFavorites: false);
        Reload();
    }

    private async void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "清空全部记录（包含收藏）？此操作不可撤销。",
            "框译",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        await App.Current.HistoryStore.ClearAsync(includeFavorites: true);
        Reload();
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            FileName = $"kuangyi-history-{DateTime.Now:yyyyMMdd-HHmm}.csv",
            Filter = "CSV 文件 (*.csv)|*.csv",
            DefaultExt = ".csv",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var count = await App.Current.HistoryStore.ExportCsvAsync(dialog.FileName, _favoritesOnly);
            MessageBox.Show($"已导出 {count} 条记录。", "框译", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppLog.Error("export failed", ex);
            MessageBox.Show("导出失败：" + ex.Message, "框译", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e) => OpenSelected();

    private void OpenSelected()
    {
        if (EntriesList.SelectedItem is not HistoryItem item)
        {
            return;
        }

        App.Current.ShowResultWindow();
        App.Current.ResultWindowInstance?.ShowHistoryEntry(item.Entry);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || IsInteractiveSource(e.OriginalSource))
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static bool IsInteractiveSource(object? source)
    {
        var current = source as DependencyObject;
        while (current is not null and not Window)
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase
                or ComboBox
                or TextBox)
            {
                return true;
            }

            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_forceClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    private static HistoryOptions Options()
    {
        var settings = App.Current.Settings;
        return new HistoryOptions(settings.HistoryLimit, settings.HistoryDays, settings.FavoriteLimit);
    }

    private static ImageSource? LoadThumbnail(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 120;
            image.UriSource = new Uri(path!);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static string Shorten(string text, int max)
    {
        var single = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return single.Length <= max ? single : single[..max] + "…";
    }
}

public sealed class HistoryItem
{
    public HistoryItem(HistoryEntry entry, ImageSource? thumbnail)
    {
        Entry = entry;
        Thumbnail = thumbnail;
    }

    public HistoryEntry Entry { get; }

    public ImageSource? Thumbnail { get; }

    public string Title => OneLine(Entry.SourceText);

    public string Subtitle => OneLine(Entry.TargetText);

    public string Meta =>
        $"{Languages.Resolve(Entry.SourceLang).NameZh} → {Languages.Resolve(Entry.TargetLang).NameZh} · {Entry.Provider} · {Entry.CreatedAt.ToLocalTime():MM-dd HH:mm}";

    public string FavoriteMark => Entry.IsFavorite ? "★" : string.Empty;

    private static string OneLine(string text)
        => text.Replace("\r", " ").Replace("\n", " ").Trim();
}
