using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KuangYi.App.Services;
using KuangYi.App.Windows;
using KuangYi.Core.Models;
using KuangYi.Core.Services;
using Forms = System.Windows.Forms;

namespace KuangYi.App;

public partial class App : Application
{
    private Mutex? _instanceMutex;
    private EventWaitHandle? _activateEvent;
    private bool _capturing;
    private bool _paused;
    private bool _exiting;

    public static new App Current => (App)Application.Current;

    public SettingsService SettingsStore { get; private set; } = null!;

    public AppSettings Settings { get; private set; } = new();

    public HistoryService HistoryStore { get; private set; } = null!;

    public TranslationService Translations { get; private set; } = null!;

    public HotkeyService Hotkey { get; private set; } = null!;

    public TrayService Tray { get; private set; } = null!;

    public OcrService Ocr { get; } = new();

    public CaptureService Capture { get; } = new();

    public ResultWindow? ResultWindowInstance { get; private set; }

    public HistoryWindow? HistoryWindowInstance { get; private set; }

    public SettingsWindow? SettingsWindowInstance { get; private set; }

    public MainWindow? MainWindowInstance { get; private set; }

    public async Task CaptureFromUiAsync() => await StartCaptureAsync();

    public void ExitApplication() => ExitApp();

    public void TrimMemory()
    {
        try
        {
            Translations?.TrimMemory();
        }
        catch
        {
        }
    }

    public void ShowMainWindow()
    {
        if (MainWindowInstance is null)
        {
            MainWindowInstance = new MainWindow();
            MainWindowInstance.Closed += (_, _) => MainWindowInstance = null;
        }

        MainWindowInstance.Show();
        MainWindowInstance.Activate();
    }

