using System.Drawing;
using System.Drawing.Drawing2D;
using GLMQuotaMonitor.Models;

namespace GLMQuotaMonitor;

/// <summary>
/// 浮动配额卡片 Widget
/// 三列布局：标签 | 进度条 | 百分比
/// 贴边后显示迷你进度条（竖条/横条）
/// Catppuccin Mocha (深色) / Latte (浅色) 配色
/// </summary>
public class FloatingWidget : Form
{
    // ═══ 卡片尺寸 ═══
    private const int CardWidth = 240;
    private const int CardHeight = 126;
    private const int CardHPadding = 20;
    private const int CardVPadding = 14;

    // ═══ 三列布局 ═══
    private const int LabelColWidth = 70;
    private const int BarWidth = 65;
    private const int BarHeight = 14;
    private const int PctColWidth = 50;
    private const int ColGap = 10;
    private const int BarPctGap = 5;
    private const int RowHeight = 24;

    // ═══ 贴边显示 ═══
    private const int EdgeBarWidth = 10;        // 进度条宽度
    private const int EdgeMarginDesktop = 3;    // 桌面侧留白
    private const int EdgeMarginScreen = 5;     // 屏幕边留白
    private const int EdgeStripVHeight = 80;    // 竖条高度
    private const int EdgeStripHWidth = 126;    // 横条宽度
    private const int EdgeSegGap = 3;           // 两段间距
    private const int EdgeSegPadding = 5;       // 段内 padding
    private const int EdgeSegInset = 1;         // 色块内缩
    private const int EdgeRadius = 3;           // 圆角

    // ═══ 其他 ═══
    private const int EdgeSnapThreshold = 10;
    private const int AutoHideDelayMs = 500;

