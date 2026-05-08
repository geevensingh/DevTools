using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace CopilotSessionMonitor;

/// <summary>
/// Builds the application's executable icon (multi-resolution .ico) at
/// design time. Run via the <c>--build-icon</c> command-line flag; produces
/// <c>SessionMonitor\Assets\app.ico</c>. The runtime tray icon is rendered
/// dynamically by <see cref="Services.TrayIconFactory"/> and shares the same
/// visual style.
/// </summary>
internal static class IconBuilder
{
    private static readonly int[] s_sizes = { 256, 128, 64, 48, 32, 24, 16 };

    public static int Build(string outPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

        var pngs = new MemoryStream[s_sizes.Length];
        for (int i = 0; i < s_sizes.Length; i++)
        {
            pngs[i] = RenderPng(s_sizes[i]);
        }
        WriteIco(outPath, pngs);
        return 0;
    }

    private static MemoryStream RenderPng(int size)
    {
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            float pad = size * 0.06f;
            float r = size * 0.18f;
            using var path = RoundedRect(pad, pad, size - 2 * pad, size - 2 * pad, r);
            using var fill = new LinearGradientBrush(
                new RectangleF(0, 0, size, size),
                Color.FromArgb(255, 76, 194, 255),
                Color.FromArgb(255, 108, 203, 95),
                LinearGradientMode.ForwardDiagonal);
            g.FillPath(fill, path);
            using var edge = new Pen(Color.FromArgb(180, 0, 0, 0), Math.Max(1, size / 64f));
            g.DrawPath(edge, path);

            // Chevron + cursor underline (white-on-gradient prompt motif)
            float strokeW = Math.Max(1.5f, size / 13.3f);
            using var glyph = new Pen(Color.White, strokeW)
            {
                LineJoin = LineJoin.Round,
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            float cx = size * 0.36f;
            float cy = size * 0.50f;
            float h = size * 0.22f;
            float w = size * 0.16f;
            g.DrawLines(glyph, new PointF[]
            {
                new PointF(cx - w / 2, cy - h / 2),
                new PointF(cx + w / 2, cy),
                new PointF(cx - w / 2, cy + h / 2),
            });
            float ux1 = size * 0.50f;
            float ux2 = size * 0.74f;
            float uy = cy + h / 2;
            g.DrawLine(glyph, ux1, uy, ux2, uy);
        }

        var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        return ms;
    }

    private static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
    {
        var p = new GraphicsPath();
        float d = r * 2;
        p.AddArc(x, y, d, d, 180, 90);
        p.AddArc(x + w - d, y, d, d, 270, 90);
        p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        p.AddArc(x, y + h - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private static void WriteIco(string path, MemoryStream[] pngs)
    {
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write((short)0);                 // reserved
        bw.Write((short)1);                 // type = icon
        bw.Write((short)pngs.Length);       // count

        int headerSize = 6 + pngs.Length * 16;
        int offset = headerSize;
        for (int i = 0; i < pngs.Length; i++)
        {
            int s = s_sizes[i];
            bw.Write((byte)(s == 256 ? 0 : s));    // width (0 = 256)
            bw.Write((byte)(s == 256 ? 0 : s));    // height
            bw.Write((byte)0);                     // palette
            bw.Write((byte)0);                     // reserved
            bw.Write((short)1);                    // color planes
            bw.Write((short)32);                   // bits per pixel
            bw.Write((int)pngs[i].Length);         // image size
            bw.Write(offset);                      // image offset
            offset += (int)pngs[i].Length;
        }
        for (int i = 0; i < pngs.Length; i++)
        {
            pngs[i].Position = 0;
            pngs[i].CopyTo(fs);
        }
    }
}
