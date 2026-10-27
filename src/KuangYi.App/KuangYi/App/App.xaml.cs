using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KuangYi.App.Services;
using KuangYi.App.Windows;
using KuangYi.Core.Models;
using KuangYi.Core.Services;
using Microsoft.Win32;

namespace KuangYi.App;

public partial class App : System.Windows.Application
{
	private Mutex? _instanceMutex;

	private EventWaitHandle? _activateEvent;

	private bool _capturing;

	private bool _paused;

	private bool _exiting;

	private DispatcherTimer? _themeTimer;

	public new static App Current => (App)System.Windows.Application.Current;

	public SettingsService SettingsStore { get; private set; }

	public AppSettings Settings { get; private set; } = new AppSettings();

	public HistoryService HistoryStore { get; private set; }

	public TranslationService Translations { get; private set; }

	public HotkeyService Hotkey { get; private set; }

	public TrayService Tray { get; private set; }

	public OcrService Ocr { get; } = new OcrService();

	public CaptureService Capture { get; } = new CaptureService();

	public ResultWindow? ResultWindowInstance { get; private set; }

	public HistoryWindow? HistoryWindowInstance { get; private set; }

	public SettingsWindow? SettingsWindowInstance { get; private set; }

	public MainWindow? MainWindowInstance { get; private set; }

	public async Task CaptureFromUiAsync()
	{
		await StartCaptureAsync();
	}

	public async Task CaptureFromClipboardAsync()
	{
		BitmapSource bitmapSource = null;
		try
		{
			if (System.Windows.Clipboard.ContainsImage())
			{
				bitmapSource = System.Windows.Clipboard.GetImage();
			}
		}
		catch (Exception ex)
		{
			AppLog.Warn("clipboard image read failed: " + ex.Message);
		}
		if (bitmapSource == null)
		{
			Tray.ShowBalloon("框译", "剪贴板里没有图片。先复制一张截图再试。");
			return;
		}
		if (bitmapSource.CanFreeze)
		{
			bitmapSource.Freeze();
		}
		await ShowImageAsync(bitmapSource);
	}

	public async Task CaptureFromFileAsync()
	{
		Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
		{
			Title = "选择要识别的图片",
			Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|所有文件|*.*",
			CheckFileExists = true,
			Multiselect = false
		};
		if (openFileDialog.ShowDialog() != true)
		{
			return;
		}
		try
		{
			BitmapImage bitmapImage = new BitmapImage();
			bitmapImage.BeginInit();
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			bitmapImage.UriSource = new Uri(openFileDialog.FileName, UriKind.Absolute);
			bitmapImage.EndInit();
			bitmapImage.Freeze();
			await ShowImageAsync(bitmapImage);
		}
		catch (Exception ex)
		{
			AppLog.Error("image file ocr failed", ex);
			Tray.ShowBalloon("框译", "读取图片失败：" + ex.Message, ToolTipIcon.Error);
		}
	}

	private async Task ShowImageAsync(BitmapSource image)
	{
		try
		{
			System.Drawing.Point position = Cursor.Position;
			Int32Rect screenRect = new Int32Rect(position.X, position.Y, Math.Max(1, image.PixelWidth), Math.Max(1, image.PixelHeight));
			ShowResultWindow();
			await ResultWindowInstance.ShowCaptureAsync(image, screenRect);
		}
		catch (Exception ex)
		{
			AppLog.Error("image ocr pipeline failed", ex);
			Tray.ShowBalloon("框译", "图片识别失败：" + ex.Message, ToolTipIcon.Error);
		}
	}

