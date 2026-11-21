using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using KuangYi.Core.Models;
using KuangYi.Core.Services;
using KuangYi.Core.Services.Translation;

namespace KuangYi.App.Windows;

public partial class BatchWindow : Window
{
    private readonly ObservableCollection<BatchRow> _rows = new();
    private CancellationTokenSource? _cts;

    public BatchWindow()
    {
        InitializeComponent();
        ItemsGrid.ItemsSource = _rows;
    }

    private void OpenFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择图片或 PDF",
            Filter = "图片和 PDF|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.pdf|所有文件|*.*",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        foreach (var file in dialog.FileNames)
        {
            _rows.Add(new BatchRow(file));
        }
        StatusText.Text = $"已加入 {_rows.Count} 个文件";
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_cts != null || _rows.Count == 0)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        StartButton.IsEnabled = false;
        Progress.Value = 0;
        var pipeline = App.Current.Translations.CreatePipeline(App.Current.Settings);
        string target = string.IsNullOrEmpty(App.Current.Settings.TargetLanguage)
            ? Languages.DefaultTargetForCurrentSystem()
            : App.Current.Settings.TargetLanguage;
        try
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                _cts.Token.ThrowIfCancellationRequested();
                var row = _rows[i];
                row.Status = "处理中…";
                StatusText.Text = $"正在处理 {i + 1} / {_rows.Count}";
                try
                {
                    if (string.Equals(Path.GetExtension(row.Path), ".pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        await ProcessPdfAsync(row, pipeline, target, _cts.Token);
                    }
                    else
                    {
                        await ProcessImageAsync(row, pipeline, target, _cts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    row.Status = "失败：" + ex.Message;
                }

                Progress.Value = (i + 1) * 100.0 / _rows.Count;
            }
            StatusText.Text = "批量处理完成";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "已取消";
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            StartButton.IsEnabled = true;
        }
    }

    private async Task ProcessImageAsync(BatchRow row, TranslationPipeline pipeline, string target, CancellationToken ct)
    {
        var image = LoadImage(row.Path);
        await TranslateImageAsync(row, image, pipeline, target, ct);
    }

    private async Task ProcessPdfAsync(BatchRow row, TranslationPipeline pipeline, string target, CancellationToken ct)
    {
        var file = await global::Windows.Storage.StorageFile.GetFileFromPathAsync(row.Path);
        var document = await global::Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
        var source = new StringBuilder();
        var translated = new StringBuilder();
        bool hadFailure = false;
        for (uint pageIndex = 0; pageIndex < document.PageCount; pageIndex++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var page = document.GetPage(pageIndex);
                using var stream = new global::Windows.Storage.Streams.InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(stream);
                stream.Seek(0);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream.AsStreamForRead();
                image.EndInit();
                image.Freeze();
                var result = await RecognizeAndTranslateAsync(image, pipeline, target, ct);
                if (string.IsNullOrWhiteSpace(result.Source))
                {
                    row.Status = $"PDF 第 {pageIndex + 1}/{document.PageCount} 页 · 空白页，已跳过";
                    continue;
                }

                source.AppendLine(result.Source);
                translated.AppendLine(result.Translation);
                row.Status = $"PDF 第 {pageIndex + 1}/{document.PageCount} 页";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                hadFailure = true;
                row.Status = $"PDF 第 {pageIndex + 1} 页失败：" + ex.Message;
            }
        }
        row.SourceText = source.ToString().Trim();
        row.Translation = translated.ToString().Trim();
        row.Status = hadFailure ? "完成（部分页面失败）" : "完成";
    }

    private async Task TranslateImageAsync(BatchRow row, BitmapSource image, TranslationPipeline pipeline, string target, CancellationToken ct)
    {
        var result = await RecognizeAndTranslateAsync(image, pipeline, target, ct);
        row.SourceText = result.Source;
        row.Translation = result.Translation;
        row.Status = string.IsNullOrWhiteSpace(result.Source) ? "未识别到文字" : "完成";
    }

    private async Task<(string Source, string Translation)> RecognizeAndTranslateAsync(
        BitmapSource image,
        TranslationPipeline pipeline,
        string target,
        CancellationToken ct)
    {
        var settings = App.Current.Settings;
        var configured = settings.SourceLanguage;
        var ocr = await App.Current.Ocr.RecognizeAsync(
            image,
            string.Equals(configured, "auto", StringComparison.OrdinalIgnoreCase) ? null : configured,
            settings.OcrQuality,
            ct);
        if (string.IsNullOrWhiteSpace(ocr.Text))
        {
            return (string.Empty, string.Empty);
        }

        string source = string.Equals(configured, "auto", StringComparison.OrdinalIgnoreCase)
            ? LanguageDetector.Detect(ocr.Text).Language.Code
            : configured;
        string formattedSource = TextFormatter.Reflow(ocr.Text, source);
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            return (formattedSource, "源语言与目标语言相同");
        }

        var glossary = App.Current.Translations.Glossary;
        var prepared = glossary.Prepare(ocr.Text, source, target);
        string mode = settings.LocalOnly ? "offline" : (settings.OnlineEnabled ? settings.OnlineProvider : "offline");
        string cacheKey = TranslationCache.CreateKey(
            ocr.Text.Trim(),
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
                return (formattedSource, TextFormatter.Reflow(cached.Text, target));
            }
        }

        var outcome = await pipeline.TranslateAsync(new TranslationRequest(prepared.Text, source, target), ct);
        if (!outcome.Success || outcome.Result == null)
        {
            return (formattedSource, "翻译失败：" + (outcome.SummaryError ?? "没有可用翻译源"));
        }

        string translated = TextFormatter.Reflow(
            glossary.Restore(outcome.Result.Text, prepared.Replacements),
            target);
        if (settings.CacheEnabled
            && (string.Equals(mode, "offline", StringComparison.OrdinalIgnoreCase) || !outcome.Result.IsOffline))
        {
            await App.Current.Translations.Cache.StoreAsync(
                cacheKey,
                ocr.Text.Trim(),
                new TranslationResult(translated, outcome.Result.ProviderId, outcome.Result.ProviderName, outcome.Result.IsOffline),
                ct);
        }

        return (formattedSource, translated);
    }

    private static BitmapSource LoadImage(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出批量翻译",
            Filter = "CSV 文件|*.csv|所有文件|*.*",
            FileName = "kuangyi-batch.csv",
            AddExtension = true,
            DefaultExt = ".csv"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine("file,status,source,translation");
        foreach (var row in _rows)
        {
            builder.Append(Csv(row.Path)).Append(',')
                .Append(Csv(row.Status)).Append(',')
                .Append(Csv(row.SourceText)).Append(',')
                .Append(Csv(row.Translation)).AppendLine();
        }
        File.WriteAllText(dialog.FileName, builder.ToString(), new UTF8Encoding(false));
        StatusText.Text = "已导出：" + dialog.FileName;
    }

    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _rows.Clear();
        Progress.Value = 0;
        StatusText.Text = string.Empty;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        Close();
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

public sealed class BatchRow : INotifyPropertyChanged
{
    private string _status = "待处理";
    private string _sourceText = string.Empty;
    private string _translation = string.Empty;

    public BatchRow(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public string SourceText
    {
        get => _sourceText;
        set => SetField(ref _sourceText, value);
    }

    public string Translation
    {
        get => _translation;
        set => SetField(ref _translation, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
