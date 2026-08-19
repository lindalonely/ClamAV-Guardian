using System.Drawing;
using System.Drawing.Drawing2D;

namespace ClamAVGuardian.Resources;

public enum AppIcon
{
    Home,
    Search,
    Shield,
    Lock,
    Refresh,
    Document,
    Gear,
    Clock,
    Database,
    Warning,
    Download,
    Play,
    Stop,
    Save,
    Plus,
    Trash,
    Undo,
    Folder,
}

/// <summary>
/// Small flat vector icons drawn directly with GDI+ (same technique as the tray icon in
/// IconFactory) instead of depending on a system icon font — keeps rendering identical
/// across every Windows version/DPI setting rather than trusting Segoe MDL2/Fluent glyph
/// availability.
/// </summary>
public static class IconSet
{
    public static Bitmap Render(AppIcon icon, int size, Color color)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Draw(g, icon, new RectangleF(0, 0, size, size), color);
        return bmp;
    }

    public static void Draw(Graphics g, AppIcon icon, RectangleF b, Color color)
    {
        var prevSmoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var brush = new SolidBrush(color);
        using var pen = new Pen(color, b.Width * 0.11f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

        PointF P(float xf, float yf) => new(b.X + xf * b.Width, b.Y + yf * b.Height);
        RectangleF R(float xf, float yf, float wf, float hf) => new(b.X + xf * b.Width, b.Y + yf * b.Height, wf * b.Width, hf * b.Height);

        switch (icon)
        {
            case AppIcon.Home:
            {
                using var path = new GraphicsPath();
                path.AddPolygon(new[] { P(0.5f, 0.06f), P(0.92f, 0.42f), P(0.78f, 0.42f), P(0.78f, 0.94f), P(0.22f, 0.94f), P(0.22f, 0.42f), P(0.08f, 0.42f) });
                g.FillPath(brush, path);
                break;
            }

            case AppIcon.Search:
            {
                g.DrawEllipse(pen, R(0.08f, 0.08f, 0.56f, 0.56f));
                g.DrawLine(pen, P(0.58f, 0.58f), P(0.92f, 0.92f));
                break;
            }

            case AppIcon.Shield:
            {
                using var path = new GraphicsPath();
                path.AddPolygon(new[] { P(0.5f, 0.04f), P(0.9f, 0.2f), P(0.9f, 0.5f), P(0.5f, 0.96f), P(0.1f, 0.5f), P(0.1f, 0.2f) });
                g.FillPath(brush, path);
                break;
            }

            case AppIcon.Lock:
            {
                g.DrawLines(pen, new[] { P(0.32f, 0.44f), P(0.32f, 0.24f), P(0.68f, 0.24f), P(0.68f, 0.44f) });
                using var path = new GraphicsPath();
                path.AddRoundedRect(R(0.16f, 0.4f, 0.68f, 0.52f), b.Width * 0.08f);
                g.FillPath(brush, path);
                break;
            }

            case AppIcon.Refresh:
            {
                g.DrawArc(pen, R(0.1f, 0.1f, 0.8f, 0.8f), -30, 260);
                using var arrow = new GraphicsPath();
                arrow.AddPolygon(new[] { P(0.74f, 0.06f), P(0.98f, 0.14f), P(0.86f, 0.34f) });
                g.FillPath(brush, arrow);
                break;
            }

            case AppIcon.Document:
            {
                using var path = new GraphicsPath();
                path.AddPolygon(new[] { P(0.2f, 0.05f), P(0.62f, 0.05f), P(0.8f, 0.23f), P(0.8f, 0.95f), P(0.2f, 0.95f) });
                g.DrawPath(pen, path);
                g.DrawLine(pen, P(0.34f, 0.48f), P(0.66f, 0.48f));
                g.DrawLine(pen, P(0.34f, 0.65f), P(0.66f, 0.65f));
                g.DrawLine(pen, P(0.34f, 0.82f), P(0.56f, 0.82f));
                break;
            }

            case AppIcon.Gear:
            {
                var center = P(0.5f, 0.5f);
                var outerR = b.Width * 0.42f;
                var ringR = b.Width * 0.28f;
                var toothW = b.Width * 0.14f;
                for (var i = 0; i < 8; i++)
                {
                    var state = g.Save();
                    g.TranslateTransform(center.X, center.Y);
                    g.RotateTransform(i * 45f);
                    g.FillRectangle(brush, -toothW / 2, -outerR, toothW, outerR - ringR + b.Width * 0.05f);
                    g.Restore(state);
                }
                using var ringPen = new Pen(color, b.Width * 0.16f);
                g.DrawEllipse(ringPen, center.X - ringR, center.Y - ringR, ringR * 2, ringR * 2);
                break;
            }

            case AppIcon.Clock:
            {
                g.DrawEllipse(pen, R(0.06f, 0.06f, 0.88f, 0.88f));
                g.DrawLine(pen, P(0.5f, 0.5f), P(0.5f, 0.26f));
                g.DrawLine(pen, P(0.5f, 0.5f), P(0.7f, 0.6f));
                break;
            }

            case AppIcon.Database:
            {
                g.FillEllipse(brush, R(0.12f, 0.06f, 0.76f, 0.24f));
                g.FillRectangle(brush, b.X + 0.12f * b.Width, b.Y + 0.18f * b.Height, 0.76f * b.Width, 0.64f * b.Height);
                g.FillEllipse(brush, R(0.12f, 0.7f, 0.76f, 0.24f));
                break;
            }

            case AppIcon.Warning:
            {
                using var path = new GraphicsPath();
                path.AddPolygon(new[] { P(0.5f, 0.05f), P(0.95f, 0.9f), P(0.05f, 0.9f) });
                g.FillPath(brush, path);
                using var markBrush = new SolidBrush(Color.White);
                g.FillRectangle(markBrush, b.X + 0.46f * b.Width, b.Y + 0.36f * b.Height, 0.08f * b.Width, 0.28f * b.Height);
                g.FillEllipse(markBrush, b.X + 0.45f * b.Width, b.Y + 0.72f * b.Height, 0.1f * b.Width, 0.1f * b.Height);
                break;
            }

            case AppIcon.Download:
            {
                g.DrawLine(pen, P(0.5f, 0.06f), P(0.5f, 0.6f));
                using var arrow = new GraphicsPath();
                arrow.AddPolygon(new[] { P(0.28f, 0.42f), P(0.72f, 0.42f), P(0.5f, 0.68f) });
                g.FillPath(brush, arrow);
                g.DrawLine(pen, P(0.16f, 0.88f), P(0.84f, 0.88f));
                break;
            }

            case AppIcon.Play:
            {
                using var path = new GraphicsPath();
                path.AddPolygon(new[] { P(0.24f, 0.1f), P(0.24f, 0.9f), P(0.9f, 0.5f) });
                g.FillPath(brush, path);
                break;
            }

            case AppIcon.Stop:
            {
                using var path = new GraphicsPath();
                path.AddRoundedRect(R(0.16f, 0.16f, 0.68f, 0.68f), b.Width * 0.08f);
                g.FillPath(brush, path);
                break;
            }

            case AppIcon.Save:
            {
                using var outer = new GraphicsPath();
                outer.AddRoundedRect(R(0.08f, 0.08f, 0.84f, 0.84f), b.Width * 0.08f);
                g.DrawPath(pen, outer);
                g.FillRectangle(brush, b.X + 0.28f * b.Width, b.Y + 0.08f * b.Height, 0.44f * b.Width, 0.26f * b.Height);
                g.DrawRectangle(pen, b.X + 0.22f * b.Width, b.Y + 0.52f * b.Height, 0.56f * b.Width, 0.34f * b.Height);
                break;
            }

            case AppIcon.Plus:
            {
                g.DrawLine(pen, P(0.5f, 0.12f), P(0.5f, 0.88f));
                g.DrawLine(pen, P(0.12f, 0.5f), P(0.88f, 0.5f));
                break;
            }

            case AppIcon.Trash:
            {
                g.DrawLine(pen, P(0.16f, 0.28f), P(0.84f, 0.28f));
                g.FillRectangle(brush, b.X + 0.36f * b.Width, b.Y + 0.08f * b.Height, 0.28f * b.Width, 0.12f * b.Height);
                using var body = new GraphicsPath();
                body.AddPolygon(new[] { P(0.22f, 0.28f), P(0.78f, 0.28f), P(0.72f, 0.94f), P(0.28f, 0.94f) });
                g.FillPath(brush, body);
                break;
            }

            case AppIcon.Undo:
            {
                g.DrawArc(pen, R(0.12f, 0.16f, 0.72f, 0.72f), 40, 260);
                using var arrow = new GraphicsPath();
                arrow.AddPolygon(new[] { P(0.34f, 0.06f), P(0.34f, 0.34f), P(0.08f, 0.24f) });
                g.FillPath(brush, arrow);
                break;
            }

            case AppIcon.Folder:
            {
                using var path = new GraphicsPath();
                path.AddPolygon(new[] { P(0.08f, 0.24f), P(0.4f, 0.24f), P(0.48f, 0.34f), P(0.92f, 0.34f), P(0.92f, 0.86f), P(0.08f, 0.86f) });
                g.FillPath(brush, path);
                break;
            }
        }

        g.SmoothingMode = prevSmoothing;
    }

    private static void AddRoundedRect(this GraphicsPath path, RectangleF bounds, float radius)
    {
        var d = radius * 2;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
    }
}