	public void ExitApplication()
	{
		ExitApp();
	}

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
		if (MainWindowInstance == null)
		{
			MainWindowInstance = new MainWindow();
			MainWindowInstance.Closed += (object? _, EventArgs _) =>
			{
				MainWindowInstance = null;
			};
		}
		MainWindowInstance.Show();
		MainWindowInstance.Activate();
	}

	public void ToggleMainWindow()
	{
		MainWindow mainWindowInstance = MainWindowInstance;
		if (mainWindowInstance != null && mainWindowInstance.IsVisible)
		{
			MainWindowInstance.Hide();
			TrimMemory();
		}
		else
		{
			ShowMainWindow();
		}
	}

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
		DispatcherUnhandledException += OnDispatcherUnhandledException;
		if (!AcquireSingleInstance())
		{
			System.Windows.MessageBox.Show("框译已经在运行。\n\n如果任务栏托盘里没有看到图标，请点击任务栏右下角的 ^ 展开隐藏图标，把“框译”拖到外面固定。\n\n程序窗口出现后，按 F2 即可框选翻译。", "框译", MessageBoxButton.OK, MessageBoxImage.Asterisk);
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
		AppLog.Info("offline models: " + (Translations.OfflineAvailable ? string.Join(",", Translations.OfflineDirections) : "none"));
		ThemeService.Apply(Settings.Theme);
		_themeTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(5L)
		};
		_themeTimer.Tick += (object? _, EventArgs _) =>
		{
			ThemeService.Apply(Settings.Theme);
		};
		_themeTimer.Start();
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
				System.Windows.MessageBox.Show("无法显示免责声明，程序将退出。请检查 程序\\免责声明.txt 是否存在。", "框译", MessageBoxButton.OK, MessageBoxImage.Hand);
				Shutdown();
				return;
			}
			Settings.DisclaimerAccepted = true;
			SettingsStore.Save(Settings);
			AppLog.Info("disclaimer accepted");
		}
		Tray = new TrayService();
		Tray.CaptureRequested += (object? _, EventArgs _) =>
		{
			StartCaptureAsync();
		};
		Tray.ShowRequested += (object? _, EventArgs _) =>
		{
			ShowMainWindow();
		};
		Tray.ResultRequested += (object? _, EventArgs _) =>
		{
			ShowLastResult();
		};
		Tray.ToggleRequested += (object? _, EventArgs _) =>
		{
			ToggleMainWindow();
		};
		Tray.HistoryRequested += (object? _, EventArgs _) =>
		{
			ShowHistory();
		};
		Tray.SettingsRequested += (object? _, EventArgs _) =>
		{
			ShowSettings();
		};
		Tray.PauseToggled += (object? _, bool paused) =>
		{
			SetPaused(paused);
		};
		Tray.ExitRequested += (object? _, EventArgs _) =>
		{
			ExitApp();
		};
		Hotkey = new HotkeyService();
		Hotkey.Pressed += (object? _, EventArgs _) =>
		{
			Dispatcher.BeginInvoke((Action)(() =>
			{
				StartCaptureAsync();
			}));
		};
		bool flag = Hotkey.Register(Settings.Hotkey);
		Tray.SetHotkey(Settings.Hotkey, flag);
		if (!flag)
		{
			Tray.ShowBalloon("框译", Settings.Hotkey + " 注册失败：" + Hotkey.LastError + "。请在设置中改用组合键。", ToolTipIcon.Warning);
			ShowStartupNotice("快捷键不可用", $"全局快捷键 {Settings.Hotkey} 注册失败：{Hotkey.LastError}。\n\n" + "通常是有其他程序占用了该按键。请右键托盘中的“框译”图标 → 设置，改用 Ctrl+Alt+F2 等组合键。");
		}
		DispatcherTimer trayTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(1.5)
		};
		trayTimer.Tick += (object? _, EventArgs _) =>
		{
			trayTimer.Stop();
			Tray.PromoteInNotificationArea();
		};
		trayTimer.Start();
		AppLog.Info($"startup complete: hotkey={Settings.Hotkey} registered={flag} root={AppPaths.Root}");
		if (!Settings.FirstRunCompleted)
		{
			Settings.FirstRunCompleted = true;
			SettingsStore.Save(Settings);
		}
		ShowMainWindow();
	}

	public void ShowResultWindow()
	{
		if (ResultWindowInstance == null)
		{
			ResultWindowInstance = new ResultWindow();
			ResultWindowInstance.ForceClosed += (object? _, EventArgs _) =>
			{
				ResultWindowInstance = null;
			};
		}
		if (!ResultWindowInstance.IsVisible)
		{
			ResultWindowInstance.Show();
		}
		ResultWindowInstance.Activate();
	}

	public void ShowLastResult()
	{
		if (ResultWindowInstance == null)
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
		if (SettingsWindowInstance == null)
		{
			SettingsWindowInstance = new SettingsWindow();
			SettingsWindowInstance.ForceClosed += (object? _, EventArgs _) =>
			{
				SettingsWindowInstance = null;
			};
		}
		if (!SettingsWindowInstance.IsVisible)
		{
			SettingsWindowInstance.Show();
		}
		SettingsWindowInstance.Activate();
	}

	public void ShowHistory()
	{
		if (HistoryWindowInstance == null)
		{
			HistoryWindowInstance = new HistoryWindow();
			HistoryWindowInstance.ForceClosed += (object? _, EventArgs _) =>
			{
				HistoryWindowInstance = null;
			};
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
		bool flag = Hotkey.Register(Settings.Hotkey);
		Tray.SetHotkey(Settings.Hotkey, flag);
		if (!flag)
		{
			Tray.ShowBalloon("框译", Settings.Hotkey + " 注册失败：" + Hotkey.LastError, ToolTipIcon.Warning);
		}
	}

	private async Task StartCaptureAsync()
	{
		if (_capturing || _exiting)
		{
			return;
		}
		_capturing = true;
		bool wasVisible = ResultWindowInstance?.IsVisible ?? false;
		bool restoreMain = MainWindowInstance?.IsVisible ?? false;
		try
		{
			if (restoreMain)
			{
				MainWindowInstance.Hide();
			}
			if (wasVisible)
			{
				ResultWindowInstance.Hide();
				await Task.Delay(140);
			}
			else if (restoreMain)
			{
				await Task.Delay(80);
			}
			CapturedFrame frame = Capture.CaptureVirtualScreen();
			Int32Rect? int32Rect = await new CaptureOverlayWindow(frame).PickAsync();
			if (!int32Rect.HasValue)
			{
				if (wasVisible)
				{
					ResultWindowInstance?.Show();
				}
			}
			else
			{
				CroppedBitmap croppedBitmap = new CroppedBitmap(frame.Image, int32Rect.Value);
				croppedBitmap.Freeze();
				Int32Rect screenRect = new Int32Rect(frame.X + int32Rect.Value.X, frame.Y + int32Rect.Value.Y, int32Rect.Value.Width, int32Rect.Value.Height);
				ShowResultWindow();
				await ResultWindowInstance.ShowCaptureAsync(croppedBitmap, screenRect);
			}
		}
		catch (Exception ex)
		{
			AppLog.Error("capture pipeline failed", ex);
			Tray.ShowBalloon("框译", "框选翻译失败：" + ex.Message, ToolTipIcon.Error);
		}
		finally
		{
			_capturing = false;
			if (restoreMain && MainWindowInstance != null)
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
		else if (!Hotkey.Register(Settings.Hotkey))
		{
			Tray.ShowBalloon("框译", Settings.Hotkey + " 注册失败：" + Hotkey.LastError, ToolTipIcon.Warning);
			_paused = false;
		}
		Tray.SetPaused(paused);
	}

	private void ShowFirstRun()
	{
		Settings.FirstRunCompleted = true;
		SettingsStore.Save(Settings);
		ShowStartupNotice("框译已启动", "按 " + Settings.Hotkey + " 框选屏幕区域，松开鼠标开始识别。\n\n程序驻留系统托盘；如果任务栏右下角没有图标，请点击 ^ 展开隐藏图标。\n\n离线模型已内置，断网可用；在线翻译默认关闭。");
	}

	public void ShowStartupNotice(string title, string body)
	{
		try
		{
			new StartupNoticeWindow(title, body).Show();
		}
		catch (Exception ex)
		{
			AppLog.Warn("startup notice failed: " + ex.Message);
		}
	}

	private void ExitApp()
	{
		if (!_exiting)
		{
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
	}

	private bool AcquireSingleInstance()
	{
		_instanceMutex = new Mutex(initiallyOwned: true, "Local\\KuangYi.SingleInstance.Mutex", out var createdNew);
		if (!createdNew)
		{
			try
			{
				using EventWaitHandle eventWaitHandle = EventWaitHandle.OpenExisting("Local\\KuangYi.SingleInstance.Activate");
				eventWaitHandle.Set();
			}
			catch
			{
			}
			return false;
		}
		_activateEvent = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, "Local\\KuangYi.SingleInstance.Activate");
		Thread thread = new Thread(() =>
		{
			while (true)
			{
				try
				{
					_activateEvent.WaitOne();
					Dispatcher.BeginInvoke((Action)(() =>
					{
						Tray?.ShowBalloon("框译", "程序已在运行。按 " + Settings.Hotkey + " 开始框选。");
					}));
				}
				catch
				{
					break;
				}
			}
		});
		thread.IsBackground = true;
		thread.Name = "KuangYi.ActivationListener";
		thread.Start();
		return true;
	}

	private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
	{
		AppLog.Error("dispatcher unhandled exception", e.Exception);
		e.Handled = true;
		try
		{
			Tray?.ShowBalloon("框译", "发生了一个错误，已记录到日志：" + e.Exception.Message, ToolTipIcon.Error);
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
