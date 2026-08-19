using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using ClamAVGuardian.Resources;

namespace ClamAVGuardian.Controls;

public class SidebarNavButton : Panel
{
    private bool _selected;
    private bool _hover;
    private readonly AppIcon _icon;

    public event EventHandler? Activated;

    public string Text2
    {
        get => _label.Text;
        set => _label.Text = value;
    }

    private readonly Label _label;

    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            Invalidate();
            _label.ForeColor = value ? Theme.SidebarTextActive : Theme.SidebarText;
            _label.Font = value ? new Font(Theme.FontNav, FontStyle.Bold) : Theme.FontNav;
        }
    }

    public SidebarNavButton(string text, AppIcon icon)
    {
        _icon = icon;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Height = 42;
        Dock = DockStyle.Top;
        Cursor = Cursors.Hand;
        BackColor = Theme.SidebarBg;

        _label = new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(52, 0, 0, 0),
            ForeColor = Theme.SidebarText,
            BackColor = Color.Transparent,
            Font = Theme.FontNav,
        };
        Controls.Add(_label);

        MouseEnter += (_, _) => { _hover = true; Invalidate(); };
        MouseLeave += (_, _) => { _hover = false; Invalidate(); };
        _label.MouseEnter += (_, _) => { _hover = true; Invalidate(); };
        _label.MouseLeave += (_, _) => { _hover = false; Invalidate(); };
        Click += (_, _) => Activated?.Invoke(this, EventArgs.Empty);
        _label.Click += (_, _) => Activated?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.None;

        var bg = _selected ? Theme.SidebarSelectedBg : (_hover ? Color.FromArgb(26, 32, 44) : Theme.SidebarBg);
        using var brush = new SolidBrush(bg);
        e.Graphics.FillRectangle(brush, ClientRectangle);

        if (_selected)
        {
            using var accentBrush = new SolidBrush(Theme.AccentBlue);
            e.Graphics.FillRectangle(accentBrush, 0, 0, 3, Height);
        }

        var iconColor = _selected ? Theme.SidebarTextActive : Theme.SidebarText;
        var iconSize = 18f;
        IconSet.Draw(e.Graphics, _icon, new RectangleF(20, (Height - iconSize) / 2f, iconSize, iconSize), iconColor);

        base.OnPaint(e);
    }
}
