using System.Drawing;
using System.Drawing.Drawing2D;
using GLMQuotaMonitor.Models;

namespace GLMQuotaMonitor;

/// <summary>
/// 托盘应用主上下文
/// 管理托盘图标、右键菜单、详情弹窗
/// </summary>
public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly ConfigService _configService;
    private readonly QuotaService _quotaService;
    private readonly ThemeService _themeService;

    private ToolStripMenuItem? _floatingBarToggle;
    private ToolStripMenuItem? _autoStartToggle;
    private ToolStripDropDown? _popup;
    private FloatingBar? _floatingBar;

    public TrayApplicationContext()
    {
        _configService = new ConfigService();
        _quotaService = new QuotaService();
        _themeService = new ThemeService();

        // 创建右键菜单
        _contextMenu = CreateContextMenu();

        // 创建托盘图标
        _notifyIcon = new NotifyIcon
        {
            Icon = TrayIconFactory.GetIcon(QuotaStatus.Offline),
            Text = "GLM 配额监控 - 初始化中...",
            Visible = true,
            ContextMenuStrip = _contextMenu
        };

        _notifyIcon.MouseClick += OnTrayIconClick;

        // 订阅事件
        _quotaService.QuotaUpdated += OnQuotaUpdated;
        _quotaService.Error += OnQuotaError;
        _configService.ConfigChanged += OnConfigChanged;
        _themeService.SetMode(_configService.Config.Theme);

        // 启动轮询
        StartMonitoring();

        // 如果配置了浮动条，显示它
        if (_configService.Config.ShowFloatingBar)
            ShowFloatingBar();

        // 如果没有 Token，弹设置窗口
        if (!_configService.HasToken())
        {
            // 延迟一下再弹，等 UI 就绪
            Task.Delay(500).ContinueWith(_ =>
            {
                try { ShowSettings(); }
                catch { }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }

    /// <summary>
    /// 创建右键菜单
    /// </summary>
    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip();

        var settingsItem = new ToolStripMenuItem("⚙ 设置");
        settingsItem.Click += (_, _) => ShowSettings();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new ToolStripSeparator());

        _floatingBarToggle = new ToolStripMenuItem("显示浮动条")
        {
            CheckOnClick = true,
            Checked = _configService.Config.ShowFloatingBar
        };
        _floatingBarToggle.Click += OnToggleFloatingBar;
        menu.Items.Add(_floatingBarToggle);

        _autoStartToggle = new ToolStripMenuItem("开机自启动")
        {
            CheckOnClick = true,
            Checked = _configService.Config.AutoStart
        };
        _autoStartToggle.Click += OnToggleAutoStart;
        menu.Items.Add(_autoStartToggle);

        menu.Items.Add(new ToolStripSeparator());

        var refreshItem = new ToolStripMenuItem("⟳ 立即刷新");
        refreshItem.Click += (_, _) => RefreshQuota();
        menu.Items.Add(refreshItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("✕ 退出");
        exitItem.Click += (_, _) => ExitApplication();
        menu.Items.Add(exitItem);

        return menu;
    }

    /// <summary>
    /// 托盘图标左键点击 - 显示详情弹窗
    /// </summary>
    private void OnTrayIconClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            ShowPopup();
    }

    /// <summary>
    /// 显示配额详情弹窗
    /// </summary>
    private void ShowPopup()
    {
        // 如果已有弹窗，关闭它
        if (_popup?.Visible == true)
        {
            _popup.Close();
            _popup = null;
            return;
        }

        var snapshot = _quotaService.GetLastSnapshot();
        bool isDark = _themeService.IsDark;

        int panelWidth = 340;
        int panelHeight = 210;

        var panel = CreatePopupPanel(snapshot, isDark, panelWidth, panelHeight);

        var host = new ToolStripControlHost(panel)
        {
            AutoSize = false,
            Size = new Size(panelWidth, panelHeight)
        };

        _popup = new ToolStripDropDown
        {
            DropShadowEnabled = true,
            AutoClose = true
        };
        _popup.Items.Add(host);

        // 定位到屏幕右下角（在任务栏上方）
        var screen = Screen.PrimaryScreen!.WorkingArea;
        _popup.Show(screen.Right - panelWidth - 10, screen.Bottom - panelHeight - 10);
    }

    /// <summary>
    /// 创建弹窗面板（固定尺寸，不用 AutoSize）
    /// </summary>
    private Panel CreatePopupPanel(QuotaSnapshot snapshot, bool isDark, int width, int height)
    {
        Color bgColor = isDark ? Color.FromArgb(22, 33, 62) : Color.FromArgb(245, 245, 250);
        Color textColor = isDark ? Color.FromArgb(224, 224, 224) : Color.FromArgb(30, 30, 30);
        Color subTextColor = isDark ? Color.FromArgb(123, 140, 176) : Color.FromArgb(100, 100, 100);
        Color separatorColor = isDark ? Color.FromArgb(30, 42, 74) : Color.FromArgb(220, 220, 230);

        var panel = new Panel
        {
            BackColor = bgColor,
            Size = new Size(width, height)
        };

        int contentWidth = width - 32; // 左右各 16px padding
        int y = 12;

        // 标题行
        var lblTitle = CreateLabel("GLM 配额监控", 14, FontStyle.Bold, textColor, 16, y);
        panel.Controls.Add(lblTitle);
        y += 32;

        // 分隔线
        panel.Controls.Add(CreateSeparator(separatorColor, 16, y, contentWidth));
        y += 12;

        // MCP 配额
        y = AddQuotaRow(panel, snapshot.McpQuota, isDark, 16, y, contentWidth, _configService.Config);
        y += 8;

        // 5h Token
        y = AddQuotaRow(panel, snapshot.Token5hQuota, isDark, 16, y, contentWidth, _configService.Config);
        y += 12;

        // 分隔线
        panel.Controls.Add(CreateSeparator(separatorColor, 16, y, contentWidth));
        y += 12;

        // 时间戳
        string timeStr = snapshot.IsOffline
            ? "离线 - 显示上次数据"
            : $"🕐 上次刷新: {snapshot.Timestamp:HH:mm:ss}";
        var lblTime = CreateLabel(timeStr, 9, FontStyle.Regular, subTextColor, 16, y);
        panel.Controls.Add(lblTime);

        // 刷新按钮（右下角）
        var btnRefresh = new Button
        {
            Text = "⟳ 刷新",
            FlatStyle = FlatStyle.Flat,
            ForeColor = isDark ? Color.FromArgb(123, 140, 222) : Color.FromArgb(60, 80, 180),
            BackColor = Color.Transparent,
            Font = new Font("Microsoft YaHei UI", 9f),
            Location = new Point(width - 90, height - 34),
            Size = new Size(74, 24),
            Cursor = Cursors.Hand
        };
        btnRefresh.Click += (_, _) => RefreshQuota();
        panel.Controls.Add(btnRefresh);

        return panel;
    }

    /// <summary>
    /// 添加一行配额信息到面板
    /// 布局：[名称 60px] [进度条 flex] [百分比 48px]
    /// </summary>
    private static int AddQuotaRow(Panel panel, QuotaItem item, bool isDark, int x, int y, int contentWidth, AppConfig config)
    {
        // 名称标签
        var lbl = CreateLabel(item.Name, 10, FontStyle.Regular,
            isDark ? Color.FromArgb(136, 146, 176) : Color.FromArgb(100, 100, 100),
            x, y + 2);
        panel.Controls.Add(lbl);

        // 进度条
        int barX = x + 64;
        int pctLabelWidth = 48;
        int barWidth = contentWidth - 64 - pctLabelWidth - 8;
        int barHeight = 10;
        int barY = y + 2;

        var barPanel = new Panel
        {
            Location = new Point(barX, barY),
            Size = new Size(barWidth, barHeight),
            BackColor = isDark ? Color.FromArgb(26, 26, 62) : Color.FromArgb(230, 230, 235)
        };
        barPanel.Paint += (_, e) =>
        {
            double pct = Math.Clamp(item.Percentage, 0, 100);
            int fillWidth = (int)(barWidth * pct / 100);

            Color barColor;
            if (pct >= config.CriticalThreshold)
                barColor = isDark ? Color.FromArgb(214, 48, 49) : Color.FromArgb(200, 0, 0);
            else if (pct >= config.WarningThreshold)
                barColor = isDark ? Color.FromArgb(225, 112, 85) : Color.FromArgb(200, 150, 0);
            else
                barColor = isDark ? Color.FromArgb(0, 206, 201) : Color.FromArgb(0, 160, 140);

            using var brush = new SolidBrush(barColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillRectangle(brush, 0, 0, fillWidth, barHeight);
        };
        panel.Controls.Add(barPanel);

        // 百分比
        double pctValue = item.Percentage;
        Color pctColor;
        if (pctValue >= config.CriticalThreshold)
            pctColor = Color.FromArgb(255, 118, 117);
        else if (pctValue >= config.WarningThreshold)
            pctColor = Color.FromArgb(253, 203, 110);
        else
            pctColor = isDark ? Color.FromArgb(0, 206, 201) : Color.FromArgb(0, 160, 140);

        int pctX = barX + barWidth + 8;
        var lblPct = CreateLabel($"{pctValue:F0}%", 11, FontStyle.Bold, pctColor, pctX, y);
        panel.Controls.Add(lblPct);

        return y + 26;
    }

    private static Label CreateLabel(string text, float fontSize, FontStyle style, Color color, int x, int y)
    {
        return new Label
        {
            Text = text,
            Font = new Font("Microsoft YaHei UI", fontSize, style),
            ForeColor = color,
            BackColor = Color.Transparent,
            Location = new Point(x, y),
            AutoSize = true
        };
    }

    private static Panel CreateSeparator(Color color, int x, int y, int width)
    {
        return new Panel
        {
            BackColor = color,
            Location = new Point(x, y),
            Size = new Size(width, 1)
        };
    }

    /// <summary>
    /// 配额数据更新回调
    /// </summary>
    private void OnQuotaUpdated(QuotaSnapshot snapshot)
    {
        if (!_notifyIcon.Visible) return;

        // 更新托盘图标
        _notifyIcon.Icon = TrayIconFactory.GetIcon(snapshot.Status);

        // 更新 tooltip
        string mcpPct = $"{snapshot.McpQuota.Percentage:F0}%";
        string tokenPct = $"{snapshot.Token5hQuota.Percentage:F0}%";
        string calls = FormatNumber(snapshot.CallCount);
        _notifyIcon.Text = $"GLM: MCP {mcpPct} | 5h {tokenPct} | {calls}";

        // 如果超限，弹通知
        if (snapshot.Status == QuotaStatus.Critical && !snapshot.IsOffline)
        {
            _notifyIcon.ShowBalloonTip(5000,
                "GLM 配额预警",
                $"MCP 配额: {mcpPct}\n5h Token: {tokenPct}",
                ToolTipIcon.Warning);
        }

        // 更新浮动条
        _floatingBar?.UpdateData(snapshot);
    }

    /// <summary>
    /// 错误回调
    /// </summary>
    private void OnQuotaError(string message)
    {
        System.Diagnostics.Debug.WriteLine($"[QuotaService] {message}");
    }

    /// <summary>
    /// 配置变更回调
    /// </summary>
    private void OnConfigChanged(AppConfig config)
    {
        _themeService.SetMode(config.Theme);
        StartMonitoring();

        if (config.ShowFloatingBar)
            ShowFloatingBar();
        else
            HideFloatingBar();
    }

    /// <summary>
    /// 启动/重启监控
    /// </summary>
    private void StartMonitoring()
    {
        var config = _configService.Config;
        string token = config.GetEffectiveAuthToken();
        string baseUrl = config.GetBaseUrl();

        _quotaService.StartPolling(
            config.GetEffectivePollingInterval(),
            () => config.GetEffectiveAuthToken(),
            () => config.GetBaseUrl());
    }

    /// <summary>
    /// 手动刷新
    /// </summary>
    private async void RefreshQuota()
    {
        var config = _configService.Config;
        await _quotaService.QuickRefresh(config.GetEffectiveAuthToken(), config.GetBaseUrl());
    }

    /// <summary>
    /// 显示/隐藏浮动条
    /// </summary>
    private void OnToggleFloatingBar(object? sender, EventArgs e)
    {
        var config = _configService.Config;
        config.ShowFloatingBar = _floatingBarToggle?.Checked ?? false;
        _configService.Save(config);
    }

    /// <summary>
    /// 切换开机自启动
    /// </summary>
    private void OnToggleAutoStart(object? sender, EventArgs e)
    {
        var config = _configService.Config;
        config.AutoStart = _autoStartToggle?.Checked ?? false;
        _configService.Save(config);

        SetAutoStart(config.AutoStart);
    }

    /// <summary>
    /// 显示浮动条
    /// </summary>
    private void ShowFloatingBar()
    {
        if (_floatingBar != null) return;

        _floatingBar = new FloatingBar(_themeService, _configService.Config);
        _floatingBar.UpdateData(_quotaService.GetLastSnapshot());
        _floatingBar.Show();
    }

    /// <summary>
    /// 隐藏浮动条
    /// </summary>
    private void HideFloatingBar()
    {
        _floatingBar?.Close();
        _floatingBar?.Dispose();
        _floatingBar = null;
    }

    /// <summary>
    /// 显示设置窗口
    /// </summary>
    private void ShowSettings()
    {
        using var form = new SettingsForm(_configService, _themeService);
        form.ShowDialog();
    }

    /// <summary>
    /// 设置开机自启动（写注册表）
    /// </summary>
    private static void SetAutoStart(bool enable)
    {
        const string regPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string appName = "GLMQuotaMonitor";

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(regPath, true);
            if (key == null) return;

            if (enable)
            {
                string exePath = Environment.ProcessPath ?? "";
                key.SetValue(appName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(appName, false);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AutoStart] {ex.Message}");
        }
    }

    /// <summary>
    /// 格式化数字显示
    /// </summary>
    private static string FormatNumber(long num)
    {
        if (num >= 1_000_000_000) return $"{num / 1_000_000_000.0:F1}B";
        if (num >= 1_000_000) return $"{num / 1_000_000.0:F1}M";
        if (num >= 1_000) return $"{num / 1_000.0:F1}K";
        return num.ToString();
    }

    /// <summary>
    /// 退出应用
    /// </summary>
    private void ExitApplication()
    {
        _notifyIcon.Visible = false;
        _quotaService.Dispose();
        _themeService.Dispose();
        _floatingBar?.Dispose();
        TrayIconFactory.ClearCache();
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _contextMenu.Dispose();
            _quotaService.Dispose();
            _themeService.Dispose();
            _floatingBar?.Dispose();
            _popup?.Dispose();
            TrayIconFactory.ClearCache();
        }
        base.Dispose(disposing);
    }
}
