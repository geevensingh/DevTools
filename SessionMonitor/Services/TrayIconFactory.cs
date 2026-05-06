using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace CopilotSessionMonitor.Services;

/// <summary>
/// Builds 16x16 tray icons at runtime so we don't ship multiple .ico files.
/// Uses System.Drawing on the UI thread (we never call this hot path).
/// </summary>
public static class TrayIconFactory
{
    public static Icon Build(Core.SessionStatus worstState)
    {
        // 32x32 looks crisp on high-DPI; Windows downscales to 16 on 100% DPI.
        const int size = 32;
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            // Rounded square with two-color gradient — readable on light & dark taskbars.
            using var path = RoundedRect(new Rectangle(2, 2, size - 4, size - 4), 6);
            using var fill = new LinearGradientBrush(new Rectangle(0, 0, size, size),
                Color.FromArgb(255, 76, 194, 255),
                Color.FromArgb(255, 108, 203, 95),
                LinearGradientMode.ForwardDiagonal);
            g.FillPath(fill, path);
            using var pen = new Pen(Color.FromArgb(160, 0, 0, 0), 1);
            g.DrawPath(pen, path);

            // Inner glyph: a small terminal cursor / chevron motif.
            using var glyphPen = new Pen(Color.White, 2.4f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLines(glyphPen, new[]
            {
                new PointF(11, 12),
                new PointF(15, 16),
                new PointF(11, 20),
            });
            g.DrawLine(glyphPen, 17, 20, 22, 20);

            // Status dot in the bottom-right.
            var dotColor = ColorFor(worstState);
            using var dotBrush = new SolidBrush(dotColor);
            using var dotEdge = new Pen(Color.FromArgb(255, 24, 25, 28), 2);
            var dotRect = new Rectangle(size - 14, size - 14, 12, 12);
            g.FillEllipse(dotBrush, dotRect);
            g.DrawEllipse(dotEdge, dotRect);
        }

        var hIcon = bmp.GetHicon();
        // Icon.FromHandle does not own the handle; we duplicate to a managed Icon and destroy the source.
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        return icon;
    }

    private static Color ColorFor(Core.SessionStatus s) => s switch
    {
        Core.SessionStatus.Red => Color.FromArgb(255, 231, 72, 86),
        Core.SessionStatus.Blue => Color.FromArgb(255, 76, 194, 255),
        Core.SessionStatus.Yellow => Color.FromArgb(255, 247, 195, 49),
        Core.SessionStatus.Green => Color.FromArgb(255, 108, 203, 95),
        _ => Color.FromArgb(255, 110, 113, 119),
    };

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var p = new GraphicsPath();
        int d = radius * 2;
        p.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        p.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        p.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        p.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