    private readonly ThemeService _themeService;
    private readonly ConfigService _configService;
    private readonly Action _onRefresh;
    private readonly Action _onLocate;
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
        Action onLocate,
        Action onCycleTheme,
        Action onShowSettings)
    {
        _themeService = themeService;
        _configService = configService;
        _onRefresh = onRefresh;
        _onLocate = onLocate;
        _onCycleTheme = onCycleTheme;
        _onShowSettings = onShowSettings;
        var config = configService.Config;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(30, 30, 46);

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
        if (_themeService.IsDark)
            menu.Renderer = new ToolStripDarkRenderer();

        var refreshItem = new ToolStripMenuItem("⟳ 立即刷新");
        refreshItem.Click += (_, _) => _onRefresh();
        menu.Items.Add(refreshItem);
        menu.Items.Add(new ToolStripSeparator());
        var locateItem = new ToolStripMenuItem("📍 定位浮动条");
        locateItem.Click += (_, _) => _onLocate();
        menu.Items.Add(locateItem);
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
        MouseEnter += (_, _) =>
        {
            _hideTimer.Stop();
            if (_isSnapped && !_isExpanded) Expand();
        };
        MouseLeave += (_, _) => { if (_isSnapped) _hideTimer.Start(); };

        _themeChangedHandler = (isDark) =>
        {
            void Update()
            {
                // 更新菜单渲染器
                if (ContextMenuStrip != null)
                    ContextMenuStrip.Renderer = isDark
                        ? new ToolStripDarkRenderer()
                        : new ToolStripProfessionalRenderer();

                if (_isSnapped && !_isExpanded)
                    BackColor = isDark ? Color.FromArgb(24, 24, 37) : Color.FromArgb(230, 233, 239);
                else
                    BackColor = isDark ? Color.FromArgb(30, 30, 46) : Color.FromArgb(239, 241, 245);
                Invalidate();
            }
            if (InvokeRequired) BeginInvoke(Update);
            else Update();
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

        if (_isSnapped && !_isExpanded)
            PaintEdgeStrip(g, _themeService.IsDark, Width, Height);
        else
            PaintFullCard(g, _themeService.IsDark, _configService.Config, Width, Height);
    }

    /// <summary>
    /// 完整卡片绘制（正常/展开状态）
    /// </summary>
    private void PaintFullCard(Graphics g, bool isDark, AppConfig cfg, int w, int h)
    {
        // Catppuccin 颜色
        Color bg = isDark ? Color.FromArgb(30, 30, 46) : Color.FromArgb(239, 241, 245);
        Color borderColor = isDark ? Color.FromArgb(49, 50, 68) : Color.FromArgb(204, 208, 218);
        Color statColor = isDark ? Color.FromArgb(127, 132, 156) : Color.FromArgb(140, 143, 161);
        Color footerColor = isDark ? Color.FromArgb(88, 91, 112) : Color.FromArgb(156, 160, 176);

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
        y += 12;

        // ═══ 统计行（居中）═══
        if (!_snapshot.IsOffline)
        {
            using var statBrush = new SolidBrush(statColor);
            string line = $"{FormatNumber(_snapshot.CallCount)} 次调用  |  {FormatTokenUsage(_snapshot.TokenUsage)} Token";
            var textSize = g.MeasureString(line, _detailFont);
            float statsX = (w - textSize.Width) / 2;
            g.DrawString(line, _detailFont, statBrush, statsX, y);
        }
        y += 22;

        // ═══ 底部行 ═══
        using var footerBrush = new SolidBrush(footerColor);

        if (_snapshot.Token5hQuota.ResetDateTime.HasValue)
        {
            string resetText = $"重置 {_snapshot.Token5hQuota.ResetDateTime.Value:HH:mm}";
            g.DrawString(resetText, _tinyFont, footerBrush, x, y);
        }

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

        // 标签
        Color labelColor = isDark ? Color.FromArgb(166, 173, 200) : Color.FromArgb(108, 111, 133);
        using var labelBrush = new SolidBrush(labelColor);
        float labelY = y + (RowHeight - _labelFont.GetHeight(g)) / 2;
        g.DrawString(item.Name, _labelFont, labelBrush, labelX, labelY);

        // 进度条
        int barY = y + (RowHeight - BarHeight) / 2;
        Color barBg = isDark ? Color.FromArgb(49, 50, 68) : Color.FromArgb(204, 208, 218);
        using (var bgBrush = new SolidBrush(barBg))
            g.FillRectangle(bgBrush, barX, barY, BarWidth, BarHeight);

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

        // 百分比（右对齐）
        using var pctBrush = new SolidBrush(pctColor);
        string pctText = $"{pct:F0}%";
        var pctSize = g.MeasureString(pctText, _valueFont);
        float pctDrawX = pctX + PctColWidth - pctSize.Width;
        float pctDrawY = y + (RowHeight - pctSize.Height) / 2;
        g.DrawString(pctText, _valueFont, pctBrush, pctDrawX, pctDrawY);

        return y + RowHeight;
    }

    #endregion

    #region 贴边迷你条绘制

    /// <summary>
    /// 贴边迷你条绘制
    /// </summary>
    private void PaintEdgeStrip(Graphics g, bool isDark, int w, int h)
    {
        Color bg = isDark ? Color.FromArgb(24, 24, 37) : Color.FromArgb(230, 233, 239);
        Color segBg = isDark ? Color.FromArgb(49, 50, 68) : Color.FromArgb(204, 208, 218);

        using (var bgBrush = new SolidBrush(bg))
        using (var path = GraphicsExtensions.MakeRoundRect(0, 0, w, h, EdgeRadius))
            g.FillPath(bgBrush, path);

        if (_snapEdge == DockStyle.Top)
            PaintEdgeStripHorizontal(g, isDark, segBg, w, h);
        else
            PaintEdgeStripVertical(g, isDark, segBg, w, h);
    }

    private void PaintEdgeStripVertical(Graphics g, bool isDark, Color segBg, int w, int h)
    {
        // 留白：桌面侧 3px + 进度条 10px + 屏幕边 5px = 18px
        int barX = _snapEdge == DockStyle.Right ? EdgeMarginDesktop : EdgeMarginScreen;
        int barW = EdgeBarWidth;
        int barY = EdgeSegPadding;
        int barH = h - EdgeSegPadding * 2;
        int segH = (barH - EdgeSegGap) / 2;

        DrawEdgeSegment(g, segBg, isDark, _snapshot.McpQuota, _configService.Config,
            barX, barY, barW, segH, vertical: true);
        DrawEdgeSegment(g, segBg, isDark, _snapshot.Token5hQuota, _configService.Config,
            barX, barY + segH + EdgeSegGap, barW, segH, vertical: true);
    }

    private void PaintEdgeStripHorizontal(Graphics g, bool isDark, Color segBg, int w, int h)
    {
        // 留白：屏幕边(上) 5px + 进度条区域 + 桌面侧(下) 3px = 18px
        int barX = EdgeSegPadding;
        int barW = w - EdgeSegPadding * 2;
        int barY = EdgeMarginScreen; // 跳过顶部屏幕边留白
        int barH = EdgeBarWidth;
        int segW = (barW - EdgeSegGap) / 2;

        DrawEdgeSegment(g, segBg, isDark, _snapshot.McpQuota, _configService.Config,
            barX, barY, segW, barH, vertical: false);
        DrawEdgeSegment(g, segBg, isDark, _snapshot.Token5hQuota, _configService.Config,
            barX + segW + EdgeSegGap, barY, segW, barH, vertical: false);
    }

    private void DrawEdgeSegment(Graphics g, Color segBg, bool isDark,
        QuotaItem item, AppConfig config, int x, int y, int w, int h, bool vertical)
    {
        double pct = Math.Clamp(item.Percentage, 0, 100);

        using (var bgBrush = new SolidBrush(segBg))
            g.FillRectangle(bgBrush, x, y, w, h);

        // 色块颜色（边缘条用柔和色调）
        Color fillColor;
        if (pct >= config.CriticalThreshold)
            fillColor = isDark ? Color.FromArgb(243, 139, 168) : Color.FromArgb(192, 80, 80);
        else if (pct >= config.WarningThreshold)
            fillColor = isDark ? Color.FromArgb(249, 226, 175) : Color.FromArgb(201, 168, 76);
        else if (item.Type == "TIME_LIMIT")
            fillColor = isDark ? Color.FromArgb(137, 180, 250) : Color.FromArgb(106, 159, 216);
        else
            fillColor = isDark ? Color.FromArgb(148, 226, 213) : Color.FromArgb(95, 168, 160);

        using var fillBrush = new SolidBrush(fillColor);
        if (vertical)
        {
            int fillH = (int)((h - EdgeSegInset * 2) * pct / 100);
            if (fillH > 0)
                g.FillRectangle(fillBrush,
                    x + EdgeSegInset, y + h - EdgeSegInset - fillH,
                    w - EdgeSegInset * 2, fillH);
        }
        else
        {
            int fillW = (int)((w - EdgeSegInset * 2) * pct / 100);
            if (fillW > 0)
                g.FillRectangle(fillBrush,
                    x + EdgeSegInset, y + EdgeSegInset,
                    fillW, h - EdgeSegInset * 2);
        }
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
            Size = new Size(CardWidth, CardHeight);
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

    /// <summary>
    /// 定位浮动条：从贴边位置展开 3 秒后收回
    /// </summary>
    public void Locate()
    {
        if (!_isSnapped || _isExpanded) return;

        Expand();

        var locateTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        locateTimer.Tick += (_, _) =>
        {
            locateTimer.Stop();
            locateTimer.Dispose();
            CollapseIfSnapped();
        };
        locateTimer.Start();
    }

    #region 贴边吸附

    private void SnapToEdge(DockStyle edge, Rectangle screen)
    {
        _isSnapped = true;
        _snapEdge = edge;
        _isExpanded = false;

        int currentX = Location.X;
        int currentY = Location.Y;

        switch (edge)
        {
            case DockStyle.Right:
                Size = new Size(EdgeMarginDesktop + EdgeBarWidth + EdgeMarginScreen, EdgeStripVHeight);
                Location = new Point(screen.Right - Width, Math.Clamp(currentY, screen.Top, screen.Bottom - Height));
                break;
            case DockStyle.Left:
                Size = new Size(EdgeMarginDesktop + EdgeBarWidth + EdgeMarginScreen, EdgeStripVHeight);
                Location = new Point(screen.Left, Math.Clamp(currentY, screen.Top, screen.Bottom - Height));
                break;
            case DockStyle.Top:
                Size = new Size(EdgeStripHWidth, EdgeMarginDesktop + EdgeBarWidth + EdgeMarginScreen);
                Location = new Point(Math.Clamp(currentX, screen.Left, screen.Right - Width), screen.Top);
                break;
        }

        BackColor = _themeService.IsDark
            ? Color.FromArgb(24, 24, 37)
            : Color.FromArgb(230, 233, 239);

        Invalidate();
    }

    private void Expand()
    {
        if (!_isSnapped) return;
        _isExpanded = true;

        Size = new Size(CardWidth, CardHeight);

        var screen = Screen.PrimaryScreen!.WorkingArea;
        switch (_snapEdge)
        {
            case DockStyle.Left: Location = new Point(screen.Left + EdgeMarginScreen, Location.Y); break;
            case DockStyle.Right: Location = new Point(screen.Right - CardWidth - EdgeMarginScreen, Location.Y); break;
            case DockStyle.Top: Location = new Point(Location.X, screen.Top + EdgeMarginScreen); break;
        }

        Invalidate();
    }

    private void CollapseIfSnapped()
    {
        if (!_isSnapped || !_isExpanded) return;
        _isExpanded = false;

        var screen = Screen.PrimaryScreen!.WorkingArea;
        int currentX = Location.X;
        int currentY = Location.Y;

        switch (_snapEdge)
        {
            case DockStyle.Right:
                Size = new Size(EdgeMarginDesktop + EdgeBarWidth + EdgeMarginScreen, EdgeStripVHeight);
                Location = new Point(screen.Right - Width, Math.Clamp(currentY, screen.Top, screen.Bottom - Height));
                break;
            case DockStyle.Left:
                Size = new Size(EdgeMarginDesktop + EdgeBarWidth + EdgeMarginScreen, EdgeStripVHeight);
                Location = new Point(screen.Left, Math.Clamp(currentY, screen.Top, screen.Bottom - Height));
                break;
            case DockStyle.Top:
                Size = new Size(EdgeStripHWidth, EdgeMarginDesktop + EdgeBarWidth + EdgeMarginScreen);
                Location = new Point(Math.Clamp(currentX, screen.Left, screen.Right - Width), screen.Top);
                break;
        }

        BackColor = _themeService.IsDark
            ? Color.FromArgb(24, 24, 37)
            : Color.FromArgb(230, 233, 239);

        Invalidate();
    }

    #endregion

    private void PersistPosition()
    {
        if (_isSnapped) return;
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
