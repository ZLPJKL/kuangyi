using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KuangYi.App.Interop;
using KuangYi.App.Services;
using KuangYi.Core.Models;
using KuangYi.Core.Services;

namespace KuangYi.App.Windows;

public partial class ResultWindow : Window, IComponentConnector
{
	private CancellationTokenSource? _workCts;

	private BitmapSource? _crop;

	private bool _initializing;

	private bool _updating;

	private bool _suppressHistory = true;

	private bool _forceClose;

	private int _translateVersion;

	private string _detectedCode = string.Empty;

	public event EventHandler? ForceClosed;

	public ResultWindow()
	{
		InitializeComponent();
		_initializing = true;
		SourceLangCombo.ItemsSource = Languages.SourceChoices;
		TargetLangCombo.ItemsSource = Languages.All;
		string sourceLanguage = App.Current.Settings.SourceLanguage;
		SelectSource(sourceLanguage);
		SelectTarget(string.IsNullOrEmpty(App.Current.Settings.TargetLanguage) ? Languages.DefaultTargetForCurrentSystem() : App.Current.Settings.TargetLanguage);
		_initializing = false;
		Closing += OnClosing;
		Closed += (object? _, EventArgs _) =>
		{
			ForceClosed?.Invoke(this, EventArgs.Empty);
		};
		StateChanged += (object? _, EventArgs _) =>
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

	public void SavePlacement()
	{
		if (!double.IsNaN(Left) && !double.IsNaN(Top) && !double.IsInfinity(Left) && !double.IsInfinity(Top))
		{
			AppSettings settings = App.Current.Settings;
			settings.ResultWindowLeft = Left;
			settings.ResultWindowTop = Top;
			App.Current.SettingsStore.Save(settings);
		}
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
		CancellationTokenSource cts = ReplaceWork();
		int version = _translateVersion;
		SetStatus("正在识别…");
		try
		{
			string configured = App.Current.Settings.SourceLanguage;
			OcrOutput ocrOutput = await App.Current.Ocr.RecognizeAsync(crop, string.Equals(configured, "auto", StringComparison.OrdinalIgnoreCase) ? null : configured, cts.Token);
			if (version == _translateVersion)
			{
				if (string.IsNullOrWhiteSpace(ocrOutput.Text))
				{
					SetStatus("未识别到文字，请重新框选或扩大区域");
					return;
				}
				SourceBox.Text = ocrOutput.Text;
				string code = (_detectedCode = (string.Equals(configured, "auto", StringComparison.OrdinalIgnoreCase) ? MapOcrTag(ocrOutput.LanguageTag) : configured));
				_updating = true;
				SelectSource(code);
				_updating = false;
				SourceHint.Text = BuildOcrInfo(ocrOutput, code);
				SetStatus($"识别完成 · {ocrOutput.Lines.Count} 行");
				await TranslateCurrentAsync(cts.Token, version);
			}
		}
		catch (OcrLanguageUnavailableException ex)
		{
			string nameZh = Languages.Resolve(ex.LanguageCode).NameZh;
			SetStatus("未安装" + nameZh + "识别语言包：请在设置里点“安装识别语言包”");
			AppLog.Warn("ocr language unavailable: " + ex.LanguageCode);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex3)
		{
			AppLog.Error("OCR failed", ex3);
			SetStatus("识别失败：" + ex3.Message);
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
		SourceHint.Text = "识别原文，可直接修改后重译";
		SourceHint.ToolTip = null;
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
		SourceHint.Text = "识别原文，可直接修改后重译";
		SourceHint.ToolTip = null;
		ProviderBadge.Visibility = Visibility.Collapsed;
		EnableOnlineButton.Visibility = Visibility.Collapsed;
		SetBusy(busy: false);
		Topmost = false;
		PinButton.Content = "置顶";
		PinButton.ToolTip = "固定显示在其他窗口上方（默认关闭）";
		_updating = true;
		SelectSource(App.Current.Settings.SourceLanguage);
		SelectTarget(string.IsNullOrEmpty(App.Current.Settings.TargetLanguage) ? Languages.DefaultTargetForCurrentSystem() : App.Current.Settings.TargetLanguage);
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
		string text = SourceBox.Text.Trim();
		if (text.Length == 0)
		{
			SetStatus("没有可翻译的文字");
			return;
		}
		string source = ResolveSource(text);
		string target = SelectedTargetCode();
		SetBusy(busy: true);
		SetStatus("正在翻译…");
		Stopwatch watch = Stopwatch.StartNew();
		try
		{
			TranslationOutcome translationOutcome = await App.Current.Translations.CreatePipeline(App.Current.Settings).TranslateAsync(new TranslationRequest(text, source, target), ct);
			if (version != _translateVersion)
			{
				return;
			}
			watch.Stop();
			if (translationOutcome.Success)
			{
				TranslationResult result = translationOutcome.Result;
				if ((object)result != null)
				{
					OutputBox.Text = result.Text;
					ProviderText.Text = $"{result.ProviderName} · {Languages.Resolve(source).NameZh} → {Languages.Resolve(target).NameZh}";
					ProviderBadge.Visibility = Visibility.Visible;
					EnableOnlineButton.Visibility = Visibility.Collapsed;
					string value = BuildOnlineFailureNote(translationOutcome);
					SetStatus(string.Equals(source, target, StringComparison.OrdinalIgnoreCase) ? "源语言与目标语言相同" : $"完成 · {watch.ElapsedMilliseconds} ms{value}");
					if (saveHistory && !_suppressHistory)
					{
						await SaveHistoryAsync(text, result, source, target, watch.ElapsedMilliseconds);
					}
					return;
				}
			}
			OutputBox.Text = string.Empty;
			ProviderBadge.Visibility = Visibility.Collapsed;
			SetStatus("翻译失败：" + (translationOutcome.SummaryError ?? "没有可用的翻译源"));
			EnableOnlineButton.Visibility = ((App.Current.Settings.LocalOnly || App.Current.Settings.OnlineEnabled) ? Visibility.Collapsed : Visibility.Visible);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			AppLog.Error("translation failed", ex2);
			SetStatus("翻译失败：" + ex2.Message);
		}
		finally
		{
			if (version == _translateVersion)
			{
				SetBusy(busy: false);
			}
		}
	}

	private async Task SaveHistoryAsync(string sourceText, TranslationResult result, string source, string target, long elapsedMs)
	{
		AppSettings settings = App.Current.Settings;
		if (!settings.HistoryEnabled || string.IsNullOrWhiteSpace(result.Text))
		{
			return;
		}
		string text = null;
		if (settings.ThumbnailsEnabled && _crop != null)
		{
			text = Path.Combine(AppPaths.ThumbnailDir, Guid.NewGuid().ToString("N") + ".png");
			try
			{
				CaptureService.SavePng(_crop, text, 320);
			}
			catch (Exception ex)
			{
				AppLog.Warn("thumbnail save failed: " + ex.Message);
				text = null;
			}
		}
		if (settings.KeepOriginalImage && _crop != null)
		{
			try
			{
				CaptureService.SavePng(_crop, Path.Combine(AppPaths.OriginalDir, Guid.NewGuid().ToString("N") + ".png"));
			}
			catch (Exception ex2)
			{
				AppLog.Warn("original image save failed: " + ex2.Message);
			}
		}
		HistoryEntry entry = new HistoryEntry
		{
			CreatedAt = DateTimeOffset.UtcNow,
			SourceLang = source,
			TargetLang = target,
			SourceText = sourceText,
			TargetText = result.Text,
			Provider = result.ProviderName,
			ThumbPath = text,
			ElapsedMs = elapsedMs
		};
		try
		{
			await App.Current.HistoryStore.AddAsync(entry, new HistoryOptions(settings.HistoryLimit, settings.HistoryDays, settings.FavoriteLimit));
		}
		catch (Exception ex3)
		{
			AppLog.Error("history write failed", ex3);
		}
	}

	private string ResolveSource(string text)
	{
		if (!(SourceLangCombo.SelectedItem is LanguageInfo { IsAuto: false } languageInfo))
		{
			DetectionResult detectionResult = LanguageDetector.Detect(text);
			if (detectionResult.IsConfident)
			{
				return detectionResult.Language.Code;
			}
			if (!string.IsNullOrEmpty(_detectedCode))
			{
				return _detectedCode;
			}
			return Languages.English.Code;
		}
		return languageInfo.Code;
	}

	private string SelectedTargetCode()
	{
		return (TargetLangCombo.SelectedItem as LanguageInfo)?.Code ?? Languages.Chinese.Code;
	}

	private void SelectSource(string code)
	{
		LanguageInfo languageInfo = Languages.SourceChoices.FirstOrDefault((LanguageInfo l) => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));
		SourceLangCombo.SelectedItem = languageInfo ?? Languages.AutoInfo;
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
			if (!TryApplySavedPlacement())
			{
				nint handle = new WindowInteropHelper(this).Handle;
				double num = (double)NativeMethods.GetDpiForWindow(handle) / 96.0;
				if (num <= 0.0)
				{
					num = 1.0;
				}
				int num2 = (int)Math.Ceiling(ActualWidth * num);
				int num3 = (int)Math.Ceiling(ActualHeight * num);
				Rectangle workingArea = Screen.FromRectangle(new Rectangle(screenRect.X, screenRect.Y, Math.Max(1, screenRect.Width), Math.Max(1, screenRect.Height))).WorkingArea;
				int num4 = 12;
				int num5 = screenRect.X + screenRect.Width + num4;
				int num6 = screenRect.Y;
				if (num5 + num2 > workingArea.Right)
				{
					num5 = screenRect.X - num2 - num4;
				}
				if (num5 < workingArea.Left + 8)
				{
					num5 = workingArea.Left + 8;
				}
				if (num5 + num2 > workingArea.Right)
				{
					num5 = Math.Max(workingArea.Left + 8, workingArea.Right - num2 - 8);
				}
				if (num6 < workingArea.Top + 8)
				{
					num6 = workingArea.Top + 8;
				}
				if (num6 + num3 > workingArea.Bottom)
				{
					num6 = Math.Max(workingArea.Top + 8, workingArea.Bottom - num3 - 8);
				}
				NativeMethods.SetWindowPos(handle, IntPtr.Zero, num5, num6, 0, 0, 21u);
			}
		}
		catch (Exception ex)
		{
			AppLog.Warn("positioning failed: " + ex.Message);
		}
	}

