using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace KuangYi.App.Windows;

public partial class PinImageWindow : Window
{
    private readonly BitmapSource _image;

    public PinImageWindow(BitmapSource image)
    {
        InitializeComponent();
        _image = image;
        PreviewImage.Source = image;
        if (image.PixelWidth > 0 && image.PixelHeight > 0)
        {
            double scale = Math.Min(1.0, 560.0 / image.PixelWidth);
            Width = Math.Max(260, image.PixelWidth * scale + 36);
            Height = Math.Max(180, image.PixelHeight * scale + 70);
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetImage(_image);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "复制失败：" + ex.Message, "框译", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || IsButtonSource(e.OriginalSource))
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static bool IsButtonSource(object source)
    {
        var current = source as DependencyObject;
        while (current != null)
        {
            if (current is System.Windows.Controls.Button)
            {
                return true;
            }

            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return false;
    }
}
