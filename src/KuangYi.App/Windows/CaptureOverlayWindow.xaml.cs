using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using KuangYi.App.Interop;
using KuangYi.App.Services;

namespace KuangYi.App.Windows;

public partial class CaptureOverlayWindow : Window
{
    private readonly CapturedFrame _frame;
    private readonly TaskCompletionSource<Int32Rect?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private double _scale = 1.0;
    private Point _startPhysical;
    private Point _currentPhysical;
    private bool _dragging;
    private bool _committed;

    public CaptureOverlayWindow(CapturedFrame frame)
    {
        _frame = frame;
        InitializeComponent();

        ScreenImage.Source = frame.Image;

        Loaded += OnLoaded;
        Closed += OnClosed;
        PreviewKeyDown += OnPreviewKeyDown;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseRightButtonDown += (_, e) =>
        {
            e.Handled = true;
            Cancel();
        };
    }

    public async Task<Int32Rect?> PickAsync()
    {
        Show();
        Activate();

        var handle = new WindowInteropHelper(this).Handle;
        NativeMethods.SetForegroundWindow(handle);
        Focus();
        Keyboard.Focus(this);
        return await _completion.Task;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var dpi = NativeMethods.GetDpiForWindow(handle);
        _scale = dpi <= 0 ? 1.0 : dpi / 96.0;

        Width = _frame.Width / _scale;
        Height = _frame.Height / _scale;
        Left = _frame.X / _scale;
        Top = _frame.Y / _scale;

        NativeMethods.SetWindowPos(
            handle,
            NativeMethods.HwndTopmost,
            _frame.X,
            _frame.Y,
            _frame.Width,
            _frame.Height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);

        UpdateVisuals();
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _startPhysical = GetCursorPhysical();
        _currentPhysical = _startPhysical;
        HintBadge.Visibility = Visibility.Collapsed;
        CaptureMouse();
        UpdateVisuals();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _currentPhysical = GetCursorPhysical();
        UpdateVisuals();
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleaseMouseCapture();
        _currentPhysical = GetCursorPhysical();
        UpdateVisuals();

        var rect = GetPhysicalRect();
        if (rect.Width < 4 || rect.Height < 4)
        {
            Cancel();
            return;
        }

        _committed = true;
        var frameRect = new Int32Rect(
            (int)Math.Round(rect.X - _frame.X),
            (int)Math.Round(rect.Y - _frame.Y),
            (int)Math.Round(rect.Width),
            (int)Math.Round(rect.Height));

        _completion.TrySetResult(frameRect);
        Close();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Cancel();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (!_committed)
        {
            _completion.TrySetResult(null);
        }
    }

    private void Cancel()
    {
        _committed = false;
        _completion.TrySetResult(null);
        Close();
    }

    private void UpdateVisuals()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var rect = GetPhysicalRect();
        var width = ActualWidth;
        var height = ActualHeight;

        if (!_dragging && (rect.Width < 1 || rect.Height < 1))
        {
            SetRect(DimTop, 0, 0, width, 0);
            SetRect(DimBottom, 0, height, width, 0);
            SetRect(DimLeft, 0, 0, 0, height);
            SetRect(DimRight, width, 0, 0, height);
            SelectionBorder.Visibility = Visibility.Collapsed;
            SizeBadge.Visibility = Visibility.Collapsed;
            return;
        }

        if (!_dragging && rect.Width >= _frame.Width && rect.Height >= _frame.Height)
        {
            SetRect(DimTop, 0, 0, width, 0);
            SetRect(DimBottom, 0, height, width, 0);
            SetRect(DimLeft, 0, 0, 0, height);
            SetRect(DimRight, width, 0, 0, height);
            SelectionBorder.Visibility = Visibility.Collapsed;
            SizeBadge.Visibility = Visibility.Collapsed;
            return;
        }

        var dip = new Rect(
            (rect.X - _frame.X) / _scale,
            (rect.Y - _frame.Y) / _scale,
            rect.Width / _scale,
            rect.Height / _scale);

        SetRect(DimTop, 0, 0, width, Math.Max(0, dip.Y));
        SetRect(DimBottom, 0, dip.Bottom, width, Math.Max(0, height - dip.Bottom));
        SetRect(DimLeft, 0, dip.Y, Math.Max(0, dip.X), dip.Height);
        SetRect(DimRight, dip.Right, dip.Y, Math.Max(0, width - dip.Right), dip.Height);

        SelectionBorder.Visibility = Visibility.Visible;
        SetRect(SelectionBorder, dip.X, dip.Y, Math.Max(0, dip.Width), Math.Max(0, dip.Height));

        SizeText.Text = $"{rect.Width:0} × {rect.Height:0}";
        SizeBadge.Visibility = Visibility.Visible;
        Canvas.SetLeft(SizeBadge, dip.X);
        Canvas.SetTop(SizeBadge, Math.Max(0, dip.Y - 30));
    }

    private Rect GetPhysicalRect()
    {
        var x1 = Math.Min(_startPhysical.X, _currentPhysical.X);
        var y1 = Math.Min(_startPhysical.Y, _currentPhysical.Y);
        var x2 = Math.Max(_startPhysical.X, _currentPhysical.X);
        var y2 = Math.Max(_startPhysical.Y, _currentPhysical.Y);

        var left = Math.Clamp(x1, _frame.X, _frame.X + _frame.Width);
        var top = Math.Clamp(y1, _frame.Y, _frame.Y + _frame.Height);
        var right = Math.Clamp(x2, _frame.X, _frame.X + _frame.Width);
        var bottom = Math.Clamp(y2, _frame.Y, _frame.Y + _frame.Height);

        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static Point GetCursorPhysical()
    {
        NativeMethods.GetCursorPos(out var point);
        return new Point(point.X, point.Y);
    }

    private static void SetRect(FrameworkElement element, double x, double y, double width, double height)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        element.Width = Math.Max(0, width);
        element.Height = Math.Max(0, height);
    }
}
