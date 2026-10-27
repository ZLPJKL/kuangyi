using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using KuangYi.App.Services;
using KuangYi.Core.Models;
using KuangYi.Core.Services;

namespace KuangYi.App.Windows;

public partial class SettingsWindow : Window, IComponentConnector
{
	private bool _initializing = true;

	private bool _forceClose;

	public event EventHandler? ForceClosed;

	public SettingsWindow()
	{
		InitializeComponent();
		ThemeCombo.ItemsSource = new string[3] { "纯白主题", "纯黑主题", "跟随系统" };
		Closing += OnClosing;
		Closed += (object? _, EventArgs _) =>
		{
			ForceClosed?.Invoke(this, EventArgs.Empty);
		};
		Reload();
	}

	public void Reload()
	{
		_initializing = true;
		AppSettings settings = App.Current.Settings;
		HotkeyBox.Text = settings.Hotkey;
		ThemeCombo.SelectedIndex = (string.Equals(settings.Theme, "Black", StringComparison.OrdinalIgnoreCase) ? 1 : (string.Equals(settings.Theme, "Auto", StringComparison.OrdinalIgnoreCase) ? 2 : 0));
		SourceLangCombo.ItemsSource = Languages.SourceChoices;
		SourceLangCombo.SelectedItem = Languages.SourceChoices.FirstOrDefault((LanguageInfo l) => string.Equals(l.Code, settings.SourceLanguage, StringComparison.OrdinalIgnoreCase)) ?? Languages.AutoInfo;
		TargetLangCombo.ItemsSource = Languages.All;
		TargetLangCombo.SelectedItem = Languages.Resolve(string.IsNullOrEmpty(settings.TargetLanguage) ? Languages.DefaultTargetForCurrentSystem() : settings.TargetLanguage);
		OnlineCheck.IsChecked = settings.OnlineEnabled;
		ProviderCombo.SelectedValue = settings.OnlineProvider;
		EndpointBox.Text = settings.LibreTranslateEndpoint;
		ApiKeyBox.Text = (string.Equals(settings.OnlineProvider, "deepl", StringComparison.OrdinalIgnoreCase) ? settings.DeepLApiKey : settings.LibreTranslateApiKey);
		LocalOnlyCheck.IsChecked = settings.LocalOnly;
		HistoryCheck.IsChecked = settings.HistoryEnabled;
		HistoryLimitBox.Text = settings.HistoryLimit.ToString();
		HistoryDaysBox.Text = settings.HistoryDays.ToString();
		ThumbnailCheck.IsChecked = settings.ThumbnailsEnabled;
		OriginalCheck.IsChecked = settings.KeepOriginalImage;
		StartupCheck.IsChecked = settings.StartWithWindows;
		ErrorText.Text = string.Empty;
		TestOnlineText.Text = string.Empty;
		UpdateOnlineState();
		UpdateEnvironmentStatus();
		_initializing = false;
		RefreshStatsAsync();
	}

	public void ForceClose()
	{
		_forceClose = true;
		Close();
	}

	private void UpdateEnvironmentStatus()
	{
		IReadOnlyList<string> availableLanguageTags = App.Current.Ocr.AvailableLanguageTags;
		if (availableLanguageTags.Count == 0)
		{
			OcrStatusText.Text = "识别语言：未发现任何 Windows 识别语言包，请先在系统设置中安装语言。";
		}
		else
		{
			List<string> list = new List<string>();
			string[] array = new string[5] { "zh", "en", "ja", "ko", "ru" };
			foreach (string code in array)
			{
				if (!App.Current.Ocr.IsLanguageInstalled(code))
				{
					list.Add(Languages.Resolve(code).NameZh);
				}
			}
			string text = ((list.Count == 0) ? "（五语齐全）" : ("（缺少：" + string.Join("、", list) + "）"));
			OcrStatusText.Text = "识别语言：" + string.Join("、", availableLanguageTags) + text;
		}
		DictionaryStatusText.Text = (App.Current.Translations.DictionaryAvailable ? ("词典数据包：已安装（" + AppPaths.DictionaryDb + "）") : ("词典数据包：未安装。把 SQLite 词库放到 " + AppPaths.DictionaryDb + " 后重启即可用于单词与短语。"));
		IReadOnlyList<string> offlineDirections = App.Current.Translations.OfflineDirections;
		ModelStatusText.Text = ((offlineDirections.Count == 0) ? ("离线翻译模型：未安装。把模型目录放到 " + App.Current.Translations.OfflineModelRoot + " 后重启生效。") : ($"离线翻译模型：已安装 {offlineDirections.Count} 个方向（{string.Join("、", offlineDirections)}）。" + Environment.NewLine + "目录：" + App.Current.Translations.OfflineModelRoot));
	}

