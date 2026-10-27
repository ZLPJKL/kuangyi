using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using KuangYi.App.Interop;
using KuangYi.App.Services;

namespace KuangYi.App.Windows;

public partial class CaptureOverlayWindow : Window, IComponentConnector
{
	private readonly CapturedFrame _frame;

	private readonly TaskCompletionSource<Int32Rect?> _completion = new TaskCompletionSource<Int32Rect?>(TaskCreationOptions.RunContinuationsAsynchronously);

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
		MouseRightButtonDown += (object _, MouseButtonEventArgs e) =>
		{
			e.Handled = true;
			Cancel();
		};
	}

	public async Task<Int32Rect?> PickAsync()
	{
		Show();
		Activate();
		NativeMethods.SetForegroundWindow(new WindowInteropHelper(this).Handle);
		Focus();
		Keyboard.Focus(this);
		return await _completion.Task;
	}

	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		nint handle = new WindowInteropHelper(this).Handle;
		uint dpiForWindow = NativeMethods.GetDpiForWindow(handle);
		_scale = ((dpiForWindow == 0) ? 1.0 : ((double)dpiForWindow / 96.0));
		Width = (double)_frame.Width / _scale;
		Height = (double)_frame.Height / _scale;
		Left = (double)_frame.X / _scale;
		Top = (double)_frame.Y / _scale;
		NativeMethods.SetWindowPos(handle, NativeMethods.HwndTopmost, _frame.X, _frame.Y, _frame.Width, _frame.Height, 80u);
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
		if (_dragging)
		{
			_currentPhysical = GetCursorPhysical();
			UpdateVisuals();
		}
	}

	private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		if (_dragging)
		{
			_dragging = false;
			ReleaseMouseCapture();
			_currentPhysical = GetCursorPhysical();
			UpdateVisuals();
			Rect physicalRect = GetPhysicalRect();
			if (physicalRect.Width < 4.0 || physicalRect.Height < 4.0)
			{
				Cancel();
				return;
			}
			_committed = true;
			Int32Rect value = new Int32Rect((int)Math.Round(physicalRect.X - (double)_frame.X), (int)Math.Round(physicalRect.Y - (double)_frame.Y), (int)Math.Round(physicalRect.Width), (int)Math.Round(physicalRect.Height));
			_completion.TrySetResult(value);
			Close();
		}
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
		if (!(ActualWidth <= 0.0) && !(ActualHeight <= 0.0))
		{
			Rect physicalRect = GetPhysicalRect();
			double actualWidth = ActualWidth;
			double actualHeight = ActualHeight;
			if (!_dragging && (physicalRect.Width < 1.0 || physicalRect.Height < 1.0))
			{
				SetRect(DimTop, 0.0, 0.0, actualWidth, 0.0);
				SetRect(DimBottom, 0.0, actualHeight, actualWidth, 0.0);
				SetRect(DimLeft, 0.0, 0.0, 0.0, actualHeight);
				SetRect(DimRight, actualWidth, 0.0, 0.0, actualHeight);
				SelectionBorder.Visibility = Visibility.Collapsed;
				SizeBadge.Visibility = Visibility.Collapsed;
				return;
			}
			if (!_dragging && physicalRect.Width >= (double)_frame.Width && physicalRect.Height >= (double)_frame.Height)
			{
				SetRect(DimTop, 0.0, 0.0, actualWidth, 0.0);
				SetRect(DimBottom, 0.0, actualHeight, actualWidth, 0.0);
				SetRect(DimLeft, 0.0, 0.0, 0.0, actualHeight);
				SetRect(DimRight, actualWidth, 0.0, 0.0, actualHeight);
				SelectionBorder.Visibility = Visibility.Collapsed;
				SizeBadge.Visibility = Visibility.Collapsed;
				return;
			}
			Rect rect = new Rect((physicalRect.X - (double)_frame.X) / _scale, (physicalRect.Y - (double)_frame.Y) / _scale, physicalRect.Width / _scale, physicalRect.Height / _scale);
			SetRect(DimTop, 0.0, 0.0, actualWidth, Math.Max(0.0, rect.Y));
			SetRect(DimBottom, 0.0, rect.Bottom, actualWidth, Math.Max(0.0, actualHeight - rect.Bottom));
			SetRect(DimLeft, 0.0, rect.Y, Math.Max(0.0, rect.X), rect.Height);
			SetRect(DimRight, rect.Right, rect.Y, Math.Max(0.0, actualWidth - rect.Right), rect.Height);
			SelectionBorder.Visibility = Visibility.Visible;
			SetRect(SelectionBorder, rect.X, rect.Y, Math.Max(0.0, rect.Width), Math.Max(0.0, rect.Height));
			SizeText.Text = $"{physicalRect.Width:0} × {physicalRect.Height:0}";
			SizeBadge.Visibility = Visibility.Visible;
			Canvas.SetLeft(SizeBadge, rect.X);
			Canvas.SetTop(SizeBadge, Math.Max(0.0, rect.Y - 30.0));
		}
	}

	private Rect GetPhysicalRect()
	{
		double value = Math.Min(_startPhysical.X, _currentPhysical.X);
		double value2 = Math.Min(_startPhysical.Y, _currentPhysical.Y);
		double value3 = Math.Max(_startPhysical.X, _currentPhysical.X);
		double value4 = Math.Max(_startPhysical.Y, _currentPhysical.Y);
		double num = Math.Clamp(value, _frame.X, _frame.X + _frame.Width);
		double num2 = Math.Clamp(value2, _frame.Y, _frame.Y + _frame.Height);
		double num3 = Math.Clamp(value3, _frame.X, _frame.X + _frame.Width);
		double num4 = Math.Clamp(value4, _frame.Y, _frame.Y + _frame.Height);
		return new Rect(num, num2, Math.Max(0.0, num3 - num), Math.Max(0.0, num4 - num2));
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
		element.Width = Math.Max(0.0, width);
		element.Height = Math.Max(0.0, height);
	}
}
