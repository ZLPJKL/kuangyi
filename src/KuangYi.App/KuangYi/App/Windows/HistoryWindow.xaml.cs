using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KuangYi.Core.Models;
using KuangYi.Core.Services;
using Microsoft.Win32;

namespace KuangYi.App.Windows;

public partial class HistoryWindow : Window, IComponentConnector
{
	private bool _forceClose;

	private bool _favoritesOnly;

	private int _version;

	public event EventHandler? ForceClosed;

	public HistoryWindow()
	{
		InitializeComponent();
		Closing += OnClosing;
		Closed += (object? _, EventArgs _) =>
		{
			ForceClosed?.Invoke(this, EventArgs.Empty);
		};
		Activated += (object? _, EventArgs _) =>
		{
			if (IsVisible)
			{
				Reload();
			}
		};
		UpdateTabs();
		Reload();
	}

	public void Reload()
	{
		ReloadAsync();
	}

	private async Task ReloadAsync()
	{
		int version = ++_version;
		try
		{
			AppSettings settings = App.Current.Settings;
			IReadOnlyList<HistoryEntry> entries = await App.Current.HistoryStore.SearchAsync(SearchBox.Text, _favoritesOnly);
			HistoryStats historyStats = await App.Current.HistoryStore.GetStatsAsync();
			if (version == _version)
			{
				List<HistoryItem> list = entries.Select((HistoryEntry e) => new HistoryItem(e, LoadThumbnail(e.ThumbPath))).ToList();
				EntriesList.ItemsSource = list;
				UpdateEmptyText();
				EmptyText.Visibility = ((list.Count != 0) ? Visibility.Collapsed : Visibility.Visible);
				CountText.Text = $"最近 {historyStats.RecentCount}/{settings.HistoryLimit} · 收藏 {historyStats.FavoriteCount}/{settings.FavoriteLimit}";
				UpdateSelectionState();
			}
		}
		catch (Exception ex)
		{
			AppLog.Error("history reload failed", ex);
		}
	}

	private void UpdateEmptyText()
	{
		if (SearchBox.Text.Trim().Length > 0)
		{
			EmptyText.Text = "没有找到匹配的记录。换个关键词，或清空搜索框。";
		}
		else if (_favoritesOnly)
		{
			EmptyText.Text = "收藏夹还是空的。在「最近」里选中一条记录，点底部「收藏」；收藏的记录会一直保留。";
		}
		else
		{
			EmptyText.Text = "最近还没有记录。按 F2 框选并翻译后，这里会出现记录。";
		}
	}

	private void UpdateTabs()
	{
		RecentTab.Style = (Style)FindResource(_favoritesOnly ? "GhostButton" : "PrimaryButton");
		FavoriteTab.Style = (Style)FindResource(_favoritesOnly ? "PrimaryButton" : "GhostButton");
		RecentTab.Padding = new Thickness(12.0, 4.0, 12.0, 4.0);
		FavoriteTab.Padding = new Thickness(12.0, 4.0, 12.0, 4.0);
	}

	private void UpdateSelectionState()
	{
		HistoryItem historyItem = EntriesList.SelectedItem as HistoryItem;
		FavoriteButton.Content = ((historyItem != null && historyItem.Entry.IsFavorite) ? "取消收藏" : "收藏");
		bool isEnabled = historyItem != null;
		FavoriteButton.IsEnabled = isEnabled;
		DeleteButton.IsEnabled = isEnabled;
		OpenButton.IsEnabled = isEnabled;
	}

	private void RecentTab_Click(object sender, RoutedEventArgs e)
	{
		if (_favoritesOnly)
		{
			_favoritesOnly = false;
			UpdateTabs();
			Reload();
		}
	}

	private void FavoriteTab_Click(object sender, RoutedEventArgs e)
	{
		if (!_favoritesOnly)
		{
			_favoritesOnly = true;
			UpdateTabs();
			Reload();
		}
	}

	private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
	{
		Reload();
	}

