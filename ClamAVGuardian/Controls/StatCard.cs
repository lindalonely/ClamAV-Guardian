using System.Drawing;
using System.Windows.Forms;
using ClamAVGuardian.Resources;

namespace ClamAVGuardian.Controls;

public class StatCard : RoundedPanel
{
    private readonly Label _titleLabel;
    private readonly Label _valueLabel;
    private readonly Label _subtitleLabel;
    private readonly Panel _accentBar;
    private readonly PictureBox _iconBox;
    private AppIcon _icon = AppIcon.Shield;

    public string TitleText
    {
        get => _titleLabel.Text;
        set => _titleLabel.Text = value;
    }

    public string ValueText
    {
        get => _valueLabel.Text;
        set => _valueLabel.Text = value;
    }

    public string SubtitleText
    {
        get => _subtitleLabel.Text;
        set
        {
            _subtitleLabel.Text = value;
            _subtitleLabel.Visible = !string.IsNullOrEmpty(value);
        }
    }

    public AppIcon Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            RefreshIcon();
        }
    }

    public Color AccentColor
    {
        get => _accentBar.BackColor;
        set
        {
            _accentBar.BackColor = value;
            _valueLabel.ForeColor = value;
            RefreshIcon();
        }
    }

    private void RefreshIcon()
    {
        _iconBox.Image?.Dispose();
        _iconBox.Image = IconSet.Render(_icon, 20, AccentColor);
    }

    public StatCard()
    {
        Padding = new Padding(16, 14, 16, 12);
        Size = new Size(250, 110);

        _accentBar = new Panel { Dock = DockStyle.Left, Width = 4, BackColor = Theme.AccentBlue };

        _iconBox = new PictureBox
        {
            Size = new Size(20, 20),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(Size.Width - 16 - 20, 14),
            BackColor = Color.Transparent,
            SizeMode = PictureBoxSizeMode.CenterImage,
        };

        _titleLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 20,
            Font = Theme.FontStatLabel,
            ForeColor = Theme.TextSecondary,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(12, 0, 0, 0),
        };

        _valueLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 40,
            Font = Theme.FontStatValue,
            ForeColor = Theme.AccentBlue,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(12, 2, 0, 0),
            AutoEllipsis = true,
        };

        _subtitleLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 18,
            Font = Theme.FontStatLabel,
            ForeColor = Theme.TextSecondary,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            Visible = false,
        };

        var contentPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 20, 0) };
        contentPanel.Controls.Add(_subtitleLabel);
        contentPanel.Controls.Add(_valueLabel);
        contentPanel.Controls.Add(_titleLabel);

        Controls.Add(contentPanel);
        Controls.Add(_accentBar);
        Controls.Add(_iconBox);
        RefreshIcon();
    }
}
