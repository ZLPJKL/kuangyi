using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KuangYi.App.Interop;
using KuangYi.App.Services;
using KuangYi.Core.Models;
using KuangYi.Core.Services;
using KuangYi.Core.Services.Translation;

namespace KuangYi.App.Windows;

public partial class LiveTranslationWindow : Window
{
    private readonly Int32Rect _region;
    private readonly DispatcherTimer _timer;
    private LiveRegionBorderWindow? _border;
    private CancellationTokenSource? _cts;
    private string _sourceMode = "auto";
    private string _targetCode = "zh";
    private string _lastOcrText = string.Empty;
    private bool _initializing;
    private bool _busy;
    private bool _paused;

    public LiveTranslationWindow(Int32Rect region)
    {
        InitializeComponent();
        _region = region;
        _initializing = true;
        SourceLangCombo.ItemsSource = Languages.SourceChoices;
        TargetLangCombo.ItemsSource = Languages.All;
        _sourceMode = App.Current.Settings.SourceLanguage;
        _targetCode = string.IsNullOrEmpty(App.Current.Settings.TargetLanguage)
            ? Languages.DefaultTargetForCurrentSystem()
            : App.Current.Settings.TargetLanguage;
        SourceLangCombo.SelectedItem = Languages.SourceChoices.FirstOrDefault(
            language => string.Equals(language.Code, _sourceMode, StringComparison.OrdinalIgnoreCase)) ?? Languages.AutoInfo;
        TargetLangCombo.SelectedItem = Languages.Resolve(_targetCode);
        _initializing = false;
        RegionText.Text = $"{region.Width} × {region.Height} 像素 · 可拖动结果窗";
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        Loaded += OnLoaded;
        SourceInitialized += (_, _) => ApplyCaptureExclusion();
        Closed += OnClosed;
    }

