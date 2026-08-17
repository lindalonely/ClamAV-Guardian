using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ClamAVGuardian.Models;
using ClamAVGuardian.Resources;

namespace ClamAVGuardian.Forms;

/// <summary>
/// Shows a cancellable countdown before carrying out a system power action the user
/// pre-selected for "after scan completes" — gives them a window to back out before
/// the PC actually shuts down, restarts, or sleeps.
/// </summary>
public class PostScanActionDialog : Form
{
    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    private readonly PostScanAction _action;
    private readonly System.Windows.Forms.Timer _timer;
    private int _secondsLeft = 30;
    private readonly Label _lblCountdown;

    public PostScanActionDialog(PostScanAction action)
    {
        _action = action;

        Text = "ClamAV Guardian";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(380, 160);
        BackColor = Color.White;
        TopMost = true;

        var actionName = ActionLabel(action);

        var titleLabel = new Label
        {
            Text = $"Scan complete. Your PC will {actionName.ToLower()} soon.",
            Font = Theme.FontBodyBold,
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(20, 16, 20, 0),
        };

        _lblCountdown = new Label
        {
            Font = Theme.FontHeading,
            ForeColor = Theme.AccentBlue,
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 50,
            TextAlign = ContentAlignment.MiddleCenter,
        };

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(16) };
        var btnCancel = new Button { Text = "Cancel", Width = 100 };
        var btnNow = new Button { Text = $"{actionName} Now", Width = 140, Margin = new Padding(0, 0, 8, 0) };
        Theme.StyleSecondaryButton(btnCancel);
        Theme.StyleDangerButton(btnNow);
        btnCancel.Click += (_, _) => { _timer.Stop(); DialogResult = DialogResult.Cancel; Close(); };
        btnNow.Click += (_, _) => { _timer.Stop(); Execute(); DialogResult = DialogResult.OK; Close(); };
        buttonPanel.Controls.Add(btnCancel);
        buttonPanel.Controls.Add(btnNow);

        Controls.Add(_lblCountdown);
        Controls.Add(titleLabel);
        Controls.Add(buttonPanel);

        _timer.Tick += (_, _) =>
        {
            _secondsLeft--;
            UpdateCountdownLabel();
            if (_secondsLeft <= 0)
            {
                _timer.Stop();
                Execute();
                DialogResult = DialogResult.OK;
                Close();
            }
        };

        UpdateCountdownLabel();
        Shown += (_, _) => _timer.Start();
    }

    private void UpdateCountdownLabel() => _lblCountdown.Text = $"{_secondsLeft}s";

    private static string ActionLabel(PostScanAction action) => action switch
    {
        PostScanAction.Shutdown => "Shut down",
        PostScanAction.Restart => "Restart",
        PostScanAction.Sleep => "Sleep",
        _ => "Do nothing",
    };

    private void Execute()
    {
        try
        {
            switch (_action)
            {
                case PostScanAction.Shutdown:
                    System.Diagnostics.Process.Start("shutdown", "/s /t 0");
                    break;
                case PostScanAction.Restart:
                    System.Diagnostics.Process.Start("shutdown", "/r /t 0");
                    break;
                case PostScanAction.Sleep:
                    SetSuspendState(hibernate: false, forceCritical: true, disableWakeEvent: false);
                    break;
            }
        }
        catch (Exception ex)
        {
            Services.ClientLogger.Error($"Failed to execute post-scan action '{_action}'", ex);
        }
    }
}
