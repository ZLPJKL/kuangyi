using System.Windows;
using System.Windows.Input;

namespace KuangYi.App.Windows;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ApplySavedPlacement();
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
                Hide();
                App.Current.TrimMemory();
            }
        };
        Closed += (_, _) => SavePlacement();
    }

    public void SavePlacement()
    {
        if (double.IsNaN(Left) || double.IsNaN(Top) || double.IsInfinity(Left) || double.IsInfinity(Top))
        {
            return;
        }

        var settings = App.Current.Settings;
        settings.MainWindowLeft = Left;
        settings.MainWindowTop = Top;
        App.Current.SettingsStore.Save(settings);
    }

    private void ApplySavedPlacement()
    {
        var settings = App.Current.Settings;
        if (settings.MainWindowLeft is not double left || settings.MainWindowTop is not double top)
        {
            return;
        }

        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualWidth = SystemParameters.VirtualScreenWidth;
        var virtualHeight = SystemParameters.VirtualScreenHeight;

        if (left < virtualLeft - 40
            || top < virtualTop - 20
            || left > virtualLeft + virtualWidth - 80
            || top > virtualTop + virtualHeight - 60)
        {
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = left;
        Top = top;
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

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        SavePlacement();
        Hide();
        App.Current.TrimMemory();
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => App.Current.ShowSettings();

    private void History_Click(object sender, RoutedEventArgs e) => App.Current.ShowHistory();

    private void Result_Click(object sender, RoutedEventArgs e) => App.Current.ShowLastResult();

    private void Exit_Click(object sender, RoutedEventArgs e) => App.Current.ExitApplication();

    private void Close_Click(object sender, RoutedEventArgs e) => App.Current.ExitApplication();

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
