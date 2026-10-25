using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KuangYi.App.Interop;

namespace KuangYi.App.Services;

public sealed record CapturedFrame(BitmapSource Image, int X, int Y, int Width, int Height);

public sealed class CaptureService
{
    public CapturedFrame CaptureVirtualScreen()
    {
        var x = NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen);
        var y = NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen);
        var width = NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen);
        var height = NativeMethods.GetSystemMetrics(NativeMethods.SmCyVirtualScreen);

        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("无法读取屏幕尺寸");
        }

        var image = CaptureWithBitBlt(x, y, width, height);
        return new CapturedFrame(image, x, y, width, height);
    }

    public static void SavePng(BitmapSource image, string path, int? maxWidth = null)
    {
        BitmapSource output = image;
        if (maxWidth is { } limit && image.PixelWidth > limit)
        {
            var scale = limit / (double)image.PixelWidth;
            var transformed = new TransformedBitmap(image, new ScaleTransform(scale, scale));
            transformed.Freeze();
            output = transformed;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(output));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static BitmapSource CaptureWithBitBlt(int x, int y, int width, int height)
    {
        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法获取屏幕设备上下文");
        }

        var memoryDc = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        var previous = IntPtr.Zero;

        try
        {
            memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
            bitmap = NativeMethods.CreateCompatibleBitmap(screenDc, width, height);
            if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建截图缓冲区");
            }

            previous = NativeMethods.SelectObject(memoryDc, bitmap);
            var copied = NativeMethods.BitBlt(
                memoryDc,
                0,
                0,
                width,
                height,
                screenDc,
                x,
                y,
                NativeMethods.RopSourceCopy | NativeMethods.RopCaptureBlt);

            if (!copied)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "屏幕截图失败");
            }

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            if (memoryDc != IntPtr.Zero && previous != IntPtr.Zero)
            {
                NativeMethods.SelectObject(memoryDc, previous);
            }

            if (bitmap != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(bitmap);
            }

            if (memoryDc != IntPtr.Zero)
            {
                NativeMethods.DeleteDC(memoryDc);
            }

            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}
