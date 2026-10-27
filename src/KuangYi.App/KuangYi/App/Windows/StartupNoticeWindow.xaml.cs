using System;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;

namespace KuangYi.App.Windows;

public partial class StartupNoticeWindow : Window, IComponentConnector
{
	private readonly DispatcherTimer _timer;

	public StartupNoticeWindow(string title, string body)
	{
		InitializeComponent();
		TitleText.Text = title;
		BodyText.Text = body;
		_timer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(8L)
		};
		_timer.Tick += (object? _, EventArgs _) =>
		{
			_timer.Stop();
			Close();
		};
		Loaded += (object _, RoutedEventArgs _) =>
		{
			PositionNearTray();
			_timer.Start();
		};
	}

	private void PositionNearTray()
	{
		try
		{
			Rect workArea = SystemParameters.WorkArea;
			Left = Math.Max(workArea.Left + 8.0, workArea.Right - ActualWidth - 24.0);
			Top = Math.Max(workArea.Top + 8.0, workArea.Bottom - ActualHeight - 24.0);
		}
		catch
		{
		}
	}

	private void Close_Click(object sender, RoutedEventArgs e)
	{
		_timer.Stop();
		Close();
	}
}