    public void ToggleMainWindow()
    {
        if (MainWindowInstance is { IsVisible: true })
        {
            MainWindowInstance.Hide();
            TrimMemory();
            return;
        }

        ShowMainWindow();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        if (!AcquireSingleInstance())
        {
            MessageBox.Show(
                "框译已经在运行。\n\n"
                + "如果任务栏托盘里没有看到图标，请点击任务栏右下角的 ^ 展开隐藏图标，"
                + "把“框译”拖到外面固定。\n\n"
                + "程序窗口出现后，按 F2 即可框选翻译。",
                "框译",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        AppPaths.EnsureCreated();

        SettingsStore = new SettingsService(AppPaths.SettingsFile);
        Settings = SettingsStore.Load();
        if (string.IsNullOrEmpty(Settings.TargetLanguage))
        {
            Settings.TargetLanguage = Languages.DefaultTargetForCurrentSystem();
        }

        HistoryStore = new HistoryService(AppPaths.HistoryDb, AppPaths.ThumbnailDir);
        Translations = new TranslationService(AppPaths.DictionaryDb);
        AppLog.Info("offline models: " + (Translations.OfflineAvailable
            ? string.Join(",", Translations.OfflineDirections)
            : "none"));

        ThemeService.Apply(Settings.Theme);

        if (!Settings.DisclaimerAccepted)
        {
            try
            {
                if (new DisclaimerWindow().ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("disclaimer dialog failed", ex);
                MessageBox.Show(
                    "无法显示免责声明，程序将退出。请检查 程序\\免责声明.txt 是否存在。",
                    "框译",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown();
                return;
            }

            Settings.DisclaimerAccepted = true;
            SettingsStore.Save(Settings);
            AppLog.Info("disclaimer accepted");
        }

        Tray = new TrayService();
        Tray.CaptureRequested += (_, _) => _ = StartCaptureAsync();
        Tray.ShowRequested += (_, _) => ShowMainWindow();
        Tray.ResultRequested += (_, _) => ShowLastResult();
        Tray.ToggleRequested += (_, _) => ToggleMainWindow();
        Tray.HistoryRequested += (_, _) => ShowHistory();
        Tray.SettingsRequested += (_, _) => ShowSettings();
        Tray.PauseToggled += (_, paused) => SetPaused(paused);
        Tray.ExitRequested += (_, _) => ExitApp();

        Hotkey = new HotkeyService();
        Hotkey.Pressed += (_, _) => Dispatcher.BeginInvoke(new Action(() => _ = StartCaptureAsync()));

        var registered = Hotkey.Register(Settings.Hotkey);
        Tray.SetHotkey(Settings.Hotkey, registered);
        if (!registered)
        {
            Tray.ShowBalloon(
                "框译",
                $"{Settings.Hotkey} 注册失败：{Hotkey.LastError}。请在设置中改用组合键。",
                Forms.ToolTipIcon.Warning);

            ShowStartupNotice(
                "快捷键不可用",
                $"全局快捷键 {Settings.Hotkey} 注册失败：{Hotkey.LastError}。\n\n"
                + "通常是有其他程序占用了该按键。请右键托盘中的“框译”图标 → 设置，"
                + "改用 Ctrl+Alt+F2 等组合键。");
        }

        var trayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        trayTimer.Tick += (_, _) =>
        {
            trayTimer.Stop();
            Tray.PromoteInNotificationArea();
        };
        trayTimer.Start();

        AppLog.Info($"startup complete: hotkey={Settings.Hotkey} registered={registered} root={AppPaths.Root}");

        if (!Settings.FirstRunCompleted)
        {
            Settings.FirstRunCompleted = true;
            SettingsStore.Save(Settings);
        }

        ShowMainWindow();
    }

    public void ShowResultWindow()
    {
        if (ResultWindowInstance is null)
        {
            ResultWindowInstance = new ResultWindow();
            ResultWindowInstance.ForceClosed += (_, _) => ResultWindowInstance = null;
        }

        if (!ResultWindowInstance.IsVisible)
        {
            ResultWindowInstance.Show();
        }

        ResultWindowInstance.Activate();
    }

    public void ShowLastResult()
    {
        if (ResultWindowInstance is null)
        {
            ShowMainWindow();
            return;
        }

        if (!ResultWindowInstance.IsVisible)
        {
            ResultWindowInstance.Show();
        }

        ResultWindowInstance.Activate();
    }

    public void ShowSettings()
    {
        if (SettingsWindowInstance is null)
        {
            SettingsWindowInstance = new SettingsWindow();
            SettingsWindowInstance.ForceClosed += (_, _) => SettingsWindowInstance = null;
        }

        if (!SettingsWindowInstance.IsVisible)
        {
            SettingsWindowInstance.Show();
        }

        SettingsWindowInstance.Activate();
    }

    public void ShowHistory()
    {
        if (HistoryWindowInstance is null)
        {
            HistoryWindowInstance = new HistoryWindow();
            HistoryWindowInstance.ForceClosed += (_, _) => HistoryWindowInstance = null;
        }

        HistoryWindowInstance.Reload();
        if (!HistoryWindowInstance.IsVisible)
        {
            HistoryWindowInstance.Show();
        }

        HistoryWindowInstance.Activate();
    }

    public void ApplySettings(AppSettings updated)
    {
        updated = SettingsService.Normalize(updated);
        Settings = updated;
        SettingsStore.Save(Settings);
        ThemeService.Apply(Settings.Theme);

        var registered = Hotkey.Register(Settings.Hotkey);
        Tray.SetHotkey(Settings.Hotkey, registered);
        if (!registered)
        {
            Tray.ShowBalloon(
                "框译",
                $"{Settings.Hotkey} 注册失败：{Hotkey.LastError}",
                Forms.ToolTipIcon.Warning);
        }
    }

    private async Task StartCaptureAsync()
    {
        if (_capturing || _exiting)
        {
            return;
        }

        _capturing = true;
        var wasVisible = ResultWindowInstance?.IsVisible == true;
        var restoreMain = MainWindowInstance is { IsVisible: true };

        try
        {
            if (restoreMain)
            {
                MainWindowInstance!.Hide();
            }

            if (wasVisible)
            {
                ResultWindowInstance!.Hide();
                await Task.Delay(140);
            }
            else if (restoreMain)
            {
                await Task.Delay(80);
            }

            var frame = Capture.CaptureVirtualScreen();
            var overlay = new CaptureOverlayWindow(frame);
            var selection = await overlay.PickAsync();

            if (selection is null)
            {
                if (wasVisible)
                {
                    ResultWindowInstance?.Show();
                }

                return;
            }

            var crop = new CroppedBitmap(frame.Image, selection.Value);
            crop.Freeze();

            var screenRect = new Int32Rect(
                frame.X + selection.Value.X,
                frame.Y + selection.Value.Y,
                selection.Value.Width,
                selection.Value.Height);

            ShowResultWindow();
            await ResultWindowInstance!.ShowCaptureAsync(crop, screenRect);
        }
        catch (Exception ex)
        {
            AppLog.Error("capture pipeline failed", ex);
            Tray.ShowBalloon("框译", "框选翻译失败：" + ex.Message, Forms.ToolTipIcon.Error);
        }
        finally
        {
            _capturing = false;

            if (restoreMain && MainWindowInstance is not null)
            {
                MainWindowInstance.Show();
            }
        }
    }

    private void SetPaused(bool paused)
    {
        _paused = paused;
        if (paused)
        {
            Hotkey.Unregister();
            Tray.ShowBalloon("框译", "全局热键已暂停，可从托盘菜单恢复。");
        }
        else
        {
            var registered = Hotkey.Register(Settings.Hotkey);
            if (!registered)
            {
                Tray.ShowBalloon("框译", $"{Settings.Hotkey} 注册失败：{Hotkey.LastError}", Forms.ToolTipIcon.Warning);
                _paused = false;
            }
        }

        Tray.SetPaused(paused);
    }

    private void ShowFirstRun()
    {
        Settings.FirstRunCompleted = true;
        SettingsStore.Save(Settings);

        ShowStartupNotice(
            "框译已启动",
            $"按 {Settings.Hotkey} 框选屏幕区域，松开鼠标开始识别。\n\n"
            + "程序驻留系统托盘；如果任务栏右下角没有图标，请点击 ^ 展开隐藏图标。\n\n"
            + "离线模型已内置，断网可用；在线翻译默认关闭。");
    }

    public void ShowStartupNotice(string title, string body)
    {
        try
        {
            var window = new StartupNoticeWindow(title, body);
            window.Show();
        }
        catch (Exception ex)
        {
            AppLog.Warn("startup notice failed: " + ex.Message);
        }
    }

    private void ExitApp()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;

        try
        {
            Hotkey?.Dispose();
        }
        catch
        {
        }

        try
        {
            Tray?.Dispose();
        }
        catch
        {
        }

        MainWindowInstance?.SavePlacement();
        ResultWindowInstance?.SavePlacement();

        ResultWindowInstance?.ForceClose();
        SettingsWindowInstance?.ForceClose();
        HistoryWindowInstance?.ForceClose();

        try
        {
            _activateEvent?.Dispose();
        }
        catch
        {
        }

        try
        {
            _instanceMutex?.ReleaseMutex();
            _instanceMutex?.Dispose();
        }
        catch
        {
        }

        Shutdown();
    }

    private bool AcquireSingleInstance()
    {
        _instanceMutex = new Mutex(true, @"Local\KuangYi.SingleInstance.Mutex", out var created);

        if (!created)
        {
            try
            {
                using var existing = EventWaitHandle.OpenExisting(@"Local\KuangYi.SingleInstance.Activate");
                existing.Set();
            }
            catch
            {
                // the first instance may still be starting up
            }

            return false;
        }

        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\KuangYi.SingleInstance.Activate");
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    _activateEvent.WaitOne();
                    Dispatcher.BeginInvoke(new Action(() =>
                        Tray?.ShowBalloon("框译", $"程序已在运行。按 {Settings.Hotkey} 开始框选。")));
                }
                catch
                {
                    return;
                }
            }
        })
        {
            IsBackground = true,
            Name = "KuangYi.ActivationListener",
        };

        thread.Start();
        return true;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("dispatcher unhandled exception", e.Exception);
        e.Handled = true;

        try
        {
            Tray?.ShowBalloon("框译", "发生了一个错误，已记录到日志：" + e.Exception.Message, Forms.ToolTipIcon.Error);
        }
        catch
        {
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _activateEvent?.Dispose();
        }
        catch
        {
        }

        try
        {
            _instanceMutex?.Dispose();
        }
        catch
        {
        }

        base.OnExit(e);
    }
}
