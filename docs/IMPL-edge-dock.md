# 实现计划：贴边吸附显示

## FloatingWidget.cs 修改清单

### 1. 常量区

删除：
```csharp
private const int RevealEdgeWidth = 4;   // 第 29 行
```

新增：
```csharp
private const int EdgeRevealWidth = 12;
private const int EdgeStripVHeight = 80;
private const int EdgeStripHWidth = 126;
private const int EdgeMargin = 6;
private const int EdgeSegGap = 3;
private const int EdgeSegPadding = 5;
private const int EdgeSegInset = 1;
private const int EdgeRadius = 3;
```

### 2. OnPaint 提取为两个方法

当前 OnPaint（第 141-217 行）拆分为：

```csharp
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
```

`PaintFullCard` = 当前 OnPaint 的全部绘图逻辑（背景、边框、配额行、统计行、底部行）。

### 3. 新增 PaintEdgeStrip 方法

```csharp
private void PaintEdgeStrip(Graphics g, bool isDark, int w, int h)
{
    // 颜色
    Color bg = isDark ? Color.FromArgb(24, 24, 37) : Color.FromArgb(230, 233, 239);
    Color segBg = isDark ? Color.FromArgb(49, 50, 68) : Color.FromArgb(204, 208, 218);

    // 背景
    using (var bgBrush = new SolidBrush(bg))
    using (var path = GraphicsExtensions.MakeRoundRect(0, 0, w, h, EdgeRadius))
        g.FillPath(bgBrush, path);

    if (_snapEdge == DockStyle.Top)
    {
        // 横条：从左往右
        PaintEdgeStripHorizontal(g, isDark, segBg, w, h);
    }
    else
    {
        // 竖条：从下往上
        PaintEdgeStripVertical(g, isDark, segBg, w, h);
    }
}
```

### 4. PaintEdgeStripVertical

```csharp
private void PaintEdgeStripVertical(Graphics g, bool isDark, Color segBg, int w, int h)
{
    // 进度条区域：留白在靠屏幕边的一侧
    int barX, barW;
    if (_snapEdge == DockStyle.Right)
    {
        barX = EdgeMargin;          // 留白在左
        barW = EdgeRevealWidth;
    }
    else // Left
    {
        barX = 0;                   // 留白在右（窗口宽度 = EdgeMargin + EdgeRevealWidth）
        barW = EdgeRevealWidth;
    }

    int barY = EdgeSegPadding;
    int barH = h - EdgeSegPadding * 2;
    int segH = (barH - EdgeSegGap) / 2;

    // 上段 MCP
    DrawEdgeSegment(g, segBg, isDark, _snapshot.McpQuota, _configService.Config,
        barX, barY, barW, segH, vertical: true);

    // 下段 5h
    DrawEdgeSegment(g, segBg, isDark, _snapshot.Token5hQuota, _configService.Config,
        barX, barY + segH + EdgeSegGap, barW, segH, vertical: true);
}
```

### 5. PaintEdgeStripHorizontal

```csharp
private void PaintEdgeStripHorizontal(Graphics g, bool isDark, Color segBg, int w, int h)
{
    int barX = EdgeSegPadding;
    int barW = w - EdgeSegPadding * 2;
    int barY = EdgeMargin;          // 留白在上
    int barH = EdgeRevealWidth;
    int segW = (barW - EdgeSegGap) / 2;

    // 左段 MCP
    DrawEdgeSegment(g, segBg, isDark, _snapshot.McpQuota, _configService.Config,
        barX, barY, segW, barH, vertical: false);

    // 右段 5h
    DrawEdgeSegment(g, segBg, isDark, _snapshot.Token5hQuota, _configService.Config,
        barX + segW + EdgeSegGap, barY, segW, barH, vertical: false);
}
```

### 6. DrawEdgeSegment

```csharp
private void DrawEdgeSegment(Graphics g, Color segBg, bool isDark,
    QuotaItem item, AppConfig config, int x, int y, int w, int h, bool vertical)
{
    double pct = Math.Clamp(item.Percentage, 0, 100);

    // 段背景
    using (var bgBrush = new SolidBrush(segBg))
        g.FillRectangle(bgBrush, x, y, w, h);

    // 色块颜色
    Color fillColor;
    if (pct >= config.CriticalThreshold)
        fillColor = isDark ? Color.FromArgb(243, 139, 168) : Color.FromArgb(192, 80, 80);
    else if (pct >= config.WarningThreshold)
        fillColor = isDark ? Color.FromArgb(249, 226, 175) : Color.FromArgb(201, 168, 76);
    else if (item.Type == "TIME_LIMIT")
        fillColor = isDark ? Color.FromArgb(137, 180, 250) : Color.FromArgb(106, 159, 216);
    else
        fillColor = isDark ? Color.FromArgb(148, 226, 213) : Color.FromArgb(95, 168, 160);

    // 色块
    using var fillBrush = new SolidBrush(fillColor);
    if (vertical)
    {
        // 从下往上
        int fillH = (int)((h - EdgeSegInset * 2) * pct / 100);
        if (fillH > 0)
            g.FillRectangle(fillBrush,
                x + EdgeSegInset, y + h - EdgeSegInset - fillH,
                w - EdgeSegInset * 2, fillH);
    }
    else
    {
        // 从左往右
        int fillW = (int)((w - EdgeSegInset * 2) * pct / 100);
        if (fillW > 0)
            g.FillRectangle(fillBrush,
                x + EdgeSegInset, y + EdgeSegInset,
                fillW, h - EdgeSegInset * 2);
    }
}
```