	private void EntriesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		UpdateSelectionState();
	}

	private void EntriesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
	{
		OpenSelected();
	}

	private async void Favorite_Click(object sender, RoutedEventArgs e)
	{
		if (!(EntriesList.SelectedItem is HistoryItem historyItem))
		{
			return;
		}
		try
		{
			switch (await App.Current.HistoryStore.ToggleFavoriteAsync(historyItem.Entry.Id, Options()))
			{
			case FavoriteToggleResult.LimitReached:
				MessageBox.Show("收藏已达上限（50 条）。请先取消收藏部分条目，或使用“替换最旧收藏”的思路手动整理。", "框译", MessageBoxButton.OK, MessageBoxImage.Asterisk);
				break;
			case FavoriteToggleResult.NotFound:
				MessageBox.Show("该记录已不存在。", "框译", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				break;
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
		if (EntriesList.SelectedItem is HistoryItem historyItem && MessageBox.Show("删除这条记录？\n\n" + Shorten(historyItem.Entry.SourceText, 80), "框译", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
		{
			await App.Current.HistoryStore.DeleteAsync(historyItem.Entry.Id);
			Reload();
		}
	}

	private async void ClearRecent_Click(object sender, RoutedEventArgs e)
	{
		if (MessageBox.Show($"清空最近记录？收藏的条目会保留。\n\n最近记录上限：{App.Current.Settings.HistoryLimit} 条。", "框译", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) == MessageBoxResult.Yes)
		{
			await App.Current.HistoryStore.ClearAsync(includeFavorites: false);
			Reload();
		}
	}

	private async void ClearAll_Click(object sender, RoutedEventArgs e)
	{
		if (MessageBox.Show("清空全部记录（包含收藏）？此操作不可撤销。", "框译", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) == MessageBoxResult.Yes)
		{
			await App.Current.HistoryStore.ClearAsync(includeFavorites: true);
			Reload();
		}
	}

	private async void Export_Click(object sender, RoutedEventArgs e)
	{
		SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			FileName = $"kuangyi-history-{DateTime.Now:yyyyMMdd-HHmm}.csv",
			Filter = "CSV 文件 (*.csv)|*.csv",
			DefaultExt = ".csv"
		};
		if (saveFileDialog.ShowDialog(this) != true)
		{
			return;
		}
		try
		{
			int value = await App.Current.HistoryStore.ExportCsvAsync(saveFileDialog.FileName, _favoritesOnly);
			MessageBox.Show($"已导出 {value} 条记录。", "框译", MessageBoxButton.OK, MessageBoxImage.Asterisk);
		}
		catch (Exception ex)
		{
			AppLog.Error("export failed", ex);
			MessageBox.Show("导出失败：" + ex.Message, "框译", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
	}

	private void Open_Click(object sender, RoutedEventArgs e)
	{
		OpenSelected();
	}

	private void OpenSelected()
	{
		if (EntriesList.SelectedItem is HistoryItem historyItem)
		{
			App.Current.ShowResultWindow();
			App.Current.ResultWindowInstance?.ShowHistoryEntry(historyItem.Entry);
		}
	}

	private void Close_Click(object sender, RoutedEventArgs e)
	{
		Hide();
	}

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
		DependencyObject dependencyObject = source as DependencyObject;
		while (dependencyObject != null && !(dependencyObject is Window))
		{
			if (dependencyObject is ButtonBase || dependencyObject is ComboBox || dependencyObject is TextBox)
			{
				return true;
			}
			dependencyObject = VisualTreeHelper.GetParent(dependencyObject);
		}
		return false;
	}

	private void OnClosing(object? sender, CancelEventArgs e)
	{
		if (!_forceClose)
		{
			e.Cancel = true;
			Hide();
		}
	}

	public void ForceClose()
	{
		_forceClose = true;
		Close();
	}

	private static HistoryOptions Options()
	{
		AppSettings settings = App.Current.Settings;
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
			BitmapImage bitmapImage = new BitmapImage();
			bitmapImage.BeginInit();
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			bitmapImage.DecodePixelWidth = 120;
			bitmapImage.UriSource = new Uri(path);
			bitmapImage.EndInit();
			bitmapImage.Freeze();
			return bitmapImage;
		}
		catch
		{
			return null;
		}
	}

	private static string Shorten(string text, int max)
	{
		string text2 = text.Replace("\r", " ").Replace("\n", " ").Trim();
		if (text2.Length > max)
		{
			return text2.Substring(0, max) + "…";
		}
		return text2;
	}
}
