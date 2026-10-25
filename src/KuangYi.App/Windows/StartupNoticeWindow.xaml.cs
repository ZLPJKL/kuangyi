using System.Windows;
using System.Windows.Threading;

namespace KuangYi.App.Windows;

public partial class StartupNoticeWindow : Window
{
    private readonly DispatcherTimer _timer;

    public StartupNoticeWindow(string title, string body)
    {
        InitializeComponent();
        TitleText.Text = title;
        BodyText.Text = body;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Close();
        };

        Loaded += (_, _) =>
        {
            PositionNearTray();
            _timer.Start();
        };
    }

    private void PositionNearTray()
    {
        try
        {
            var area = SystemParameters.WorkArea;
            Left = Math.Max(area.Left + 8, area.Right - ActualWidth - 24);
            Top = Math.Max(area.Top + 8, area.Bottom - ActualHeight - 24);
        }
        catch
        {
            // positioning is cosmetic; never let it break startup
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        Close();
    }
}
