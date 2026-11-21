using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using KuangYi.Core.Services;

namespace KuangYi.App.Windows;

public partial class ModelDownloadWindow : Window
{
    private CancellationTokenSource? _cts;
    private bool _completed;

    public ModelDownloadWindow()
    {
        InitializeComponent();
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_cts != null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        StartButton.IsEnabled = false;
        CancelButton.Visibility = Visibility.Visible;
        OpenFolderButton.Visibility = Visibility.Collapsed;
        Progress.Value = 0;
        StatusText.Text = "正在查找最新语言包…";
        try
        {
            string url = await FindModelUrlAsync(_cts.Token);
            StatusText.Text = "正在下载…";
            string temp = Path.Combine(Path.GetTempPath(), "kuangyi-models-" + Guid.NewGuid().ToString("N") + ".zip");
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, _cts.Token);
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength;
            await using (var input = await response.Content.ReadAsStreamAsync(_cts.Token))
            await using (var output = File.Create(temp))
            {
                var buffer = new byte[128 * 1024];
                long received = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, _cts.Token)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), _cts.Token);
                    received += read;
                    if (total is > 0)
                    {
                        Progress.Value = Math.Min(100, received * 100.0 / total.Value);
                        StatusText.Text = $"正在下载… {received / 1024.0 / 1024.0:F1} / {total.Value / 1024.0 / 1024.0:F1} MB";
                    }
                }
            }

            StatusText.Text = "正在解压…";
            string destination = AppPaths.Root;
            using (var archive = ZipFile.OpenRead(temp))
            {
                bool hasModelsRoot = archive.Entries.Any(entry => entry.FullName.StartsWith("models/", StringComparison.OrdinalIgnoreCase));
                if (!hasModelsRoot)
                {
                    destination = AppPaths.ModelDir;
                }
            }

            Directory.CreateDirectory(destination);
            ZipFile.ExtractToDirectory(temp, destination, overwriteFiles: true);
            File.Delete(temp);
            Progress.Value = 100;
            StatusText.Text = "语言包已安装。请重启框译，让模型重新加载。";
            _completed = true;
            StartButton.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            OpenFolderButton.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "已取消下载。";
        }
        catch (Exception ex)
        {
            StatusText.Text = "下载失败：" + ex.Message + "\n也可以从 GitHub Release 手动下载语言包。";
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            StartButton.IsEnabled = true;
            CancelButton.Visibility = Visibility.Collapsed;
            if (!_completed)
            {
                StartButton.Visibility = Visibility.Visible;
                StartButton.Content = "重试";
            }
        }
    }

    private static async Task<string> FindModelUrlAsync(CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("KuangYi-ModelDownloader");
        try
        {
            string json = await http.GetStringAsync("https://api.github.com/repos/ZLPJKL/kuangyi/releases/latest", ct);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    string name = asset.GetProperty("name").GetString() ?? string.Empty;
                    if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                        && (name.Contains("models", StringComparison.OrdinalIgnoreCase)
                            || name.Contains("语言包", StringComparison.OrdinalIgnoreCase)))
                    {
                        return asset.GetProperty("browser_download_url").GetString()
                               ?? throw new InvalidOperationException("语言包下载地址为空");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("model release lookup failed: " + ex.Message);
        }

        return "https://github.com/ZLPJKL/kuangyi/releases/latest/download/KuangYi-v1.2.3-models.zip";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = AppPaths.Root,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = "无法打开目录：" + ex.Message;
        }
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
