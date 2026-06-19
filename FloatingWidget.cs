using System.Drawing;
using System.Drawing.Drawing2D;
using GLMQuotaMonitor.Models;

namespace GLMQuotaMonitor;

/// <summary>
/// 浮动配额卡片 Widget
/// 三列布局：标签 | 进度条 | 百分比，每区一行，紧凑居中
/// 底部：刷新按钮 + 更新时间
/// </summary>
public class FloatingWidget : Form
{
    private const int CardWidth = 240;
    private const int CardHeight = 130;
    private const int CardHPadding = 20;
    private const int CardVPadding = 14;
    private const int Inset = 2;

    private const int LabelColWidth = 70;
    private const int BarWidth = 70;
    private const int BarHeight = 14;
    private const int PctColWidth = 50;
    private const int ColGap = 5;
    private const int RowHeight = 24;

    private const int EdgeSnapThreshold = 10;
    private const int RevealEdgeWidth = 4;
    private const int AutoHideDelayMs = 500;

    private readonly ThemeService _themeService;
    private readonly ConfigService _configService;
    private readonly Action _onRefresh;
    private readonly System.Windows.Forms.Timer _hideTimer;
    private readonly Action<bool> _themeChangedHandler;

    private QuotaSnapshot _snapshot = new() { IsOffline = true };
    private bool _isDragging;
    private Point _dragOffset;
    private bool _isSnapped;
    private DockStyle _snapEdge = DockStyle.None;
    private bool _isExpanded;
    private Rectangle _refreshBtnRect;

    // 缓存字体
    private readonly Font _labelFont = new("Segoe UI", 9f);
    private readonly Font _valueFont = new("Segoe UI", 11f, FontStyle.Bold);
    private readonly Font _detailFont = new("Segoe UI", 8f);
    private readonly Font _refreshFont = new("Segoe UI", 8.5f);

    public FloatingWidget(ThemeService themeService, ConfigService configService, Action onRefresh)
    {
        _themeService = themeService;
        _configService = configService;
        _onRefresh = onRefresh;
        var config = configService.Config;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;

        Size = new Size(CardWidth, CardHeight);

        if (config.FloatingBarX.HasValue && config.FloatingBarY.HasValue)
            Location = new Point(config.FloatingBarX.Value, config.FloatingBarY.Value);
        else
        {
            var screen = Screen.PrimaryScreen!.WorkingArea;
            Location = new Point(screen.Right - CardWidth - 20, screen.Top + screen.Height / 2 - CardHeight / 2);
        }

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

        // 背景（内缩 2px，避免 TransparencyKey 抗锯齿问题）
        Color bg = isDark ? Color.FromArgb(235, 18, 24, 42) : Color.FromArgb(245, 248, 252);
        using (var bgBrush = new SolidBrush(bg))
        using (var path = GraphicsExtensions.MakeRoundRect(Inset, Inset, w - Inset * 2, h - Inset * 2, 4))
            g.FillPath(bgBrush, path);

        // 边框
        Color border = isDark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0);
        using (var borderPen = new Pen(border))
        using (var path = GraphicsExtensions.MakeRoundRect(Inset, Inset, w - Inset * 2 - 1, h - Inset * 2 - 1, 4))
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

        // ═══ 统计行（居中）═══
        if (!_snapshot.IsOffline)
        {
            Color statColor = isDark ? Color.FromArgb(120, 130, 160) : Color.FromArgb(100, 100, 120);
            using var statBrush = new SolidBrush(statColor);
            string line = $"{FormatNumber(_snapshot.CallCount)} 次调用  |  {FormatTokenUsage(_snapshot.TokenUsage)} Token";
            var textSize = g.MeasureString(line, _detailFont);
            float statsX = (w - textSize.Width) / 2;
            g.DrawString(line, _detailFont, statBrush, statsX, y);
        }
        y += 16;

        // ═══ 刷新行 ═══
        // 左侧：刷新按钮
        Color refreshColor = isDark ? Color.FromArgb(123, 140, 222) : Color.FromArgb(60, 80, 180);
        using var refreshBrush = new SolidBrush(refreshColor);
        string refreshText = "⟳ 刷新";
        g.DrawString(refreshText, _refreshFont, refreshBrush, x, y);
        var refreshSize = g.MeasureString(refreshText, _refreshFont);
        _refreshBtnRect = new Rectangle(x, y, (int)refreshSize.Width + 4, (int)refreshSize.Height + 2);

        // 右侧：更新时间
        Color timeColor = isDark ? Color.FromArgb(80, 90, 115) : Color.FromArgb(140, 140, 160);
        using var timeBrush = new SolidBrush(timeColor);
        string timeText = _snapshot.IsOffline ? "离线" : $"{_snapshot.Timestamp:HH:mm:ss} 更新";
        var timeSize = g.MeasureString(timeText, _detailFont);
        g.DrawString(timeText, _detailFont, timeBrush, x + cw - timeSize.Width, y + 1);
    }

    /// <summary>
    /// 绘制单行配额（三列：标签 | 进度条 | 百分比）
    /// </summary>
    private int DrawQuotaRow(Graphics g, QuotaItem item, AppConfig config, bool isDark, int x, int y)
    {
        double pct = Math.Clamp(item.Percentage, 0, 100);

        // 列 X 位置
        int labelX = x;
        int barX = x + LabelColWidth + ColGap;
        int pctX = barX + BarWidth + ColGap;

        // ── 标签（左列，垂直居中）──
        Color labelColor = isDark ? Color.FromArgb(120, 130, 160) : Color.FromArgb(100, 100, 120);
        using var labelBrush = new SolidBrush(labelColor);
        float labelY = y + (RowHeight - _labelFont.GetHeight(g)) / 2;
        g.DrawString(item.Name, _labelFont, labelBrush, labelX, labelY);

        // ── 进度条（中列，垂直居中）──
        int barY = y + (RowHeight - BarHeight) / 2;

        Color barBg = isDark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(25, 0, 0, 0);
        using (var bgBrush = new SolidBrush(barBg))
            g.FillRectangle(bgBrush, barX, barY, BarWidth, BarHeight);

        // 进度条颜色：MCP 蓝色，5h 青色，超限统一黄/红
        Color normalColor;
        if (item.Type == "TIME_LIMIT")
            normalColor = isDark ? Color.FromArgb(70, 130, 230) : Color.FromArgb(50, 100, 200);
        else
            normalColor = isDark ? Color.FromArgb(0, 210, 205) : Color.FromArgb(0, 160, 140);

        Color pctColor;
        if (pct >= config.CriticalThreshold) pctColor = Color.FromArgb(255, 118, 117);
        else if (pct >= config.WarningThreshold) pctColor = Color.FromArgb(255, 220, 100);
        else pctColor = normalColor;

        int fillW = (int)(BarWidth * pct / 100);
        if (fillW > 0)
        {
            using var fillBrush = new SolidBrush(pctColor);
            g.FillRectangle(fillBrush, barX, barY, fillW, BarHeight);
        }

        // ── 百分比（右列，右对齐，垂直居中）──
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

        // 刷新按钮命中检测
        if (_refreshBtnRect.Contains(e.Location))
        {
            _onRefresh?.Invoke();
            return;
        }

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
            _labelFont.Dispose();
            _valueFont.Dispose();
            _detailFont.Dispose();
            _refreshFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