### 7. SnapToEdge 修改

```csharp
private void SnapToEdge(DockStyle edge, Rectangle screen)
{
    _isSnapped = true;
    _snapEdge = edge;
    _isExpanded = false;

    switch (edge)
    {
        case DockStyle.Right:
            Size = new Size(EdgeMargin + EdgeRevealWidth, EdgeStripVHeight);
            Location = new Point(screen.Right - Width, screen.Top + screen.Height / 2 - Height / 2);
            break;
        case DockStyle.Left:
            Size = new Size(EdgeMargin + EdgeRevealWidth, EdgeStripVHeight);
            Location = new Point(screen.Left, screen.Top + screen.Height / 2 - Height / 2);
            break;
        case DockStyle.Top:
            Size = new Size(EdgeStripHWidth, EdgeMargin + EdgeRevealWidth);
            Location = new Point(screen.Left + screen.Width / 2 - Width / 2, screen.Top);
            break;
    }

    BackColor = _themeService.IsDark
        ? Color.FromArgb(24, 24, 37)
        : Color.FromArgb(230, 233, 239);

    Invalidate();
}
```

### 8. Expand 修改

```csharp
private void Expand()
{
    if (!_isSnapped) return;
    _isExpanded = true;

    Size = new Size(CardWidth, CardHeight);

    var screen = Screen.PrimaryScreen!.WorkingArea;
    switch (_snapEdge)
    {
        case DockStyle.Left: Location = new Point(screen.Left + EdgeMargin, Location.Y); break;
        case DockStyle.Right: Location = new Point(screen.Right - CardWidth - EdgeMargin, Location.Y); break;
        case DockStyle.Top: Location = new Point(Location.X, screen.Top + EdgeMargin); break;
    }

    Invalidate();
}
```

### 9. CollapseIfSnapped 修改

恢复迷你条尺寸和位置（不改变 _isSnapped/_snapEdge/_isExpanded 标志）：

```csharp
private void CollapseIfSnapped()
{
    if (!_isSnapped || !_isExpanded) return;
    _isExpanded = false;

    var screen = Screen.PrimaryScreen!.WorkingArea;
    switch (_snapEdge)
    {
        case DockStyle.Right:
            Size = new Size(EdgeMargin + EdgeRevealWidth, EdgeStripVHeight);
            Location = new Point(screen.Right - Width, screen.Top + screen.Height / 2 - Height / 2);
            break;
        case DockStyle.Left:
            Size = new Size(EdgeMargin + EdgeRevealWidth, EdgeStripVHeight);
            Location = new Point(screen.Left, screen.Top + screen.Height / 2 - Height / 2);
            break;
        case DockStyle.Top:
            Size = new Size(EdgeStripHWidth, EdgeMargin + EdgeRevealWidth);
            Location = new Point(screen.Left + screen.Width / 2 - Width / 2, screen.Top);
            break;
    }

    BackColor = _themeService.IsDark
        ? Color.FromArgb(24, 24, 37)
        : Color.FromArgb(230, 233, 239);

    Invalidate();
}
```

### 10. OnMouseDown 修改

### 10. OnMouseDown 修改

拖动解除贴边时恢复尺寸：
```csharp
if (_isSnapped)
{
    _isSnapped = false;
    _isExpanded = false;
    _snapEdge = DockStyle.None;
    Size = new Size(CardWidth, CardHeight); // 新增
}
```

### 11. PersistPosition 修改

贴边时不保存：
```csharp
private void PersistPosition()
{
    if (_isSnapped) return;
    // ... 原有逻辑
}
```

### 12. _themeChangedHandler 修改

```csharp
_themeChangedHandler = (isDark) =>
{
    void Update()
    {
        if (_isSnapped && !_isExpanded)
            BackColor = isDark ? Color.FromArgb(24, 24, 37) : Color.FromArgb(230, 233, 239);
        else
            BackColor = isDark ? Color.FromArgb(30, 30, 46) : Color.FromArgb(239, 241, 245);
        Invalidate();
    }
    if (InvokeRequired) BeginInvoke(Update);
    else Update();
};
```

### 13. MouseEnter/MouseLeave 修改

MouseEnter 在贴边时触发展开，同时恢复 BackColor：
```csharp
MouseEnter += (_, _) =>
{
    _hideTimer.Stop();
    if (_isSnapped && !_isExpanded) Expand();
};
```

## 不修改

- TrayApplicationContext.cs
- QuotaService.cs、其他文件