    private void ApplyCaptureExclusion()    {
        try
        {
            nint handle = new WindowInteropHelper(this).Handle;
            NativeMethods.SetWindowDisplayAffinity(handle, NativeMethods.WdaExcludeFromCapture);
        }
        catch (Exception ex)
        {
            AppLog.Warn("live result capture exclusion failed: " + ex.Message);
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionNearRegion();
        ShowBorder();
        _timer.Start();
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_busy || _paused)
        {
            return;
        }

        _busy = true;
        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            BitmapSource image = App.Current.Capture.CaptureRegion(_region);
            var settings = App.Current.Settings;
            string sourceMode = _sourceMode;
            var ocr = await App.Current.Ocr.RecognizeAsync(
                image,
                string.Equals(sourceMode, "auto", StringComparison.OrdinalIgnoreCase) ? null : sourceMode,
                settings.OcrQuality,
                ct);
            if (string.IsNullOrWhiteSpace(ocr.Text))
            {
                StatusText.Text = $"实时 · {DateTime.Now:HH:mm:ss} · 未识别到文字";
                return;
            }

            string normalized = ocr.Text.Trim();
            if (!string.IsNullOrEmpty(_lastOcrText)
                && string.Equals(normalized, _lastOcrText, StringComparison.Ordinal))
            {
                StatusText.Text = $"实时 · {DateTime.Now:HH:mm:ss} · 内容未变化";
                return;
            }

            _lastOcrText = normalized;
            string source = string.Equals(sourceMode, "auto", StringComparison.OrdinalIgnoreCase)
                ? LanguageDetector.Detect(normalized).Language.Code
                : sourceMode;
            string target = string.IsNullOrEmpty(_targetCode)
                ? Languages.DefaultTargetForCurrentSystem()
                : _targetCode;
            SourceBox.Text = TextFormatter.Reflow(normalized, source);
            if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            {
                OutputBox.Text = "源语言与目标语言相同";
                StatusText.Text = $"实时 · {DateTime.Now:HH:mm:ss}";
                return;
            }

            var glossary = App.Current.Translations.Glossary;
            string mode = settings.LocalOnly ? "offline" : (settings.OnlineEnabled ? settings.OnlineProvider : "offline");
            string cacheKey = TranslationCache.CreateKey(
                normalized,
                source,
                target,
                mode,
                glossary.Version,
                App.Current.Translations.CacheFingerprint);
            if (settings.CacheEnabled)
            {
                var cached = await App.Current.Translations.Cache.TryGetAsync(cacheKey, ct);
                if (cached != null)
                {
                    OutputBox.Text = TextFormatter.Reflow(cached.Text, target);
                    StatusText.Text = $"实时 · {DateTime.Now:HH:mm:ss} · 缓存命中";
                    return;
                }
            }

            var prepared = glossary.Prepare(normalized, source, target);
            var outcome = await App.Current.Translations.CreatePipeline(settings)
                .TranslateAsync(new TranslationRequest(prepared.Text, source, target), ct);
            if (outcome.Success && outcome.Result != null)
            {
                string translated = TextFormatter.Reflow(
                    glossary.Restore(outcome.Result.Text, prepared.Replacements),
                    target);
                OutputBox.Text = translated;
                StatusText.Text = $"实时 · {DateTime.Now:HH:mm:ss} · {outcome.Result.ProviderName}";
                if (settings.CacheEnabled
                    && (string.Equals(mode, "offline", StringComparison.OrdinalIgnoreCase) || !outcome.Result.IsOffline))
                {
                    await App.Current.Translations.Cache.StoreAsync(
                        cacheKey,
                        normalized,
                        new TranslationResult(translated, outcome.Result.ProviderId, outcome.Result.ProviderName, outcome.Result.IsOffline),
                        ct);
                }
            }
            else
            {
                OutputBox.Text = "翻译失败：" + (outcome.SummaryError ?? "没有可用翻译源");
                StatusText.Text = $"实时 · {DateTime.Now:HH:mm:ss}";
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLog.Warn("live translation failed: " + ex.Message);
            StatusText.Text = "实时翻译失败：" + ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private void PositionNearRegion()
    {
        try
        {
            UpdateLayout();
            nint handle = new WindowInteropHelper(this).Handle;
            double scale = NativeMethods.GetDpiForWindow(handle) / 96.0;
            if (scale <= 0)
            {
                scale = 1.0;
            }

            int width = (int)Math.Ceiling(ActualWidth * scale);
            int height = (int)Math.Ceiling(ActualHeight * scale);
            var screen = System.Windows.Forms.Screen.FromRectangle(
                new System.Drawing.Rectangle(_region.X, _region.Y, Math.Max(1, _region.Width), Math.Max(1, _region.Height)));
            int x = _region.X + _region.Width + 16;
            int y = _region.Y;
            if (x + width > screen.WorkingArea.Right)
            {
                x = _region.X - width - 16;
            }
            x = Math.Max(screen.WorkingArea.Left + 8, Math.Min(x, screen.WorkingArea.Right - width - 8));
            y = Math.Max(screen.WorkingArea.Top + 8, Math.Min(y, screen.WorkingArea.Bottom - height - 8));
            NativeMethods.SetWindowPos(handle, (nint)(-1), x, y, 0, 0, 0x0011);
        }
        catch (Exception ex)
        {
            AppLog.Warn("live window position failed: " + ex.Message);
        }
    }

    private void ShowBorder()
    {
        try
        {
            _border = new LiveRegionBorderWindow(_region);
            _border.Show();
        }
        catch (Exception ex)
        {
            AppLog.Warn("live border failed: " + ex.Message);
        }
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        _paused = !_paused;
        PauseButton.Content = _paused ? "继续" : "暂停";
        StatusText.Text = _paused ? "已暂停" : "实时 · 继续监听";
    }

    private async void Language_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_initializing)
        {
            return;
        }

        _sourceMode = (SourceLangCombo.SelectedItem as LanguageInfo)?.Code ?? Languages.Auto;
        _targetCode = (TargetLangCombo.SelectedItem as LanguageInfo)?.Code
                      ?? Languages.DefaultTargetForCurrentSystem();
        _lastOcrText = string.Empty;
        await RefreshAsync();
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        PinButton.Content = Topmost ? "取消置顶" : "置顶";
    }

    private async void Reselect_Click(object sender, RoutedEventArgs e)
    {
        Close();
        await App.Current.StartLiveTranslationAsync();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(OutputBox.Text);
            StatusText.Text = "译文已复制";
        }
        catch (Exception ex)
        {
            StatusText.Text = "复制失败：" + ex.Message;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop();
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        try
        {
            _border?.Close();
        }
        catch
        {
        }
        _border = null;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}
