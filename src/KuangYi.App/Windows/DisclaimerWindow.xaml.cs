using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Interop;

namespace KuangYi.App.Windows;

public partial class DisclaimerWindow : Window
{
    private const int WmClose = 0x0010;
    private const int WmSysCommand = 0x0112;
    private const int ScClose = 0xF060;

    private bool _accepted;

    public DisclaimerWindow()
    {
        InitializeComponent();
        BodyText.Text = LoadDisclaimer();
        Closing += OnClosing;
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WndProc);
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                e.Handled = true;
            }
        };
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!_accepted
            && (message == WmClose
                || (message == WmSysCommand && (wParam.ToInt64() & 0xFFF0) == ScClose)))
        {
            handled = true;
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    private static string LoadDisclaimer()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "免责声明.txt");
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }
        catch
        {
        }

        return "框译是免费软件，无广告、无内购。默认本地运行：OCR 和离线翻译都在本机完成；"
               + "在线翻译默认关闭，关闭时软件不会主动联网。机器翻译可能存在错误，请自行判断，"
               + "不要用于医疗、法律、金融等高风险决策。用户使用本软件翻译、复制、发布内容等行为"
               + "及其后果由用户自行承担，软件及开发者不承担任何责任。";
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        _accepted = true;
        DialogResult = true;
        Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_accepted)
        {
            e.Cancel = true;
        }
    }
}
