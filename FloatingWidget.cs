using System.Drawing;
using System.Drawing.Drawing2D;
using GLMQuotaMonitor.Models;

namespace GLMQuotaMonitor;

/// <summary>
/// 浮动配额卡片 Widget
/// 垂直卡片布局，悬停显示详情面板，支持侧边吸附
/// </summary>
public class FloatingWidget : Form
{
    private const int CardWidth = 240;
    private const int CardHPadding = 24;
    private const int CardVPadding = 16;
    private const int EdgeSnapThreshold = 10;
    private const int RevealEdgeWidth = 4;
    private const int AutoHideDelayMs = 500;
    private const int HoverShowDelayMs = 300;
    private const int HoverHideDelayMs = 200;

    private readonly ThemeService _themeService;
    private readonly ConfigService _configService;
    private readonly System.Windows.Forms.Timer _hideTimer;
    private readonly Action<bool> _themeChangedHandler;

    private QuotaSnapshot _snapshot = new() { IsOffline = true };
    private bool _isDragging;
    private Point _dragOffset;
    private bool _isSnapped;
    private DockStyle _snapEdge = DockStyle.None;
    private bool _isExpanded;

    // 缓存字体
    private readonly Font _headerFont = new("Segoe UI", 9f, FontStyle.Bold);
    private readonly Font _labelFont = new("Segoe UI", 8.5f);
    private readonly Font _valueFont = new("Segoe UI", 13f, FontStyle.Bold);
    private readonly Font _detailFont = new("Segoe UI", 8f);

    public FloatingWidget(ThemeService themeService, ConfigService configService)
    {
        _themeService = themeService;
        _configService = configService;
        var config = configService.Config;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;

        // 固定尺寸：留足呼吸空间
        Size = new Size(CardWidth, 240);

        // 初始位置
        if (config.FloatingBarX.HasValue && config.FloatingBarY.HasValue)
            Location = new Point(config.FloatingBarX.Value, config.FloatingBarY.Value);
        else
        {
            var screen = Screen.PrimaryScreen!.WorkingArea;
            Location = new Point(screen.Right - CardWidth - 20, screen.Top + screen.Height / 2 - 100);
        }

        // 定时器
        _hideTimer = new System.Windows.Forms.Timer { Interval = AutoHideDelayMs };
        _hideTimer.Tick += (_, _) => CollapseIfSnapped();

        // 事件
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

        // 预计算内容高度
        int w = Width;
        int h = Height;

        // 背景
        Color bg = isDark ? Color.FromArgb(235, 18, 24, 42) : Color.FromArgb(245, 248, 252);
        using (var bgBrush = new SolidBrush(bg))
        using (var path = GraphicsExtensions.MakeRoundRect(0, 0, w, h, 4))
            g.FillPath(bgBrush, path);

        // 细边框
        Color border = isDark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0);
        using (var borderPen = new Pen(border))
        using (var path = GraphicsExtensions.MakeRoundRect(0, 0, w - 1, h - 1, 4))
            g.DrawPath(borderPen, path);

        int x = CardHPadding;
        int y = CardVPadding;
        int cw = w - CardHPadding * 2;

        // ═══ Header: 状态灯 + 标题 ═══
        var status = _snapshot.GetStatus(cfg.WarningThreshold, cfg.CriticalThreshold);
        Color dotColor = status switch
        {
            QuotaStatus.Normal => Color.FromArgb(0, 180, 0),
            QuotaStatus.Warning => Color.FromArgb(230, 180, 0),
            QuotaStatus.Critical => Color.FromArgb(200, 0, 0),
            _ => Color.FromArgb(128, 128, 128)
        };
        using (var dotBrush = new SolidBrush(dotColor))
            g.FillEllipse(dotBrush, x, y + 2, 7, 7);

        Color headerColor = isDark ? Color.FromArgb(160, 170, 195) : Color.FromArgb(80, 80, 100);
        using var headerBrush = new SolidBrush(headerColor);
        g.DrawString("GLM 配额", _headerFont, headerBrush, x + 12, y);

        y += 28;

        // ═══ 分隔线 ═══
        Color sepColor = isDark ? Color.FromArgb(25, 255, 255, 255) : Color.FromArgb(20, 0, 0, 0);
        using (var sepPen = new Pen(sepColor))
            g.DrawLine(sepPen, x, y, x + cw, y);
        y += 14;

