using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ClamAVGuardian.Client.Ipc;
using ClamAVGuardian.Ipc;
using ClamAVGuardian.Models;
using ClamAVGuardian.Services;
using ClamAVGuardian.Resources;
using ClamAVGuardian.Controls;

namespace ClamAVGuardian.Forms;

public class MainForm : Form
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);

    private const int SW_RESTORE = 9;
    private const int SW_SHOW = 5;
    private const byte VK_MENU = 0x12; // ALT
    private const uint KEYEVENTF_KEYUP = 0x0002;

    public const string ShowRequestEventName = "ClamAVGuardian_ShowRequest";

    private AppSettings _settings = new();
    private ClamAvInstallation? _install;
    private readonly GuardianServiceClient _client = new();
    private bool _isRealTimeEnabled;
    private NotifyIcon _trayIcon = null!;
    private bool _isExiting;
    private int _threatsThisSession;
    private EventWaitHandle? _showRequestEvent;
    private CancellationTokenSource? _showRequestListenerCts;

    // Navigation
    private Panel _contentHost = null!;
    private readonly List<SidebarNavButton> _navButtons = new();
    private readonly Dictionary<SidebarNavButton, Panel> _pagesByNav = new();

    // Dashboard
    private Label _lblClamAvStatus = null!;
    private Button _btnInstallClamAv = null!;
    private ProgressBar _clamAvInstallProgressBar = null!;
    private StatCard _cardProtection = null!;
    private StatCard _cardEngine = null!;
    private StatCard _cardDbVersion = null!;
    private StatCard _cardLastUpdate = null!;
    private StatCard _cardLastScan = null!;
    private StatCard _cardQuarantine = null!;
    private StatCard _cardThreatsSession = null!;
    private ListBox _lstRecentActivity = null!;
    private Label _lblActivityEmpty = null!;

    // Scan tab
    private RadioButton _rbQuick = null!, _rbFull = null!, _rbCustom = null!;
    private TextBox _txtCustomPath = null!;
    private Button _btnStartScan = null!, _btnCancelScan = null!;
    private ProgressBar _scanProgress = null!;
    private Label _lblScanStatus = null!;
    private ListView _lvScanResults = null!;
    private StatCard _scanCardFiles = null!, _scanCardInfected = null!, _scanCardErrors = null!, _scanCardDuration = null!;
    private ComboBox _cmbAfterScanAction = null!;
    private RoundedPanel _scanActionBanner = null!;
    private Label _lblScanBanner = null!;
    private Button _btnQuarantineAllInfected = null!;
    private int _liveFilesScanned, _liveInfected, _liveErrors, _liveQuarantined;
    private System.Windows.Forms.Timer? _scanDurationTimer;
    private DateTime _scanStartedAt;
    private bool _scanInProgress;

    // Real-Time tab
    private CheckBox _chkRealTimeEnabled = null!;
    private CheckBox _chkAutoQuarantine = null!;
    private ListBox _lstWatchedFolders = null!;
    private ListBox _lstRealtimeFeed = null!;
    private Label _lblRealtimeFeedEmpty = null!;
    private Label _lblEngine = null!;
    private Label _lblClamdStatus = null!;
    private Button _btnInstallClamd = null!;

    // Quarantine tab
    private ListView _lvQuarantine = null!;
    private Label _lblQuarantineEmpty = null!;

    // Updates tab
    private StatCard _cardServiceState = null!;
    private StatCard _cardDbVersionUpd = null!;
    private StatCard _cardLastUpdateUpd = null!;
    private NumericUpDown _numCheckInterval = null!;
    private TextBox _txtUpdateLog = null!;

    // Logs tab
    private TextBox _txtLogViewer = null!;
    private ComboBox _cmbLogSource = null!;

    // Settings tab
    private TextBox _txtClamAvPath = null!;
    private TextBox _txtQuarantinePath = null!;
    private CheckBox _chkStartWithWindows = null!;
    private CheckBox _chkStartMinimized = null!;
    private CheckBox _chkShowNotifications = null!;
    private ListBox _lstExclusions = null!;
    private Label _lblUpdateStatus = null!;
    private Label _lblUpdateVersions = null!;
    private ProgressBar _updateProgressBar = null!;

    public MainForm()
    {
        Text = "ClamAV Guardian";
        Width = 1080;
        Height = 720;
        StartPosition = FormStartPosition.CenterScreen;
        Icon = IconFactory.CreateAppIcon();
        MinimumSize = new Size(960, 600);
        BackColor = Theme.ContentBg;
        Font = Theme.FontBody;

        BuildTrayIcon();
        BuildUi();
        StartShowRequestListener();

        Load += async (_, _) => await InitializeAsync();
        FormClosing += MainForm_FormClosing;
    }

    /// <summary>
    /// A second launch attempt (e.g. clicking the desktop/Start Menu icon while already
    /// auto-started and hidden in the tray) signals this event instead of showing a "already
    /// running" dialog; we just bring the existing window forward, like any normal tray app.
    /// </summary>
    private void StartShowRequestListener()
    {
        _showRequestEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowRequestEventName);
        _showRequestListenerCts = new CancellationTokenSource();
        var token = _showRequestListenerCts.Token;

        _ = Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                if (_showRequestEvent.WaitOne(TimeSpan.FromSeconds(1)))
                {
                    SafeInvoke(ShowAndFocus);
                }
            }
        }, token);
    }

    private void SafeInvoke(Action action)
    {
        if (IsDisposed) return;
        try { BeginInvoke(action); }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    private void BuildTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Dashboard", IconSet.Render(AppIcon.Home, 16, Theme.TextPrimary), (_, _) => ShowAndFocus());
        menu.Items.Add("Scan Now", IconSet.Render(AppIcon.Search, 16, Theme.TextPrimary), async (_, _) => { ShowAndFocus(); await StartScanAsync(); });
        menu.Items.Add("Update Now", IconSet.Render(AppIcon.Refresh, 16, Theme.TextPrimary), async (_, _) => await RunUpdateNowAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Pause Real-Time Protection", IconSet.Render(AppIcon.Shield, 16, Theme.TextPrimary), async (_, _) => await ToggleRealTimeAsync(false));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", IconSet.Render(AppIcon.Stop, 16, Theme.TextPrimary), (_, _) => { _isExiting = true; Close(); });

        _trayIcon = new NotifyIcon
        {
            Icon = IconFactory.CreateTrayIcon(TrayIconState.Disabled),
            Visible = true,
            Text = "ClamAV Guardian",
            ContextMenuStrip = menu,
        };
        _trayIcon.DoubleClick += (_, _) => ShowAndFocus();
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowAndFocus();
        };
    }

    private void ShowAndFocus()
    {
        try
        {
            ShowInTaskbar = true;
            if (!Visible)
            {
                Show();
            }
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }

            var hWnd = Handle;
            ShowWindow(hWnd, SW_RESTORE);
            ShowWindow(hWnd, SW_SHOW);

            keybd_event(VK_MENU, 0, 0, IntPtr.Zero);
            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, IntPtr.Zero);

            var foregroundWindow = GetForegroundWindow();
            var foregroundThreadId = GetWindowThreadProcessId(foregroundWindow, IntPtr.Zero);
            var currentThreadId = GetCurrentThreadId();

            if (foregroundWindow != hWnd && foregroundThreadId != currentThreadId && foregroundThreadId != 0)
            {
                AttachThreadInput(currentThreadId, foregroundThreadId, true);
                try { SetForegroundWindow(hWnd); }
                finally { AttachThreadInput(currentThreadId, foregroundThreadId, false); }
            }
            else
            {
                SetForegroundWindow(hWnd);
            }

            BringToFront();
            Activate();
            Focus();
        }
        catch (Exception ex)
        {
            ClientLogger.Error("Failed to show/focus main window from tray", ex);
        }
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_isExiting)
        {
            e.Cancel = true;
            Hide();
            _trayIcon.ShowBalloonTip(1500, "ClamAV Guardian", "Still running in the background.", ToolTipIcon.Info);
            return;
        }

        _showRequestListenerCts?.Cancel();
        _showRequestEvent?.Dispose();
        _client.Dispose();
        _trayIcon.Visible = false;
    }

    private async Task InitializeAsync()
    {
        ClientLogger.LineWritten += line => SafeInvoke(() => AppendActivity(line));

        _client.ScanItemScanned += item => SafeInvoke(() => OnScanItem(item));
        _client.RealTimeThreatDetected += (item, quarantined) => SafeInvoke(() => OnThreatDetected(item, quarantined));
        _client.RealTimeFileScanned += path => SafeInvoke(() => AppendRealtimeFeed($"Scanned: {path}"));
        _client.RealTimeEngineStatus += msg => SafeInvoke(() => OnRealTimeEngineStatus(msg));
        _client.LogLine += line => SafeInvoke(() => AppendActivity(line));
        _client.UpdateLogLine += line => SafeInvoke(() => _txtUpdateLog.AppendText(line + Environment.NewLine));
        _client.ConnectionStateChanged += connected => SafeInvoke(() => OnConnectionStateChanged(connected));
        _client.ClamAvInstallProgress += progress => SafeInvoke(() => OnClamAvInstallProgress(progress));
        _client.AppUpdateProgress += progress => SafeInvoke(() => OnAppUpdateProgress(progress));

        _lblClamAvStatus.Text = "Connecting to ClamAV Guardian service...";
        _lblClamAvStatus.ForeColor = Theme.TextSecondary;
        _client.Start();

        // Wait briefly for the first connection so the UI doesn't flash "not configured"
        // before we've actually had a chance to ask the service.
        for (var i = 0; i < 50 && !_client.IsConnected; i++)
        {
            await Task.Delay(100);
        }

        if (!_client.IsConnected)
        {
            _lblClamAvStatus.Text = "Can't reach the ClamAV Guardian service. Is it installed and running?";
            _lblClamAvStatus.ForeColor = Theme.AccentRed;
            SetProtectionCard("Service Unavailable", Theme.AccentRed);
            return;
        }

        await RefreshFromServiceAsync();
    }

    private async void OnConnectionStateChanged(bool connected)
    {
        if (connected)
        {
            await RefreshFromServiceAsync();
        }
        else
        {
            _lblClamAvStatus.Text = "Lost connection to the ClamAV Guardian service. Reconnecting...";
            _lblClamAvStatus.ForeColor = Theme.AccentAmber;
            SetProtectionCard("Reconnecting...", Theme.AccentAmber);
        }
    }

    private async Task RefreshFromServiceAsync()
    {
        _settings = await _client.Service.GetSettingsAsync();
        await ApplySecureDefaultsIfNeededAsync();

        RefreshExclusionsList();
        RefreshWatchedFoldersList();
        await RefreshQuarantineListAsync();
        _chkAutoQuarantine.Checked = _settings.AutoQuarantineOnDetection;
        _chkShowNotifications.Checked = _settings.ShowNotifications;
        _chkStartMinimized.Checked = _settings.StartMinimized;
        _chkStartWithWindows.Checked = _settings.StartWithWindows;
        _cmbAfterScanAction.SelectedIndex = (int)_settings.AfterScanAction;
        _txtQuarantinePath.Text = _settings.QuarantinePath;
        await EnsureDesktopShortcutAsync();

        if (_settings.StartMinimized && WindowState != FormWindowState.Minimized)
        {
            Hide();
        }

        _install = await _client.Service.GetCurrentInstallationAsync();
        _txtClamAvPath.Text = _install?.InstallDir ?? _settings.ClamAvInstallPath;

        if (_install == null)
        {
            _lblClamAvStatus.Text = "ClamAV not found. Install it automatically, or set the path manually in Settings.";
            _lblClamAvStatus.ForeColor = Theme.AccentAmber;
            _btnInstallClamAv.Visible = true;
            SetProtectionCard("Not Configured", Theme.AccentAmber);
            return;
        }

        _btnInstallClamAv.Visible = false;
        _lblClamAvStatus.Text = $"ClamAV found at {_install.InstallDir}";
        _lblClamAvStatus.ForeColor = Theme.AccentGreen;

        await RefreshUpdateStatusAsync();
        await RefreshClamdStatusAsync();

        _isRealTimeEnabled = _settings.RealTimeProtectionEnabled || await _client.Service.IsRealTimeProtectionRunningAsync();
        if (_isRealTimeEnabled)
        {
            SetProtectionCard("Protected", Theme.AccentGreen);
            _chkRealTimeEnabled.Checked = true;
            _trayIcon.Icon = IconFactory.CreateTrayIcon(TrayIconState.Protected);
            var engine = await _client.Service.GetRealTimeEngineDescriptionAsync();
            OnRealTimeEngineStatus(engine);
        }
        else
        {
            SetProtectionCard("Not Protected", Theme.AccentRed);
        }
    }

    /// <summary>
    /// One-time upgrade of an existing settings.json (or the natural state of a brand-new
    /// one) to protection-on-by-default: real-time protection, auto-quarantine, notifications,
    /// start with Windows, and start minimized. Runs once per install — after this, whatever
    /// the user chooses in Settings sticks, this never re-forces anything. Also seeds a
    /// starter watch list (Desktop/Downloads/Documents) since enabling real-time protection
    /// with zero watched folders configured would be a no-op.
    /// </summary>
    private async Task ApplySecureDefaultsIfNeededAsync()
    {
        if (_settings.SecureDefaultsApplied) return;

        _settings.StartWithWindows = true;
        _settings.StartMinimized = true;
        _settings.RealTimeProtectionEnabled = true;
        _settings.AutoQuarantineOnDetection = true;
        _settings.ShowNotifications = true;

        if (_settings.RealTimeWatchedFolders.Count == 0)
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var candidates = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Path.Combine(userProfile, "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            };

            foreach (var folder in candidates)
            {
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder) && !_settings.RealTimeWatchedFolders.Contains(folder))
                {
                    _settings.RealTimeWatchedFolders.Add(folder);
                }
            }
        }

        _settings.SecureDefaultsApplied = true;
        await SaveSettingsAsync();

        // SaveSettingsAsync only persists the flag; if the service was already running before
        // this upgrade (an existing install, not a fresh one), its real-time watcher needs an
        // explicit start command too — it won't pick up the new setting on its own until the
        // next service restart otherwise. No-ops safely if ClamAV isn't located yet.
        await ToggleRealTimeAsync(true);

        ClientLogger.Info("Applied secure-by-default settings for the first time (real-time protection, auto-quarantine, start with Windows, start minimized, notifications).");
    }

    #region UI construction

    private void BuildUi()
    {
        var sidebar = BuildSidebar();
        _contentHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.ContentBg };

        Controls.Add(_contentHost);
        Controls.Add(sidebar);

        AddPage("Dashboard", AppIcon.Home, BuildDashboardPage(), selectFirst: true);
        AddPage("Scan", AppIcon.Search, BuildScanPage());
        AddPage("Real-Time Protection", AppIcon.Shield, BuildRealTimePage());
        AddPage("Quarantine", AppIcon.Lock, BuildQuarantinePage());
        AddPage("Updates", AppIcon.Refresh, BuildUpdatesPage());
        AddPage("Logs", AppIcon.Document, BuildLogsPage());
        AddPage("Settings", AppIcon.Gear, BuildSettingsPage());
    }

    private Panel BuildSidebar()
    {
        var sidebar = new Panel { Dock = DockStyle.Left, Width = 220, BackColor = Theme.SidebarBg };

        var titlePanel = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Theme.SidebarBg };
        var titleLabel = new Label
        {
            Text = "ClamAV Guardian",
            Dock = DockStyle.Fill,
            ForeColor = Theme.SidebarTextActive,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI Semibold", 12f),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(20, 0, 0, 0),
        };
        titlePanel.Controls.Add(titleLabel);

        var navContainer = new Panel { Dock = DockStyle.Top, Height = 42 * 7, BackColor = Theme.SidebarBg };

        var footerPanel = new Panel { Dock = DockStyle.Bottom, Height = 28, BackColor = Theme.SidebarBg };
        var footerLabel = new Label
        {
            Text = "Powered by 7iNDA",
            Dock = DockStyle.Fill,
            ForeColor = Theme.SidebarText,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 8f),
            TextAlign = ContentAlignment.MiddleCenter,
        };
        footerPanel.Controls.Add(footerLabel);

        sidebar.Controls.Add(navContainer);
        sidebar.Controls.Add(footerPanel);
        sidebar.Controls.Add(titlePanel);

        sidebar.Tag = navContainer;
        return sidebar;
    }

    private void AddPage(string title, AppIcon icon, Panel page, bool selectFirst = false)
    {
        page.Dock = DockStyle.Fill;
        page.Visible = false;
        _contentHost.Controls.Add(page);

        var sidebar = Controls.OfType<Panel>().First(p => p.BackColor == Theme.SidebarBg && p.Dock == DockStyle.Left);
        var navContainer = (Panel)sidebar.Tag!;

        var navButton = new SidebarNavButton(title, icon);
        navButton.Activated += (_, _) => SelectPage(navButton);
        navContainer.Controls.Add(navButton);
        // For Dock=Top siblings, the LOWEST z-order index sits closest to the docked edge,
        // so each newly-added button must go to index 0 to land below (not above) the ones
        // already there — the reverse of what "append at the end" intuitively suggests.
        navContainer.Controls.SetChildIndex(navButton, 0);

        _navButtons.Add(navButton);
        _pagesByNav[navButton] = page;

        if (selectFirst)
        {
            page.Visible = true;
            navButton.Selected = true;
        }
    }

    private void SelectPage(SidebarNavButton selected)
    {
        foreach (var nav in _navButtons)
        {
            var isSelected = nav == selected;
            nav.Selected = isSelected;
            _pagesByNav[nav].Visible = isSelected;
        }
    }

    private static Label PageHeading(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = Theme.FontHeading,
        ForeColor = Theme.TextPrimary,
        Margin = new Padding(0, 0, 0, 4),
    };

    private static Label SectionHeading(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = Theme.FontSubheading,
        ForeColor = Theme.TextPrimary,
        Margin = new Padding(0, 12, 0, 6),
    };

    private Panel BuildDashboardPage()
    {
        var page = new Panel { Padding = new Padding(28), BackColor = Theme.ContentBg };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(PageHeading("Dashboard"));

        var clamAvStatusRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
        _lblClamAvStatus = new Label
        {
            Text = "Locating ClamAV...",
            AutoSize = true,
            Font = Theme.FontBody,
            ForeColor = Theme.TextSecondary,
            Margin = new Padding(0, 4, 12, 0),
        };
        _btnInstallClamAv = new Button { Text = "Install ClamAV Automatically", Visible = false, AutoSize = true };
        Theme.StylePrimaryButton(_btnInstallClamAv);
        Theme.SetIcon(_btnInstallClamAv, AppIcon.Download);
        _btnInstallClamAv.Click += async (_, _) => await InstallClamAvAsync();
        clamAvStatusRow.Controls.Add(_lblClamAvStatus);
        clamAvStatusRow.Controls.Add(_btnInstallClamAv);
        root.Controls.Add(clamAvStatusRow);

        _clamAvInstallProgressBar = new ProgressBar
        {
            Dock = DockStyle.Top,
            Height = 6,
            Style = ProgressBarStyle.Continuous,
            Visible = false,
            Margin = new Padding(0, 0, 0, 12),
        };
        root.Controls.Add(_clamAvInstallProgressBar);

        var statsFlow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        _cardProtection = NewCard("Protection Status", "Checking...", Theme.AccentGray, AppIcon.Shield);
        _cardEngine = NewCard("Real-Time Engine", "Inactive", Theme.AccentGray, AppIcon.Search, "Not running");
        _cardDbVersion = NewCard("Database Version", "Unknown", Theme.AccentBlue, AppIcon.Database);
        _cardLastUpdate = NewCard("Last Update", "Never", Theme.AccentBlue, AppIcon.Clock);
        _cardLastScan = NewCard("Last Scan", "Never run", Theme.AccentBlue, AppIcon.Search);
        _cardQuarantine = NewCard("Quarantined Items", "0", Theme.AccentAmber, AppIcon.Lock);
        _cardThreatsSession = NewCard("Threats This Session", "0", Theme.AccentGreen, AppIcon.Warning, "No threats detected");

        foreach (var card in new[] { _cardProtection, _cardEngine, _cardDbVersion, _cardLastUpdate, _cardLastScan, _cardQuarantine, _cardThreatsSession })
        {
            card.Margin = new Padding(0, 0, 16, 16);
            statsFlow.Controls.Add(card);
        }
        root.Controls.Add(statsFlow);

        var actionsFlow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        var btnScanNow = new Button { Text = "Scan Now", AutoSize = true };
        var btnUpdateNow = new Button { Text = "Update Now", AutoSize = true, Margin = new Padding(12, 0, 0, 0) };
        Theme.StylePrimaryButton(btnScanNow);
        Theme.StyleSecondaryButton(btnUpdateNow);
        Theme.SetIcon(btnScanNow, AppIcon.Search);
        Theme.SetIcon(btnUpdateNow, AppIcon.Refresh);
        btnScanNow.Click += async (_, _) => { SelectPage(_navButtons[1]); await StartScanAsync(); };
        btnUpdateNow.Click += async (_, _) => await RunUpdateNowAsync();
        actionsFlow.Controls.Add(btnScanNow);
        actionsFlow.Controls.Add(btnUpdateNow);
        root.Controls.Add(actionsFlow);

        root.Controls.Add(SectionHeading("Recent Activity"));

        var activityCard = new RoundedPanel { Dock = DockStyle.Fill, Height = 260, Padding = new Padding(4) };
        _lstRecentActivity = new ListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            Font = Theme.FontBody,
            ForeColor = Theme.TextPrimary,
            BackColor = Theme.CardBg,
        };
        _lblActivityEmpty = CreateEmptyStateLabel("No recent activity yet. Run a scan or update to see it here.");
        activityCard.Controls.Add(_lstRecentActivity);
        activityCard.Controls.Add(_lblActivityEmpty);
        root.Controls.Add(activityCard);
        root.SetRow(activityCard, 5);

        page.Controls.Add(root);
        return page;
    }

    private static Label CreateEmptyStateLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = Theme.FontBody,
        ForeColor = Theme.TextSecondary,
        BackColor = Color.Transparent,
    };

    /// <summary>
    /// WinForms ListView columns don't natively support "fill remaining space" — without
    /// this, resizing the window just leaves a growing dead gray gap instead of the table
    /// actually using the extra width. Column 0 absorbs whatever's left after the others.
    /// </summary>
    private static void MakeFirstColumnFill(ListView listView, int minWidth = 200)
    {
        void Resize()
        {
            if (listView.Columns.Count == 0 || listView.ClientSize.Width == 0) return;
            var otherWidths = 0;
            for (var i = 1; i < listView.Columns.Count; i++) otherWidths += listView.Columns[i].Width;
            var scrollbarAllowance = listView.Items.Count > 0 ? SystemInformation.VerticalScrollBarWidth : 0;
            listView.Columns[0].Width = Math.Max(minWidth, listView.ClientSize.Width - otherWidths - scrollbarAllowance - 2);
        }

        listView.Resize += (_, _) => Resize();
        listView.HandleCreated += (_, _) => Resize();
    }

    private static StatCard NewCard(string title, string value, Color accent, AppIcon icon, string? subtitle = null)
    {
        var card = new StatCard { TitleText = title, ValueText = value, AccentColor = accent, Icon = icon };
        if (subtitle != null) card.SubtitleText = subtitle;
        return card;
    }

    private void SetProtectionCard(string status, Color color)
    {
        _cardProtection.ValueText = status;
        _cardProtection.AccentColor = color;
    }

    private Panel BuildScanPage()
    {
        var page = new Panel { Padding = new Padding(28), BackColor = Theme.ContentBg };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(PageHeading("Scan"));

        var optionsPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 8, 0, 8) };
        _rbQuick = new RadioButton { Text = "Quick Scan", Checked = true, AutoSize = true, Font = Theme.FontBody };
        _rbFull = new RadioButton { Text = "Full Scan", AutoSize = true, Margin = new Padding(16, 0, 0, 0), Font = Theme.FontBody };
        _rbCustom = new RadioButton { Text = "Custom Folder:", AutoSize = true, Margin = new Padding(16, 0, 0, 0), Font = Theme.FontBody };
        _txtCustomPath = new TextBox { Width = 320, Margin = new Padding(6, 2, 0, 0) };
        Theme.StyleTextBox(_txtCustomPath);
        var btnBrowse = new Button { Text = "Browse...", AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
        Theme.StyleSecondaryButton(btnBrowse);
        Theme.SetIcon(btnBrowse, AppIcon.Folder);
        btnBrowse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog();
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _txtCustomPath.Text = dlg.SelectedPath;
                _rbCustom.Checked = true;
            }
        };
        optionsPanel.Controls.Add(_rbQuick);
        optionsPanel.Controls.Add(_rbFull);
        optionsPanel.Controls.Add(_rbCustom);
        optionsPanel.Controls.Add(_txtCustomPath);
        optionsPanel.Controls.Add(btnBrowse);

        var actionPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 0, 0, 8) };
        _btnStartScan = new Button { Text = "Start Scan", AutoSize = true };
        _btnCancelScan = new Button { Text = "Cancel", AutoSize = true, Enabled = false, Margin = new Padding(8, 0, 0, 0) };
        Theme.StylePrimaryButton(_btnStartScan);
        Theme.StyleSecondaryButton(_btnCancelScan);
        Theme.SetIcon(_btnStartScan, AppIcon.Play);
        Theme.SetIcon(_btnCancelScan, AppIcon.Stop);
        _btnStartScan.Click += async (_, _) => await StartScanAsync();
        _btnCancelScan.Click += async (_, _) =>
        {
            if (_client.IsConnected) await _client.Service.CancelScanAsync();
        };
        actionPanel.Controls.Add(_btnStartScan);
        actionPanel.Controls.Add(_btnCancelScan);
        actionPanel.Controls.Add(new Label
        {
            Text = "After scan completes:",
            AutoSize = true,
            Font = Theme.FontBody,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(20, 8, 4, 0),
        });
        _cmbAfterScanAction = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140, Font = Theme.FontBody, Margin = new Padding(0, 4, 0, 0) };
        _cmbAfterScanAction.Items.AddRange(new object[] { "Do nothing", "Shut down PC", "Restart PC", "Sleep PC" });
        _cmbAfterScanAction.SelectedIndexChanged += async (_, _) =>
        {
            _settings.AfterScanAction = (PostScanAction)_cmbAfterScanAction.SelectedIndex;
            await SaveSettingsAsync();
        };
        actionPanel.Controls.Add(_cmbAfterScanAction);

        var statsFlow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 0, 0, 8) };
        _scanCardFiles = NewCard("Files Scanned", "0", Theme.AccentBlue, AppIcon.Document);
        _scanCardInfected = NewCard("Infected", "0", Theme.AccentGreen, AppIcon.Warning);
        _scanCardErrors = NewCard("Errors", "0", Theme.AccentGray, AppIcon.Warning);
        _scanCardDuration = NewCard("Duration", "00:00", Theme.AccentBlue, AppIcon.Clock);
        foreach (var card in new[] { _scanCardFiles, _scanCardInfected, _scanCardErrors, _scanCardDuration })
        {
            card.Size = new Size(180, 90);
            card.Margin = new Padding(0, 0, 12, 0);
            statsFlow.Controls.Add(card);
        }

        var statusPanel = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true };
        _scanProgress = new ProgressBar { Dock = DockStyle.Top, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 0, Height = 6 };
        _lblScanStatus = new Label { Text = "Idle.", AutoSize = true, Font = Theme.FontBody, ForeColor = Theme.TextSecondary, Margin = new Padding(0, 6, 0, 6) };
        statusPanel.Controls.Add(_scanProgress);
        statusPanel.Controls.Add(_lblScanStatus);

        _scanActionBanner = new RoundedPanel { Dock = DockStyle.Top, Height = 56, Visible = false, Padding = new Padding(16, 0, 16, 0), Margin = new Padding(0, 0, 0, 8) };
        var bannerLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        bannerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bannerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _lblScanBanner = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = Theme.FontBodyBold, BackColor = Color.Transparent };
        _btnQuarantineAllInfected = new Button { Text = "Quarantine All Infected", Anchor = AnchorStyles.Right, AutoSize = true };
        Theme.StyleDangerButton(_btnQuarantineAllInfected);
        Theme.SetIcon(_btnQuarantineAllInfected, AppIcon.Lock);
        _btnQuarantineAllInfected.Click += async (_, _) => await QuarantineAllInfectedScanResultsAsync();
        bannerLayout.Controls.Add(_lblScanBanner, 0, 0);
        bannerLayout.Controls.Add(_btnQuarantineAllInfected, 1, 0);
        _scanActionBanner.Controls.Add(bannerLayout);

        var resultsCard = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(4) };
        _lvScanResults = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = false };
        Theme.StyleListView(_lvScanResults);
        _lvScanResults.Columns.Add("File", 500);
        _lvScanResults.Columns.Add("Status", 120);
        _lvScanResults.Columns.Add("Threat", 240);
        _lvScanResults.ContextMenuStrip = BuildScanResultsContextMenu();
        MakeFirstColumnFill(_lvScanResults);
        resultsCard.Controls.Add(_lvScanResults);

        root.Controls.Add(optionsPanel);
        root.Controls.Add(actionPanel);
        root.Controls.Add(statsFlow);
        root.Controls.Add(statusPanel);
        root.Controls.Add(_scanActionBanner);
        root.Controls.Add(resultsCard);
        root.SetRow(resultsCard, 6);
        page.Controls.Add(root);
        return page;
    }

    private ContextMenuStrip BuildScanResultsContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Quarantine", null, async (_, _) => await QuarantineSelectedScanResultAsync());
        menu.Items.Add("Open Containing Folder", null, (_, _) => OpenSelectedScanResultFolder());
        return menu;
    }

    private Panel BuildRealTimePage()
    {
        var page = new Panel { Padding = new Padding(28), BackColor = Theme.ContentBg };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var heading = PageHeading("Real-Time Protection");
        root.Controls.Add(heading, 0, 0);
        root.SetColumnSpan(heading, 2);

        var leftPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = false };
        _chkRealTimeEnabled = new CheckBox { Text = "Enable Real-Time Protection", AutoSize = true, Font = Theme.FontBodyBold, Margin = new Padding(0, 8, 0, 0) };
        _chkRealTimeEnabled.CheckedChanged += async (_, _) => await ToggleRealTimeAsync(_chkRealTimeEnabled.Checked);
        _chkAutoQuarantine = new CheckBox { Text = "Automatically quarantine detected threats", AutoSize = true, Font = Theme.FontBody, Margin = new Padding(0, 8, 0, 8) };
        _chkAutoQuarantine.CheckedChanged += async (_, _) =>
        {
            _settings.AutoQuarantineOnDetection = _chkAutoQuarantine.Checked;
            await SaveSettingsAsync();
        };
        _lblEngine = new Label { Text = "Engine: inactive", AutoSize = true, Font = Theme.FontBody, ForeColor = Theme.TextSecondary, Margin = new Padding(0, 0, 0, 4) };

        var clamdRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        _lblClamdStatus = new Label
        {
            Text = "clamd (fast scanning engine): checking...",
            AutoSize = true,
            Font = Theme.FontBody,
            ForeColor = Theme.TextSecondary,
            Margin = new Padding(0, 6, 12, 0),
        };
        _btnInstallClamd = new Button { Text = "Enable Fast Scanning", AutoSize = true, Visible = false };
        Theme.StyleSecondaryButton(_btnInstallClamd);
        Theme.SetIcon(_btnInstallClamd, AppIcon.Download);
        _btnInstallClamd.Click += async (_, _) => await InstallClamdAsync();
        clamdRow.Controls.Add(_lblClamdStatus);
        clamdRow.Controls.Add(_btnInstallClamd);

        var folderLabel = SectionHeading("Watched folders");
        var foldersCard = new RoundedPanel { Height = 170, Dock = DockStyle.Top, Padding = new Padding(4) };
        _lstWatchedFolders = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = Theme.FontBody };
        foldersCard.Controls.Add(_lstWatchedFolders);

        var folderButtons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        var btnAddFolder = new Button { Text = "Add Folder...", AutoSize = true };
        var btnRemoveFolder = new Button { Text = "Remove Selected", AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
        Theme.StyleSecondaryButton(btnAddFolder);
        Theme.StyleSecondaryButton(btnRemoveFolder);
        Theme.SetIcon(btnAddFolder, AppIcon.Plus);
        Theme.SetIcon(btnRemoveFolder, AppIcon.Trash);
        btnAddFolder.Click += async (_, _) =>
        {
            using var dlg = new FolderBrowserDialog();
            if (dlg.ShowDialog() == DialogResult.OK && !_settings.RealTimeWatchedFolders.Contains(dlg.SelectedPath))
            {
                _settings.RealTimeWatchedFolders.Add(dlg.SelectedPath);
                await SaveSettingsAsync();
                RefreshWatchedFoldersList();
                if (_chkRealTimeEnabled.Checked) await ToggleRealTimeAsync(true);
            }
        };
        btnRemoveFolder.Click += async (_, _) =>
        {
            if (_lstWatchedFolders.SelectedItem is string folder)
            {
                _settings.RealTimeWatchedFolders.Remove(folder);
                await SaveSettingsAsync();
                RefreshWatchedFoldersList();
                if (_chkRealTimeEnabled.Checked) await ToggleRealTimeAsync(true);
            }
        };
        folderButtons.Controls.Add(btnAddFolder);
        folderButtons.Controls.Add(btnRemoveFolder);

        leftPanel.Controls.Add(_chkRealTimeEnabled);
        leftPanel.Controls.Add(_chkAutoQuarantine);
        leftPanel.Controls.Add(_lblEngine);
        leftPanel.Controls.Add(clamdRow);
        leftPanel.Controls.Add(folderLabel);
        leftPanel.Controls.Add(foldersCard);
        leftPanel.Controls.Add(folderButtons);

        var rightPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        rightPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rightPanel.Controls.Add(SectionHeading("Live activity"));
        var feedCard = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(4) };
        _lstRealtimeFeed = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = Theme.FontBody };
        _lblRealtimeFeedEmpty = CreateEmptyStateLabel("No activity yet. Enable protection above to start watching your folders.");
        feedCard.Controls.Add(_lstRealtimeFeed);
        feedCard.Controls.Add(_lblRealtimeFeedEmpty);
        rightPanel.Controls.Add(feedCard);
        rightPanel.SetRow(feedCard, 1);

        root.Controls.Add(leftPanel, 0, 1);
        root.Controls.Add(rightPanel, 1, 1);

        page.Controls.Add(root);
        return page;
    }

    private Panel BuildQuarantinePage()
    {
        var page = new Panel { Padding = new Padding(28), BackColor = Theme.ContentBg };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(PageHeading("Quarantine"));

        var buttonRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 8, 0, 8) };
        var btnRestore = new Button { Text = "Restore", AutoSize = true };
        var btnDelete = new Button { Text = "Delete Permanently", AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
        var btnDeleteAll = new Button { Text = "Delete All", AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
        var btnRefresh = new Button { Text = "Refresh", AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
        Theme.StyleSecondaryButton(btnRestore);
        Theme.StyleDangerButton(btnDelete);
        Theme.StyleDangerButton(btnDeleteAll);
        Theme.StyleSecondaryButton(btnRefresh);
        Theme.SetIcon(btnRestore, AppIcon.Undo);
        Theme.SetIcon(btnDelete, AppIcon.Trash);
        Theme.SetIcon(btnDeleteAll, AppIcon.Trash);
        Theme.SetIcon(btnRefresh, AppIcon.Refresh);

        btnRestore.Click += async (_, _) =>
        {
            if (GetSelectedQuarantineId() is string id && _client.IsConnected && await _client.Service.RestoreQuarantineEntryAsync(id))
                await RefreshQuarantineListAsync();
        };
        btnDelete.Click += async (_, _) =>
        {
            if (GetSelectedQuarantineId() is string id &&
                MessageBox.Show("Permanently delete this file? This cannot be undone.", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes &&
                _client.IsConnected && await _client.Service.DeleteQuarantineEntryAsync(id))
                await RefreshQuarantineListAsync();
        };
        btnDeleteAll.Click += async (_, _) =>
        {
            if (MessageBox.Show("Permanently delete ALL quarantined files? This cannot be undone.", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes && _client.IsConnected)
            {
                await _client.Service.DeleteAllQuarantineEntriesAsync();
                await RefreshQuarantineListAsync();
            }
        };
        btnRefresh.Click += async (_, _) => await RefreshQuarantineListAsync();

        buttonRow.Controls.Add(btnRestore);
        buttonRow.Controls.Add(btnDelete);
        buttonRow.Controls.Add(btnDeleteAll);
        buttonRow.Controls.Add(btnRefresh);

        var listCard = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(4) };
        _lvQuarantine = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = false };
        Theme.StyleListView(_lvQuarantine);
        _lvQuarantine.Columns.Add("Original Path", 380);
        _lvQuarantine.Columns.Add("Threat", 200);
        _lvQuarantine.Columns.Add("Quarantined At", 160);
        _lvQuarantine.Columns.Add("Size (bytes)", 100);
        MakeFirstColumnFill(_lvQuarantine);
        _lblQuarantineEmpty = CreateEmptyStateLabel("No quarantined files. Threats found during scans or real-time protection will show up here.");
        listCard.Controls.Add(_lvQuarantine);
        listCard.Controls.Add(_lblQuarantineEmpty);

        root.Controls.Add(buttonRow);
        root.Controls.Add(listCard);
        root.SetRow(listCard, 2);
        page.Controls.Add(root);
        return page;
    }

    private Panel BuildUpdatesPage()
    {
        var page = new Panel { Padding = new Padding(28), BackColor = Theme.ContentBg };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // heading
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // stat cards
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // interval panel
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // button row
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // "log" section heading
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // log (fills remaining space)

        root.Controls.Add(PageHeading("Updates"));

        var statsFlow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 8, 0, 8) };
        _cardServiceState = NewCard("FreshClam Service", "Unknown", Theme.AccentGray, AppIcon.Refresh);
        _cardDbVersionUpd = NewCard("Database Version", "Unknown", Theme.AccentBlue, AppIcon.Database);
        _cardLastUpdateUpd = NewCard("Last Update", "Never", Theme.AccentBlue, AppIcon.Clock);
        foreach (var card in new[] { _cardServiceState, _cardDbVersionUpd, _cardLastUpdateUpd })
        {
            card.Size = new Size(250, 110);
            card.Margin = new Padding(0, 0, 16, 12);
            statsFlow.Controls.Add(card);
        }

        var intervalPanel = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        intervalPanel.Controls.Add(new Label { Text = "Check every (hours):", AutoSize = true, Font = Theme.FontBody, Margin = new Padding(0, 6, 6, 0) });
        _numCheckInterval = new NumericUpDown { Minimum = 1, Maximum = 24, Value = 2, Width = 60 };
        var btnSaveInterval = new Button { Text = "Save Interval", AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
        Theme.StyleSecondaryButton(btnSaveInterval);
        Theme.SetIcon(btnSaveInterval, AppIcon.Save);
        btnSaveInterval.Click += async (_, _) =>
        {
            if (!_client.IsConnected) return;
            await _client.Service.SetUpdateCheckIntervalAsync((int)_numCheckInterval.Value);
            MessageBox.Show("Update interval saved.", "ClamAV Guardian");
        };
        intervalPanel.Controls.Add(_numCheckInterval);
        intervalPanel.Controls.Add(btnSaveInterval);

        var buttonRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        var btnUpdateNow = new Button { Text = "Update Now", AutoSize = true };
        var btnStartService = new Button { Text = "Start FreshClam Service", AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
        var btnStopService = new Button { Text = "Stop FreshClam Service", AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
        Theme.StylePrimaryButton(btnUpdateNow);
        Theme.StyleSecondaryButton(btnStartService);
        Theme.StyleSecondaryButton(btnStopService);
        Theme.SetIcon(btnUpdateNow, AppIcon.Refresh);
        Theme.SetIcon(btnStartService, AppIcon.Play);
        Theme.SetIcon(btnStopService, AppIcon.Stop);
        btnUpdateNow.Click += async (_, _) => await RunUpdateNowAsync();
        btnStartService.Click += async (_, _) => { if (_client.IsConnected) await _client.Service.StartFreshClamServiceAsync(); await RefreshUpdateStatusAsync(); };
        btnStopService.Click += async (_, _) => { if (_client.IsConnected) await _client.Service.StopFreshClamServiceAsync(); await RefreshUpdateStatusAsync(); };
        buttonRow.Controls.Add(btnUpdateNow);
        buttonRow.Controls.Add(btnStartService);
        buttonRow.Controls.Add(btnStopService);

        var logHeading = SectionHeading("Update Log");
        logHeading.Margin = new Padding(0, 4, 0, 6);

        var logCard = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        _txtUpdateLog = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = Theme.FontMono };
        logCard.Controls.Add(_txtUpdateLog);

        root.Controls.Add(statsFlow);
        root.Controls.Add(intervalPanel);
        root.Controls.Add(buttonRow);
        root.Controls.Add(logHeading);
        root.Controls.Add(logCard);
        root.SetRow(logCard, 5);

        page.Controls.Add(root);
        return page;
    }

    private Panel BuildLogsPage()
    {
        var page = new Panel { Padding = new Padding(28), BackColor = Theme.ContentBg };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(PageHeading("Logs"));

        var topRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 8, 0, 8) };
        _cmbLogSource = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Font = Theme.FontBody };
        _cmbLogSource.Items.AddRange(new object[] { "Service Log", "FreshClam Log" });
        _cmbLogSource.SelectedIndex = 0;
        var btnRefreshLog = new Button { Text = "Refresh", AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
        Theme.StyleSecondaryButton(btnRefreshLog);
        Theme.SetIcon(btnRefreshLog, AppIcon.Refresh);
        btnRefreshLog.Click += async (_, _) => await RefreshLogViewerAsync();
        _cmbLogSource.SelectedIndexChanged += async (_, _) => await RefreshLogViewerAsync();

        topRow.Controls.Add(_cmbLogSource);
        topRow.Controls.Add(btnRefreshLog);

        var logCard = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        _txtLogViewer = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = Theme.FontMono };
        logCard.Controls.Add(_txtLogViewer);

        root.Controls.Add(topRow);
        root.Controls.Add(logCard);
        root.SetRow(logCard, 2);
        page.Controls.Add(root);
        return page;
    }

    private Panel BuildSettingsPage()
    {
        var page = new Panel { Padding = new Padding(28), BackColor = Theme.ContentBg };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(PageHeading("Settings"));

        var clamAvGroup = new RoundedPanel { AutoSize = false, Height = 110, Dock = DockStyle.Top, Padding = new Padding(16), Margin = new Padding(0, 12, 0, 0) };
        var clamAvLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        clamAvLayout.Controls.Add(new Label { Text = "ClamAV Installation", AutoSize = true, Font = Theme.FontBodyBold, Margin = new Padding(0, 0, 0, 8) });
        var clamAvRow = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 4, AutoSize = true };
        clamAvRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        clamAvRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        clamAvRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        clamAvRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _txtClamAvPath = new TextBox { Dock = DockStyle.Fill, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        Theme.StyleTextBox(_txtClamAvPath);
        var btnBrowseClamAv = new Button { Text = "Browse...", AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
        var btnAutoDetect = new Button { Text = "Auto-Detect", AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
        var btnApplyClamAv = new Button { Text = "Apply", AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
        Theme.StyleSecondaryButton(btnBrowseClamAv);
        Theme.StyleSecondaryButton(btnAutoDetect);
        Theme.StylePrimaryButton(btnApplyClamAv);
        Theme.SetIcon(btnBrowseClamAv, AppIcon.Folder);
        Theme.SetIcon(btnAutoDetect, AppIcon.Search);
        Theme.SetIcon(btnApplyClamAv, AppIcon.Save);
        btnBrowseClamAv.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog();
            if (dlg.ShowDialog() == DialogResult.OK) _txtClamAvPath.Text = dlg.SelectedPath;
        };
        btnAutoDetect.Click += async (_, _) =>
        {
            if (!_client.IsConnected) return;
            var found = await _client.Service.LocateClamAvAsync(null);
            if (found != null) _txtClamAvPath.Text = found.InstallDir;
            else MessageBox.Show("Could not auto-detect ClamAV. Please browse to the install folder manually.", "ClamAV Guardian");
        };
        btnApplyClamAv.Click += async (_, _) => await ApplyClamAvPathAsync();
        clamAvRow.Controls.Add(_txtClamAvPath, 0, 0);
        clamAvRow.Controls.Add(btnBrowseClamAv, 1, 0);
        clamAvRow.Controls.Add(btnAutoDetect, 2, 0);
        clamAvRow.Controls.Add(btnApplyClamAv, 3, 0);
        clamAvLayout.Controls.Add(clamAvRow);
        clamAvGroup.Controls.Add(clamAvLayout);

        var quarantineGroup = new RoundedPanel { Height = 100, Dock = DockStyle.Top, Padding = new Padding(16), Margin = new Padding(0, 12, 0, 0) };
        var quarantineLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        quarantineLayout.Controls.Add(new Label { Text = "Quarantine Location", AutoSize = true, Font = Theme.FontBodyBold, Margin = new Padding(0, 0, 0, 8) });
        var quarantineRow = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true };
        quarantineRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        quarantineRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _txtQuarantinePath = new TextBox { Dock = DockStyle.Fill, Anchor = AnchorStyles.Left | AnchorStyles.Right, ReadOnly = true };
        Theme.StyleTextBox(_txtQuarantinePath);
        var lblQuarantineNote = new Label
        {
            Text = "Managed by the service (machine-wide).",
            AutoSize = true,
            Font = Theme.FontStatLabel,
            ForeColor = Theme.TextSecondary,
            Margin = new Padding(8, 8, 0, 0),
        };
        quarantineRow.Controls.Add(_txtQuarantinePath, 0, 0);
        quarantineRow.Controls.Add(lblQuarantineNote, 1, 0);
        quarantineLayout.Controls.Add(quarantineRow);
        quarantineGroup.Controls.Add(quarantineLayout);

        var generalGroup = new RoundedPanel { Height = 130, Dock = DockStyle.Top, Padding = new Padding(16), Margin = new Padding(0, 12, 0, 0) };
        var generalLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        generalLayout.Controls.Add(new Label { Text = "General", AutoSize = true, Font = Theme.FontBodyBold, Margin = new Padding(0, 0, 0, 8) });
        _chkStartWithWindows = new CheckBox { Text = "Start ClamAV Guardian when Windows starts", AutoSize = true, Font = Theme.FontBody };
        _chkStartWithWindows.CheckedChanged += async (_, _) => await ApplyStartWithWindowsAsync(_chkStartWithWindows.Checked);
        _chkStartMinimized = new CheckBox { Text = "Start minimized to the system tray", AutoSize = true, Font = Theme.FontBody };
        _chkStartMinimized.CheckedChanged += async (_, _) =>
        {
            _settings.StartMinimized = _chkStartMinimized.Checked;
            await SaveSettingsAsync();
        };
        _chkShowNotifications = new CheckBox { Text = "Show desktop notifications", AutoSize = true, Font = Theme.FontBody };
        _chkShowNotifications.CheckedChanged += async (_, _) =>
        {
            _settings.ShowNotifications = _chkShowNotifications.Checked;
            await SaveSettingsAsync();
        };
        generalLayout.Controls.Add(_chkStartWithWindows);
        generalLayout.Controls.Add(_chkStartMinimized);
        generalLayout.Controls.Add(_chkShowNotifications);
        generalGroup.Controls.Add(generalLayout);

        var exclusionsGroup = new RoundedPanel { Height = 230, Dock = DockStyle.Top, Padding = new Padding(16), Margin = new Padding(0, 12, 0, 0) };
        var exclusionsLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        exclusionsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        exclusionsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        exclusionsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        exclusionsLayout.Controls.Add(new Label { Text = "Scan Exclusions (folders)", AutoSize = true, Font = Theme.FontBodyBold, Margin = new Padding(0, 0, 0, 8) });
        _lstExclusions = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, Font = Theme.FontBody };
        var exclusionButtons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        var btnAddExclusion = new Button { Text = "Add Folder...", AutoSize = true };
        var btnRemoveExclusion = new Button { Text = "Remove Selected", AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
        Theme.StyleSecondaryButton(btnAddExclusion);
        Theme.StyleSecondaryButton(btnRemoveExclusion);
        Theme.SetIcon(btnAddExclusion, AppIcon.Plus);
        Theme.SetIcon(btnRemoveExclusion, AppIcon.Trash);
        btnAddExclusion.Click += async (_, _) =>
        {
            using var dlg = new FolderBrowserDialog();
            if (dlg.ShowDialog() == DialogResult.OK && !_settings.ScanExclusionPaths.Contains(dlg.SelectedPath))
            {
                _settings.ScanExclusionPaths.Add(dlg.SelectedPath);
                await SaveSettingsAsync();
                RefreshExclusionsList();
            }
        };
        btnRemoveExclusion.Click += async (_, _) =>
        {
            if (_lstExclusions.SelectedItem is string folder)
            {
                _settings.ScanExclusionPaths.Remove(folder);
                await SaveSettingsAsync();
                RefreshExclusionsList();
            }
        };
        exclusionButtons.Controls.Add(btnAddExclusion);
        exclusionButtons.Controls.Add(btnRemoveExclusion);
        exclusionsLayout.Controls.Add(_lstExclusions);
        exclusionsLayout.Controls.Add(exclusionButtons);
        exclusionsLayout.SetRow(_lstExclusions, 1);
        exclusionsLayout.SetRow(exclusionButtons, 2);
        exclusionsGroup.Controls.Add(exclusionsLayout);

        var updateGroup = new RoundedPanel { Height = 130, Dock = DockStyle.Top, Padding = new Padding(16), Margin = new Padding(0, 12, 0, 0) };
        var updateLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        updateLayout.Controls.Add(new Label { Text = "Software Updates", AutoSize = true, Font = Theme.FontBodyBold, Margin = new Padding(0, 0, 0, 8) });
        var updateRow = new FlowLayoutPanel { AutoSize = true };
        var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        _lblUpdateStatus = new Label
        {
            Text = $"ClamAV Guardian v{currentVersion?.ToString(3)}. Checked automatically every 24 hours.",
            AutoSize = true,
            Font = Theme.FontBody,
            ForeColor = Theme.TextSecondary,
            Margin = new Padding(0, 6, 12, 0),
        };
        var btnCheckForUpdates = new Button { Text = "Check for Updates", AutoSize = true };
        Theme.StyleSecondaryButton(btnCheckForUpdates);
        Theme.SetIcon(btnCheckForUpdates, AppIcon.Refresh);
        btnCheckForUpdates.Click += async (_, _) => await CheckForUpdatesAsync();
        updateRow.Controls.Add(_lblUpdateStatus);
        updateRow.Controls.Add(btnCheckForUpdates);
        updateLayout.Controls.Add(updateRow);

        _lblUpdateVersions = new Label
        {
            Text = string.Empty,
            AutoSize = true,
            Font = Theme.FontBody,
            ForeColor = Theme.TextSecondary,
            Margin = new Padding(0, 2, 0, 4),
            Visible = false,
        };
        updateLayout.Controls.Add(_lblUpdateVersions);

        _updateProgressBar = new ProgressBar
        {
            Width = 300,
            Height = 8,
            Style = ProgressBarStyle.Continuous,
            Visible = false,
            Margin = new Padding(0, 0, 0, 4),
        };
        updateLayout.Controls.Add(_updateProgressBar);

        updateGroup.Controls.Add(updateLayout);

        var aboutGroup = new RoundedPanel { Height = 90, Dock = DockStyle.Top, Padding = new Padding(16), Margin = new Padding(0, 12, 0, 0) };
        var aboutLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        aboutLayout.Controls.Add(new Label { Text = "About", AutoSize = true, Font = Theme.FontBodyBold, Margin = new Padding(0, 0, 0, 8) });
        var aboutRow = new FlowLayoutPanel { AutoSize = true };
        aboutRow.Controls.Add(new Label
        {
            Text = $"ClamAV Guardian v{currentVersion?.ToString(3)} — Powered by 7iNDA",
            AutoSize = true,
            Font = Theme.FontBody,
            ForeColor = Theme.TextSecondary,
            Margin = new Padding(0, 6, 16, 0),
        });
        var btnTerms = new Button { Text = "Terms && Conditions", AutoSize = true };
        var btnPrivacy = new Button { Text = "Privacy Policy", AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
        Theme.StyleSecondaryButton(btnTerms);
        Theme.StyleSecondaryButton(btnPrivacy);
        Theme.SetIcon(btnTerms, AppIcon.Document);
        Theme.SetIcon(btnPrivacy, AppIcon.Document);
        btnTerms.Click += (_, _) => OpenBundledDocument("TermsAndConditions.pdf");
        btnPrivacy.Click += (_, _) => OpenBundledDocument("PrivacyPolicy.pdf");
        aboutRow.Controls.Add(btnTerms);
        aboutRow.Controls.Add(btnPrivacy);
        aboutLayout.Controls.Add(aboutRow);
        aboutGroup.Controls.Add(aboutLayout);

        root.Controls.Add(clamAvGroup);
        root.Controls.Add(quarantineGroup);
        root.Controls.Add(generalGroup);
        root.Controls.Add(exclusionsGroup);
        root.Controls.Add(updateGroup);
        root.Controls.Add(aboutGroup);

        page.Controls.Add(root);
        return page;
    }

    #endregion

    #region Actions

    private async Task SaveSettingsAsync()
    {
        if (!_client.IsConnected) return;
        await _client.Service.SaveSettingsAsync(_settings);
    }

    private async Task CheckForUpdatesAsync()
    {
        if (!_client.IsConnected)
        {
            MessageBox.Show("Not connected to the ClamAV Guardian service.", "ClamAV Guardian");
            return;
        }

        _lblUpdateStatus.Text = "Checking for updates...";
        var result = await _client.Service.CheckForAppUpdateAsync();

        if (!result.UpdateAvailable)
        {
            _lblUpdateStatus.Text = $"You're up to date (checked just now).";
            return;
        }

        _lblUpdateStatus.Text = $"Version {result.LatestVersion} is available.";
        var choice = MessageBox.Show(
            $"ClamAV Guardian v{result.LatestVersion} is available. Install it now?\n\nThe background service will apply it automatically (no restart needed for your files/settings) and briefly restart itself.",
            "Update Available",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information);

        if (choice == DialogResult.Yes)
        {
            _updateProgressBar.Visible = true;
            _updateProgressBar.Value = 0;
            _lblUpdateVersions.Visible = true;
            _lblUpdateVersions.Text = $"Current: v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version} → Target: v{result.LatestVersion}";
            await _client.Service.ApplyAppUpdateAsync();
        }
    }

    private void OnAppUpdateProgress(DownloadProgress progress)
    {
        _updateProgressBar.Visible = progress.Stage is not (DownloadStage.Done or DownloadStage.Failed);
        _lblUpdateVersions.Visible = progress.TargetVersion != null;

        if (progress.TargetVersion != null)
        {
            _lblUpdateVersions.Text = $"Current: v{progress.CurrentVersion} → Target: v{progress.TargetVersion}";
        }

        if (progress.Stage == DownloadStage.Downloading && progress.TotalBytes > 0)
        {
            _updateProgressBar.Style = ProgressBarStyle.Continuous;
            _updateProgressBar.Value = Math.Clamp(progress.PercentComplete, 0, 100);
            _lblUpdateStatus.Text = $"{progress.Message} ({progress.PercentComplete}% — {FormatBytes(progress.BytesReceived)} / {FormatBytes(progress.TotalBytes)})";
        }
        else
        {
            _updateProgressBar.Style = ProgressBarStyle.Marquee;
            _lblUpdateStatus.Text = progress.Message;
        }

        if (progress.Stage == DownloadStage.Failed)
        {
            _lblUpdateStatus.ForeColor = Theme.AccentRed;
        }
        else if (progress.Stage == DownloadStage.Done)
        {
            _lblUpdateStatus.ForeColor = Theme.AccentGreen;
        }
        else
        {
            _lblUpdateStatus.ForeColor = Theme.TextSecondary;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):0.#} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes} B";
    }

    private async Task InstallClamAvAsync()
    {
        if (!_client.IsConnected)
        {
            MessageBox.Show("Not connected to the ClamAV Guardian service.", "ClamAV Guardian");
            return;
        }

        _btnInstallClamAv.Enabled = false;
        _clamAvInstallProgressBar.Visible = true;
        _clamAvInstallProgressBar.Value = 0;
        _lblClamAvStatus.Text = "Starting ClamAV install...";
        _lblClamAvStatus.ForeColor = Theme.TextSecondary;

        var result = await _client.Service.InstallClamAvAsync();

        _btnInstallClamAv.Enabled = true;
        _clamAvInstallProgressBar.Visible = false;

        if (result.Success)
        {
            _install = await _client.Service.GetCurrentInstallationAsync();
            _btnInstallClamAv.Visible = _install == null;
            _lblClamAvStatus.Text = _install != null ? $"ClamAV found at {_install.InstallDir}" : result.Message;
            _lblClamAvStatus.ForeColor = _install != null ? Theme.AccentGreen : Theme.AccentAmber;
            await RefreshUpdateStatusAsync();
        }
        else
        {
            _lblClamAvStatus.Text = $"Install failed: {result.Message}";
            _lblClamAvStatus.ForeColor = Theme.AccentRed;
            MessageBox.Show($"Failed to install ClamAV automatically: {result.Message}\n\nYou can still install it manually from clamav.net and point Settings at it.", "ClamAV Guardian", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OnClamAvInstallProgress(DownloadProgress progress)
    {
        AppendActivity(progress.Message);

        if (progress.Stage == DownloadStage.Downloading && progress.TotalBytes > 0)
        {
            _clamAvInstallProgressBar.Visible = true;
            _clamAvInstallProgressBar.Style = ProgressBarStyle.Continuous;
            _clamAvInstallProgressBar.Value = Math.Clamp(progress.PercentComplete, 0, 100);
            _lblClamAvStatus.Text = $"{progress.Message} ({progress.PercentComplete}% — {FormatBytes(progress.BytesReceived)} / {FormatBytes(progress.TotalBytes)})";
        }
        else
        {
            _clamAvInstallProgressBar.Style = ProgressBarStyle.Marquee;
            _lblClamAvStatus.Text = progress.Message;
        }

        _lblClamAvStatus.ForeColor = progress.Stage == DownloadStage.Failed ? Theme.AccentRed : Theme.TextSecondary;
    }

    private async Task ApplyClamAvPathAsync()
    {
        if (!_client.IsConnected)
        {
            MessageBox.Show("Not connected to the ClamAV Guardian service.", "ClamAV Guardian");
            return;
        }

        var candidate = await _client.Service.ApplyClamAvPathAsync(_txtClamAvPath.Text);
        if (candidate == null)
        {
            MessageBox.Show("clamscan.exe / freshclam.exe were not found at that path.", "ClamAV Guardian", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _install = candidate;
        _lblClamAvStatus.Text = $"ClamAV found at {_install.InstallDir}";
        _lblClamAvStatus.ForeColor = Theme.AccentGreen;
        await RefreshUpdateStatusAsync();
        MessageBox.Show("ClamAV located successfully.", "ClamAV Guardian");
    }

    private async Task ApplyStartWithWindowsAsync(bool enabled)
    {
        var success = enabled
            ? StartupService.Enable(Application.ExecutablePath)
            : StartupService.Disable();

        if (!success)
        {
            MessageBox.Show(
                enabled
                    ? "Failed to register the startup entry. Check the Logs tab for details."
                    : "Failed to remove the startup entry. Check the Logs tab for details.",
                "ClamAV Guardian",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        _settings.StartWithWindows = enabled;
        await SaveSettingsAsync();
    }

    /// <summary>
    /// The MSI's per-machine Desktop shortcut lands on the shared Public Desktop, which
    /// Explorer normally merges onto every user's visible desktop for free — so on a normal
    /// machine, creating a second shortcut on the personal Desktop would just be a duplicate
    /// icon. The one exception is OneDrive (or similar) Known Folder redirection: when it
    /// takes over the Desktop known folder, Explorer stops merging in the Public Desktop's
    /// contents, silently hiding the MSI's shortcut. We only create our own copy in that
    /// specific case (detected by the personal Desktop path resolving under a OneDrive
    /// folder), and otherwise clean up a stray duplicate left behind by older versions of
    /// this app that created the fallback shortcut unconditionally.
    /// </summary>
    private Task EnsureDesktopShortcutAsync()
    {
        try
        {
            var desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var shortcutPath = Path.Combine(desktopDir, "ClamAV Guardian.lnk");
            var isRedirectedByOneDrive = desktopDir.Contains("OneDrive", StringComparison.OrdinalIgnoreCase);

            if (!isRedirectedByOneDrive)
            {
                if (File.Exists(shortcutPath))
                {
                    File.Delete(shortcutPath);
                    ClientLogger.Info($"Removed duplicate desktop shortcut at '{shortcutPath}' — the Public Desktop shortcut from setup already covers this.");
                }
                return Task.CompletedTask;
            }

            if (File.Exists(shortcutPath)) return Task.CompletedTask;

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType != null)
            {
                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell != null)
                {
                    dynamic shortcut = shell.CreateShortcut(shortcutPath);
                    shortcut.TargetPath = Application.ExecutablePath;
                    shortcut.WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath);
                    shortcut.Description = "Manage ClamAV scanning, updates, and real-time protection";
                    shortcut.Save();

                    ClientLogger.Info($"Created desktop shortcut at '{shortcutPath}' (OneDrive-redirected Desktop detected; the Public Desktop shortcut wouldn't be visible here).");
                }
            }
        }
        catch (Exception ex)
        {
            ClientLogger.Error("Failed to reconcile desktop shortcut", ex);
        }

        return Task.CompletedTask;
    }

    private async Task StartScanAsync()
    {
        if (!_client.IsConnected || _install == null)
        {
            MessageBox.Show("ClamAV is not configured yet. Set the install path in Settings.", "ClamAV Guardian");
            return;
        }

        if (_scanInProgress) return;

        var request = new ScanRequest { Kind = ScanKind.Quick };
        if (_rbFull.Checked)
        {
            request.Kind = ScanKind.Full;
        }
        else if (_rbCustom.Checked)
        {
            if (!Directory.Exists(_txtCustomPath.Text))
            {
                MessageBox.Show("Choose a valid custom folder first.", "ClamAV Guardian");
                return;
            }
            request.Kind = ScanKind.Custom;
            request.CustomPath = _txtCustomPath.Text;
        }

        _scanInProgress = true;
        _lvScanResults.Items.Clear();
        _btnStartScan.Enabled = false;
        _btnCancelScan.Enabled = true;
        _scanProgress.MarqueeAnimationSpeed = 30;
        _lblScanStatus.Text = "Scanning...";
        _cardLastScan.ValueText = "Scanning...";
        _cardLastScan.AccentColor = Theme.AccentBlue;
        _scanActionBanner.Visible = false;

        _liveFilesScanned = 0;
        _liveInfected = 0;
        _liveErrors = 0;
        _liveQuarantined = 0;
        _scanCardFiles.ValueText = "0";
        _scanCardInfected.ValueText = "0";
        _scanCardInfected.AccentColor = Theme.AccentGreen;
        _scanCardErrors.ValueText = "0";
        _scanCardDuration.ValueText = "00:00";

        _scanStartedAt = DateTime.Now;
        _scanDurationTimer?.Stop();
        _scanDurationTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _scanDurationTimer.Tick += (_, _) => _scanCardDuration.ValueText = (DateTime.Now - _scanStartedAt).ToString(@"mm\:ss");
        _scanDurationTimer.Start();

        ScanSummary summary;
        try
        {
            summary = await _client.Service.StartScanAsync(request, CancellationToken.None);
        }
        catch (Exception ex)
        {
            ClientLogger.Error("Scan request failed", ex);
            summary = new ScanSummary { WasCancelled = true };
        }

        _scanInProgress = false;
        _scanDurationTimer.Stop();
        _scanProgress.MarqueeAnimationSpeed = 0;
        _btnStartScan.Enabled = true;
        _btnCancelScan.Enabled = false;
        _scanCardDuration.ValueText = summary.Duration.ToString(@"mm\:ss");

        if (summary.DatabaseMissing)
        {
            _lblScanStatus.Text = "No virus database found. Go to Updates and click 'Update Now' first.";
            _cardLastScan.ValueText = "No database";
            _cardLastScan.SubtitleText = "Run Update Now first";
            _cardLastScan.AccentColor = Theme.AccentAmber;
            return;
        }

        _lblScanStatus.Text = summary.WasCancelled
            ? "Scan cancelled."
            : $"Scan complete in {summary.Duration:mm\\:ss} — {summary.FilesScanned} files scanned, {summary.InfectedFound} infected, {summary.Errors} errors.";

        _cardLastScan.ValueText = summary.WasCancelled ? "Cancelled" : DateTime.Now.ToString("g");
        _cardLastScan.SubtitleText = summary.WasCancelled ? "" : $"{summary.FilesScanned} scanned, {summary.InfectedFound} infected";
        _cardLastScan.AccentColor = summary.InfectedFound > 0 ? Theme.AccentRed : Theme.AccentBlue;

        if (!summary.WasCancelled)
        {
            if (summary.InfectedFound > 0)
            {
                var remaining = summary.InfectedFound - _liveQuarantined;
                _lblScanBanner.Text = remaining == 0
                    ? $"{summary.InfectedFound} threat(s) found and automatically quarantined."
                    : $"{summary.InfectedFound} threat(s) found — {_liveQuarantined} quarantined automatically, {remaining} need action.";
                _lblScanBanner.ForeColor = Theme.AccentRed;
                _btnQuarantineAllInfected.Visible = remaining > 0;
                _scanActionBanner.BorderColor = Theme.AccentRed;
            }
            else
            {
                _lblScanBanner.Text = $"No threats found. {summary.FilesScanned} files scanned cleanly.";
                _lblScanBanner.ForeColor = Theme.AccentGreen;
                _btnQuarantineAllInfected.Visible = false;
                _scanActionBanner.BorderColor = Theme.AccentGreen;
            }
            _scanActionBanner.Visible = true;
            _scanActionBanner.Invalidate();
        }

        await RefreshQuarantineListAsync();

        if (!summary.WasCancelled && !summary.DatabaseMissing && _settings.AfterScanAction != PostScanAction.None)
        {
            using var dialog = new PostScanActionDialog(_settings.AfterScanAction);
            dialog.ShowDialog(this);
        }
    }

    private async Task QuarantineAllInfectedScanResultsAsync()
    {
        var pendingItems = _lvScanResults.Items.Cast<ListViewItem>()
            .Where(lvi => lvi.Tag is ScanItem item && item.Status == ScanStatus.Infected && lvi.SubItems[1].Text != "Quarantined")
            .ToList();

        if (pendingItems.Count == 0 || !_client.IsConnected) return;

        var quarantinedCount = 0;
        foreach (var lvi in pendingItems)
        {
            var item = (ScanItem)lvi.Tag!;
            if (await _client.Service.QuarantineFileAsync(item.Path, item.ThreatName ?? "Unknown"))
            {
                lvi.SubItems[1].Text = "Quarantined";
                lvi.ForeColor = Theme.AccentAmber;
                quarantinedCount++;
                _liveQuarantined++;
            }
        }

        await RefreshQuarantineListAsync();
        _btnQuarantineAllInfected.Visible = false;
        _lblScanBanner.Text = $"Quarantined {quarantinedCount} of {pendingItems.Count} remaining infected file(s).";
        AppendActivity(_lblScanBanner.Text);
        MessageBox.Show(_lblScanBanner.Text, "ClamAV Guardian");
    }

    private void OnScanItem(ScanItem item)
    {
        var lvi = new ListViewItem(item.Path);
        lvi.SubItems.Add(item.WasQuarantined ? "Quarantined" : item.Status.ToString());
        lvi.SubItems.Add(item.ThreatName ?? item.ErrorMessage ?? "");
        lvi.Tag = item;
        if (item.WasQuarantined) lvi.ForeColor = Theme.AccentAmber;
        else if (item.Status == ScanStatus.Infected) lvi.ForeColor = Theme.AccentRed;
        _lvScanResults.Items.Add(lvi);

        _liveFilesScanned++;
        _scanCardFiles.ValueText = _liveFilesScanned.ToString();

        if (item.Status == ScanStatus.Infected)
        {
            _liveInfected++;
            _scanCardInfected.ValueText = _liveInfected.ToString();
            _scanCardInfected.AccentColor = Theme.AccentRed;

            if (item.WasQuarantined)
            {
                _liveQuarantined++;
                AppendActivity($"Auto-quarantined: {item.Path} ({item.ThreatName})");
            }
            else
            {
                AppendActivity($"THREAT FOUND: {item.Path} ({item.ThreatName})");
            }

            RegisterThreatThisSession();
            NotifyThreat(item.Path, item.ThreatName ?? "Unknown");
        }
        else if (item.Status == ScanStatus.Error)
        {
            _liveErrors++;
            _scanCardErrors.ValueText = _liveErrors.ToString();
        }
    }

    private async Task QuarantineSelectedScanResultAsync()
    {
        if (_lvScanResults.SelectedItems.Count == 0 || !_client.IsConnected) return;
        var selected = _lvScanResults.SelectedItems[0];
        if (selected.Tag is not ScanItem item || item.Status != ScanStatus.Infected) return;
        if (selected.SubItems[1].Text == "Quarantined")
        {
            MessageBox.Show("This file was already quarantined.", "ClamAV Guardian");
            return;
        }

        if (await _client.Service.QuarantineFileAsync(item.Path, item.ThreatName ?? "Unknown"))
        {
            selected.SubItems[1].Text = "Quarantined";
            selected.ForeColor = Theme.AccentAmber;
            MessageBox.Show("File quarantined.", "ClamAV Guardian");
            await RefreshQuarantineListAsync();
        }
        else
        {
            MessageBox.Show("Failed to quarantine file. It may already be gone or in use.", "ClamAV Guardian");
        }
    }

    private void OpenSelectedScanResultFolder()
    {
        if (_lvScanResults.SelectedItems.Count == 0) return;
        var path = _lvScanResults.SelectedItems[0].Text;
        var dir = Path.GetDirectoryName(path);
        if (dir != null && Directory.Exists(dir))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
    }

    private static void OpenBundledDocument(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Legal", fileName);
        if (!File.Exists(path))
        {
            MessageBox.Show($"Couldn't find '{fileName}' next to the application.", "ClamAV Guardian", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    private async Task ToggleRealTimeAsync(bool enable)
    {
        if (!_client.IsConnected)
        {
            if (enable)
            {
                MessageBox.Show("Not connected to the ClamAV Guardian service.", "ClamAV Guardian");
                _chkRealTimeEnabled.Checked = false;
            }
            return;
        }

        await _client.Service.SetRealTimeProtectionEnabledAsync(enable);
        _isRealTimeEnabled = enable;

        if (enable)
        {
            SetProtectionCard("Protected", Theme.AccentGreen);
            _trayIcon.Icon = IconFactory.CreateTrayIcon(TrayIconState.Protected);
            var engine = await _client.Service.GetRealTimeEngineDescriptionAsync();
            OnRealTimeEngineStatus(engine);
        }
        else
        {
            SetProtectionCard("Not Protected", Theme.AccentRed);
            _cardEngine.ValueText = "Inactive";
            _cardEngine.SubtitleText = "Not running";
            _cardEngine.AccentColor = Theme.AccentGray;
            _trayIcon.Icon = IconFactory.CreateTrayIcon(TrayIconState.Disabled);
        }

        _chkRealTimeEnabled.Checked = enable;
        _settings.RealTimeProtectionEnabled = enable;
    }

    private async Task RefreshClamdStatusAsync()
    {
        if (!_client.IsConnected) return;

        var state = await _client.Service.GetClamdStateAsync();
        switch (state)
        {
            case ClamdServiceState.Running:
                _lblClamdStatus.Text = "clamd (fast scanning engine): running";
                _lblClamdStatus.ForeColor = Theme.AccentGreen;
                _btnInstallClamd.Visible = false;
                break;
            case ClamdServiceState.Stopped:
                _lblClamdStatus.Text = "clamd (fast scanning engine): installed but stopped";
                _lblClamdStatus.ForeColor = Theme.AccentAmber;
                _btnInstallClamd.Visible = true;
                _btnInstallClamd.Text = "Start clamd";
                break;
            case ClamdServiceState.NotInstalled:
                _lblClamdStatus.Text = "Real-time protection is scanning file-by-file (slower). Enable clamd for faster scanning.";
                _lblClamdStatus.ForeColor = Theme.TextSecondary;
                _btnInstallClamd.Visible = true;
                _btnInstallClamd.Text = "Enable Fast Scanning";
                break;
            default:
                _lblClamdStatus.Text = "clamd (fast scanning engine): unknown status";
                _lblClamdStatus.ForeColor = Theme.TextSecondary;
                _btnInstallClamd.Visible = false;
                break;
        }
    }

    private async Task InstallClamdAsync()
    {
        if (!_client.IsConnected)
        {
            MessageBox.Show("Not connected to the ClamAV Guardian service.", "ClamAV Guardian");
            return;
        }

        _btnInstallClamd.Enabled = false;
        _lblClamdStatus.Text = "Setting up clamd...";
        _lblClamdStatus.ForeColor = Theme.TextSecondary;

        var result = await _client.Service.InstallClamdAsync();

        _btnInstallClamd.Enabled = true;
        await RefreshClamdStatusAsync();

        if (!result.Success)
        {
            MessageBox.Show($"Failed to enable fast scanning: {result.Message}", "ClamAV Guardian", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OnRealTimeEngineStatus(string message)
    {
        var engineText = message.Replace("Real-time protection engine: ", "");
        _lblEngine.Text = $"Engine: {engineText}";
        _cardEngine.ValueText = engineText.Contains("clamd") ? "clamd" : "clamscan";
        _cardEngine.SubtitleText = engineText.Contains("clamd") ? "Fast (resident daemon)" : "Fallback (per-file)";
    }

    private void OnThreatDetected(ScanItem item, bool quarantined)
    {
        var message = quarantined
            ? $"Threat quarantined: {item.Path} ({item.ThreatName})"
            : $"Threat detected: {item.Path} ({item.ThreatName})";

        AppendRealtimeFeed(message);
        AppendActivity(message);
        RegisterThreatThisSession();
        NotifyThreat(item.Path, item.ThreatName ?? "Unknown");
        _ = RefreshQuarantineListAsync();

        _trayIcon.Icon = IconFactory.CreateTrayIcon(TrayIconState.Alert);
        var resetTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        resetTimer.Tick += (_, _) =>
        {
            resetTimer.Stop();
            resetTimer.Dispose();
            if (_isRealTimeEnabled)
                _trayIcon.Icon = IconFactory.CreateTrayIcon(TrayIconState.Protected);
        };
        resetTimer.Start();
    }

    private void RegisterThreatThisSession()
    {
        _threatsThisSession++;
        _cardThreatsSession.ValueText = _threatsThisSession.ToString();
        _cardThreatsSession.SubtitleText = _threatsThisSession == 0 ? "No threats detected" : "Since app started";
        _cardThreatsSession.AccentColor = _threatsThisSession > 0 ? Theme.AccentRed : Theme.AccentGreen;
    }

    private void NotifyThreat(string path, string threatName)
    {
        if (!_settings.ShowNotifications) return;
        _trayIcon.ShowBalloonTip(4000, "Threat Detected", $"{Path.GetFileName(path)}: {threatName}", ToolTipIcon.Warning);
    }

    private async Task RunUpdateNowAsync()
    {
        if (!_client.IsConnected)
        {
            MessageBox.Show("Not connected to the ClamAV Guardian service.", "ClamAV Guardian");
            return;
        }

        _lblScanStatus.Text = "Updating virus database...";
        var result = await _client.Service.RunUpdateNowAsync(CancellationToken.None);
        AppendActivity(result.Success ? "Virus database updated successfully." : $"Update failed: {result.Message}");
        await RefreshUpdateStatusAsync();
    }

    private async Task RefreshUpdateStatusAsync()
    {
        if (!_client.IsConnected) return;

        var status = await _client.Service.GetUpdateStatusAsync();
        var stateText = status.ServiceState switch
        {
            FreshClamServiceState.Running => "Running (native Windows service)",
            FreshClamServiceState.Stopped => "Installed but stopped",
            FreshClamServiceState.NotInstalled => "Not installed (using in-app scheduler)",
            _ => "Unknown",
        };

        _cardServiceState.ValueText = status.ServiceState switch
        {
            FreshClamServiceState.Running => "Running",
            FreshClamServiceState.Stopped => "Stopped",
            FreshClamServiceState.NotInstalled => "Not Installed",
            _ => "Unknown",
        };
        _cardServiceState.SubtitleText = stateText;
        _cardServiceState.AccentColor = status.ServiceState switch
        {
            FreshClamServiceState.Running => Theme.AccentGreen,
            FreshClamServiceState.Stopped => Theme.AccentAmber,
            _ => Theme.AccentGray,
        };

        var dbVersionText = status.DatabaseVersion ?? "unknown";
        _cardDbVersionUpd.ValueText = string.IsNullOrEmpty(status.DatabaseVersion) ? "Unknown" : "Installed";
        _cardDbVersionUpd.SubtitleText = dbVersionText.Length > 40 ? dbVersionText[..40] + "…" : dbVersionText;
        _cardDbVersion.ValueText = _cardDbVersionUpd.ValueText;
        _cardDbVersion.SubtitleText = _cardDbVersionUpd.SubtitleText;

        var lastUpdateText = status.LastUpdateUtc.HasValue ? status.LastUpdateUtc.Value.ToLocalTime().ToString("g") : "unknown";
        _cardLastUpdateUpd.ValueText = status.LastUpdateUtc.HasValue ? status.LastUpdateUtc.Value.ToLocalTime().ToString("MMM d, h:mm tt") : "Never";
        _cardLastUpdateUpd.SubtitleText = lastUpdateText;
        _cardLastUpdate.ValueText = status.LastUpdateUtc.HasValue ? status.LastUpdateUtc.Value.ToLocalTime().ToString("MMM d") : "Never";
        _cardLastUpdate.SubtitleText = status.LastUpdateUtc.HasValue ? status.LastUpdateUtc.Value.ToLocalTime().ToString("t") : "";

        _numCheckInterval.Value = Math.Clamp(await _client.Service.GetUpdateCheckIntervalAsync(), 1, 24);
    }

    private string? GetSelectedQuarantineId()
    {
        return _lvQuarantine.SelectedItems.Count == 0 ? null : _lvQuarantine.SelectedItems[0].Tag as string;
    }

    private async Task RefreshQuarantineListAsync()
    {
        if (!_client.IsConnected) return;

        _lvQuarantine.Items.Clear();
        var entries = await _client.Service.GetQuarantineEntriesAsync();
        foreach (var entry in entries)
        {
            var lvi = new ListViewItem(entry.OriginalPath) { Tag = entry.Id };
            lvi.SubItems.Add(entry.ThreatName);
            lvi.SubItems.Add(entry.QuarantinedAtUtc.ToLocalTime().ToString("g"));
            lvi.SubItems.Add(entry.SizeBytes.ToString());
            _lvQuarantine.Items.Add(lvi);
        }

        _cardQuarantine.ValueText = entries.Count.ToString();
        _cardQuarantine.AccentColor = entries.Count > 0 ? Theme.AccentAmber : Theme.AccentGreen;
        _lblQuarantineEmpty.Visible = entries.Count == 0;
    }

    private void RefreshWatchedFoldersList()
    {
        _lstWatchedFolders.Items.Clear();
        foreach (var folder in _settings.RealTimeWatchedFolders) _lstWatchedFolders.Items.Add(folder);
    }

    private void RefreshExclusionsList()
    {
        _lstExclusions.Items.Clear();
        foreach (var folder in _settings.ScanExclusionPaths) _lstExclusions.Items.Add(folder);
    }

    private async Task RefreshLogViewerAsync()
    {
        if (!_client.IsConnected) return;

        if (_cmbLogSource.SelectedIndex == 0)
        {
            _txtLogViewer.Text = await _client.Service.ReadAppLogTailAsync(500);
        }
        else
        {
            var freshclamLog = await _client.Service.ReadFreshClamLogAsync();
            _txtLogViewer.Text = freshclamLog ?? "freshclam.log not found.";
        }
        _txtLogViewer.SelectionStart = _txtLogViewer.Text.Length;
        _txtLogViewer.ScrollToCaret();
    }

    private void AppendActivity(string message)
    {
        _lstRecentActivity.Items.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
        while (_lstRecentActivity.Items.Count > 200) _lstRecentActivity.Items.RemoveAt(_lstRecentActivity.Items.Count - 1);
        _lblActivityEmpty.Visible = false;
    }

    private void AppendRealtimeFeed(string message)
    {
        _lstRealtimeFeed.Items.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
        while (_lstRealtimeFeed.Items.Count > 300) _lstRealtimeFeed.Items.RemoveAt(_lstRealtimeFeed.Items.Count - 1);
        _lblRealtimeFeedEmpty.Visible = false;
    }

    #endregion
}
