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

    public Color AccentColor
    {
        get => _accentBar.BackColor;
        set
        {
            _accentBar.BackColor = value;
            _valueLabel.ForeColor = value;
        }
    }

    public StatCard()
    {
        Padding = new Padding(16, 14, 16, 12);
        Size = new Size(220, 110);

        _accentBar = new Panel { Dock = DockStyle.Left, Width = 4, BackColor = Theme.AccentBlue };

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

        var contentPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 0, 0) };
        contentPanel.Controls.Add(_subtitleLabel);
        contentPanel.Controls.Add(_valueLabel);
        contentPanel.Controls.Add(_titleLabel);

        Controls.Add(contentPanel);
        Controls.Add(_accentBar);
    }
}
