using System;
using System.Runtime.InteropServices;

namespace KuangYi.App.Interop;

internal static class NativeMethods
{
	public struct Point
	{
		public int X;

		public int Y;
	}

	public const int WmHotkey = 786;

	public const uint ModAlt = 1u;

	public const uint ModControl = 2u;

	public const uint ModShift = 4u;

	public const uint ModWin = 8u;

	public const uint ModNoRepeat = 16384u;

	public const int SmXVirtualScreen = 76;

	public const int SmYVirtualScreen = 77;

	public const int SmCxVirtualScreen = 78;

	public const int SmCyVirtualScreen = 79;

	public static readonly nint HwndTopmost = new IntPtr(-1);

	public const uint SwpNoActivate = 16u;

	public const uint SwpShowWindow = 64u;

	public const uint SwpNoSize = 1u;

	public const uint SwpNoZOrder = 4u;

	public const uint RopSourceCopy = 13369376u;

	public const uint RopCaptureBlt = 1073741824u;

	public const int DwmwaWindowCornerPreference = 33;

	public const int DwmWindowCornerRound = 2;

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool UnregisterHotKey(nint hWnd, int id);

	[DllImport("user32.dll")]
	public static extern int GetSystemMetrics(int nIndex);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool GetCursorPos(out Point point);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

	[DllImport("user32.dll")]
	public static extern uint GetDpiForWindow(nint hWnd);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool SetForegroundWindow(nint hWnd);

	[DllImport("gdi32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool DeleteObject(nint hObject);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool DestroyIcon(nint hIcon);

	[DllImport("dwmapi.dll")]
	public static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

	[DllImport("user32.dll")]
	public static extern nint GetDC(nint hWnd);

	[DllImport("user32.dll")]
	public static extern int ReleaseDC(nint hWnd, nint hDC);

	[DllImport("gdi32.dll")]
	public static extern nint CreateCompatibleDC(nint hdc);

	[DllImport("gdi32.dll")]
	public static extern nint CreateCompatibleBitmap(nint hdc, int width, int height);

	[DllImport("gdi32.dll")]
	public static extern nint SelectObject(nint hdc, nint hObject);

	[DllImport("gdi32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool BitBlt(nint hdcDest, int xDest, int yDest, int width, int height, nint hdcSrc, int xSrc, int ySrc, uint rop);

	[DllImport("gdi32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool DeleteDC(nint hdc);
}
