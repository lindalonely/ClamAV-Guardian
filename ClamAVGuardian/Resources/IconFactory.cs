using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClamAVGuardian.Resources;

public enum TrayIconState
{
    Protected,
    Disabled,
    Alert
}

public static class IconFactory
{
    public static Icon CreateTrayIcon(TrayIconState state)
    {
        var color = state switch
        {
            TrayIconState.Protected => Color.FromArgb(46, 160, 67),
            TrayIconState.Alert => Color.FromArgb(218, 54, 51),
            _ => Color.FromArgb(140, 140, 140),
        };

        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var shieldPoints = new[]
            {
                new PointF(16, 2),
                new PointF(29, 7),
                new PointF(29, 16),
                new PointF(16, 30),
                new PointF(3, 16),
                new PointF(3, 7),
            };

            using var brush = new SolidBrush(color);
            using var pen = new Pen(Color.FromArgb(60, 60, 60), 1.5f);
            g.FillPolygon(brush, shieldPoints);
            g.DrawPolygon(pen, shieldPoints);

            if (state == TrayIconState.Alert)
            {
                using var textBrush = new SolidBrush(Color.White);
                using var font = new Font("Segoe UI", 14, FontStyle.Bold);
                g.DrawString("!", font, textBrush, new RectangleF(10, 6, 14, 20));
            }
        }

        var hIcon = bitmap.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    /// <summary>
    /// Pulls the icon straight from this exe's own embedded resource (ApplicationIcon in
    /// the csproj), so the window/taskbar icon matches what Explorer and shortcuts show —
    /// rather than the separately runtime-drawn tray icon, which only exists as a small
    /// bitmap and was never embedded in the exe file itself.
    /// </summary>
    public static Icon CreateAppIcon() =>
        Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? CreateTrayIcon(TrayIconState.Protected);
}