	private bool TryApplySavedPlacement()
	{
		AppSettings settings = App.Current.Settings;
		double? resultWindowLeft = settings.ResultWindowLeft;
		if (resultWindowLeft.HasValue)
		{
			double valueOrDefault = resultWindowLeft.GetValueOrDefault();
			resultWindowLeft = settings.ResultWindowTop;
			if (resultWindowLeft.HasValue)
			{
				double valueOrDefault2 = resultWindowLeft.GetValueOrDefault();
				double virtualScreenLeft = SystemParameters.VirtualScreenLeft;
				double virtualScreenTop = SystemParameters.VirtualScreenTop;
				double virtualScreenWidth = SystemParameters.VirtualScreenWidth;
				double virtualScreenHeight = SystemParameters.VirtualScreenHeight;
				if (valueOrDefault < virtualScreenLeft - 40.0 || valueOrDefault2 < virtualScreenTop - 20.0 || valueOrDefault > virtualScreenLeft + virtualScreenWidth - 80.0 || valueOrDefault2 > virtualScreenTop + virtualScreenHeight - 60.0)
				{
					return false;
				}
				WindowStartupLocation = WindowStartupLocation.Manual;
				Left = valueOrDefault;
				Top = valueOrDefault2;
				return true;
			}
		}
		return false;
	}

