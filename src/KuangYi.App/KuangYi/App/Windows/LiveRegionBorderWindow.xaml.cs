using System;
using System.Windows;
using System.Windows.Interop;
using KuangYi.App.Interop;
using KuangYi.Core.Services;

namespace KuangYi.App.Windows;

public partial class LiveRegionBorderWindow : Window
{
    private const int GwlExStyle = -20;
    private const uint WsExTransparent = 0x00000020;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExNoActivate = 0x08000000;

    private readonly Int32Rect _region;

    public LiveRegionBorderWindow(Int32Rect region)
    {
        InitializeComponent();
        _region = region;
        SourceInitialized += (_, _) => ApplyNativeWindow();
        Loaded += (_, _) => ApplyPosition();
    }

    private void ApplyNativeWindow()
    {
        try
        {
            nint handle = new WindowInteropHelper(this).Handle;
            nint style = NativeMethods.GetWindowLongPtr(handle, GwlExStyle);
            nint newStyle = (nint)((long)style | WsExTransparent | WsExToolWindow | WsExNoActivate);
            NativeMethods.SetWindowLongPtr(handle, GwlExStyle, newStyle);
            NativeMethods.SetWindowDisplayAffinity(handle, NativeMethods.WdaExcludeFromCapture);
        }
        catch (Exception ex)
        {
            AppLog.Warn("live border style failed: " + ex.Message);
        }
    }

    private void ApplyPosition()
    {
        try
        {
            nint handle = new WindowInteropHelper(this).Handle;
            // The frame is drawn 8 px outside the captured region, so the
            // border itself is not included in OCR refreshes.
            NativeMethods.SetWindowPos(
                handle,
                (nint)(-1),
                _region.X - 8,
                _region.Y - 34,
                _region.Width + 16,
                _region.Height + 42,
                0x0010 | 0x0040);
        }
        catch (Exception ex)
        {
            AppLog.Warn("live border position failed: " + ex.Message);
        }
    }
}
