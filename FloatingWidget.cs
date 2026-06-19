using System.Drawing;
using System.Drawing.Drawing2D;
using GLMQuotaMonitor.Models;

namespace GLMQuotaMonitor;

/// <summary>
/// 浮动配额卡片 Widget
/// 三列布局：标签 | 进度条 | 百分比
/// Catppuccin Mocha (深色) / Latte (浅色) 配色
/// 右键菜单：刷新、浮动条、自启、主题、设置、退出
/// </summary>
public class FloatingWidget : Form
{
    private const int CardWidth = 240;
    private const int CardHeight = 138;
    private const int CardHPadding = 20;
    private const int CardVPadding = 14;

    private const int LabelColWidth = 70;
    private const int BarWidth = 65;
    private const int BarHeight = 14;
    private const int PctColWidth = 50;
    private const int ColGap = 10;
    private const int BarPctGap = 5;
    private const int RowHeight = 24;

    private const int EdgeSnapThreshold = 10;
    private const int RevealEdgeWidth = 4;
    private const int AutoHideDelayMs = 500;

    private readonly ThemeService _themeService;
    private readonly ConfigService _configService;
    private readonly Action _onRefresh;
    private readonly Action _onToggleFloat;
    private readonly Action _onToggleAutoStart;
    private readonly Action _onCycleTheme;
    private readonly Action _onShowSettings;
    private readonly System.Windows.Forms.Timer _hideTimer;
    private readonly Action<bool> _themeChangedHandler;

    private QuotaSnapshot _snapshot = new() { IsOffline = true };
    private bool _isDragging;
    private Point _dragOffset;
    private bool _isSnapped;
    private DockStyle _snapEdge = DockStyle.None;
    private bool _isExpanded;

    // 缓存字体
    private readonly Font _labelFont = new("Segoe UI", 9f);
    private readonly Font _valueFont = new("Segoe UI", 11f, FontStyle.Bold);
    private readonly Font _detailFont = new("Segoe UI", 8f);
    private readonly Font _tinyFont = new("Segoe UI", 7.5f);

    public FloatingWidget(
        ThemeService themeService,
        ConfigService configService,
        Action onRefresh,
        Action onToggleFloat,
        Action onToggleAutoStart,
        Action onCycleTheme,
        Action onShowSettings)
    {
        _themeService = themeService;
        _configService = configService;
        _onRefresh = onRefresh;
        _onToggleFloat = onToggleFloat;
        _onToggleAutoStart = onToggleAutoStart;
        _onCycleTheme = onCycleTheme;
        _onShowSettings = onShowSettings;
        var config = configService.Config;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(30, 30, 46); // Mocha Base, updated per-frame in OnPaint

        Size = new Size(CardWidth, CardHeight);

        if (config.FloatingBarX.HasValue && config.FloatingBarY.HasValue)
            Location = new Point(config.FloatingBarX.Value, config.FloatingBarY.Value);
        else
        {
            var screen = Screen.PrimaryScreen!.WorkingArea;
            Location = new Point(screen.Right - CardWidth - 20, screen.Top + screen.Height / 2 - CardHeight / 2);
        }

        // 右键菜单
        var menu = new ContextMenuStrip();
        var refreshItem = new ToolStripMenuItem("⟳ 立即刷新");
        refreshItem.Click += (_, _) => _onRefresh();
        menu.Items.Add(refreshItem);
        menu.Items.Add(new ToolStripSeparator());
        var floatToggle = new ToolStripMenuItem("显示浮动条") { CheckOnClick = true, Checked = true };
        floatToggle.Click += (_, _) => _onToggleFloat();
        menu.Items.Add(floatToggle);
        var autoStartToggle = new ToolStripMenuItem("开机自启动") { CheckOnClick = true, Checked = config.AutoStart };
        autoStartToggle.Click += (_, _) => _onToggleAutoStart();
        menu.Items.Add(autoStartToggle);
        menu.Items.Add(new ToolStripSeparator());
        var themeItem = new ToolStripMenuItem("☀ 切换主题");
        themeItem.Click += (_, _) => _onCycleTheme();
        menu.Items.Add(themeItem);
        var settingsItem = new ToolStripMenuItem("⚙ 设置");
        settingsItem.Click += (_, _) => _onShowSettings();
        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());
        var exitItem = new ToolStripMenuItem("✕ 退出");
        exitItem.Click += (_, _) => Application.Exit();
        menu.Items.Add(exitItem);
        ContextMenuStrip = menu;

        _hideTimer = new System.Windows.Forms.Timer { Interval = AutoHideDelayMs };
        _hideTimer.Tick += (_, _) => CollapseIfSnapped();

        Paint += OnPaint;
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        MouseEnter += (_, _) => { _hideTimer.Stop(); if (_isSnapped && !_isExpanded) Expand(); };
        MouseLeave += (_, _) => { if (_isSnapped) _hideTimer.Start(); };