	private async Task RefreshStatsAsync()
	{
		try
		{
			HistoryStats historyStats = await App.Current.HistoryStore.GetStatsAsync();
			HistoryStatsText.Text = $"当前：最近 {historyStats.RecentCount} 条 · 收藏 {historyStats.FavoriteCount} 条（收藏独立计数，上限 50 条）";
		}
		catch (Exception ex)
		{
			AppLog.Warn("history stats failed: " + ex.Message);
		}
	}

	private void UpdateOnlineState()
	{
		bool flag = OnlineCheck.IsChecked == true && LocalOnlyCheck.IsChecked != true;
		string a = (ProviderCombo.SelectedValue as string) ?? "mymemory";
		bool flag2 = string.Equals(a, "libretranslate", StringComparison.OrdinalIgnoreCase);
		bool flag3 = string.Equals(a, "deepl", StringComparison.OrdinalIgnoreCase);
		ProviderCombo.IsEnabled = flag;
		EndpointLabel.IsEnabled = flag & flag2;
		EndpointBox.IsEnabled = flag & flag2;
		ApiKeyLabel.Text = (flag3 ? "DeepL API Key（免费版通常以 :fx 结尾）" : "接口密钥（可选）");
		ApiKeyLabel.IsEnabled = flag && (flag2 | flag3);
		ApiKeyBox.IsEnabled = flag && (flag2 | flag3);
		ProviderHintText.Visibility = ((!flag3) ? Visibility.Collapsed : Visibility.Visible);
		DeepLTutorialButton.Visibility = ((!flag3) ? Visibility.Collapsed : Visibility.Visible);
		TestOnlineButton.IsEnabled = flag;
		EndpointLabel.Opacity = (EndpointBox.IsEnabled ? 1.0 : 0.45);
		ApiKeyLabel.Opacity = (ApiKeyBox.IsEnabled ? 1.0 : 0.45);
	}

	private void OnlineCheck_Changed(object sender, RoutedEventArgs e)
	{
		if (!_initializing)
		{
			UpdateOnlineState();
		}
	}

	private void LocalOnlyCheck_Changed(object sender, RoutedEventArgs e)
	{
		if (!_initializing)
		{
			UpdateOnlineState();
		}
	}

