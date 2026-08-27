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
    /// <summary>UI 线程同步上下文；构造时经 _uiMarshaller 触发安装，永不为主线程外的 null</summary>
    private readonly SynchronizationContext _syncContext;

    private FloatingWidget? _floatingBar;
    /// <summary>临界弹窗去抖：只在跨入临界状态时提示一次，回落后再重新武装</summary>
    private bool _criticalNotified;
    /// <summary>用于后台线程事件投递的隐藏控件；构造它也让 WinForms 安装好同步上下文</summary>
    private readonly Control _uiMarshaller;

    public TrayApplicationContext()
    {
        // 构造控件前 Current 为 null（Application.Run 尚未开始）：
        // 先创建一个 Control 触发 WinFormsSynchronizationContext 安装，再捕获供事件线程投递使用
        _uiMarshaller = new Control();
        _syncContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
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
        // （ShowDialog 自带消息循环，无需等待 Application.Run；异常至少留下日志，不能静默）
        if (!_configService.HasToken())
        {
            try { ShowSettings(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Settings] auto-open failed: {ex}");
                DebugLog($"auto-open settings failed: {ex.GetType().Name}: {ex.Message}");
            }
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
        if (SynchronizationContext.Current != _syncContext)
        {
            // 后台网络线程：统一投递到 UI 上下文，绝不能因为拿不到上下文而静默丢数据
            _syncContext.Post(_ => OnQuotaUpdated(snapshot), null);
            return;
        }
        if (!_notifyIcon.Visible) return;

        var config = _configService.Config;
        var status = snapshot.GetStatus(config.WarningThreshold, config.CriticalThreshold);

        // 更新托盘图标
        _notifyIcon.Icon = TrayIconFactory.GetIcon(status);

        // 更新 tooltip（与卡片一致：<10% 显示一位小数）
        static string Pct(double v) => v > 0 && v < 10 ? $"{v:F1}%" : $"{v:F0}%";
        string mcpPct = Pct(snapshot.McpQuota.Percentage);
        string tokenPct = Pct(snapshot.Token5hQuota.Percentage);
        string calls = FormatNumber(snapshot.CallCount);
        string resetInfo = "";
        if (snapshot.Token5hQuota.ResetDateTime.HasValue)
            resetInfo = $" | 重置{snapshot.Token5hQuota.ResetDateTime.Value:HH:mm}";
        string tooltip = $"GLM: MCP {mcpPct} | 5h {tokenPct} | {calls}{resetInfo}";
        if (tooltip.Length > 127) tooltip = tooltip[..127];
        _notifyIcon.Text = tooltip;

        // 跨入临界状态时弹一次通知；离线期间保持武装不解除，避免 Critical↔Offline 抖动退化为逐周期弹窗
        if (status == QuotaStatus.Critical && !snapshot.IsOffline)
        {
            if (!_criticalNotified)
            {
                _criticalNotified = true;
                _notifyIcon.ShowBalloonTip(5000,
                    "GLM 配额预警",
                    $"MCP 配额: {mcpPct}\n5h Token: {tokenPct}",
                    ToolTipIcon.Warning);
            }
        }
        else if (!snapshot.IsOffline)
        {
            _criticalNotified = false;
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

    /// <summary>显示设置窗口</summary>
    private void ShowSettings()
    {
        DebugLog("ShowSettings enter");
        using var form = new SettingsForm(_configService, _themeService);
        form.ShowDialog();
        DebugLog("ShowSettings closed");
    }

    /// <summary>调试日志（设 GLMQM_DEBUG=1 环境变量启用）；统一走 ConfigService 的实现</summary>
    private static void DebugLog(string message) => ConfigService.SafeDebugLog(message);

    /// <summary>
    /// 循环切换主题：Auto → Dark → Light → Auto
    /// </summary>
    private void CycleTheme()
    {
        var config = _configService.Config;
        config.Theme = config.Theme switch
        {
            ThemeMode.Auto => ThemeMode.Dark,
            ThemeMode.Dark => ThemeMode.Light,
            ThemeMode.Light => ThemeMode.Auto,
            _ => ThemeMode.Auto
        };
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
            _uiMarshaller.Dispose();
            TrayIconFactory.ClearCache();
        }
        base.Dispose(disposing);
    }
}
