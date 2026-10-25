using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KuangYi.App.Services;
using KuangYi.Core.Models;
using KuangYi.Core.Services;

namespace KuangYi.App.Windows;

public partial class SettingsWindow : Window
{
    private bool _initializing = true;
    private bool _forceClose;

    public SettingsWindow()
    {
        InitializeComponent();

        ThemeCombo.ItemsSource = new[] { "纯白主题", "纯黑主题" };

        Closing += OnClosing;
        Closed += (_, _) => ForceClosed?.Invoke(this, EventArgs.Empty);

        Reload();
    }

    public event EventHandler? ForceClosed;

    public void Reload()
    {
        _initializing = true;

        var settings = App.Current.Settings;
        HotkeyBox.Text = settings.Hotkey;
        ThemeCombo.SelectedIndex = string.Equals(settings.Theme, "Black", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        SourceLangCombo.ItemsSource = Languages.SourceChoices;
        SourceLangCombo.SelectedItem = Languages.SourceChoices.FirstOrDefault(
            l => string.Equals(l.Code, settings.SourceLanguage, StringComparison.OrdinalIgnoreCase)) ?? Languages.AutoInfo;

        TargetLangCombo.ItemsSource = Languages.All;
        TargetLangCombo.SelectedItem = Languages.Resolve(
            string.IsNullOrEmpty(settings.TargetLanguage)
                ? Languages.DefaultTargetForCurrentSystem()
                : settings.TargetLanguage);

        OnlineCheck.IsChecked = settings.OnlineEnabled;
        ProviderCombo.SelectedValue = settings.OnlineProvider;
        EndpointBox.Text = settings.LibreTranslateEndpoint;
        ApiKeyBox.Text = string.Equals(settings.OnlineProvider, "deepl", StringComparison.OrdinalIgnoreCase)
            ? settings.DeepLApiKey
            : settings.LibreTranslateApiKey;
        LocalOnlyCheck.IsChecked = settings.LocalOnly;

        HistoryCheck.IsChecked = settings.HistoryEnabled;
        HistoryLimitBox.Text = settings.HistoryLimit.ToString();
        HistoryDaysBox.Text = settings.HistoryDays.ToString();
        ThumbnailCheck.IsChecked = settings.ThumbnailsEnabled;
        OriginalCheck.IsChecked = settings.KeepOriginalImage;
        StartupCheck.IsChecked = settings.StartWithWindows;

        ErrorText.Text = string.Empty;

        UpdateOnlineState();
        UpdateEnvironmentStatus();

        _initializing = false;
        _ = RefreshStatsAsync();
    }

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    private void UpdateEnvironmentStatus()
    {
        var tags = App.Current.Ocr.AvailableLanguageTags;
        if (tags.Count == 0)
        {
            OcrStatusText.Text = "识别语言：未发现任何 Windows 识别语言包，请先在系统设置中安装语言。";
        }
        else
        {
            var missing = new List<string>();
            foreach (var code in new[] { "zh", "en", "ja", "ko", "ru" })
            {
                if (!App.Current.Ocr.IsLanguageInstalled(code))
                {
                    missing.Add(Languages.Resolve(code).NameZh);
                }
            }

            var suffix = missing.Count == 0
                ? "（五语齐全）"
                : "（缺少：" + string.Join("、", missing) + "）";

            OcrStatusText.Text = "识别语言：" + string.Join("、", tags) + suffix;
        }

        DictionaryStatusText.Text = App.Current.Translations.DictionaryAvailable
            ? "词典数据包：已安装（" + AppPaths.DictionaryDb + "）"
            : "词典数据包：未安装。把 SQLite 词库放到 " + AppPaths.DictionaryDb + " 后重启即可用于单词与短语。";

        var directions = App.Current.Translations.OfflineDirections;
        ModelStatusText.Text = directions.Count == 0
            ? "离线翻译模型：未安装。把模型目录放到 " + App.Current.Translations.OfflineModelRoot + " 后重启生效。"
            : $"离线翻译模型：已安装 {directions.Count} 个方向（{string.Join("、", directions)}）。"
              + Environment.NewLine
              + "目录：" + App.Current.Translations.OfflineModelRoot;
    }

    private async Task RefreshStatsAsync()
    {
        try
        {
            var stats = await App.Current.HistoryStore.GetStatsAsync();
            HistoryStatsText.Text =
                $"当前：最近 {stats.RecentCount} 条 · 收藏 {stats.FavoriteCount} 条（收藏独立计数，上限 50 条）";
        }
        catch (Exception ex)
        {
            AppLog.Warn("history stats failed: " + ex.Message);
        }
    }

    private void UpdateOnlineState()
    {
        var online = OnlineCheck.IsChecked == true && LocalOnlyCheck.IsChecked != true;
        var provider = ProviderCombo.SelectedValue as string ?? "mymemory";
        var libre = string.Equals(provider, "libretranslate", StringComparison.OrdinalIgnoreCase);
        var deepl = string.Equals(provider, "deepl", StringComparison.OrdinalIgnoreCase);

        ProviderCombo.IsEnabled = online;
        EndpointLabel.IsEnabled = online && libre;
        EndpointBox.IsEnabled = online && libre;
        ApiKeyLabel.Text = deepl ? "DeepL API Key（免费版通常以 :fx 结尾）" : "接口密钥（可选）";
        ApiKeyLabel.IsEnabled = online && (libre || deepl);
        ApiKeyBox.IsEnabled = online && (libre || deepl);
        ProviderHintText.Visibility = deepl ? Visibility.Visible : Visibility.Collapsed;
        DeepLTutorialButton.Visibility = deepl ? Visibility.Visible : Visibility.Collapsed;

        EndpointLabel.Opacity = EndpointBox.IsEnabled ? 1.0 : 0.45;
        ApiKeyLabel.Opacity = ApiKeyBox.IsEnabled ? 1.0 : 0.45;
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
        var settings = App.Current.Settings;
        var provider = ProviderCombo.SelectedValue as string ?? "mymemory";
        ApiKeyBox.Text = string.Equals(provider, "deepl", StringComparison.OrdinalIgnoreCase)
            ? settings.DeepLApiKey
            : settings.LibreTranslateApiKey;
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab)
        {
            return;
        }

        e.Handled = true;

        if (HotkeyGesture.IsModifierKey(e.Key) || e.Key == Key.None)
        {
            return;
        }

        HotkeyBox.Text = HotkeyGesture.Format(e.Key, Keyboard.Modifiers);
        ErrorText.Text = string.Empty;
    }

