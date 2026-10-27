using System.Windows.Media.Imaging;

namespace KuangYi.App.Services;

public sealed record CapturedFrame(BitmapSource Image, int X, int Y, int Width, int Height);