	private void ProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!_initializing)
		{
			SyncApiKeyForProvider();
			UpdateOnlineState();
		}
	}

	private void SyncApiKeyForProvider()
	{
		AppSettings settings = App.Current.Settings;
		string a = (ProviderCombo.SelectedValue as string) ?? "mymemory";
		ApiKeyBox.Text = (string.Equals(a, "deepl", StringComparison.OrdinalIgnoreCase) ? settings.DeepLApiKey : settings.LibreTranslateApiKey);
	}

	private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key != Key.Tab)
		{
			e.Handled = true;
			if (!HotkeyGesture.IsModifierKey(e.Key) && e.Key != Key.None)
			{
				HotkeyBox.Text = HotkeyGesture.Format(e.Key, Keyboard.Modifiers);
				ErrorText.Text = string.Empty;
			}
		}
	}

	private void ResetHotkey_Click(object sender, RoutedEventArgs e)
	{
		HotkeyBox.Text = "F2";
		ErrorText.Text = string.Empty;
	}

	private void Save_Click(object sender, RoutedEventArgs e)
	{
		if (!HotkeyGesture.TryParse(HotkeyBox.Text, out ParsedHotkey _, out string error))
		{
			ShowError(error ?? "快捷键无效");
			return;
		}
		if (!int.TryParse(HistoryLimitBox.Text.Trim(), out var result) || result < 10 || result > 500)
		{
			ShowError("最近记录上限需在 10 到 500 之间");
			return;
		}
		if (!int.TryParse(HistoryDaysBox.Text.Trim(), out var result2) || result2 < 1 || result2 > 3650)
		{
			ShowError("保留天数需在 1 到 3650 之间");
			return;
		}
		string text = (ProviderCombo.SelectedValue as string) ?? "mymemory";
		string text2 = EndpointBox.Text.Trim();
		string text3 = ApiKeyBox.Text.Trim();
		bool flag = OnlineCheck.IsChecked == true && LocalOnlyCheck.IsChecked != true;
		if ((string.Equals(text, "libretranslate", StringComparison.OrdinalIgnoreCase) & flag) && text2.Length == 0)
		{
			ShowError("使用 LibreTranslate 时必须填写地址");
			return;
		}
		if ((string.Equals(text, "deepl", StringComparison.OrdinalIgnoreCase) & flag) && text3.Length == 0)
		{
			ShowError("使用 DeepL 时必须填写 API Key（免费版密钥通常以 :fx 结尾）");
			return;
		}
		AppSettings appSettings = App.Current.Settings.Clone();
		appSettings.Hotkey = HotkeyBox.Text.Trim();
		AppSettings appSettings2 = appSettings;
		string theme;
		if (ThemeCombo.SelectedIndex == 1)
		{
			theme = "Black";
		}
		else
		{
			theme = ((ThemeCombo.SelectedIndex == 2) ? "Auto" : "White");
		}
		appSettings2.Theme = theme;
		appSettings.SourceLanguage = (SourceLangCombo.SelectedItem as LanguageInfo)?.Code ?? "auto";
		appSettings.TargetLanguage = (TargetLangCombo.SelectedItem as LanguageInfo)?.Code ?? Languages.Chinese.Code;
		appSettings.OnlineEnabled = OnlineCheck.IsChecked == true;
		appSettings.OnlineProvider = text;
		appSettings.LibreTranslateEndpoint = text2;
		if (string.Equals(text, "deepl", StringComparison.OrdinalIgnoreCase))
		{
			appSettings.DeepLApiKey = text3;
		}
		else if (string.Equals(text, "libretranslate", StringComparison.OrdinalIgnoreCase))
		{
			appSettings.LibreTranslateApiKey = text3;
		}
		appSettings.LocalOnly = LocalOnlyCheck.IsChecked == true;
		appSettings.HistoryEnabled = HistoryCheck.IsChecked == true;
		appSettings.HistoryLimit = result;
		appSettings.HistoryDays = result2;
		appSettings.ThumbnailsEnabled = ThumbnailCheck.IsChecked == true;
		appSettings.KeepOriginalImage = OriginalCheck.IsChecked == true;
		appSettings.StartWithWindows = StartupCheck.IsChecked == true;
		App.Current.ApplySettings(appSettings);
		StartupService.SetEnabled(appSettings.StartWithWindows);
		if (!App.Current.Hotkey.IsRegistered)
		{
			ShowError("设置已保存，但热键注册失败：" + App.Current.Hotkey.LastError);
		}
		else
		{
			Hide();
		}
	}

	private async void TestOnline_Click(object sender, RoutedEventArgs e)
	{
		string provider = (ProviderCombo.SelectedValue as string) ?? "mymemory";
		string libreEndpoint = EndpointBox.Text.Trim();
		string text = ApiKeyBox.Text.Trim();
		TestOnlineText.Text = "测试中…";
		TestOnlineButton.IsEnabled = false;
		try
		{
			string text2 = await App.Current.Translations.TestOnlineAsync(provider, text, libreEndpoint, text, App.Current.Settings.OnlineTimeoutSeconds);
			TestOnlineText.Text = "连接成功：" + text2;
		}
		catch (Exception ex)
		{
			string text3 = ex.Message;
			if (text3.Length > 120)
			{
				text3 = text3.Substring(0, 120) + "…";
			}
			TestOnlineText.Text = "测试失败：" + text3;
		}
		finally
		{
			TestOnlineButton.IsEnabled = OnlineCheck.IsChecked == true && LocalOnlyCheck.IsChecked != true;
		}
	}

	private void ShowError(string message)
	{
		ErrorText.Text = message;
	}

	private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = AppPaths.Root,
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			ShowError("无法打开目录：" + ex.Message);
		}
	}

	private void InstallOcrPack_Click(object sender, RoutedEventArgs e)
	{
		OpenLanguageSettings_Click(sender, e);
		ShowError("在“语言和区域”里给日语/韩语/俄语安装“光学字符识别”，装完重启框译。");
	}

	private void OpenDeepLTutorial_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			string text = Path.Combine(AppContext.BaseDirectory, "DeepL注册及使用教程.txt");
			if (!File.Exists(text))
			{
				ShowError("未找到 DeepL 教程文件：" + text);
				return;
			}
			Process.Start(new ProcessStartInfo(text)
			{
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			ShowError("无法打开 DeepL 教程：" + ex.Message);
		}
	}

	private void OpenDisclaimer_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			string text = Path.Combine(AppContext.BaseDirectory, "免责声明.txt");
			if (!File.Exists(text))
			{
				ShowError("未找到免责声明文件：" + text);
				return;
			}
			Process.Start(new ProcessStartInfo(text)
			{
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			ShowError("无法打开免责声明：" + ex.Message);
		}
	}

	private void OpenLanguageSettings_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = "ms-settings:regionlanguage",
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			ShowError("无法打开系统语言设置：" + ex.Message);
		}
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

	private void Cancel_Click(object sender, RoutedEventArgs e)
	{
		Hide();
	}

	private void OnClosing(object? sender, CancelEventArgs e)
	{
		if (!_forceClose)
		{
			e.Cancel = true;
			Hide();
		}
	}
}
