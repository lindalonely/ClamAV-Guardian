using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClamAVGuardian.Controls;

public class RoundedPanel : Panel
{
    public int CornerRadius { get; set; } = 10;
    public Color BorderColor { get; set; } = Resources.Theme.CardBorder;

    public RoundedPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Resources.Theme.CardBg;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = RoundedRect(rect, CornerRadius);
        using var fillBrush = new SolidBrush(BackColor);
        using var borderPen = new Pen(BorderColor, 1);

        e.Graphics.FillPath(fillBrush, path);
        e.Graphics.DrawPath(borderPen, path);

        base.OnPaint(e);
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();

        if (radius <= 0)
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
