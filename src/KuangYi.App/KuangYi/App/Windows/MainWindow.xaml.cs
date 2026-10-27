using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Markup;
using KuangYi.Core.Models;

namespace KuangYi.App.Windows;

public partial class MainWindow : Window, IComponentConnector
{
	public MainWindow()
	{
		InitializeComponent();
		ApplySavedPlacement();
		StateChanged += (object? _, EventArgs _) =>
		{
			if (WindowState == WindowState.Minimized)
			{
				WindowState = WindowState.Normal;
				Hide();
				App.Current.TrimMemory();
			}
		};
		Closed += (object? _, EventArgs _) =>
		{
			SavePlacement();
		};
	}

	public void SavePlacement()
	{
		if (!double.IsNaN(Left) && !double.IsNaN(Top) && !double.IsInfinity(Left) && !double.IsInfinity(Top))
		{
			AppSettings settings = App.Current.Settings;
			settings.MainWindowLeft = Left;
			settings.MainWindowTop = Top;
			App.Current.SettingsStore.Save(settings);
		}
	}

	private void ApplySavedPlacement()
	{
		AppSettings settings = App.Current.Settings;
		double? mainWindowLeft = settings.MainWindowLeft;
		if (!mainWindowLeft.HasValue)
		{
			return;
		}
		double valueOrDefault = mainWindowLeft.GetValueOrDefault();
		mainWindowLeft = settings.MainWindowTop;
		if (mainWindowLeft.HasValue)
		{
			double valueOrDefault2 = mainWindowLeft.GetValueOrDefault();
			double virtualScreenLeft = SystemParameters.VirtualScreenLeft;
			double virtualScreenTop = SystemParameters.VirtualScreenTop;
			double virtualScreenWidth = SystemParameters.VirtualScreenWidth;
			double virtualScreenHeight = SystemParameters.VirtualScreenHeight;
			if (!(valueOrDefault < virtualScreenLeft - 40.0) && !(valueOrDefault2 < virtualScreenTop - 20.0) && !(valueOrDefault > virtualScreenLeft + virtualScreenWidth - 80.0) && !(valueOrDefault2 > virtualScreenTop + virtualScreenHeight - 60.0))
			{
				WindowStartupLocation = WindowStartupLocation.Manual;
				Left = valueOrDefault;
				Top = valueOrDefault2;
			}
		}
	}

	private async void Capture_Click(object sender, RoutedEventArgs e)
	{
		Hide();
		try
		{
			await App.Current.CaptureFromUiAsync();
		}
		finally
		{
			Show();
		}
	}

	private async void Clipboard_Click(object sender, RoutedEventArgs e)
	{
		await RunImageActionAsync(App.Current.CaptureFromClipboardAsync);
	}

	private async void ImageFile_Click(object sender, RoutedEventArgs e)
	{
		await RunImageActionAsync(App.Current.CaptureFromFileAsync);
	}

	private async Task RunImageActionAsync(Func<Task> action)
	{
		Hide();
		try
		{
			await action();
		}
		finally
		{
			Show();
		}
	}

	private async void Window_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
		{
			e.Handled = true;
			await RunImageActionAsync(App.Current.CaptureFromClipboardAsync);
		}
	}

	private void Minimize_Click(object sender, RoutedEventArgs e)
	{
		SavePlacement();
		Hide();
		App.Current.TrimMemory();
	}

	private void Settings_Click(object sender, RoutedEventArgs e)
	{
		App.Current.ShowSettings();
	}

	private void History_Click(object sender, RoutedEventArgs e)
	{
		App.Current.ShowHistory();
	}

	private void Result_Click(object sender, RoutedEventArgs e)
	{
		App.Current.ShowLastResult();
	}

	private void Exit_Click(object sender, RoutedEventArgs e)
	{
		App.Current.ExitApplication();
	}

	private void Close_Click(object sender, RoutedEventArgs e)
	{
		App.Current.ExitApplication();
	}

	private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ButtonState != MouseButtonState.Pressed)
		{
			return;
		}
		try
		{
			DragMove();
		}
		catch (InvalidOperationException)
		{
		}
	}
}
