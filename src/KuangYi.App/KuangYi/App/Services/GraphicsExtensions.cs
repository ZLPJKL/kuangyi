using System.Drawing;
using System.Drawing.Drawing2D;

namespace KuangYi.App.Services;

internal static class GraphicsExtensions
{
	public static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle bounds, int radius)
	{
		using GraphicsPath graphicsPath = new GraphicsPath();
		int num = radius * 2;
		graphicsPath.AddArc(bounds.X, bounds.Y, num, num, 180f, 90f);
		graphicsPath.AddArc(bounds.Right - num, bounds.Y, num, num, 270f, 90f);
		graphicsPath.AddArc(bounds.Right - num, bounds.Bottom - num, num, num, 0f, 90f);
		graphicsPath.AddArc(bounds.X, bounds.Bottom - num, num, num, 90f, 90f);
		graphicsPath.CloseFigure();
		graphics.FillPath(brush, graphicsPath);
	}
}
