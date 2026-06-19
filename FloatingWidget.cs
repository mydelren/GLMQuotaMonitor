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
    private const int CardWidth = 200;
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
    private readonly System.Windows.Forms.Timer _hoverShowTimer;
    private readonly System.Windows.Forms.Timer _hoverHideTimer;
    private readonly Action<bool> _themeChangedHandler;

    private QuotaSnapshot _snapshot = new() { IsOffline = true };
    private bool _isDragging;
    private Point _dragOffset;
    private bool _isSnapped;
    private DockStyle _snapEdge = DockStyle.None;
    private bool _isExpanded;

    private DetailPanel? _detailPanel;

    // 缓存字体
    private readonly Font _headerFont = new("Segoe UI", 9f, FontStyle.Bold);
    private readonly Font _labelFont = new("Segoe UI", 8.5f);
    private readonly Font _valueFont = new("Segoe UI", 13f, FontStyle.Bold);
    private readonly Font _detailFont = new("Segoe UI", 8f);
    private readonly Font _statValueFont = new("Segoe UI", 11f, FontStyle.Bold);
    private readonly Font _statLabelFont = new("Segoe UI", 7.5f);

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

        // 初始尺寸（会在 Paint 中自动调整）
        Size = new Size(CardWidth, 200);

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

        _hoverShowTimer = new System.Windows.Forms.Timer { Interval = HoverShowDelayMs };
        _hoverShowTimer.Tick += (_, _) => ShowDetailPanel();

        _hoverHideTimer = new System.Windows.Forms.Timer { Interval = HoverHideDelayMs };
        _hoverHideTimer.Tick += (_, _) => HideDetailPanel();

        // 事件
        Paint += OnPaint;
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        MouseEnter += OnMouseEnter;
        MouseLeave += OnMouseLeave;

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
        if (InvokeRequired) BeginInvoke(() => { Invalidate(); _detailPanel?.UpdateData(snapshot); });
        else { Invalidate(); _detailPanel?.UpdateData(snapshot); }
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
        int contentH = MeasureContentHeight();
        int w = CardWidth;
        int h = contentH + CardVPadding * 2;

        // 自动调整窗口高度
        if (Math.Abs(h - Height) > 2 && !_isDragging)
        {
            Height = h;
            Invalidate();
            return;
        }

        // 背景
        Color bg = isDark ? Color.FromArgb(235, 18, 24, 42) : Color.FromArgb(245, 248, 252);
        using (var bgBrush = new SolidBrush(bg))
        using (var path = GraphicsExtensions.MakeRoundRect(0, 0, w, h, 10))
            g.FillPath(bgBrush, path);

        // 细边框
        Color border = isDark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0);
        using (var borderPen = new Pen(border))
        using (var path = GraphicsExtensions.MakeRoundRect(0, 0, w - 1, h - 1, 10))
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

        y += 24;

        // ═══ 分隔线 ═══
        Color sepColor = isDark ? Color.FromArgb(25, 255, 255, 255) : Color.FromArgb(20, 0, 0, 0);
        using (var sepPen = new Pen(sepColor))
            g.DrawLine(sepPen, x, y, x + cw, y);
        y += 10;

        // ═══ MCP 配额 ═══
        y = DrawQuotaSection(g, _snapshot.McpQuota, cfg, isDark, x, y, cw);
        y += 8;

        // ═══ 5h Token ═══
        y = DrawQuotaSection(g, _snapshot.Token5hQuota, cfg, isDark, x, y, cw);
        y += 10;

        // ═══ 分隔线 ═══
        using (var sepPen = new Pen(sepColor))
            g.DrawLine(sepPen, x, y, x + cw, y);
        y += 10;

        // ═══ 统计行 ═══
        if (!_snapshot.IsOffline)
        {
            Color statValueColor = isDark ? Color.FromArgb(200, 205, 220) : Color.FromArgb(40, 40, 50);
            Color statLabelColor = isDark ? Color.FromArgb(100, 110, 135) : Color.FromArgb(120, 120, 140);
            using var svBrush = new SolidBrush(statValueColor);
            using var slBrush = new SolidBrush(statLabelColor);

            int colW = cw / 2;
            g.DrawString(FormatNumber(_snapshot.CallCount), _statValueFont, svBrush, x, y);
            g.DrawString("次调用", _statLabelFont, slBrush, x, y + 16);
            g.DrawString(FormatTokenUsage(_snapshot.TokenUsage), _statValueFont, svBrush, x + colW, y);
            g.DrawString("Token", _statLabelFont, slBrush, x + colW, y + 16);
        }
    }

    private int DrawQuotaSection(Graphics g, QuotaItem item, AppConfig config, bool isDark,
        int x, int y, int cw)
    {
        double pct = Math.Clamp(item.Percentage, 0, 100);

        // 标签行：名称（左）+ 百分比（右）
        Color labelColor = isDark ? Color.FromArgb(120, 130, 160) : Color.FromArgb(100, 100, 120);
        using var labelBrush = new SolidBrush(labelColor);
        g.DrawString(item.Name, _labelFont, labelBrush, x, y);

        Color pctColor;
        if (pct >= config.CriticalThreshold) pctColor = Color.FromArgb(255, 118, 117);
        else if (pct >= config.WarningThreshold) pctColor = Color.FromArgb(255, 220, 100);
        else pctColor = isDark ? Color.FromArgb(0, 210, 205) : Color.FromArgb(0, 160, 140);

        using var pctBrush = new SolidBrush(pctColor);
        string pctText = $"{pct:F0}%";
        var pctSize = g.MeasureString(pctText, _valueFont);
        g.DrawString(pctText, _valueFont, pctBrush, x + cw - pctSize.Width, y - 4);

        y += 18;

        // 迷你进度条
        int barH = 4;
        Color barBg = isDark ? Color.FromArgb(20, 255, 255, 255) : Color.FromArgb(15, 0, 0, 0);
        using (var bgBrush = new SolidBrush(barBg))
            g.FillRectangle(bgBrush, x, y, cw, barH);

        int fillW = (int)(cw * pct / 100);
        if (fillW > 0)
        {
            using var fillBrush = new SolidBrush(pctColor);
            g.FillRectangle(fillBrush, x, y, fillW, barH);
        }

        y += 8;

        // 详情小字
        Color detailColor = isDark ? Color.FromArgb(80, 90, 115) : Color.FromArgb(140, 140, 160);
        using var detailBrush = new SolidBrush(detailColor);

        if (item.Type == "TOKENS_LIMIT")
        {
            // 显示重置时间
            string resetText = "重置: --:--";
            // 从 snapshot 获取重置时间（需要从 API 原始数据中提取）
            // 暂时显示用量
            if (item.Total > 0)
                resetText = $"{FormatNumber(item.Used)} / {FormatNumber(item.Total)}";
            g.DrawString(resetText, _detailFont, detailBrush, x, y);
        }
        else
        {
            g.DrawString($"{FormatNumber(item.Used)} / {FormatNumber(item.Total)}", _detailFont, detailBrush, x, y);
        }

        return y + 14;
    }

    private int MeasureContentHeight()
    {
        // header(24) + sep(10) + mcp(40) + gap(8) + token(40) + sep(10) + stats(30) = 162
        int h = 24 + 10; // header + sep
        h += 40; // mcp section
        h += 8;  // gap
        h += 40; // token section
        h += 10; // sep
        if (!_snapshot.IsOffline) h += 30; // stats
        return h;
    }

    #endregion

    #region 悬停详情面板

    private void OnMouseEnter(object? sender, EventArgs e)
    {
        _hoverHideTimer.Stop();
        _hideTimer.Stop();

        if (_isSnapped && !_isExpanded) Expand();

        _hoverShowTimer.Stop();
        _hoverShowTimer.Start();
    }

    private void OnMouseLeave(object? sender, EventArgs e)
    {
        _hoverShowTimer.Stop();

        // 延迟隐藏，允许鼠标移到详情面板
        _hoverHideTimer.Stop();
        _hoverHideTimer.Start();

        if (_isSnapped) _hideTimer.Start();
    }

    private void ShowDetailPanel()
    {
        _hoverShowTimer.Stop();

        if (_detailPanel != null && !_detailPanel.IsDisposed) return;

        _detailPanel = new DetailPanel(_themeService, _configService, _snapshot);
        _detailPanel.MouseEnter += (_, _) => { _hoverHideTimer.Stop(); _hideTimer.Stop(); };
        _detailPanel.MouseLeave += (_, _) =>
        {
            _hoverHideTimer.Stop();
            _hoverHideTimer.Start();
        };
        _detailPanel.FormClosed += (_, _) => { _detailPanel = null; };

        // 定位在 widget 下方
        var screen = Screen.PrimaryScreen!.WorkingArea;
        int px = Location.X;
        int py = Location.Y + Height + 4;

        // 确保不超出屏幕
        if (py + _detailPanel.Height > screen.Bottom)
            py = Location.Y - _detailPanel.Height - 4;
        if (px + _detailPanel.Width > screen.Right)
            px = screen.Right - _detailPanel.Width - 10;
        if (px < screen.Left)
            px = screen.Left + 10;

        _detailPanel.Location = new Point(px, py);
        _detailPanel.Show();
    }

    private void HideDetailPanel()
    {
        _hoverHideTimer.Stop();
        if (_detailPanel != null && !_detailPanel.IsDisposed)
        {
            _detailPanel.Close();
            _detailPanel = null;
        }
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
            _hoverShowTimer.Dispose();
            _hoverHideTimer.Dispose();
            _themeService.ThemeChanged -= _themeChangedHandler;
            _headerFont.Dispose();
            _labelFont.Dispose();
            _valueFont.Dispose();
            _detailFont.Dispose();
            _statValueFont.Dispose();
            _statLabelFont.Dispose();
            _detailPanel?.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// 悬停详情面板（显示在 widget 下方）
/// </summary>
public class DetailPanel : Form
{
    private readonly ThemeService _themeService;
    private QuotaSnapshot _snapshot;

    private readonly Font _titleFont = new("Segoe UI", 10f, FontStyle.Bold);
    private readonly Font _labelFont = new("Segoe UI", 9f);
    private readonly Font _valueFont = new("Segoe UI", 12f, FontStyle.Bold);
    private readonly Font _detailFont = new("Segoe UI", 8.5f);

    public DetailPanel(ThemeService themeService, ConfigService configService, QuotaSnapshot snapshot)
    {
        _themeService = themeService;
        _snapshot = snapshot;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        Size = new Size(260, 200);

        Paint += OnPaint;
    }

    public void UpdateData(QuotaSnapshot snapshot)
    {
        _snapshot = snapshot;
        if (InvokeRequired) BeginInvoke(() => Invalidate());
        else Invalidate();
    }

    private void OnPaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        bool isDark = _themeService.IsDark;
        int w = Width, h = Height;

        Color bg = isDark ? Color.FromArgb(240, 18, 24, 42) : Color.FromArgb(250, 248, 252);
        using (var bgBrush = new SolidBrush(bg))
        using (var path = GraphicsExtensions.MakeRoundRect(0, 0, w, h, 10))
            g.FillPath(bgBrush, path);

        Color border = isDark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0);
        using (var borderPen = new Pen(border))
        using (var path = GraphicsExtensions.MakeRoundRect(0, 0, w - 1, h - 1, 10))
            g.DrawPath(borderPen, path);

        int pad = 16;
        int y = pad;

        Color titleColor = isDark ? Color.FromArgb(200, 205, 220) : Color.FromArgb(40, 40, 50);
        Color labelColor = isDark ? Color.FromArgb(120, 130, 160) : Color.FromArgb(100, 100, 120);
        Color valueColor = isDark ? Color.FromArgb(224, 228, 235) : Color.FromArgb(30, 30, 40);
        Color subColor = isDark ? Color.FromArgb(80, 90, 115) : Color.FromArgb(140, 140, 160);

        using var titleBrush = new SolidBrush(titleColor);
        using var labelBrush = new SolidBrush(labelColor);
        using var valueBrush = new SolidBrush(valueColor);
        using var subBrush = new SolidBrush(subColor);

        // 标题
        g.DrawString("配额详情", _titleFont, titleBrush, pad, y);
        y += 28;

        // MCP 详情
        DrawDetailRow(g, "MCP 月度配额", _snapshot.McpQuota, labelBrush, valueBrush, subBrush, pad, y, w);
        y += 50;

        // 5h Token 详情
        DrawDetailRow(g, "5h Token 流控", _snapshot.Token5hQuota, labelBrush, valueBrush, subBrush, pad, y, w);
        y += 50;

        // 统计
        if (!_snapshot.IsOffline)
        {
            Color sepColor = isDark ? Color.FromArgb(25, 255, 255, 255) : Color.FromArgb(20, 0, 0, 0);
            using var sepPen = new Pen(sepColor);
            g.DrawLine(sepPen, pad, y, w - pad, y);
            y += 10;

            g.DrawString($"调用次数: {FormatNumber(_snapshot.CallCount)}", _detailFont, subBrush, pad, y);
            g.DrawString($"Token 用量: {FormatTokenUsage(_snapshot.TokenUsage)}", _detailFont, subBrush, pad, y + 16);
        }
    }

    private void DrawDetailRow(Graphics g, string title, QuotaItem item,
        Brush labelBrush, Brush valueBrush, Brush subBrush, int x, int y, int w)
    {
        g.DrawString(title, _labelFont, labelBrush, x, y);
        y += 18;

        string pctText = $"{item.Percentage:F0}%";
        g.DrawString(pctText, _valueFont, valueBrush, x, y);

        string detailText = $"{FormatNumber(item.Used)} / {FormatNumber(item.Total)}";
        var pctW = g.MeasureString(pctText, _valueFont).Width;
        g.DrawString(detailText, _detailFont, subBrush, x + pctW + 8, y + 4);
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
            _titleFont.Dispose();
            _labelFont.Dispose();
            _valueFont.Dispose();
            _detailFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
