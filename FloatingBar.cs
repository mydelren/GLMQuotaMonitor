using System.Drawing;
using System.Drawing.Drawing2D;
using GLMQuotaMonitor.Models;

namespace GLMQuotaMonitor;

/// <summary>
/// 浮动监控条
/// 半透明置顶小条，支持拖动和贴边吸附
/// 设计：各段独立绘制，标签/数值视觉分离，直角边框
/// </summary>
public class FloatingBar : Form
{
    private const int BarHeight = 36;
    private const int DefaultBarWidth = 420;
    private const int EdgeSnapThreshold = 10;
    private const int AutoHideDelayMs = 500;
    private const int RevealEdgeWidth = 4;

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

    // 缓存字体（避免每次 Paint 创建）
    private readonly Font _labelFont = new("Segoe UI", 8.5f);
    private readonly Font _valueFont = new("Segoe UI", 10.5f, FontStyle.Bold);

    public FloatingBar(ThemeService themeService, ConfigService configService)
    {
        _themeService = themeService;
        _configService = configService;
        var config = configService.Config;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(DefaultBarWidth, BarHeight);

        // 直角：用 TransparencyKey 实现透明窗口
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;

        // 初始位置
        if (config.FloatingBarX.HasValue && config.FloatingBarY.HasValue)
        {
            Location = new Point(config.FloatingBarX.Value, config.FloatingBarY.Value);
        }
        else
        {
            var screen = Screen.PrimaryScreen!.WorkingArea;
            Location = new Point(
                screen.Right - DefaultBarWidth - 20,
                screen.Top + screen.Height / 2 - BarHeight / 2);
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

    private void OnPaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        bool isDark = _themeService.IsDark;
        int w = Width, h = Height;
        int pad = 14;

        // ── 背景（直角矩形）──
        Color bgColor = isDark
            ? Color.FromArgb(230, 18, 24, 42)
            : Color.FromArgb(240, 248, 248, 252);
        using (var bgBrush = new SolidBrush(bgColor))
            g.FillRectangle(bgBrush, 0, 0, w, h);

        // ── 状态圆点 ──
        var cfg = _configService.Config;
        var status = _snapshot.GetStatus(cfg.WarningThreshold, cfg.CriticalThreshold);
        Color dotColor = status switch
        {
            QuotaStatus.Normal => Color.FromArgb(0, 180, 0),
            QuotaStatus.Warning => Color.FromArgb(230, 180, 0),
            QuotaStatus.Critical => Color.FromArgb(200, 0, 0),
            _ => Color.FromArgb(128, 128, 128)
        };
        int dotSize = 8;
        int dotX = pad;
        int dotY = (h - dotSize) / 2;
        using (var dotBrush = new SolidBrush(dotColor))
            g.FillEllipse(dotBrush, dotX, dotY, dotSize, dotSize);

        // ── 内容区域（圆点右侧）──
        int contentX = dotX + dotSize + 12;
        int contentW = w - contentX - pad;
        float cx = contentX;

        if (_snapshot.IsOffline)
        {
            // 离线状态
            using var offlineBrush = new SolidBrush(isDark ? Color.FromArgb(140, 150, 175) : Color.FromArgb(100, 100, 100));
            g.DrawString("GLM 配额监控 — 离线", _valueFont, offlineBrush, cx, (h - _valueFont.GetHeight(g)) / 2);
            return;
        }

        // ── MCP 配额 ──
        Color labelColor = isDark ? Color.FromArgb(140, 150, 175) : Color.FromArgb(110, 115, 130);
        Color valueColor = isDark ? Color.FromArgb(224, 228, 235) : Color.FromArgb(35, 35, 45);
        using var labelBrush = new SolidBrush(labelColor);
        using var valueBrush = new SolidBrush(valueColor);

        cx = DrawSegment(g, "MCP", _snapshot.McpQuota.Percentage, cfg, isDark, cx, h, labelBrush, valueBrush);
        cx = DrawSeparator(g, cx, h, isDark);

        // ── 5h Token ──
        cx = DrawSegment(g, "5h", _snapshot.Token5hQuota.Percentage, cfg, isDark, cx, h, labelBrush, valueBrush);

        // ── 调用次数（如果有数据）──
        if (_snapshot.CallCount > 0)
        {
            cx = DrawSeparator(g, cx, h, isDark);
            string callText = $"{FormatNumber(_snapshot.CallCount)} 次";
            g.DrawString(callText, _valueFont, valueBrush, cx, (h - _valueFont.GetHeight(g)) / 2);
        }
    }

    /// <summary>
    /// 绘制一个配额段：标签 + 数值
    /// </summary>
    private float DrawSegment(Graphics g, string label, double pct, AppConfig config, bool isDark,
        float x, int h, Brush labelBrush, Brush valueBrush)
    {
        float labelY = (h - _labelFont.GetHeight(g)) / 2;
        g.DrawString(label, _labelFont, labelBrush, x, labelY);
        float labelW = g.MeasureString(label, _labelFont).Width;
        x += labelW + 6;

        // 数值（带状态颜色）
        Color pctColor;
        if (pct >= config.CriticalThreshold)
            pctColor = Color.FromArgb(255, 118, 117);
        else if (pct >= config.WarningThreshold)
            pctColor = Color.FromArgb(253, 203, 110);
        else
            pctColor = isDark ? Color.FromArgb(0, 210, 205) : Color.FromArgb(0, 160, 140);

        using var pctBrush = new SolidBrush(pctColor);
        string pctText = $"{pct:F0}%";
        float valY = (h - _valueFont.GetHeight(g)) / 2;
        g.DrawString(pctText, _valueFont, pctBrush, x, valY);
        x += g.MeasureString(pctText, _valueFont).Width + 4;

        return x;
    }

    /// <summary>
    /// 绘制分隔点
    /// </summary>
    private float DrawSeparator(Graphics g, float x, int h, bool isDark)
    {
        Color sepColor = isDark ? Color.FromArgb(80, 90, 115) : Color.FromArgb(180, 180, 190);
        using var sepBrush = new SolidBrush(sepColor);
        string sep = "\u00B7"; // 中点
        float sepW = g.MeasureString(sep, _labelFont).Width;
        float sepY = (h - _labelFont.GetHeight(g)) / 2;
        g.DrawString(sep, _labelFont, sepBrush, x + 4, sepY);
        return x + sepW + 10;
    }

    private static string FormatNumber(long num)
    {
        if (num >= 1_000_000_000) return $"{num / 1_000_000_000.0:F1}B";
        if (num >= 1_000_000) return $"{num / 1_000_000.0:F1}M";
        if (num >= 1_000) return $"{num / 1_000.0:F1}K";
        return num.ToString();
    }

    #region 拖动 & 贴边吸附

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _isDragging = true;
            _dragOffset = e.Location;
            if (_isSnapped)
            {
                _isSnapped = false;
                _isExpanded = false;
                _snapEdge = DockStyle.None;
                Size = new Size(DefaultBarWidth, BarHeight);
            }
        }
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            var sp = PointToScreen(e.Location);
            Location = new Point(sp.X - _dragOffset.X, sp.Y - _dragOffset.Y);
        }
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;

        var screen = Screen.PrimaryScreen!.WorkingArea;
        var loc = Location;

        if (loc.X <= screen.Left + EdgeSnapThreshold)
            SnapToEdge(DockStyle.Left, screen);
        else if (loc.X + Width >= screen.Right - EdgeSnapThreshold)
            SnapToEdge(DockStyle.Right, screen);
        else if (loc.Y <= screen.Top + EdgeSnapThreshold)
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hideTimer.Dispose();
            _themeService.ThemeChanged -= _themeChangedHandler;
            _labelFont.Dispose();
            _valueFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
