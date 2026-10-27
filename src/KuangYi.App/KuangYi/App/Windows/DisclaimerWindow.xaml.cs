using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;

namespace KuangYi.App.Windows;

public partial class DisclaimerWindow : Window, IComponentConnector
{
	private const int WmClose = 16;

	private const int WmSysCommand = 274;

	private const int ScClose = 61536;

	private bool _accepted;

	public DisclaimerWindow()
	{
		InitializeComponent();
		BodyText.Text = LoadDisclaimer();
		Closing += OnClosing;
		SourceInitialized += (object? _, EventArgs _) =>
		{
			HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
		};
		PreviewKeyDown += (object _, KeyEventArgs e) =>
		{
			if (e.Key == Key.Escape)
			{
				e.Handled = true;
			}
		};
	}

	private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
	{
		if (!_accepted)
		{
			if (message == 16)
			{
				goto IL_002d;
			}
			if (message == 274)
			{
				nint num = wParam;
				if ((((IntPtr)num).ToInt64() & 0xFFF0) == 61536)
				{
					goto IL_002d;
				}
			}
		}
		return IntPtr.Zero;
		IL_002d:
		handled = true;
		return IntPtr.Zero;
	}

	private static string LoadDisclaimer()
	{
		try
		{
			string path = Path.Combine(AppContext.BaseDirectory, "免责声明.txt");
			if (File.Exists(path))
			{
				return File.ReadAllText(path);
			}
		}
		catch
		{
		}
		return "框译是免费软件，无广告、无内购。默认本地运行：OCR 和离线翻译都在本机完成；在线翻译默认关闭，关闭时软件不会主动联网。机器翻译可能存在错误，请自行判断，不要用于医疗、法律、金融等高风险决策。用户使用本软件翻译、复制、发布内容等行为及其后果由用户自行承担，软件及开发者不承担任何责任。";
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
