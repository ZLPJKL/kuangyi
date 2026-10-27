using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using KuangYi.App.Interop;
using KuangYi.Core.Services;
using Microsoft.Win32;

namespace KuangYi.App.Services;

public sealed class TrayService : IDisposable
{
	private readonly NotifyIcon _icon;

	private readonly ToolStripMenuItem _captureItem;

	private readonly ToolStripMenuItem _pauseItem;

	private Icon? _currentIcon;

	public event EventHandler? CaptureRequested;

	public event EventHandler? ShowRequested;

	public event EventHandler? ResultRequested;

	public event EventHandler? ToggleRequested;

	public event EventHandler? HistoryRequested;

	public event EventHandler? SettingsRequested;

	public event EventHandler<bool>? PauseToggled;

	public event EventHandler? ExitRequested;

	public TrayService()
	{
		ContextMenuStrip contextMenuStrip = new ContextMenuStrip
		{
			ShowImageMargin = false,
			RenderMode = ToolStripRenderMode.System
		};
		ToolStripMenuItem toolStripMenuItem = new ToolStripMenuItem("显示主窗口");
		toolStripMenuItem.Click += (object? _, EventArgs _) =>
		{
			ShowRequested?.Invoke(this, EventArgs.Empty);
		};
		ToolStripMenuItem toolStripMenuItem2 = new ToolStripMenuItem("显示翻译结果");
		toolStripMenuItem2.Click += (object? _, EventArgs _) =>
		{
			ResultRequested?.Invoke(this, EventArgs.Empty);
		};
		_captureItem = new ToolStripMenuItem("框选翻译 (F2)")
		{
			Font = new Font(contextMenuStrip.Font, System.Drawing.FontStyle.Bold)
		};
		_captureItem.Click += (object? _, EventArgs _) =>
		{
			CaptureRequested?.Invoke(this, EventArgs.Empty);
		};
		ToolStripMenuItem toolStripMenuItem3 = new ToolStripMenuItem("历史记录");
		toolStripMenuItem3.Click += (object? _, EventArgs _) =>
		{
			HistoryRequested?.Invoke(this, EventArgs.Empty);
		};
		ToolStripMenuItem toolStripMenuItem4 = new ToolStripMenuItem("设置");
		toolStripMenuItem4.Click += (object? _, EventArgs _) =>
		{
			SettingsRequested?.Invoke(this, EventArgs.Empty);
		};
		_pauseItem = new ToolStripMenuItem("暂停热键");
		_pauseItem.Click += (object? _, EventArgs _) =>
		{
			PauseToggled?.Invoke(this, !_pauseItem.Checked);
		};
		ToolStripMenuItem toolStripMenuItem5 = new ToolStripMenuItem("退出");
		toolStripMenuItem5.Click += (object? _, EventArgs _) =>
		{
			ExitRequested?.Invoke(this, EventArgs.Empty);
		};
		contextMenuStrip.Items.Add(toolStripMenuItem);
		contextMenuStrip.Items.Add(toolStripMenuItem2);
		contextMenuStrip.Items.Add(_captureItem);
		contextMenuStrip.Items.Add(toolStripMenuItem3);
		contextMenuStrip.Items.Add(toolStripMenuItem4);
		contextMenuStrip.Items.Add(new ToolStripSeparator());
		contextMenuStrip.Items.Add(_pauseItem);
		contextMenuStrip.Items.Add(toolStripMenuItem5);
		_currentIcon = CreateIcon();
		_icon = new NotifyIcon
		{
			Icon = _currentIcon,
			Text = "框译 · 屏幕框选翻译",
			Visible = true,
			ContextMenuStrip = contextMenuStrip
		};
		_icon.MouseClick += (object? _, MouseEventArgs e) =>
		{
			if (e.Button == MouseButtons.Left)
			{
				ToggleRequested?.Invoke(this, EventArgs.Empty);
			}
		};
	}

	public void SetHotkey(string gesture, bool registered)
	{
		_captureItem.Text = (registered ? ("框选翻译 (" + gesture + ")") : ("框选翻译 (" + gesture + "，注册失败)"));
	}

	public void SetPaused(bool paused)
	{
		_pauseItem.Checked = paused;
		_pauseItem.Text = (paused ? "恢复热键" : "暂停热键");
	}

	public void PromoteInNotificationArea()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Control Panel\\NotifyIconSettings", writable: true);
			if (registryKey == null)
			{
				return;
			}
			string processPath = Environment.ProcessPath;
			if (string.IsNullOrEmpty(processPath))
			{
				return;
			}
			string[] subKeyNames = registryKey.GetSubKeyNames();
			foreach (string name in subKeyNames)
			{
				using RegistryKey registryKey2 = registryKey.OpenSubKey(name, writable: true);
				if (registryKey2?.GetValue("ExecutablePath") is string a && string.Equals(a, processPath, StringComparison.OrdinalIgnoreCase))
				{
					registryKey2.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
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
			Stream stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute))?.Stream;
			if (stream != null)
			{
				using (stream)
				{
					using Icon icon = new Icon(stream, new System.Drawing.Size(32, 32));
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
		using Bitmap bitmap = new Bitmap(32, 32);
		using (Graphics graphics = Graphics.FromImage(bitmap))
		{
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			graphics.Clear(Color.Transparent);
			using SolidBrush brush = new SolidBrush(Color.FromArgb(31, 111, 235));
			graphics.FillRoundedRectangle(brush, new Rectangle(1, 1, 30, 30), 8);
			using Font font = new Font("Microsoft YaHei UI", 17f, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
			using SolidBrush brush2 = new SolidBrush(Color.White);
			StringFormat format = new StringFormat
			{
				Alignment = StringAlignment.Center,
				LineAlignment = StringAlignment.Center
			};
			graphics.DrawString("译", font, brush2, new RectangleF(0f, 0f, 32f, 33f), format);
		}
		nint hicon = bitmap.GetHicon();
		try
		{
			using Icon icon = Icon.FromHandle(hicon);
			return (Icon)icon.Clone();
		}
		finally
		{
			NativeMethods.DestroyIcon(hicon);
		}
	}

	public void Dispose()
	{
		_icon.Visible = false;
		_icon.Dispose();
		_currentIcon?.Dispose();
	}
}
