using System.Drawing;
using System.Drawing.Drawing2D;
using GLMQuotaMonitor.Models;

namespace GLMQuotaMonitor;

/// <summary>
/// 浮动监控条
/// 半透明置顶小条，支持拖动和贴边吸附
/// </summary>
public class FloatingBar : Form
{
    private const int BarHeight = 34;
    private const int DefaultBarWidth = 360;
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

    // 贴边状态
    private bool _isSnapped;
    private DockStyle _snapEdge = DockStyle.None;
    private bool _isExpanded;

    public FloatingBar(ThemeService themeService, ConfigService configService)
    {
        _themeService = themeService;
        _configService = configService;
        var config = configService.Config;

        // 窗口基本设置
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(DefaultBarWidth, BarHeight);

        // 初始位置
        if (config.FloatingBarX.HasValue && config.FloatingBarY.HasValue)
        {
            Location = new Point(config.FloatingBarX.Value, config.FloatingBarY.Value);
        }
        else
        {
            var screen = Screen.PrimaryScreen!.WorkingArea;
            // 居中偏右，任务栏上方
            Location = new Point(
                screen.Right - DefaultBarWidth - 20,
                screen.Top + screen.Height / 2 - BarHeight / 2);
        }

        // 自动隐藏定时器
        _hideTimer = new System.Windows.Forms.Timer { Interval = AutoHideDelayMs };
        _hideTimer.Tick += (_, _) => CollapseIfSnapped();

        // 圆角
        ApplyRoundedCorners();

        // 事件
        Paint += OnPaint;
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        MouseEnter += (_, _) =>
        {
            _hideTimer.Stop();
            if (_isSnapped && !_isExpanded) Expand();
        };
        MouseLeave += (_, _) =>
        {
            if (_isSnapped) _hideTimer.Start();
        };

        // 主题变更时重绘（存引用以便正确取消订阅，处理跨线程调用）
        _themeChangedHandler = (_) =>
        {
            if (InvokeRequired)
                BeginInvoke(() => Invalidate());
            else
                Invalidate();
        };
        _themeService.ThemeChanged += _themeChangedHandler;
    }

    /// <summary>
    /// 更新显示数据
    /// </summary>
    public void UpdateData(QuotaSnapshot snapshot)
    {
        _snapshot = snapshot;
        if (InvokeRequired)
            BeginInvoke(() => Invalidate());
        else
            Invalidate();
    }

    /// <summary>
    /// 绘制浮动条内容
    /// </summary>
    private void OnPaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        bool isDark = _themeService.IsDark;

        // 背景
        Color bgColor = isDark
            ? Color.FromArgb(220, 22, 33, 62)
            : Color.FromArgb(240, 245, 245, 250);

        using (var brush = new SolidBrush(bgColor))
        {
            g.FillRectangle(brush, ClientRectangle);
        }

        // 状态指示小圆点（每次绘制读取最新配置）
        var cfg = _configService.Config;
        var status = _snapshot.GetStatus(cfg.WarningThreshold, cfg.CriticalThreshold);
        Color dotColor = status switch
        {
            QuotaStatus.Normal => Color.FromArgb(0, 180, 0),
            QuotaStatus.Warning => Color.FromArgb(230, 180, 0),
            QuotaStatus.Critical => Color.FromArgb(200, 0, 0),
            _ => Color.FromArgb(128, 128, 128)
        };
        using (var dotBrush = new SolidBrush(dotColor))
        {
            g.FillEllipse(dotBrush, 10, Height / 2 - 4, 8, 8);
        }

        // 文字
        string text = FormatBarText();
        Color textColor = isDark ? Color.FromArgb(224, 224, 224) : Color.FromArgb(30, 30, 30);
        using var font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
        using var textBrush = new SolidBrush(textColor);

        var textSize = g.MeasureString(text, font);
        float tx = 24; // 圆点右侧
        float ty = (Height - textSize.Height) / 2;
        g.DrawString(text, font, textBrush, tx, ty);
    }

    /// <summary>
    /// 格式化浮动条文字
    /// </summary>
    private string FormatBarText()
    {
        if (_snapshot.IsOffline)
            return "GLM: 离线";

        string mcp = $"{_snapshot.McpQuota.Percentage:F0}%";
        string token = $"{_snapshot.Token5hQuota.Percentage:F0}%";
        string calls = _snapshot.CallCount > 0 ? $" | {FormatNumber(_snapshot.CallCount)}次" : "";

        return $"MCP: {mcp} | 5h: {token}{calls}";
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

            // 如果正在贴边状态，先展开再拖
            if (_isSnapped)
            {
                _isSnapped = false;
                _isExpanded = false;
                _snapEdge = DockStyle.None;
                Size = new Size(DefaultBarWidth, BarHeight);
                ApplyRoundedCorners();
            }
        }
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            var screenPoint = PointToScreen(e.Location);
            Location = new Point(screenPoint.X - _dragOffset.X, screenPoint.Y - _dragOffset.Y);
        }
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;

        // 检测贴边
        var screen = Screen.PrimaryScreen!.WorkingArea;
        var loc = Location;

        if (loc.X <= screen.Left + EdgeSnapThreshold)
            SnapToEdge(DockStyle.Left, screen);
        else if (loc.X + Width >= screen.Right - EdgeSnapThreshold)
            SnapToEdge(DockStyle.Right, screen);
        else if (loc.Y <= screen.Top + EdgeSnapThreshold)
            SnapToEdge(DockStyle.Top, screen);

        // 保存位置到配置
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
            case DockStyle.Left:
                Location = new Point(screen.Left, Location.Y);
                break;
            case DockStyle.Right:
                Location = new Point(screen.Right - Width, Location.Y);
                break;
            case DockStyle.Top:
                Location = new Point(Location.X, screen.Top);
                break;
        }
    }

    private void CollapseIfSnapped()
    {
        if (!_isSnapped || !_isExpanded) return;
        _isExpanded = false;

        var screen = Screen.PrimaryScreen!.WorkingArea;
        switch (_snapEdge)
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

    #endregion

    /// <summary>
    /// 保存当前位置到配置
    /// </summary>
    private void PersistPosition()
    {
        try
        {
            var config = _configService.Config;
            config.FloatingBarX = Location.X;
            config.FloatingBarY = Location.Y;
            _configService.Save(config);
        }
        catch
        {
            // 保存失败不影响使用
        }
    }

    /// <summary>
    /// 应用圆角
    /// </summary>
    private void ApplyRoundedCorners()
    {
        var oldRegion = Region;
        using var path = new GraphicsPath();
        int radius = 8;
        path.AddArc(0, 0, radius * 2, radius * 2, 180, 90);
        path.AddArc(Width - radius * 2, 0, radius * 2, radius * 2, 270, 90);
        path.AddArc(Width - radius * 2, Height - radius * 2, radius * 2, radius * 2, 0, 90);
        path.AddArc(0, Height - radius * 2, radius * 2, radius * 2, 90, 90);
        path.CloseFigure();
        Region = new Region(path);
        oldRegion?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hideTimer.Dispose();
            _themeService.ThemeChanged -= _themeChangedHandler;
        }
        base.Dispose(disposing);
    }
}
