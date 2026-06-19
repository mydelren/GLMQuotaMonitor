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
    private readonly SynchronizationContext? _syncContext;

    private ToolStripMenuItem? _floatingBarToggle;
    private ToolStripMenuItem? _autoStartToggle;
    private ToolStripDropDown? _popup;
    private FloatingWidget? _floatingBar;

    public TrayApplicationContext()
    {
        _syncContext = SynchronizationContext.Current;
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
        _floatingBarToggle.Click += OnToggleFloatingWidget;
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
    /// 显示配额详情弹窗（自绘面板，匹配 HTML demo 风格）
    /// </summary>
    private void ShowPopup()
    {
        if (_popup?.Visible == true)
        {
            _popup.Close();
            return;
        }

        _popup?.Dispose();
        _popup = null;

        var snapshot = _quotaService.GetLastSnapshot();
        bool isDark = _themeService.IsDark;
        var config = _configService.Config;

        int pw = 360, ph = 310;

        // 全自绘面板
        var panel = new Panel { Size = new Size(pw, ph), BackColor = Color.Transparent };
        var snapshotRef = snapshot;
        panel.Paint += (_, e) => PaintPopup(e.Graphics, snapshotRef, isDark, config, pw, ph);

        // 刷新按钮（叠加在面板上）
        var btnRefresh = new Button
        {
            Text = "⟳ 刷新",
            FlatStyle = FlatStyle.Flat,
            ForeColor = isDark ? Color.FromArgb(123, 140, 222) : Color.FromArgb(60, 80, 180),
            BackColor = Color.Transparent,
            Location = new Point(pw - 100, ph - 36),
            Size = new Size(84, 26),
            Cursor = Cursors.Hand,
            TabStop = false
        };
        btnRefresh.FlatAppearance.BorderSize = 0;
        btnRefresh.Click += (_, _) => RefreshQuota();
        panel.Controls.Add(btnRefresh);

        var host = new ToolStripControlHost(panel) { AutoSize = false, Size = new Size(pw, ph), Margin = new Padding(0), Padding = new Padding(0) };
        _popup = new ToolStripDropDown { DropShadowEnabled = true, AutoClose = true, Padding = new Padding(0), Margin = new Padding(0) };
        _popup.Items.Add(host);

        var screen = Screen.PrimaryScreen!.WorkingArea;
        _popup.Show(screen.Right - pw - 10, screen.Bottom - ph - 10);
    }

    /// <summary>
    /// 全自绘弹窗（匹配 HTML demo：深蓝底、圆角进度条、统计数据行）
    /// </summary>
    private void PaintPopup(Graphics g, QuotaSnapshot s, bool isDark, AppConfig config, int w, int h)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // 颜色
        Color bgHeader = isDark ? Color.FromArgb(15, 52, 96) : Color.FromArgb(230, 235, 245);
        Color bgBody = isDark ? Color.FromArgb(22, 33, 62) : Color.FromArgb(245, 245, 250);
        Color bgFooter = isDark ? Color.FromArgb(0, 0, 0, 40) : Color.FromArgb(0, 0, 0, 10);
        Color cText = isDark ? Color.FromArgb(224, 224, 224) : Color.FromArgb(30, 30, 30);
        Color cSub = isDark ? Color.FromArgb(123, 140, 176) : Color.FromArgb(100, 100, 100);
        Color cSep = isDark ? Color.FromArgb(30, 42, 74) : Color.FromArgb(220, 220, 230);
        Color cBarBg = isDark ? Color.FromArgb(26, 26, 62) : Color.FromArgb(230, 230, 235);

        // 圆角背景
        using (var path = GraphicsExtensions.MakeRoundRect(0, 0, w, h, 10))
        using (var bgBrush = new SolidBrush(bgBody))
            g.FillPath(bgBrush, path);

        int pad = 20;
        int cw = w - pad * 2;
        float y = pad;

        // ═══ Header (56px) ═══
        using (var headerBrush = new SolidBrush(bgHeader))
            g.FillRectangle(headerBrush, 0, 0, w, 56);

        // Logo
        using var logoBrush = new SolidBrush(Color.FromArgb(0, 180, 228));
        g.FillRoundedRectangle(logoBrush, pad, 14, 28, 28, 7);
        using var logoFont = new Font("Segoe UI", 14f, FontStyle.Bold);
        using var whiteBrush = new SolidBrush(Color.White);
        var logoSize = g.MeasureString("G", logoFont);
        g.DrawString("G", logoFont, whiteBrush, pad + (28 - logoSize.Width) / 2, 14 + (28 - logoSize.Height) / 2);

        // 标题
        using var titleFont = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold);
        using var titleBrush = new SolidBrush(cText);
        g.DrawString("GLM 配额监控", titleFont, titleBrush, pad + 36, 17);

        // 平台标签
        string platform = DetectPlatformName();
        using var platFont = new Font("Microsoft YaHei UI", 8.5f);
        var platSize = g.MeasureString(platform, platFont);
        int platW = (int)platSize.Width + 16;
        int platX = w - pad - platW;
        using var platBg = new SolidBrush(Color.FromArgb(30, 123, 140, 222));
        g.FillRoundedRectangle(platBg, platX, 18, platW, 22, 11);
        using var platFg = new SolidBrush(Color.FromArgb(123, 140, 222));
        g.DrawString(platform, platFont, platFg, platX + 8, 19);

        y = 66;

        // ═══ 分隔线 ═══
        using var sepPen = new Pen(cSep);
        g.DrawLine(sepPen, pad, y, w - pad, y);
        y += 16;

        // ═══ MCP 配额行 ═══
        y = PaintQuotaRow(g, s.McpQuota, isDark, config, pad, y, cw, cSub, cBarBg);
        y += 14;

        // ═══ 5h Token 行 ═══
        y = PaintQuotaRow(g, s.Token5hQuota, isDark, config, pad, y, cw, cSub, cBarBg);
        y += 16;

        // ═══ 分隔线 ═══
        g.DrawLine(sepPen, pad, y, w - pad, y);
        y += 16;

        // ═══ 统计行 ═══
        if (!s.IsOffline)
        {
            using var statFont = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold);
            using var statSub = new Font("Microsoft YaHei UI", 8.5f);
            using var statFg = new SolidBrush(cText);
            using var statSubFg = new SolidBrush(cSub);

            int colW = cw / 2;
            // 调用次数
            g.DrawString(FormatNumber(s.CallCount), statFont, statFg, pad, y);
            g.DrawString("调用次数 (24H)", statSub, statSubFg, pad, y + 20);
            // Token 用量
            g.DrawString(FormatTokenUsage(s.TokenUsage), statFont, statFg, pad + colW, y);
            g.DrawString("Token 用量 (24H)", statSub, statSubFg, pad + colW, y + 20);
        }
        y += 48;

        // ═══ Footer ═══
        using (var footerBrush = new SolidBrush(bgFooter))
            g.FillRectangle(footerBrush, 0, h - 40, w, 40);
        g.DrawLine(sepPen, 0, h - 40, w, h - 40);

        string timeStr = s.IsOffline ? "离线" : $"🕐 上次刷新: {s.Timestamp:HH:mm:ss}";
        using var timeFont = new Font("Microsoft YaHei UI", 9f);
        using var timeBrush = new SolidBrush(cSub);
        g.DrawString(timeStr, timeFont, timeBrush, pad, h - 30);
    }

    /// <summary>
    /// 绘制配额行（名称+百分比 / 进度条 / 用量小字，三行布局）
    /// 匹配 HTML demo 风格，避免文字和进度条重叠
    /// </summary>
    private static float PaintQuotaRow(Graphics g, QuotaItem item, bool isDark, AppConfig config,
        int x, float y, int cw, Color cSub, Color cBarBg)
    {
        double pct = Math.Clamp(item.Percentage, 0, 100);

        // ── 第一行：名称（左）+ 百分比（右）──
        using var labelFont = new Font("Microsoft YaHei UI", 9.5f);
        using var labelBrush = new SolidBrush(cSub);
        g.DrawString(item.Name, labelFont, labelBrush, x, y);

        Color pctColor;
        if (pct >= config.CriticalThreshold)
            pctColor = Color.FromArgb(255, 118, 117);
        else if (pct >= config.WarningThreshold)
            pctColor = Color.FromArgb(255, 220, 100);
        else
            pctColor = isDark ? Color.FromArgb(0, 206, 201) : Color.FromArgb(0, 160, 140);

        using var pctFont = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold);
        using var pctBrush = new SolidBrush(pctColor);
        string pctText = $"{pct:F0}%";
        var pctSize = g.MeasureString(pctText, pctFont);
        g.DrawString(pctText, pctFont, pctBrush, x + cw - pctSize.Width, y - 2);

        // ── 第二行：进度条（全宽，纯色）──
        float barY = y + 22;
        int barH = 8;
        using (var bgBrush = new SolidBrush(cBarBg))
            g.FillRoundedRectangle(bgBrush, x, barY, cw, barH, 4);

        int fillW = Math.Max(0, (int)(cw * pct / 100));
        if (fillW > 0)
        {
            Color barColor;
            if (pct >= config.CriticalThreshold)
                barColor = isDark ? Color.FromArgb(214, 48, 49) : Color.FromArgb(200, 0, 0);
            else if (pct >= config.WarningThreshold)
                barColor = isDark ? Color.FromArgb(225, 112, 85) : Color.FromArgb(200, 150, 0);
            else
                barColor = isDark ? Color.FromArgb(0, 206, 201) : Color.FromArgb(0, 160, 140);

            using var fillBrush = new SolidBrush(barColor);
            if (fillW < 10)
                g.FillRectangle(fillBrush, x, barY, fillW, barH);
            else
                g.FillRoundedRectangle(fillBrush, x, barY, fillW, barH, 4);
        }

        // ── 第三行：用量小字（进度条下方，留足间距）──
        string usageText = $"{FormatNumber(item.Used)} / {FormatNumber(item.Total)}";
        using var usageFont = new Font("Microsoft YaHei UI", 8.5f);
        using var usageBrush = new SolidBrush(cSub);
        g.DrawString(usageText, usageFont, usageBrush, x, barY + 12);

        return y + 46;
    }

    private string DetectPlatformName()
    {
        string baseUrl = _configService.Config.GetBaseUrl();
        if (baseUrl.Contains("z.ai")) return "Z.ai";
        if (baseUrl.Contains("dev.bigmodel")) return "智谱 (dev)";
        return "智谱 AI";
    }

    private static string FormatTokenUsage(long tokens)
    {
        if (tokens >= 1_000_000_000) return $"{tokens / 1_000_000_000.0:F1}B";
        if (tokens >= 1_000_000) return $"{tokens / 1_000_000.0:F0}M";
        if (tokens >= 1_000) return $"{tokens / 1_000.0:F0}K";
        return tokens.ToString();
    }

    /// <summary>
    /// 配额数据更新回调（QuotaService 在后台线程触发，需 marshal 到 UI 线程）
    /// </summary>
    private void OnQuotaUpdated(QuotaSnapshot snapshot)
    {
        if (SynchronizationContext.Current == null)
        {
            // 当前不在 UI 线程，通过 SynchronizationContext 调度
            var syncCtx = _syncContext;
            if (syncCtx != null)
                syncCtx.Post(_ => OnQuotaUpdated(snapshot), null);
            return;
        }
        if (!_notifyIcon.Visible) return;

        var config = _configService.Config;
        var status = snapshot.GetStatus(config.WarningThreshold, config.CriticalThreshold);

        // 更新托盘图标
        _notifyIcon.Icon = TrayIconFactory.GetIcon(status);

        // 更新 tooltip
        string mcpPct = $"{snapshot.McpQuota.Percentage:F0}%";
        string tokenPct = $"{snapshot.Token5hQuota.Percentage:F0}%";
        string calls = FormatNumber(snapshot.CallCount);
        string tooltip = $"GLM: MCP {mcpPct} | 5h {tokenPct} | {calls}";
        _notifyIcon.Text = tooltip.Length > 127 ? tooltip[..127] : tooltip;

        // 如果超限，弹通知
        if (status == QuotaStatus.Critical && !snapshot.IsOffline)
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
            HideFloatingWidget();
    }

    /// <summary>
    /// 启动/重启监控（闭包通过 _configService 读取最新配置，避免引用过期对象）
    /// </summary>
    private void StartMonitoring()
    {
        _quotaService.StartPolling(
            _configService.Config.GetEffectivePollingInterval(),
            () => _configService.Config.GetEffectiveAuthToken(),
            () => _configService.Config.GetBaseUrl());
    }

    /// <summary>
    /// 手动刷新
    /// </summary>
    private async void RefreshQuota()
    {
        var config = _configService.Config;
        await _quotaService.QuickRefresh(
            config.GetEffectiveAuthToken(),
            config.GetBaseUrl(),
            config.GetEffectivePollingInterval());
    }

    /// <summary>
    /// 显示/隐藏浮动条
    /// </summary>
    private void OnToggleFloatingWidget(object? sender, EventArgs e)
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

        _floatingBar = new FloatingWidget(_themeService, _configService);
        _floatingBar.UpdateData(_quotaService.GetLastSnapshot());
        _floatingBar.Show();
    }

    /// <summary>
    /// 隐藏浮动条
    /// </summary>
    private void HideFloatingWidget()
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
    /// 退出应用（清理由 Dispose 统一处理）
    /// </summary>
    private void ExitApplication()
    {
        _notifyIcon.Visible = false;
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
