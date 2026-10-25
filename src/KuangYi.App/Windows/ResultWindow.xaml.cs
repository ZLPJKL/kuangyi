using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using KuangYi.App.Interop;
using KuangYi.App.Services;
using KuangYi.Core.Models;
using KuangYi.Core.Services;
using KuangYi.Core.Services.Translation;
using Forms = System.Windows.Forms;

namespace KuangYi.App.Windows;

public partial class ResultWindow : Window
{
    private CancellationTokenSource? _workCts;
    private BitmapSource? _crop;
    private bool _initializing;
    private bool _updating;
    private bool _suppressHistory = true;
    private bool _forceClose;
    private int _translateVersion;
    private string _detectedCode = string.Empty;

    public ResultWindow()
    {
        InitializeComponent();

        _initializing = true;
        SourceLangCombo.ItemsSource = Languages.SourceChoices;
        TargetLangCombo.ItemsSource = Languages.All;

        var configuredSource = App.Current.Settings.SourceLanguage;
        SelectSource(configuredSource);

        var target = string.IsNullOrEmpty(App.Current.Settings.TargetLanguage)
            ? Languages.DefaultTargetForCurrentSystem()
            : App.Current.Settings.TargetLanguage;
        SelectTarget(target);
        _initializing = false;

        Closing += OnClosing;
        Closed += (_, _) => ForceClosed?.Invoke(this, EventArgs.Empty);
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
                SavePlacement();
                Hide();
                App.Current.TrimMemory();
            }
        };
    }

    public event EventHandler? ForceClosed;

    public void SavePlacement()
    {
        if (double.IsNaN(Left) || double.IsNaN(Top) || double.IsInfinity(Left) || double.IsInfinity(Top))
        {
            return;
        }

        var settings = App.Current.Settings;
        settings.ResultWindowLeft = Left;
        settings.ResultWindowTop = Top;
        App.Current.SettingsStore.Save(settings);
    }

    public async Task ShowCaptureAsync(BitmapSource crop, Int32Rect screenRect)
    {
        _crop = crop;
        _suppressHistory = false;
        ResetForCapture();

        if (!IsVisible)
        {
            Show();
        }

        Activate();
        UpdateLayout();
        PositionNear(screenRect);

        var cts = ReplaceWork();
        var version = _translateVersion;
        SetStatus("正在识别…");

        try
        {
            var configured = App.Current.Settings.SourceLanguage;
            var ocr = await App.Current.Ocr.RecognizeAsync(
                crop,
                string.Equals(configured, Languages.Auto, StringComparison.OrdinalIgnoreCase) ? null : configured,
                cts.Token);

            if (version != _translateVersion)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(ocr.Text))
            {
                SetStatus("未识别到文字，请重新框选或扩大区域");
                return;
            }

            SourceBox.Text = ocr.Text;

            var sourceCode = string.Equals(configured, Languages.Auto, StringComparison.OrdinalIgnoreCase)
                ? MapOcrTag(ocr.LanguageTag)
                : configured;

            _detectedCode = sourceCode;
            _updating = true;
            SelectSource(sourceCode);
            _updating = false;

            SetStatus($"识别完成 · {ocr.Lines.Count} 行");
            await TranslateCurrentAsync(cts.Token, version);
        }
        catch (OcrLanguageUnavailableException ex)
        {
            var name = Languages.Resolve(ex.LanguageCode).NameZh;
            SetStatus($"未安装{name}识别语言包：请在设置里点“安装识别语言包”");
            AppLog.Warn("ocr language unavailable: " + ex.LanguageCode);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLog.Error("OCR failed", ex);
            SetStatus("识别失败：" + ex.Message);
        }
    }

    public void ShowHistoryEntry(HistoryEntry entry)
    {
        _suppressHistory = true;
        _crop = null;
        ReplaceWork();

        Topmost = false;
        PinButton.Content = "置顶";
        PinButton.ToolTip = "固定显示在其他窗口上方（默认关闭）";

        _updating = true;
        SourceBox.Text = entry.SourceText;
        OutputBox.Text = entry.TargetText;
        SelectSource(entry.SourceLang);
        SelectTarget(entry.TargetLang);
        _updating = false;

        ProviderText.Text = entry.Provider;
        ProviderBadge.Visibility = Visibility.Visible;
        EnableOnlineButton.Visibility = Visibility.Collapsed;

        SetStatus($"历史记录 · {entry.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}");

        if (!IsVisible)
        {
            Show();
        }

        Activate();
    }

    public void ForceClose()
    {
        _forceClose = true;
        _workCts?.Cancel();
        Close();
    }

    private void ResetForCapture()
    {
        _translateVersion++;
        SourceBox.Text = string.Empty;
        OutputBox.Text = string.Empty;
        ProviderBadge.Visibility = Visibility.Collapsed;
        EnableOnlineButton.Visibility = Visibility.Collapsed;
        SetBusy(false);

        Topmost = false;
        PinButton.Content = "置顶";
        PinButton.ToolTip = "固定显示在其他窗口上方（默认关闭）";

        _updating = true;
        SelectSource(App.Current.Settings.SourceLanguage);
        SelectTarget(string.IsNullOrEmpty(App.Current.Settings.TargetLanguage)
            ? Languages.DefaultTargetForCurrentSystem()
            : App.Current.Settings.TargetLanguage);
        _updating = false;
    }

    private CancellationTokenSource ReplaceWork()
    {
        try
        {
            _workCts?.Cancel();
            _workCts?.Dispose();
        }
        catch
        {
        }

        _workCts = new CancellationTokenSource();
        _translateVersion++;
        return _workCts;
    }

    private async Task TranslateCurrentAsync(CancellationToken ct, int version, bool saveHistory = true)
    {
        var text = SourceBox.Text.Trim();
        if (text.Length == 0)
        {
            SetStatus("没有可翻译的文字");
            return;
        }

        var source = ResolveSource(text);
        var target = SelectedTargetCode();

        SetBusy(true);
        SetStatus("正在翻译…");
        var watch = Stopwatch.StartNew();

        try
        {
            var pipeline = App.Current.Translations.CreatePipeline(App.Current.Settings);
            var outcome = await pipeline.TranslateAsync(new TranslationRequest(text, source, target), ct);

            if (version != _translateVersion)
            {
                return;
            }

            watch.Stop();

            if (outcome.Success && outcome.Result is { } result)
            {
                OutputBox.Text = result.Text;
                ProviderText.Text = $"{result.ProviderName} · {Languages.Resolve(source).NameZh} → {Languages.Resolve(target).NameZh}";
                ProviderBadge.Visibility = Visibility.Visible;
                EnableOnlineButton.Visibility = Visibility.Collapsed;

                SetStatus(string.Equals(source, target, StringComparison.OrdinalIgnoreCase)
                    ? "源语言与目标语言相同"
                    : $"完成 · {watch.ElapsedMilliseconds} ms");

                if (saveHistory && !_suppressHistory)
                {
                    await SaveHistoryAsync(text, result, source, target, watch.ElapsedMilliseconds);
                }
            }
            else
            {
                OutputBox.Text = string.Empty;
                ProviderBadge.Visibility = Visibility.Collapsed;
                SetStatus("翻译失败：" + (outcome.SummaryError ?? "没有可用的翻译源"));

                EnableOnlineButton.Visibility =
                    !App.Current.Settings.LocalOnly && !App.Current.Settings.OnlineEnabled
                        ? Visibility.Visible
                        : Visibility.Collapsed;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLog.Error("translation failed", ex);
            SetStatus("翻译失败：" + ex.Message);
        }
        finally
        {
            if (version == _translateVersion)
            {
                SetBusy(false);
            }
        }
    }

    private async Task SaveHistoryAsync(string sourceText, TranslationResult result, string source, string target, long elapsedMs)
    {
        var settings = App.Current.Settings;
        if (!settings.HistoryEnabled || string.IsNullOrWhiteSpace(result.Text))
        {
            return;
        }

        string? thumbPath = null;
        if (settings.ThumbnailsEnabled && _crop is not null)
        {
            thumbPath = Path.Combine(AppPaths.ThumbnailDir, Guid.NewGuid().ToString("N") + ".png");
            try
            {
                CaptureService.SavePng(_crop, thumbPath, 320);
            }
            catch (Exception ex)
            {
                AppLog.Warn("thumbnail save failed: " + ex.Message);
                thumbPath = null;
            }
        }

        if (settings.KeepOriginalImage && _crop is not null)
        {
            try
            {
                CaptureService.SavePng(_crop, Path.Combine(AppPaths.OriginalDir, Guid.NewGuid().ToString("N") + ".png"));
            }
            catch (Exception ex)
            {
                AppLog.Warn("original image save failed: " + ex.Message);
            }
        }

        var entry = new HistoryEntry
        {
            CreatedAt = DateTimeOffset.UtcNow,
            SourceLang = source,
            TargetLang = target,
            SourceText = sourceText,
            TargetText = result.Text,
            Provider = result.ProviderName,
            ThumbPath = thumbPath,
            ElapsedMs = elapsedMs,
        };

        try
        {
            await App.Current.HistoryStore.AddAsync(
                entry,
                new HistoryOptions(settings.HistoryLimit, settings.HistoryDays, settings.FavoriteLimit));
        }
        catch (Exception ex)
        {
            AppLog.Error("history write failed", ex);
        }
    }

    private string ResolveSource(string text)
    {
        var selected = SourceLangCombo.SelectedItem as LanguageInfo;
        if (selected is null || selected.IsAuto)
        {
            var detected = LanguageDetector.Detect(text);
            if (detected.IsConfident)
            {
                return detected.Language.Code;
            }

            return string.IsNullOrEmpty(_detectedCode) ? Languages.English.Code : _detectedCode;
        }

        return selected.Code;
    }

    private string SelectedTargetCode()
        => (TargetLangCombo.SelectedItem as LanguageInfo)?.Code ?? Languages.Chinese.Code;

    private void SelectSource(string code)
    {
        var item = Languages.SourceChoices.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));
        SourceLangCombo.SelectedItem = item ?? Languages.AutoInfo;
    }

    private void SelectTarget(string code)
    {
        TargetLangCombo.SelectedItem = Languages.Resolve(code);
    }

    private static string MapOcrTag(string tag)
    {
        if (tag.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return Languages.Chinese.Code;
        }

        if (tag.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
        {
            return Languages.Japanese.Code;
        }

        if (tag.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
        {
            return Languages.Korean.Code;
        }

        if (tag.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
        {
            return Languages.Russian.Code;
        }

        return Languages.English.Code;
    }

    private void PositionNear(Int32Rect screenRect)
    {
        try
        {
            if (TryApplySavedPlacement())
            {
                return;
            }

            var handle = new WindowInteropHelper(this).Handle;
            var scale = NativeMethods.GetDpiForWindow(handle) / 96.0;
            if (scale <= 0)
            {
                scale = 1;
            }

            var width = (int)Math.Ceiling(ActualWidth * scale);
            var height = (int)Math.Ceiling(ActualHeight * scale);

            var screen = Forms.Screen.FromRectangle(new System.Drawing.Rectangle(
                screenRect.X,
                screenRect.Y,
                Math.Max(1, screenRect.Width),
                Math.Max(1, screenRect.Height)));

            var area = screen.WorkingArea;
            var gap = 12;
            var x = screenRect.X + screenRect.Width + gap;
            var y = screenRect.Y;

            if (x + width > area.Right)
            {
                x = screenRect.X - width - gap;
            }

            if (x < area.Left + 8)
            {
                x = area.Left + 8;
            }

            if (x + width > area.Right)
            {
                x = Math.Max(area.Left + 8, area.Right - width - 8);
            }

            if (y < area.Top + 8)
            {
                y = area.Top + 8;
            }

            if (y + height > area.Bottom)
            {
                y = Math.Max(area.Top + 8, area.Bottom - height - 8);
            }

            NativeMethods.SetWindowPos(
                handle,
                IntPtr.Zero,
                x,
                y,
                0,
                0,
                NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
        }
        catch (Exception ex)
        {
            AppLog.Warn("positioning failed: " + ex.Message);
        }
    }

    private bool TryApplySavedPlacement()
    {
        var settings = App.Current.Settings;
        if (settings.ResultWindowLeft is not double left || settings.ResultWindowTop is not double top)
        {
            return false;
        }

        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualWidth = SystemParameters.VirtualScreenWidth;
        var virtualHeight = SystemParameters.VirtualScreenHeight;

        if (left < virtualLeft - 40
            || top < virtualTop - 20
            || left > virtualLeft + virtualWidth - 80
            || top > virtualTop + virtualHeight - 60)
        {
            return false;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = left;
        Top = top;
        return true;
    }

    private void SetStatus(string message)
        => StatusText.Text = message;

    private void SetBusy(bool busy)
    {
        TranslateButton.IsEnabled = !busy;
        SourceBox.IsReadOnly = busy;
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
            e.Handled = true;
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

    private async void Translate_Click(object sender, RoutedEventArgs e)
    {
        var cts = ReplaceWork();
        await TranslateCurrentAsync(cts.Token, _translateVersion);
    }

    private async void SourceLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || _updating || SourceLangCombo.SelectedItem is not LanguageInfo selected)
        {
            return;
        }

        if (_crop is not null && !_suppressHistory)
        {
            var cts = ReplaceWork();
            await ReprocessAsync(cts.Token, _translateVersion, selected.IsAuto ? null : selected.Code);
            return;
        }

        if (string.IsNullOrWhiteSpace(SourceBox.Text))
        {
            return;
        }

        var translateCts = ReplaceWork();
        await TranslateCurrentAsync(translateCts.Token, _translateVersion);
    }

    private async Task ReprocessAsync(CancellationToken ct, int version, string? preferredLanguage)
    {
        if (_crop is null)
        {
            return;
        }

        SetStatus("正在重新识别…");

        try
        {
            var ocr = await App.Current.Ocr.RecognizeAsync(_crop, preferredLanguage, ct);
            if (version != _translateVersion)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(ocr.Text))
            {
                SetStatus("未识别到文字，请重新框选或扩大区域");
                return;
            }

            SourceBox.Text = ocr.Text;

            var sourceCode = preferredLanguage ?? MapOcrTag(ocr.LanguageTag);
            _detectedCode = sourceCode;
            _updating = true;
            SelectSource(sourceCode);
            _updating = false;

            SetStatus($"识别完成 · {ocr.Lines.Count} 行");
            await TranslateCurrentAsync(ct, version);
        }
        catch (OcrLanguageUnavailableException ex)
        {
            var name = Languages.Resolve(ex.LanguageCode).NameZh;
            SetStatus($"未安装{name}识别语言包：请在设置里安装后重启框译");
            AppLog.Warn("ocr language unavailable: " + ex.LanguageCode);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLog.Error("OCR reprocess failed", ex);
            SetStatus("重新识别失败：" + ex.Message);
        }
    }

    private async void TargetLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || _updating || TargetLangCombo.SelectedItem is not LanguageInfo language)
        {
            return;
        }

        var settings = App.Current.Settings;
        if (!string.Equals(settings.TargetLanguage, language.Code, StringComparison.OrdinalIgnoreCase))
        {
            settings.TargetLanguage = language.Code;
            App.Current.SettingsStore.Save(settings);
        }

        if (string.IsNullOrWhiteSpace(SourceBox.Text))
        {
            return;
        }

        var cts = ReplaceWork();
        await TranslateCurrentAsync(cts.Token, _translateVersion);
    }

    private void CopyTranslation_Click(object sender, RoutedEventArgs e) => Copy(OutputBox.Text, "译文");

    private void CopySource_Click(object sender, RoutedEventArgs e) => Copy(SourceBox.Text, "原文");

    private void Copy(string text, string label)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("没有可复制的内容");
            return;
        }

        try
        {
            Clipboard.SetText(text);
            SetStatus($"已复制{label}");
        }
        catch (Exception)
        {
            SetStatus("复制失败，剪贴板被其他程序占用");
        }
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        PinButton.Content = Topmost ? "取消置顶" : "置顶";
        PinButton.ToolTip = Topmost
            ? "已固定在最上层，点击取消"
            : "固定显示在其他窗口上方（默认关闭）";
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => App.Current.ShowSettings();

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        SavePlacement();
        Hide();
        App.Current.TrimMemory();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        SavePlacement();
        Hide();
        App.Current.TrimMemory();
    }

    private async void EnableOnline_Click(object sender, RoutedEventArgs e)
    {
        var settings = App.Current.Settings;
        settings.OnlineEnabled = true;
        settings.LocalOnly = false;
        App.Current.ApplySettings(settings);

        var cts = ReplaceWork();
        await TranslateCurrentAsync(cts.Token, _translateVersion);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        SavePlacement();

        if (_forceClose)
        {
            return;
        }

        e.Cancel = true;
        _workCts?.Cancel();
        Hide();
    }
}