        // ═══ MCP 配额 ═══
        y = DrawQuotaSection(g, _snapshot.McpQuota, cfg, isDark, x, y, cw);
        y += 12;

        // ═══ 5h Token ═══
        y = DrawQuotaSection(g, _snapshot.Token5hQuota, cfg, isDark, x, y, cw);
        y += 14;

        // ═══ 分隔线 ═══
        using (var sepPen = new Pen(sepColor))
            g.DrawLine(sepPen, x, y, x + cw, y);
        y += 14;

        // ═══ 统计行（数字+单位一体，避免压占）═══
        if (!_snapshot.IsOffline)
        {
            Color statColor = isDark ? Color.FromArgb(160, 170, 195) : Color.FromArgb(60, 60, 80);
            using var statBrush = new SolidBrush(statColor);

            string callsText = $"{FormatNumber(_snapshot.CallCount)} 次调用";
            string tokenText = $"{FormatTokenUsage(_snapshot.TokenUsage)} Token";
            string statsLine = $"{callsText}    {tokenText}";
            g.DrawString(statsLine, _detailFont, statBrush, x, y);
        }
    }

    private int DrawQuotaSection(Graphics g, QuotaItem item, AppConfig config, bool isDark,
        int x, int y, int cw)
    {
        double pct = Math.Clamp(item.Percentage, 0, 100);

        // ── 第一行：标签 ──
        Color labelColor = isDark ? Color.FromArgb(120, 130, 160) : Color.FromArgb(100, 100, 120);
        using var labelBrush = new SolidBrush(labelColor);
        g.DrawString(item.Name, _labelFont, labelBrush, x, y);
        y += 20;

        // ── 第二行：百分比（左）+ 进度条（右）──
        Color pctColor;
        if (pct >= config.CriticalThreshold) pctColor = Color.FromArgb(255, 118, 117);
        else if (pct >= config.WarningThreshold) pctColor = Color.FromArgb(255, 220, 100);
        else pctColor = isDark ? Color.FromArgb(0, 210, 205) : Color.FromArgb(0, 160, 140);

        using var pctBrush = new SolidBrush(pctColor);
        string pctText = $"{pct:F0}%";
        g.DrawString(pctText, _valueFont, pctBrush, x, y - 3);

        int pctW = (int)g.MeasureString(pctText, _valueFont).Width + 10;
        int barX = x + pctW;
        int barW = cw - pctW;
        int barH = 6;
        int barY = y + 5;

        Color barBg = isDark ? Color.FromArgb(20, 255, 255, 255) : Color.FromArgb(15, 0, 0, 0);
        using (var bgBrush = new SolidBrush(barBg))
            g.FillRectangle(bgBrush, barX, barY, barW, barH);

        int fillW = (int)(barW * pct / 100);
        if (fillW > 0)
        {
            using var fillBrush = new SolidBrush(pctColor);
            g.FillRectangle(fillBrush, barX, barY, fillW, barH);
        }

        y += 20;

        // ── 第三行：详情小字 ──
        Color detailColor = isDark ? Color.FromArgb(80, 90, 115) : Color.FromArgb(140, 140, 160);
        using var detailBrush = new SolidBrush(detailColor);

        if (item.Type == "TOKENS_LIMIT" && item.ResetDateTime.HasValue)
        {
            var resetTime = item.ResetDateTime.Value;
            string resetText = $"重置: {resetTime:HH:mm}";
            g.DrawString(resetText, _detailFont, detailBrush, x, y);
        }
        else if (item.Total > 0)
        {
            g.DrawString($"{FormatNumber(item.Used)} / {FormatNumber(item.Total)}", _detailFont, detailBrush, x, y);
        }

        return y + 16;
    }

    #endregion

    #region 拖动 & 贴边

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

    private void SnapToEdge(DockStyle edge, Rectangle screen)
    {
        _isSnapped = true;
        _snapEdge = edge;
        _isExpanded = false;
        switch (edge)
        {
            case DockStyle.Left:
                Location = new Point(screen.Left - Width + RevealEdgeWidth, Location.Y);
                break;
            case DockStyle.Right:
                Location = new Point(screen.Right - RevealEdgeWidth, Location.Y);
                break;
            case DockStyle.Top:
                Location = new Point(Location.X, screen.Top - Height + RevealEdgeWidth);
                break;
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
            _headerFont.Dispose();
            _labelFont.Dispose();
            _valueFont.Dispose();
            _detailFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
