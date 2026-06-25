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

    private FloatingWidget? _floatingBar;
    private long _lastNotifiedResetTime = long.MinValue;
    private DateTime _lastNotifiedTime = DateTime.MinValue;

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

        // 订阅主题变更（更新菜单渲染器）
        _themeService.ThemeChanged += OnThemeChanged;
    }

    private void OnThemeChanged(bool isDark)
    {
        _contextMenu.Renderer = isDark
            ? new ToolStripDarkRenderer()
            : new ToolStripProfessionalRenderer();
    }

    /// <summary>
    /// 创建右键菜单
    /// </summary>
    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip();
        if (_themeService.IsDark)
            menu.Renderer = new ToolStripDarkRenderer();

        var settingsItem = new ToolStripMenuItem("⚙ 设置");
        settingsItem.Click += (_, _) => ShowSettings();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new ToolStripSeparator());

        var locateItem = new ToolStripMenuItem("📍 定位浮动条");
        locateItem.Click += (_, _) => LocateFloatingBar();
        menu.Items.Add(locateItem);

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
        string resetInfo = "";
        if (snapshot.Token5hQuota.ResetDateTime.HasValue)
            resetInfo = $" | 重置{snapshot.Token5hQuota.ResetDateTime.Value:HH:mm}";
        string tooltip = $"GLM: MCP {mcpPct} | 5h {tokenPct} | {calls}{resetInfo}";
        if (tooltip.Length > 127) tooltip = tooltip[..127];
        _notifyIcon.Text = tooltip;

        // 如果超限，弹通知
        if (status == QuotaStatus.Critical && !snapshot.IsOffline)
        {
            long resetTime = snapshot.Token5hQuota.NextResetTime;
            bool shouldNotify;

            if (resetTime > 0)
                shouldNotify = resetTime != _lastNotifiedResetTime;
            else
                shouldNotify = (DateTime.Now - _lastNotifiedTime).TotalHours >= 1;

            if (shouldNotify)
            {
                _lastNotifiedResetTime = resetTime;
                _lastNotifiedTime = DateTime.Now;
                _notifyIcon.ShowBalloonTip(5000,
                    "GLM 配额预警",
                    $"MCP 配额: {mcpPct}\n5h Token: {tokenPct}",
                    ToolTipIcon.Warning);
            }
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
    /// 定位浮动条（从贴边位置展开 3 秒后收回）
    /// </summary>
    private void LocateFloatingBar()
    {
        _floatingBar?.Locate();
    }

    /// <summary>
    /// 显示浮动条
    /// </summary>
    private void ShowFloatingBar()
    {
        if (_floatingBar != null) return;

        _floatingBar = new FloatingWidget(
            _themeService, _configService,
            RefreshQuota,
            LocateFloatingBar,
            CycleTheme,
            ShowSettings);
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

    private void CycleTheme()
    {
        var config = _configService.Config;
        config.Theme = _themeService.IsDark ? ThemeMode.Light : ThemeMode.Dark;
        _configService.Save(config);
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
            _themeService.ThemeChanged -= OnThemeChanged;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _contextMenu.Dispose();
            _quotaService.Dispose();
            _themeService.Dispose();
            _floatingBar?.Dispose();
            TrayIconFactory.ClearCache();
        }
        base.Dispose(disposing);
    }
}