    private void ResetHotkey_Click(object sender, RoutedEventArgs e)
    {
        HotkeyBox.Text = "F2";
        ErrorText.Text = string.Empty;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!HotkeyGesture.TryParse(HotkeyBox.Text, out _, out var hotkeyError))
        {
            ShowError(hotkeyError ?? "快捷键无效");
            return;
        }

        if (!int.TryParse(HistoryLimitBox.Text.Trim(), out var limit) || limit < 10 || limit > 500)
        {
            ShowError("最近记录上限需在 10 到 500 之间");
            return;
        }

        if (!int.TryParse(HistoryDaysBox.Text.Trim(), out var days) || days < 1 || days > 3650)
        {
            ShowError("保留天数需在 1 到 3650 之间");
            return;
        }

        var provider = ProviderCombo.SelectedValue as string ?? "mymemory";
        var endpoint = EndpointBox.Text.Trim();
        var apiKey = ApiKeyBox.Text.Trim();
        var online = OnlineCheck.IsChecked == true && LocalOnlyCheck.IsChecked != true;
        if (string.Equals(provider, "libretranslate", StringComparison.OrdinalIgnoreCase)
            && online
            && endpoint.Length == 0)
        {
            ShowError("使用 LibreTranslate 时必须填写地址");
            return;
        }

        if (string.Equals(provider, "deepl", StringComparison.OrdinalIgnoreCase)
            && online
            && apiKey.Length == 0)
        {
            ShowError("使用 DeepL 时必须填写 API Key（免费版密钥通常以 :fx 结尾）");
            return;
        }

        var updated = App.Current.Settings.Clone();
        updated.Hotkey = HotkeyBox.Text.Trim();
        updated.Theme = ThemeCombo.SelectedIndex == 1 ? "Black" : "White";
        updated.SourceLanguage = (SourceLangCombo.SelectedItem as LanguageInfo)?.Code ?? Languages.Auto;
        updated.TargetLanguage = (TargetLangCombo.SelectedItem as LanguageInfo)?.Code ?? Languages.Chinese.Code;
        updated.OnlineEnabled = OnlineCheck.IsChecked == true;
        updated.OnlineProvider = provider;
        updated.LibreTranslateEndpoint = endpoint;
        if (string.Equals(provider, "deepl", StringComparison.OrdinalIgnoreCase))
        {
            updated.DeepLApiKey = apiKey;
        }
        else if (string.Equals(provider, "libretranslate", StringComparison.OrdinalIgnoreCase))
        {
            updated.LibreTranslateApiKey = apiKey;
        }
        updated.LocalOnly = LocalOnlyCheck.IsChecked == true;
        updated.HistoryEnabled = HistoryCheck.IsChecked == true;
        updated.HistoryLimit = limit;
        updated.HistoryDays = days;
        updated.ThumbnailsEnabled = ThumbnailCheck.IsChecked == true;
        updated.KeepOriginalImage = OriginalCheck.IsChecked == true;
        updated.StartWithWindows = StartupCheck.IsChecked == true;

        App.Current.ApplySettings(updated);
        StartupService.SetEnabled(updated.StartWithWindows);

        if (!App.Current.Hotkey.IsRegistered)
        {
            ShowError("设置已保存，但热键注册失败：" + App.Current.Hotkey.LastError);
            return;
        }

        Hide();
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
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ShowError("无法打开目录：" + ex.Message);
        }
    }

    private void InstallOcrPack_Click(object sender, RoutedEventArgs e)
    {
        // Installing Windows OCR packs through DISM can hang on Windows Update.
        // Open the official language settings page instead and let Windows handle it.
        OpenLanguageSettings_Click(sender, e);
        ShowError("在“语言和区域”里给日语/韩语/俄语安装“光学字符识别”，装完重启框译。");
    }

    private void OpenDeepLTutorial_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "DeepL注册及使用教程.txt");
            if (!File.Exists(path))
            {
                ShowError("未找到 DeepL 教程文件：" + path);
                return;
            }

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
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
            var path = Path.Combine(AppContext.BaseDirectory, "免责声明.txt");
            if (!File.Exists(path))
            {
                ShowError("未找到免责声明文件：" + path);
                return;
            }

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
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
                UseShellExecute = true,
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

    private void Cancel_Click(object sender, RoutedEventArgs e) => Hide();

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_forceClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }
}
