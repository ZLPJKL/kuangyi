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

	private readonly RoundedContextMenu _menu;

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
		_menu = new RoundedContextMenu
		{
			ShowImageMargin = true,
			ShowCheckMargin = false,
			ImageScalingSize = new System.Drawing.Size(18, 18),
			Font = new Font("Microsoft YaHei UI", 9.5f, System.Drawing.FontStyle.Regular, GraphicsUnit.Point),
			BackColor = Color.White,
			ForeColor = Color.FromArgb(26, 26, 26),
			Padding = new Padding(7, 7, 7, 7),
			MinimumSize = new System.Drawing.Size(228, 0),
			RenderMode = ToolStripRenderMode.Professional,
			Renderer = new TrayMenuRenderer(),
			DropShadowEnabled = true
		};

		var header = new ToolStripLabel("框译  ·  屏幕框选翻译")
		{
			AutoSize = false,
			Width = 214,
			Height = 34,
			TextAlign = ContentAlignment.MiddleLeft,
			Font = new Font(_menu.Font.FontFamily, 10f, System.Drawing.FontStyle.Bold, GraphicsUnit.Point),
			ForeColor = Color.FromArgb(31, 111, 235),
			Padding = new Padding(12, 7, 12, 4)
		};

		ToolStripMenuItem showItem = CreateItem("显示主窗口", "\uE80F");
		showItem.Click += (object? _, EventArgs _) =>
		{
			ShowRequested?.Invoke(this, EventArgs.Empty);
		};
		ToolStripMenuItem resultItem = CreateItem("显示翻译结果", "\uE8A5");
		resultItem.Click += (object? _, EventArgs _) =>
		{
			ResultRequested?.Invoke(this, EventArgs.Empty);
		};
		_captureItem = CreateItem("框选翻译 (F2)", "\uE123", accent: true, primary: true);
		_captureItem.Click += (object? _, EventArgs _) =>
		{
			CaptureRequested?.Invoke(this, EventArgs.Empty);
		};
		ToolStripMenuItem historyItem = CreateItem("历史记录", "\uE81C");
		historyItem.Click += (object? _, EventArgs _) =>
		{
			HistoryRequested?.Invoke(this, EventArgs.Empty);
		};
		ToolStripMenuItem settingsItem = CreateItem("设置", "\uE713");
		settingsItem.Click += (object? _, EventArgs _) =>
		{
			SettingsRequested?.Invoke(this, EventArgs.Empty);
		};
		_pauseItem = CreateItem("暂停热键", "\uE769");
		_pauseItem.Click += (object? _, EventArgs _) =>
		{
			PauseToggled?.Invoke(this, !_pauseItem.Checked);
		};
		ToolStripMenuItem exitItem = CreateItem("退出", "\uE7E8");
		exitItem.Click += (object? _, EventArgs _) =>
		{
			ExitRequested?.Invoke(this, EventArgs.Empty);
		};

		_menu.Items.Add(header);
		_menu.Items.Add(new ToolStripSeparator());
		_menu.Items.Add(showItem);
		_menu.Items.Add(resultItem);
		_menu.Items.Add(_captureItem);
		_menu.Items.Add(historyItem);
		_menu.Items.Add(settingsItem);
		_menu.Items.Add(new ToolStripSeparator());
		_menu.Items.Add(_pauseItem);
		_menu.Items.Add(exitItem);

		_currentIcon = CreateIcon();
		_icon = new NotifyIcon
		{
			Icon = _currentIcon,
			Text = "框译 · 屏幕框选翻译",
			Visible = true,
			ContextMenuStrip = _menu
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

	private static ToolStripMenuItem CreateItem(string text, string glyph, bool accent = false, bool primary = false)
	{
		var item = new ToolStripMenuItem(text)
		{
			Image = TrayMenuIcons.Create(glyph, accent),
			ImageAlign = ContentAlignment.MiddleLeft,
			TextImageRelation = TextImageRelation.ImageBeforeText,
			Padding = new Padding(2, 7, 10, 7),
			AutoSize = true,
			Tag = primary ? "primary" : null
		};
		if (primary)
		{
			item.Font = new Font("Microsoft YaHei UI", 9.5f, System.Drawing.FontStyle.Bold, GraphicsUnit.Point);
		}

		return item;
	}

	public void SetPaused(bool paused)
	{
		_pauseItem.Checked = paused;
		_pauseItem.Text = (paused ? "恢复热键" : "暂停热键");
		var oldImage = _pauseItem.Image;
		_pauseItem.Image = TrayMenuIcons.Create(paused ? "\uE768" : "\uE769");
		oldImage?.Dispose();
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
		_menu.Dispose();
		_currentIcon?.Dispose();
	}
}