	private static string BuildOcrInfo(OcrOutput ocr, string code)
	{
		string text = EngineLabel(ocr.LanguageTag);
		int num = (int)Math.Round(ocr.Confidence * 100.0);
		string text2 = "识别：" + Languages.Resolve(code).NameZh + " · " + text;
		if (num > 0)
		{
			text2 += $" · 置信度 {num}%";
		}
		return text2 + " · 可直接修改后重译";
	}

	private static string EngineLabel(string tag)
	{
		if (tag.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
		{
			return "日文引擎";
		}
		if (tag.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
		{
			return "中文引擎";
		}
		if (tag.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
		{
			return "韩文引擎";
		}
		if (tag.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
		{
			return "俄文引擎";
		}
		if (tag.StartsWith("en", StringComparison.OrdinalIgnoreCase))
		{
			return "英文引擎";
		}
		if (tag.Length != 0)
		{
			return tag;
		}
		return "OCR 引擎";
	}

	private static string BuildOnlineFailureNote(TranslationOutcome outcome)
	{
		ProviderAttempt providerAttempt = outcome.Attempts?.FirstOrDefault((ProviderAttempt a) => !a.Success && (string.Equals(a.ProviderId, "deepl", StringComparison.OrdinalIgnoreCase) || string.Equals(a.ProviderId, "libretranslate", StringComparison.OrdinalIgnoreCase) || string.Equals(a.ProviderId, "mymemory", StringComparison.OrdinalIgnoreCase)));
		if (providerAttempt == null)
		{
			return string.Empty;
		}
		string text = providerAttempt.Error ?? "未返回结果";
		if (text.Length > 80)
		{
			text = text.Substring(0, 80) + "…";
		}
		AppLog.Warn("online translation failed (" + providerAttempt.ProviderId + "): " + providerAttempt.Error);
		return $" · {providerAttempt.ProviderName} 失败，已回退离线（{text}）";
	}

	private void SetStatus(string message)
	{
		StatusText.Text = message;
	}

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
		DependencyObject dependencyObject = source as DependencyObject;
		while (dependencyObject != null && !(dependencyObject is Window))
		{
			if (dependencyObject is System.Windows.Controls.Primitives.ButtonBase || dependencyObject is System.Windows.Controls.ComboBox || dependencyObject is System.Windows.Controls.TextBox)
			{
				return true;
			}
			dependencyObject = VisualTreeHelper.GetParent(dependencyObject);
		}
		return false;
	}

	private async void Translate_Click(object sender, RoutedEventArgs e)
	{
		CancellationTokenSource cancellationTokenSource = ReplaceWork();
		await TranslateCurrentAsync(cancellationTokenSource.Token, _translateVersion);
	}

	private async void SourceLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!_initializing && !_updating && SourceLangCombo.SelectedItem is LanguageInfo languageInfo)
		{
			if (_crop != null && !_suppressHistory)
			{
				CancellationTokenSource cancellationTokenSource = ReplaceWork();
				await ReprocessAsync(cancellationTokenSource.Token, _translateVersion, languageInfo.IsAuto ? null : languageInfo.Code);
			}
			else if (!string.IsNullOrWhiteSpace(SourceBox.Text))
			{
				CancellationTokenSource cancellationTokenSource2 = ReplaceWork();
				await TranslateCurrentAsync(cancellationTokenSource2.Token, _translateVersion);
			}
		}
	}

	private async Task ReprocessAsync(CancellationToken ct, int version, string? preferredLanguage)
	{
		if (_crop == null)
		{
			return;
		}
		SetStatus("正在重新识别…");
		try
		{
			OcrOutput ocrOutput = await App.Current.Ocr.RecognizeAsync(_crop, preferredLanguage, ct);
			if (version == _translateVersion)
			{
				if (string.IsNullOrWhiteSpace(ocrOutput.Text))
				{
					SetStatus("未识别到文字，请重新框选或扩大区域");
					return;
				}
				SourceBox.Text = ocrOutput.Text;
				ResultWindow resultWindow = this;
				string? obj = preferredLanguage ?? MapOcrTag(ocrOutput.LanguageTag);
				string text = obj;
				resultWindow._detectedCode = obj;
				string code = text;
				_updating = true;
				SelectSource(code);
				_updating = false;
				SourceHint.Text = BuildOcrInfo(ocrOutput, code);
				SetStatus($"识别完成 · {ocrOutput.Lines.Count} 行");
				await TranslateCurrentAsync(ct, version);
			}
		}
		catch (OcrLanguageUnavailableException ex)
		{
			string nameZh = Languages.Resolve(ex.LanguageCode).NameZh;
			SetStatus("未安装" + nameZh + "识别语言包：请在设置里安装后重启框译");
			AppLog.Warn("ocr language unavailable: " + ex.LanguageCode);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex3)
		{
			AppLog.Error("OCR reprocess failed", ex3);
			SetStatus("重新识别失败：" + ex3.Message);
		}
	}

	private async void TargetLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!_initializing && !_updating && TargetLangCombo.SelectedItem is LanguageInfo languageInfo)
		{
			AppSettings settings = App.Current.Settings;
			if (!string.Equals(settings.TargetLanguage, languageInfo.Code, StringComparison.OrdinalIgnoreCase))
			{
				settings.TargetLanguage = languageInfo.Code;
				App.Current.SettingsStore.Save(settings);
			}
			if (!string.IsNullOrWhiteSpace(SourceBox.Text))
			{
				CancellationTokenSource cancellationTokenSource = ReplaceWork();
				await TranslateCurrentAsync(cancellationTokenSource.Token, _translateVersion);
			}
		}
	}

	private void CopyTranslation_Click(object sender, RoutedEventArgs e)
	{
		Copy(OutputBox.Text, "译文");
	}

	private void CopySource_Click(object sender, RoutedEventArgs e)
	{
		Copy(SourceBox.Text, "原文");
	}

	private void Copy(string text, string label)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			SetStatus("没有可复制的内容");
			return;
		}
		try
		{
			System.Windows.Clipboard.SetText(text);
			SetStatus("已复制" + label);
		}
		catch (Exception)
		{
			SetStatus("复制失败，剪贴板被其他程序占用");
		}
	}

	private void Pin_Click(object sender, RoutedEventArgs e)
	{
		Topmost = !Topmost;
		PinButton.Content = (Topmost ? "取消置顶" : "置顶");
		PinButton.ToolTip = (Topmost ? "已固定在最上层，点击取消" : "固定显示在其他窗口上方（默认关闭）");
	}

	private void Settings_Click(object sender, RoutedEventArgs e)
	{
		App.Current.ShowSettings();
	}

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
		AppSettings settings = App.Current.Settings;
		settings.OnlineEnabled = true;
		settings.LocalOnly = false;
		App.Current.ApplySettings(settings);
		CancellationTokenSource cancellationTokenSource = ReplaceWork();
		await TranslateCurrentAsync(cancellationTokenSource.Token, _translateVersion);
	}

	private void OnClosing(object? sender, CancelEventArgs e)
	{
		SavePlacement();
		if (!_forceClose)
		{
			e.Cancel = true;
			_workCts?.Cancel();
			Hide();
		}
	}
}
