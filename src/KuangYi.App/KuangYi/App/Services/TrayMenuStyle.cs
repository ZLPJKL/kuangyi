using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace KuangYi.App.Services;

internal sealed class RoundedContextMenu : ContextMenuStrip
{
    private const int CornerRadius = 10;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateRegion();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateRegion();
    }

    private void UpdateRegion()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        using var path = CreateRoundedPath(new Rectangle(0, 0, Width, Height), CornerRadius);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
    }

    internal static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        if (bounds.Width <= diameter || bounds.Height <= diameter)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
{
    private static readonly Color TextColor = Color.FromArgb(26, 26, 26);
    private static readonly Color MutedColor = Color.FromArgb(110, 110, 110);
    private static readonly Color AccentColor = Color.FromArgb(31, 111, 235);
    private static readonly Color SelectionColor = Color.FromArgb(234, 241, 254);
    private static readonly Color BorderColor = Color.FromArgb(226, 226, 226);

    public TrayMenuRenderer()
        : base(new TrayColorTable())
    {
        RoundedEdges = true;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        e.Graphics.Clear(Color.White);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        e.Graphics.Clear(Color.White);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        var bounds = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        using var path = RoundedContextMenu.CreateRoundedPath(bounds, 10);
        using var pen = new Pen(BorderColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawPath(pen, path);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled)
        {
            return;
        }

        var bounds = new Rectangle(7, 2, e.Item.Width - 14, e.Item.Height - 4);
        using var path = RoundedContextMenu.CreateRoundedPath(bounds, 6);
        using var brush = new SolidBrush(SelectionColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        var isPrimary = string.Equals(e.Item.Tag as string, "primary", StringComparison.Ordinal);
        var isHeader = e.Item is ToolStripLabel;
        e.TextColor = isPrimary
            ? AccentColor
            : (isHeader ? AccentColor : (e.Item.Enabled ? TextColor : MutedColor));
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var y = e.Item.Height / 2;
        using var pen = new Pen(Color.FromArgb(238, 238, 238));
        e.Graphics.DrawLine(pen, 14, y, e.ToolStrip.Width - 14, y);
    }
}

internal sealed class TrayColorTable : ProfessionalColorTable
{
    public override Color MenuItemSelected => Color.FromArgb(234, 241, 254);
    public override Color MenuItemSelectedGradientBegin => Color.FromArgb(234, 241, 254);
    public override Color MenuItemSelectedGradientEnd => Color.FromArgb(234, 241, 254);
    public override Color MenuItemBorder => Color.FromArgb(234, 241, 254);
    public override Color MenuBorder => Color.FromArgb(226, 226, 226);
    public override Color ToolStripDropDownBackground => Color.White;
    public override Color ImageMarginGradientBegin => Color.White;
    public override Color ImageMarginGradientMiddle => Color.White;
    public override Color ImageMarginGradientEnd => Color.White;
}

internal static class TrayMenuIcons
{
    private static readonly Color NormalColor = Color.FromArgb(110, 110, 110);
    private static readonly Color AccentColor = Color.FromArgb(31, 111, 235);

    public static Image Create(string glyph, bool accent = false)
    {
        var bitmap = new Bitmap(18, 18);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Color.Transparent);
        using var font = new Font("Segoe MDL2 Assets", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(accent ? AccentColor : NormalColor);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        graphics.DrawString(glyph, font, brush, new RectangleF(0f, 0f, 18f, 18f), format);
        return bitmap;
    }
}
