using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClamAVGuardian.Resources;

public static class Theme
{
    public static readonly Color SidebarBg = Color.FromArgb(17, 24, 39);
    public static readonly Color SidebarText = Color.FromArgb(156, 163, 175);
    public static readonly Color SidebarTextActive = Color.White;
    public static readonly Color SidebarSelectedBg = Color.FromArgb(31, 41, 55);

    public static readonly Color ContentBg = Color.FromArgb(243, 244, 246);
    public static readonly Color CardBg = Color.White;
    public static readonly Color CardBorder = Color.FromArgb(229, 231, 235);

    public static readonly Color TextPrimary = Color.FromArgb(17, 24, 39);
    public static readonly Color TextSecondary = Color.FromArgb(107, 114, 128);

    public static readonly Color AccentBlue = Color.FromArgb(59, 130, 246);
    public static readonly Color AccentGreen = Color.FromArgb(34, 197, 94);
    public static readonly Color AccentRed = Color.FromArgb(239, 68, 68);
    public static readonly Color AccentAmber = Color.FromArgb(245, 158, 11);
    public static readonly Color AccentGray = Color.FromArgb(156, 163, 175);

    public static Font FontHeading => new("Segoe UI Semibold", 16f);
    public static Font FontSubheading => new("Segoe UI Semibold", 11f);
    public static Font FontBody => new("Segoe UI", 9.5f);
    public static Font FontBodyBold => new("Segoe UI Semibold", 9.5f);
    public static Font FontStatValue => new("Segoe UI Semibold", 20f);
    public static Font FontStatLabel => new("Segoe UI", 9f);
    public static Font FontNav => new("Segoe UI", 10f);
    public static Font FontMono => new("Consolas", 9f);

    public static void StylePrimaryButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = AccentBlue;
        button.ForeColor = Color.White;
        button.Font = FontBodyBold;
        button.Cursor = Cursors.Hand;
        button.Height = Math.Max(button.Height, 34);
    }

    public static void StyleSecondaryButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = CardBorder;
        button.FlatAppearance.BorderSize = 1;
        button.BackColor = Color.White;
        button.ForeColor = TextPrimary;
        button.Font = FontBody;
        button.Cursor = Cursors.Hand;
        button.Height = Math.Max(button.Height, 34);
    }

    public static void StyleDangerButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = AccentRed;
        button.ForeColor = Color.White;
        button.Font = FontBodyBold;
        button.Cursor = Cursors.Hand;
        button.Height = Math.Max(button.Height, 34);
    }

    public static void StyleListView(ListView lv)
    {
        lv.BorderStyle = BorderStyle.None;
        lv.Font = FontBody;
        lv.BackColor = Color.White;
        lv.ForeColor = TextPrimary;
    }

    public static void StyleTextBox(TextBox tb)
    {
        tb.BorderStyle = BorderStyle.FixedSingle;
        tb.Font = FontBody;
    }
}
