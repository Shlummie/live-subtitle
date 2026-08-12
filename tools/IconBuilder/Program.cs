using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

if (args.Length != 1) throw new ArgumentException("Pass the output .ico path.");
using var bitmap = new Bitmap(256, 256, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
using var graphics = Graphics.FromImage(bitmap);
graphics.SmoothingMode = SmoothingMode.AntiAlias;
graphics.Clear(Color.Transparent);

using var blue = new SolidBrush(Color.FromArgb(255, 79, 142, 247));
using var white = new SolidBrush(Color.White);
using var line = new Pen(Color.FromArgb(255, 28, 88, 189), 14) { StartCap = LineCap.Round, EndCap = LineCap.Round };
graphics.FillRoundedRectangle(blue, new Rectangle(0, 0, 256, 256), 56);

using var bubble = new GraphicsPath();
bubble.AddRoundedRectangle(new Rectangle(58, 52, 140, 120), 24);
bubble.AddPolygon([new Point(92, 163), new Point(92, 202), new Point(132, 166)]);
graphics.FillPath(white, bubble);
graphics.DrawLine(line, 88, 100, 168, 100);
graphics.DrawLine(line, 88, 128, 147, 128);

var handle = bitmap.GetHicon();
try
{
    using var icon = Icon.FromHandle(handle);
    using var stream = new FileStream(args[0], FileMode.Create, FileAccess.Write, FileShare.None);
    icon.Save(stream);
}
finally
{
    DestroyIcon(handle);
}

[DllImport("user32.dll", SetLastError = true)]
static extern bool DestroyIcon(IntPtr handle);

internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle rectangle, int radius)
    {
        using var path = new GraphicsPath();
        path.AddRoundedRectangle(rectangle, radius);
        graphics.FillPath(brush, path);
    }

    public static void AddRoundedRectangle(this GraphicsPath path, Rectangle rectangle, int radius)
    {
        var diameter = radius * 2;
        path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
    }
}
