using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KuangYi.App.Interop;

namespace KuangYi.App.Services;

public sealed class CaptureService
{
	public CapturedFrame CaptureVirtualScreen()
	{
		int systemMetrics = NativeMethods.GetSystemMetrics(76);
		int systemMetrics2 = NativeMethods.GetSystemMetrics(77);
		int systemMetrics3 = NativeMethods.GetSystemMetrics(78);
		int systemMetrics4 = NativeMethods.GetSystemMetrics(79);
		if (systemMetrics3 <= 0 || systemMetrics4 <= 0)
		{
			throw new InvalidOperationException("无法读取屏幕尺寸");
		}
		return new CapturedFrame(CaptureWithBitBlt(systemMetrics, systemMetrics2, systemMetrics3, systemMetrics4), systemMetrics, systemMetrics2, systemMetrics3, systemMetrics4);
	}

	public static void SavePng(BitmapSource image, string path, int? maxWidth = null)
	{
		BitmapSource source = image;
		if (maxWidth.HasValue)
		{
			int valueOrDefault = maxWidth.GetValueOrDefault();
			if (image.PixelWidth > valueOrDefault)
			{
				double num = (double)valueOrDefault / (double)image.PixelWidth;
				TransformedBitmap transformedBitmap = new TransformedBitmap(image, new ScaleTransform(num, num));
				transformedBitmap.Freeze();
				source = transformedBitmap;
			}
		}
		string directoryName = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		PngBitmapEncoder pngBitmapEncoder = new PngBitmapEncoder();
		pngBitmapEncoder.Frames.Add(BitmapFrame.Create(source));
		using FileStream stream = File.Create(path);
		pngBitmapEncoder.Save(stream);
	}

	private static BitmapSource CaptureWithBitBlt(int x, int y, int width, int height)
	{
		nint dC = NativeMethods.GetDC(IntPtr.Zero);
		if (dC == IntPtr.Zero)
		{
			throw new Win32Exception(Marshal.GetLastWin32Error(), "无法获取屏幕设备上下文");
		}
		nint num = IntPtr.Zero;
		nint num2 = IntPtr.Zero;
		nint num3 = IntPtr.Zero;
		try
		{
			num = NativeMethods.CreateCompatibleDC(dC);
			num2 = NativeMethods.CreateCompatibleBitmap(dC, width, height);
			if (num == IntPtr.Zero || num2 == IntPtr.Zero)
			{
				throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建截图缓冲区");
			}
			num3 = NativeMethods.SelectObject(num, num2);
			if (!NativeMethods.BitBlt(num, 0, 0, width, height, dC, x, y, 1087111200u))
			{
				throw new Win32Exception(Marshal.GetLastWin32Error(), "屏幕截图失败");
			}
			BitmapSource bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(num2, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
			bitmapSource.Freeze();
			return bitmapSource;
		}
		finally
		{
			if (num != IntPtr.Zero && num3 != IntPtr.Zero)
			{
				NativeMethods.SelectObject(num, num3);
			}
			if (num2 != IntPtr.Zero)
			{
				NativeMethods.DeleteObject(num2);
			}
			if (num != IntPtr.Zero)
			{
				NativeMethods.DeleteDC(num);
			}
			NativeMethods.ReleaseDC(IntPtr.Zero, dC);
		}
	}
}
