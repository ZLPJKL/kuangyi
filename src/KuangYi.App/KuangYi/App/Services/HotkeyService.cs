using System;
using System.Windows.Interop;
using KuangYi.App.Interop;
using KuangYi.Core.Services;

namespace KuangYi.App.Services;

public sealed class HotkeyService : IDisposable
{
	private const int HotkeyId = 19289;

	private HwndSource? _source;

	private bool _registered;

	private bool _disposed;

	public string CurrentGesture { get; private set; } = "F2";

	public string? LastError { get; private set; }

	public bool IsRegistered => _registered;

	public event EventHandler? Pressed;

	public bool Register(string gesture)
	{
		Unregister();
		if (!HotkeyGesture.TryParse(gesture, out ParsedHotkey parsed, out string error))
		{
			LastError = error;
			return false;
		}
		EnsureSource();
		if (!NativeMethods.RegisterHotKey(_source.Handle, 19289, parsed.Modifiers | 0x4000, parsed.VirtualKey))
		{
			LastError = "快捷键已被其他程序占用";
			AppLog.Warn("hotkey registration failed: " + gesture);
			return false;
		}
		_registered = true;
		CurrentGesture = gesture;
		LastError = null;
		return true;
	}

	public void Unregister()
	{
		if (_registered && _source != null)
		{
			NativeMethods.UnregisterHotKey(_source.Handle, 19289);
		}
		_registered = false;
	}

	private void EnsureSource()
	{
		if (_source == null)
		{
			HwndSourceParameters hwndSourceParameters = new HwndSourceParameters("KuangYi.HotkeyWindow");
			hwndSourceParameters.Width = 0;
			hwndSourceParameters.Height = 0;
			hwndSourceParameters.PositionX = 0;
			hwndSourceParameters.PositionY = 0;
			hwndSourceParameters.WindowStyle = 0;
			hwndSourceParameters.ExtendedWindowStyle = 0;
			HwndSourceParameters parameters = hwndSourceParameters;
			_source = new HwndSource(parameters);
			_source.AddHook(WndProc);
		}
	}

	private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
	{
		if (msg == 786)
		{
			nint num = wParam;
			if (((IntPtr)num).ToInt32() == 19289)
			{
				handled = true;
				Pressed?.Invoke(this, EventArgs.Empty);
			}
		}
		return IntPtr.Zero;
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			Unregister();
			_source?.RemoveHook(WndProc);
			_source?.Dispose();
			_source = null;
		}
	}
}
