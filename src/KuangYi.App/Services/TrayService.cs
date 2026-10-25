using System.Drawing;
using System.Windows.Forms;
using KuangYi.Core.Services;

namespace KuangYi.App.Services;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _captureItem;
    private readonly ToolStripMenuItem _pauseItem;
    private Icon? _currentIcon;

    public TrayService()
    {
        var menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            RenderMode = ToolStripRenderMode.System,
        };

        var showItem = new ToolStripMenuItem("显示主窗口");
        showItem.Click += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);

        var resultItem = new ToolStripMenuItem("显示翻译结果");
        resultItem.Click += (_, _) => ResultRequested?.Invoke(this, EventArgs.Empty);

        _captureItem = new ToolStripMenuItem("框选翻译 (F2)") { Font = new Font(menu.Font, FontStyle.Bold) };
        _captureItem.Click += (_, _) => CaptureRequested?.Invoke(this, EventArgs.Empty);

        var historyItem = new ToolStripMenuItem("历史记录");
        historyItem.Click += (_, _) => HistoryRequested?.Invoke(this, EventArgs.Empty);

        var settingsItem = new ToolStripMenuItem("设置");
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);

        _pauseItem = new ToolStripMenuItem("暂停热键");
        _pauseItem.Click += (_, _) => PauseToggled?.Invoke(this, !_pauseItem.Checked);

        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        menu.Items.Add(showItem);
        menu.Items.Add(resultItem);
        menu.Items.Add(_captureItem);
        menu.Items.Add(historyItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_pauseItem);
        menu.Items.Add(exitItem);

        _currentIcon = CreateIcon();
        _icon = new NotifyIcon
        {
            Icon = _currentIcon,
            Text = "框译 · 屏幕框选翻译",
            Visible = true,
            ContextMenuStrip = menu,
        };

        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ToggleRequested?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    public event EventHandler? CaptureRequested;

    public event EventHandler? ShowRequested;

    public event EventHandler? ResultRequested;

    public event EventHandler? ToggleRequested;

    public event EventHandler? HistoryRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler<bool>? PauseToggled;

    public event EventHandler? ExitRequested;

    public void SetHotkey(string gesture, bool registered)
    {
        _captureItem.Text = registered
            ? $"框选翻译 ({gesture})"
            : $"框选翻译 ({gesture}，注册失败)";

        // Do not touch NotifyIcon.Text here: on a desktop without the shell running
        // the tooltip update can block the startup thread. The menu item carries the
        // same information and is always safe to update.
    }

    public void SetPaused(bool paused)
    {
        _pauseItem.Checked = paused;
        _pauseItem.Text = paused ? "恢复热键" : "暂停热键";
    }

    /// <summary>
    /// Windows 11 puts new notification icons in the overflow area. Promote our icon
    /// once so the user can actually find the running program.
    /// </summary>
    public void PromoteInNotificationArea()
    {
        try
        {
            using var root = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Control Panel\NotifyIconSettings",
                writable: true);
            if (root is null)
            {
                return;
            }

            var executable = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executable))
            {
                return;
            }

            foreach (var name in root.GetSubKeyNames())
            {
                using var item = root.OpenSubKey(name, writable: true);
                if (item?.GetValue("ExecutablePath") is string path
                    && string.Equals(path, executable, StringComparison.OrdinalIgnoreCase))
                {
                    item.SetValue("IsPromoted", 1, Microsoft.Win32.RegistryValueKind.DWord);
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("promote tray icon failed: " + ex.Message);
        }
    }

    public void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        try
        {
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText = text;
            _icon.BalloonTipIcon = icon;
            _icon.ShowBalloonTip(3500);
        }
        catch (Exception ex)
        {
            AppLog.Warn("balloon failed: " + ex.Message);
        }
    }

    private static Icon CreateIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute);
            var stream = System.Windows.Application.GetResourceStream(uri)?.Stream;
            if (stream is not null)
            {
                using (stream)
                using (var icon = new Icon(stream, new System.Drawing.Size(32, 32)))
                {
                    return (Icon)icon.Clone();
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("tray icon resource failed: " + ex.Message);
        }

        return CreateFallbackIcon();
    }

    private static Icon CreateFallbackIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var background = new SolidBrush(Color.FromArgb(31, 111, 235));
            g.FillRoundedRectangle(background, new Rectangle(1, 1, 30, 30), 8);
            using var font = new Font("Microsoft YaHei UI", 17, FontStyle.Bold, GraphicsUnit.Pixel);
            using var text = new SolidBrush(Color.White);
            var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("译", font, text, new RectangleF(0, 0, 32, 33), format);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            Interop.NativeMethods.DestroyIcon(handle);
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _currentIcon?.Dispose();
    }
}

internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle bounds, int radius)
    {
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }
}