        _themeChangedHandler = (_) =>
        {
            if (InvokeRequired) BeginInvoke(() => Invalidate());
            else Invalidate();
        };
        _themeService.ThemeChanged += _themeChangedHandler;
    }

    public void UpdateData(QuotaSnapshot snapshot)
    {
        _snapshot = snapshot;
        if (InvokeRequired) BeginInvoke(() => Invalidate());
        else Invalidate();
    }

    #region 绘制

    private void OnPaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        bool isDark = _themeService.IsDark;
        var cfg = _configService.Config;
        int w = Width, h = Height;

        // Catppuccin 颜色
        Color bg = isDark ? Color.FromArgb(30, 30, 46) : Color.FromArgb(239, 241, 245);
        Color borderColor = isDark ? Color.FromArgb(49, 50, 68) : Color.FromArgb(204, 208, 218);
        Color lineColor = isDark ? Color.FromArgb(69, 71, 90) : Color.FromArgb(188, 192, 204);
        Color statColor = isDark ? Color.FromArgb(127, 132, 156) : Color.FromArgb(140, 143, 161);
        Color footerColor = isDark ? Color.FromArgb(88, 91, 112) : Color.FromArgb(156, 160, 176);

        // 同步 BackColor（用于窗口方角区域，与背景色一致）
        if (BackColor != bg) BackColor = bg;

        // 背景
        using (var bgBrush = new SolidBrush(bg))
        using (var path = GraphicsExtensions.MakeRoundRect(0, 0, w, h, 4))
            g.FillPath(bgBrush, path);

        // 边框
        using (var borderPen = new Pen(borderColor))
        using (var path = GraphicsExtensions.MakeRoundRect(0, 0, w - 1, h - 1, 4))
            g.DrawPath(borderPen, path);

        int x = CardHPadding;
        int y = CardVPadding;
        int cw = w - CardHPadding * 2;

        // ═══ MCP 配额 ═══
        y = DrawQuotaRow(g, _snapshot.McpQuota, cfg, isDark, x, y);
        y += 8;

        // ═══ 5h Token ═══
        y = DrawQuotaRow(g, _snapshot.Token5hQuota, cfg, isDark, x, y);
        y += 10;

        // ═══ 分隔线 ═══
        using (var linePen = new Pen(lineColor))
            g.DrawLine(linePen, x, y, x + cw, y);
        y += 6;

        // ═══ 统计行（居中）═══
        if (!_snapshot.IsOffline)
        {
            using var statBrush = new SolidBrush(statColor);
            string line = $"{FormatNumber(_snapshot.CallCount)} 次调用  |  {FormatTokenUsage(_snapshot.TokenUsage)} Token";
            var textSize = g.MeasureString(line, _detailFont);
            float statsX = (w - textSize.Width) / 2;
            g.DrawString(line, _detailFont, statBrush, statsX, y);
        }
        y += 16;

        // ═══ 分隔线 ═══
        using (var linePen = new Pen(lineColor))
            g.DrawLine(linePen, x, y, x + cw, y);
        y += 8;

        // ═══ 底部行 ═══
        using var footerBrush = new SolidBrush(footerColor);

        // 左侧：重置时间
        if (_snapshot.Token5hQuota.ResetDateTime.HasValue)
        {
            string resetText = $"重置 {_snapshot.Token5hQuota.ResetDateTime.Value:HH:mm}";
            g.DrawString(resetText, _tinyFont, footerBrush, x, y);
        }

        // 右侧：更新时间
        string timeText = _snapshot.IsOffline ? "离线" : $"{_snapshot.Timestamp:HH:mm} 更新";
        var timeSize = g.MeasureString(timeText, _tinyFont);
        g.DrawString(timeText, _tinyFont, footerBrush, x + cw - timeSize.Width, y);
    }

    /// <summary>
    /// 绘制单行配额（三列：标签 | 进度条 | 百分比）
    /// </summary>
    private int DrawQuotaRow(Graphics g, QuotaItem item, AppConfig config, bool isDark, int x, int y)
    {
        double pct = Math.Clamp(item.Percentage, 0, 100);

        int labelX = x;
        int barX = x + LabelColWidth + ColGap;
        int pctX = barX + BarWidth + BarPctGap;

        // ── 标签 ──
        Color labelColor = isDark ? Color.FromArgb(166, 173, 200) : Color.FromArgb(108, 111, 133);
        using var labelBrush = new SolidBrush(labelColor);
        float labelY = y + (RowHeight - _labelFont.GetHeight(g)) / 2;
        g.DrawString(item.Name, _labelFont, labelBrush, labelX, labelY);

        // ── 进度条 ──
        int barY = y + (RowHeight - BarHeight) / 2;
        Color barBg = isDark ? Color.FromArgb(49, 50, 68) : Color.FromArgb(204, 208, 218);
        using (var bgBrush = new SolidBrush(barBg))
            g.FillRectangle(bgBrush, barX, barY, BarWidth, BarHeight);

        // 颜色：MCP 蓝，5h 青，超限黄/红
        Color normalColor;
        if (item.Type == "TIME_LIMIT")
            normalColor = isDark ? Color.FromArgb(137, 180, 250) : Color.FromArgb(30, 102, 245);
        else
            normalColor = isDark ? Color.FromArgb(148, 226, 213) : Color.FromArgb(23, 146, 153);

        Color pctColor;
        if (pct >= config.CriticalThreshold) pctColor = isDark ? Color.FromArgb(243, 139, 168) : Color.FromArgb(210, 15, 57);
        else if (pct >= config.WarningThreshold) pctColor = isDark ? Color.FromArgb(249, 226, 175) : Color.FromArgb(223, 142, 29);
        else pctColor = normalColor;

        int fillW = (int)(BarWidth * pct / 100);
        if (fillW > 0)
        {
            using var fillBrush = new SolidBrush(pctColor);
            g.FillRectangle(fillBrush, barX, barY, fillW, BarHeight);
        }

        // ── 百分比（右对齐）──
        using var pctBrush = new SolidBrush(pctColor);
        string pctText = $"{pct:F0}%";
        var pctSize = g.MeasureString(pctText, _valueFont);
        float pctDrawX = pctX + PctColWidth - pctSize.Width;
        float pctDrawY = y + (RowHeight - pctSize.Height) / 2;
        g.DrawString(pctText, _valueFont, pctBrush, pctDrawX, pctDrawY);

        return y + RowHeight;
    }

    #endregion

    #region 鼠标事件

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        _isDragging = true;
        _dragOffset = e.Location;
        if (_isSnapped)
        {
            _isSnapped = false;
            _isExpanded = false;
            _snapEdge = DockStyle.None;
        }
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        var sp = PointToScreen(e.Location);
        Location = new Point(sp.X - _dragOffset.X, sp.Y - _dragOffset.Y);
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;

        var screen = Screen.PrimaryScreen!.WorkingArea;
        if (Location.X <= screen.Left + EdgeSnapThreshold)
            SnapToEdge(DockStyle.Left, screen);
        else if (Location.X + Width >= screen.Right - EdgeSnapThreshold)
            SnapToEdge(DockStyle.Right, screen);
        else if (Location.Y <= screen.Top + EdgeSnapThreshold)
            SnapToEdge(DockStyle.Top, screen);

        PersistPosition();
    }

    #endregion

    #region 贴边吸附

    private void SnapToEdge(DockStyle edge, Rectangle screen)
    {
        _isSnapped = true;
        _snapEdge = edge;
        _isExpanded = false;
        switch (edge)
        {
            case DockStyle.Left: Location = new Point(screen.Left - Width + RevealEdgeWidth, Location.Y); break;
            case DockStyle.Right: Location = new Point(screen.Right - RevealEdgeWidth, Location.Y); break;
            case DockStyle.Top: Location = new Point(Location.X, screen.Top - Height + RevealEdgeWidth); break;
        }
    }

    private void Expand()
    {
        if (!_isSnapped) return;
        _isExpanded = true;
        var screen = Screen.PrimaryScreen!.WorkingArea;
        switch (_snapEdge)
        {
            case DockStyle.Left: Location = new Point(screen.Left, Location.Y); break;
            case DockStyle.Right: Location = new Point(screen.Right - Width, Location.Y); break;
            case DockStyle.Top: Location = new Point(Location.X, screen.Top); break;
        }
    }

    private void CollapseIfSnapped()
    {
        if (!_isSnapped || !_isExpanded) return;
        _isExpanded = false;
        var screen = Screen.PrimaryScreen!.WorkingArea;
        switch (_snapEdge)
        {
            case DockStyle.Left: Location = new Point(screen.Left - Width + RevealEdgeWidth, Location.Y); break;
            case DockStyle.Right: Location = new Point(screen.Right - RevealEdgeWidth, Location.Y); break;
            case DockStyle.Top: Location = new Point(Location.X, screen.Top - Height + RevealEdgeWidth); break;
        }
    }

    #endregion

    private void PersistPosition()
    {
        try
        {
            var config = _configService.Config;
            config.FloatingBarX = Location.X;
            config.FloatingBarY = Location.Y;
            _configService.Save(config);
        }
        catch { }
    }

    private static string FormatNumber(long n)
    {
        if (n >= 1_000_000_000) return $"{n / 1_000_000_000.0:F1}B";
        if (n >= 1_000_000) return $"{n / 1_000_000.0:F1}M";
        if (n >= 1_000) return $"{n / 1_000.0:F1}K";
        return n.ToString();
    }

    private static string FormatTokenUsage(long tokens)
    {
        if (tokens >= 1_000_000_000) return $"{tokens / 1_000_000_000.0:F1}B";
        if (tokens >= 1_000_000) return $"{tokens / 1_000_000.0:F0}M";
        if (tokens >= 1_000) return $"{tokens / 1_000.0:F0}K";
        return tokens.ToString();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hideTimer.Dispose();
            _themeService.ThemeChanged -= _themeChangedHandler;
            _labelFont.Dispose();
            _valueFont.Dispose();
            _detailFont.Dispose();
            _tinyFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
