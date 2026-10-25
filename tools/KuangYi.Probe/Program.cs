using System.Drawing;
using System.Drawing.Imaging;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using KuangYi.App.Services;
using KuangYi.Core.Models;
using KuangYi.Core.Services.Translation;

namespace KuangYi.Probe;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        var service = new OcrService();
        Console.WriteLine("windows-ocr languages: " + string.Join(", ", service.AvailableLanguageTags));

        if (args.Length > 0 && File.Exists(args[0]))
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(Path.GetFullPath(args[0]));
            image.EndInit();
            image.Freeze();

            foreach (var language in new string?[] { null, "en", "zh", "ja", "ko", "ru" })
            {
                try
                {
                    var probe = await service.RecognizeAsync(image, language, CancellationToken.None);
                    Console.WriteLine($"[{language ?? "auto"}] tag={probe.LanguageTag} lines={probe.Lines.Count}");
                    Console.WriteLine(probe.Text.Length > 300 ? probe.Text[..300] : probe.Text);
                }
                catch (OcrLanguageUnavailableException ex)
                {
                    Console.WriteLine($"[{language}] missing pack: {ex.LanguageCode}");
                }
            }

            return 0;
        }

        using var bitmap = new Bitmap(960, 260);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            using var font = new Font("Microsoft YaHei UI", 30, System.Drawing.FontStyle.Regular, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(Color.Black);
            graphics.DrawString("你好，世界。框选翻译测试。", font, brush, new PointF(24, 20));
            graphics.DrawString("Hello world, this is OCR.", font, brush, new PointF(24, 90));
            graphics.DrawString("こんにちは 世界 Привет мир", font, brush, new PointF(24, 160));
        }

        var handle = bitmap.GetHbitmap();
        BitmapSource source;
        try
        {
            source = Imaging.CreateBitmapSourceFromHBitmap(
                handle,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
        }
        finally
        {
            DeleteObject(handle);
        }

        var result = await service.RecognizeAsync(source, null, CancellationToken.None);
        Console.WriteLine("recognized-tag: " + result.LanguageTag);
        Console.WriteLine("recognized-lines: " + result.Lines.Count);
        Console.WriteLine("recognized-text:");
        Console.WriteLine(result.Text);

        Console.WriteLine();
        Console.WriteLine("--- screen capture probe ---");
        try
        {
            var capture = new CaptureService();
            var frame = capture.CaptureVirtualScreen();
            Console.WriteLine($"capture size={frame.Width}x{frame.Height} origin={frame.X},{frame.Y}");
            var path = Path.Combine(AppContext.BaseDirectory, "capture.png");
            CaptureService.SavePng(frame.Image, path);
            Console.WriteLine("capture saved: " + path);
        }
        catch (Exception ex)
        {
            Console.WriteLine("capture failed: " + ex.Message);
        }

        Console.WriteLine();
        Console.WriteLine("--- online translation probe ---");
        try
        {
            using var http = new HttpClient();
            var pipeline = new TranslationPipeline(new ITranslationProvider[]
            {
                new MyMemoryProvider(http, TimeSpan.FromSeconds(12)),
            });
            var outcome = await pipeline.TranslateAsync(new TranslationRequest("你好，世界", "zh", "en"));
            Console.WriteLine(outcome.Success
                ? "translation: " + outcome.Result!.Text + " (" + outcome.Result.ProviderName + ")"
                : "translation failed: " + outcome.SummaryError);
        }
        catch (Exception ex)
        {
            Console.WriteLine("translation probe failed: " + ex.Message);
        }

        return string.IsNullOrWhiteSpace(result.Text) ? 1 : 0;
    }

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);
}
