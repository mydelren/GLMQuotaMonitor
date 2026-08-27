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
    private const int CardRadius = 6;
    private const int CardHPadding = 20;
    private const int CardVPadding = 14;

    // ═══ 三列布局 ═══
    private const int LabelColWidth = 64;
    private const int BarWidth = 68;
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

    // 缓存字体（页脚/统计行曾用 7.5-8pt，实测偏小且页脚对比度不足，整体上调一档）
    private readonly Font _labelFont = new("Segoe UI", 9f);
    private readonly Font _valueFont = new("Segoe UI", 11f, FontStyle.Bold);
    private readonly Font _detailFont = new("Segoe UI", 8.5f);
    private readonly Font _tinyFont = new("Segoe UI", 8f);

    /// <summary>迷你条悬停提示</summary>
    private readonly System.Windows.Forms.ToolTip _tip = new();

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
        // 窗口真实形状由 ApplyShapeRegion 裁出圆角（避免矩形角在桌面/浅色背景下露出色块）
        BackColor = _themeService.IsDark ? Color.FromArgb(30, 30, 46) : Color.FromArgb(239, 241, 245);

        Size = new Size(CardWidth, CardHeight);

        if (config.FloatingBarX.HasValue && config.FloatingBarY.HasValue)
            Location = new Point(config.FloatingBarX.Value, config.FloatingBarY.Value);
        else
        {
            var screen = Screen.PrimaryScreen!.WorkingArea;
            Location = new Point(screen.Right - CardWidth - 20, screen.Top + screen.Height / 2 - CardHeight / 2);
        }

        // 分辨率变更/断开显示器后，保存的位置可能落在屏幕外：钳制回最近的工作区
        ClampIntoNearestScreen();

        // 恢复上次的贴边状态
        if (config.SnapEdgeValue is >= 1 and <= 3)
            RestoreSnappedState(config);

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
        void Apply()
        {
            // 折叠态才挂 tooltip；展开态清空，避免气泡在卡片上重复可见数字
            if (_isSnapped && !_isExpanded) ShowTip();
            else HideTip();
            Invalidate();
        }
        if (InvokeRequired) BeginInvoke(Apply);
        else Apply();
    }

    /// <summary>悬浮窗折叠态才需要 tooltip；展开卡片时数字已可见，隐藏避免重复</summary>
    private void ShowTip() => _tip.SetToolTip(this, BuildTooltip());
    private void HideTip() => _tip.SetToolTip(this, "");

    /// <summary>迷你条悬停提示内容（收起态唯一的数字出口）</summary>
    private string BuildTooltip()
    {
        static string P(double v) => v > 0 && v < 10 ? $"{v:F1}%" : $"{v:F0}%";
        var s = _snapshot;
        // 离线快照保留的是最后一次成功数据的配额值，但 ResetDateTime 会逐渐过期，不再拼倒计时
        string reset = !s.IsOffline && s.Token5hQuota.ResetDateTime is { } r
            ? $"（{FormatResetCountdown(r)}）"
            : "";
        return $"MCP 配额 {P(s.McpQuota.Percentage)}\n" +
               $"5h Token {P(s.Token5hQuota.Percentage)}{reset}\n" +
               (s.IsOffline ? "离线 — 显示上次数据" : $"{s.Timestamp:HH:mm} 更新");
    }

    #region 绘制

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ApplyShapeRegion();
    }

    /// <summary>
    /// 把窗口 Region 裁成当前状态的圆角形状：
    /// 只靠 Paint 画圆角时，四个角落会露出矩形窗体的本底色块（毛刺），
    /// Win10 没有 DWMWA_WINDOW_CORNER_PREFERENCE，只能用 Region 真实裁形
    /// </summary>
    private void ApplyShapeRegion()
    {
        if (Width <= 0 || Height <= 0) return;
        float r = _isSnapped && !_isExpanded ? EdgeRadius : CardRadius;
        using var path = GraphicsExtensions.MakeRoundRect(0, 0, Width, Height, r);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
    }

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
        // Catppuccin 颜色（页脚一档提到 subtext 级别，7.5pt 时代的低对比问题一并解决）
        Color bg = isDark ? Color.FromArgb(30, 30, 46) : Color.FromArgb(239, 241, 245);
        Color borderColor = isDark ? Color.FromArgb(49, 50, 68) : Color.FromArgb(204, 208, 218);
        Color statColor = isDark ? Color.FromArgb(127, 132, 156) : Color.FromArgb(108, 111, 133);
        Color footerColor = isDark ? Color.FromArgb(110, 115, 141) : Color.FromArgb(122, 125, 148);

        // 背景
        using (var bgBrush = new SolidBrush(bg))
        using (var path = GraphicsExtensions.MakeRoundRect(0, 0, w, h, CardRadius))
            g.FillPath(bgBrush, path);

        // 边框
        using (var borderPen = new Pen(borderColor))
        using (var path = GraphicsExtensions.MakeRoundRect(0, 0, w - 1, h - 1, CardRadius))
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

        // 页脚：离线时倒计时已过期失真，只保留右侧状态文本
        using var footerBrush = new SolidBrush(footerColor);

        if (!_snapshot.IsOffline && _snapshot.Token5hQuota.ResetDateTime is { } reset)
        {
            string resetText = FormatResetCountdown(reset);
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

        // 进度条（药丸形轨道）
        int barY = y + (RowHeight - BarHeight) / 2;
        Color barBg = isDark ? Color.FromArgb(49, 50, 68) : Color.FromArgb(204, 208, 218);
        using (var bgBrush = new SolidBrush(barBg))
            g.FillRoundedRectangle(bgBrush, barX, barY, BarWidth, BarHeight, BarHeight / 2f);

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
        if (pct > 0) fillW = Math.Max(fillW, 4); // 极小百分比也留可见填充
        if (fillW > 0)
        {
            // 填充裁剪到轨道圆角路径内：短填充的左端自然跟随轨道曲线，
            // 不再把左端直角"盖"到圆弧上（旧版看起来一边胶囊一边方角的原因）
            using var trackPath = GraphicsExtensions.MakeRoundRect(barX, barY, BarWidth, BarHeight, BarHeight / 2f);
            g.SetClip(trackPath);

            using var fillBrush = new SolidBrush(pctColor);
            float radius = Math.Min(BarHeight / 2f, fillW / 2f);
            g.FillRoundedRectangle(fillBrush, barX, barY, fillW, BarHeight, radius);

            // 顶部 1px 内高光：给纯色药丸一点立体感（对深浅色/任意填充色通用）
            if (fillW > radius * 2)
                using (var gloss = new Pen(Color.FromArgb(70, Color.White), 1f))
                    g.DrawLine(gloss, barX + radius, barY + 1.5f, barX + fillW - radius, barY + 1.5f);

            g.ResetClip();
        }

        // 百分比（右对齐；<10% 显示一位小数，避免 7.5% 被四舍五入成误导性的 8%/0%）
        using var pctBrush = new SolidBrush(pctColor);
        string pctText = pct > 0 && pct < 10 ? $"{pct:F1}%" : $"{pct:F0}%";
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

        _hideTimer.Stop();
        _isDragging = true;

        if (_isSnapped)
        {
            // 从迷你条展开为卡片时按光标相对比例重新锚定，避免卡片"跳"到光标右下方
            var cursor = Cursor.Position;
            float relX = Width > 0 ? e.Location.X / (float)Width : 0f;
            float relY = Height > 0 ? e.Location.Y / (float)Height : 0f;

            _isSnapped = false;
            _isExpanded = false;
            _snapEdge = DockStyle.None;
            HideTip();
            Size = new Size(CardWidth, CardHeight);

            int newX = cursor.X - (int)(CardWidth * relX);
            int newY = cursor.Y - (int)(CardHeight * relY);
            var area = Screen.FromPoint(cursor).WorkingArea;
            newX = Math.Clamp(newX, area.Left + EdgeMarginScreen, area.Right - CardWidth - EdgeMarginDesktop);
            newY = Math.Clamp(newY, area.Top + EdgeMarginScreen, area.Bottom - CardHeight);
            Location = new Point(newX, newY);

            // 以卡片新位置重新计算拖拽偏移
            _dragOffset = PointToClient(cursor);
            BackColor = _themeService.IsDark ? Color.FromArgb(30, 30, 46) : Color.FromArgb(239, 241, 245);
            Invalidate();
            return;
        }

        _dragOffset = e.Location;
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

        // 用窗口中心所在的屏幕判断吸附，支持多显示器
        var center = new Point(Location.X + Width / 2, Location.Y + Height / 2);
        var screen = Screen.FromPoint(center).WorkingArea;

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
    /// 定位浮动条：贴边收起时展开 3 秒后收回；窗口因显示器变更等原因不可见时，拉回主屏默认位置
    /// </summary>
    public void Locate()
    {
        if (_isSnapped && !_isExpanded)
        {
            Expand();
            StartCollapseTimer();
            return;
        }
        if (_isSnapped && _isExpanded) return;

        // 未贴边但窗口与任何工作区几乎无交集时，移回主屏右缘中部（用未变异的工作区计算）
        var current = new Rectangle(Location, Size);
        bool visibleEnough =
            Screen.AllScreens.Any(s => Rectangle.Intersect(s.WorkingArea, current).Width >= Width / 2 &&
                                       Rectangle.Intersect(s.WorkingArea, current).Height >= Height / 2);
        if (!visibleEnough)
        {
            var primary = Screen.PrimaryScreen!.WorkingArea;
            Location = new Point(primary.Right - CardWidth - 20,
                                 primary.Top + primary.Height / 2 - CardHeight / 2);
        }
    }

    /// <summary>3 秒后收回贴边展开的卡片</summary>
    private void StartCollapseTimer()
    {
        var timer = new System.Windows.Forms.Timer { Interval = 3000 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            CollapseIfSnapped();
        };
        timer.Start();
    }

    #region 贴边吸附

    /// <summary>给定边缘与工作区，返回迷你条的 (位置, 尺寸)</summary>
    private (Point loc, Size size) EdgeGeometry(DockStyle edge, Rectangle screen)
    {
        return edge switch
        {
            DockStyle.Right => (
                new Point(screen.Right - (EdgeMarginDesktop + EdgeBarWidth + EdgeMarginScreen), Math.Clamp(Location.Y, screen.Top, screen.Bottom - EdgeStripVHeight)),
                new Size(EdgeMarginDesktop + EdgeBarWidth + EdgeMarginScreen, EdgeStripVHeight)),
            DockStyle.Left => (
                new Point(screen.Left, Math.Clamp(Location.Y, screen.Top, screen.Bottom - EdgeStripVHeight)),
                new Size(EdgeMarginDesktop + EdgeBarWidth + EdgeMarginScreen, EdgeStripVHeight)),
            _ => // Top
                (
                new Point(Math.Clamp(Location.X, screen.Left, screen.Right - EdgeStripHWidth), screen.Top),
                new Size(EdgeStripHWidth, EdgeMarginDesktop + EdgeBarWidth + EdgeMarginScreen))
        };
    }

    private void SnapToEdge(DockStyle edge, Rectangle screen)
    {
        _isSnapped = true;
        _snapEdge = edge;
        _isExpanded = false;

        var (loc, size) = EdgeGeometry(edge, screen);
        Location = loc;
        Size = size;

        BackColor = _themeService.IsDark
            ? Color.FromArgb(24, 24, 37)
            : Color.FromArgb(230, 233, 239);

        Invalidate();
        PersistPosition();
    }

    /// <summary>启动时从配置恢复贴边状态</summary>
    private void RestoreSnappedState(AppConfig config)
    {
        _snapEdge = (DockStyle)config.SnapEdgeValue;
        if (_snapEdge is not (DockStyle.Left or DockStyle.Right or DockStyle.Top)) return;

        if (config.SnapPosition.HasValue)
        {
            // 先把主定位放进去，让 EdgeGeometry 的钳制基于合理坐标
            Location = config.SnapPosition.Value switch
            {
                int y when _snapEdge is DockStyle.Left or DockStyle.Right => new Point(Location.X, y),
                int x => new Point(x, Location.Y)
            };
        }
        var area = Screen.FromPoint(Location).WorkingArea;
        var (loc, size) = EdgeGeometry(_snapEdge, area);
        _isSnapped = true;
        Location = loc;
        Size = size;
        BackColor = _themeService.IsDark ? Color.FromArgb(24, 24, 37) : Color.FromArgb(230, 233, 239);
    }

    private void Expand()
    {
        if (!_isSnapped) return;
        _isExpanded = true;
        HideTip();

        Size = new Size(CardWidth, CardHeight);
        var screen = Screen.FromPoint(new Point(Location.X + Width / 2, Location.Y)).WorkingArea;
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
        ShowTip();

        var area = Screen.FromPoint(Location).WorkingArea;
        var (loc, size) = EdgeGeometry(_snapEdge, area);
        Location = loc;
        Size = size;

        BackColor = _themeService.IsDark
            ? Color.FromArgb(24, 24, 37)
            : Color.FromArgb(230, 233, 239);

        Invalidate();
    }

    #endregion

    /// <summary>把窗口钳制回最近屏幕的工作区（应对分辨率变更、显示器拔插）</summary>
    private void ClampIntoNearestScreen()
    {
        var area = Screen.FromPoint(Location).WorkingArea;
        int x = Math.Clamp(Location.X, area.Left, Math.Max(area.Left, area.Right - Width));
        int y = Math.Clamp(Location.Y, area.Top, Math.Max(area.Top, area.Bottom - Height));
        if (x != Location.X || y != Location.Y)
            Location = new Point(x, y);
    }

    private void PersistPosition()
    {
        try
        {
            var config = _configService.Config;
            config.FloatingBarX = Location.X;
            config.FloatingBarY = Location.Y;
            config.SnapEdgeValue = _isSnapped ? (int)_snapEdge : 0;
            config.SnapPosition = _isSnapped
                ? (_snapEdge is DockStyle.Left or DockStyle.Right ? Location.Y : Location.X)
                : null;
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

    /// <summary>把重置时间格式化为紧凑倒计时（页面按轮询间隔刷新，分钟级误差可接受）</summary>
    private static string FormatResetCountdown(DateTime target)
    {
        var span = target - DateTime.Now;
        if (span <= TimeSpan.Zero) return "即将重置";
        if (span.TotalDays >= 1) return $"{(int)span.TotalDays}d{span.Hours}h 后重置";
        if (span.TotalHours >= 1) return $"{span.Hours}h{span.Minutes:D2}m 后重置";
        return $"{Math.Max(1, span.Minutes)}m 后重置";
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
            _tip.Dispose();
            _themeService.ThemeChanged -= _themeChangedHandler;
            _labelFont.Dispose();
            _valueFont.Dispose();
            _detailFont.Dispose();
            _tinyFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
