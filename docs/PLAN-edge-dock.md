# 贴边吸附显示功能计划

## 目标

Widget 贴边后露出精致的迷你进度条：
- 贴左/右：12px × 80px 竖条，两段（MCP 上，5h 下），从下往上填充
- 贴顶：126px × 12px 横条，两段（MCP 左，5h 右），从左往右填充
- 距屏幕边留白 6px（窗口完全在屏幕内）
- 色块内缩 1.5px
- Catppuccin 深色/浅色配色（浅色模式色块使用柔和色调，与卡片不同）
- 鼠标悬停展开完整卡片，离开收回

## 常量

```csharp
// 替换现有 RevealEdgeWidth = 4
private const int EdgeRevealWidth = 12;     // 贴边露出宽度
private const int EdgeStripVHeight = 80;    // 竖条高度（左/右）
private const int EdgeStripHWidth = 126;    // 横条宽度（顶）
private const int EdgeMargin = 6;           // 距屏幕边留白
private const int EdgeSegGap = 3;           // 两段间距
private const int EdgeSegPadding = 5;       // 段内 padding（竖条上下 / 横条左右）
private const int EdgeSegInset = 1;         // 色块内缩（每侧，总 2px）
private const int EdgeRadius = 3;           // 圆角
```

## 窗口尺寸和位置

所有窗口完全在屏幕内，留白是窗口内部的空间。

**贴右边：**
```
窗口尺寸：EdgeMargin + EdgeRevealWidth = 6 + 12 = 18px 宽
          EdgeStripVHeight = 80px 高
位置：X = screen.Right - 18（窗口右边缘对齐屏幕右边缘）
      Y = 居中
内部布局：[6px 留白][12px 进度条]
```

**贴左边：**
```
窗口尺寸：EdgeMargin + EdgeRevealWidth = 18px 宽 × 80px 高
位置：X = screen.Left（窗口左边缘对齐屏幕左边缘）
内部布局：[12px 进度条][6px 留白]
```

**贴顶边：**
```
窗口尺寸：EdgeStripHWidth = 126px 宽 × EdgeMargin + EdgeRevealWidth = 18px 高
位置：X = 居中
      Y = screen.Top（窗口顶部对齐屏幕顶部）
内部布局：[6px 留白（上）][12px 进度条]
```

## OnPaint 分支

```csharp
private void OnPaint(...)
{
    if (_isSnapped && !_isExpanded)
        PaintEdgeStrip(g, isDark, w, h);
    else
        PaintFullCard(g, isDark, cfg, w, h);
}
```

`PaintFullCard` = 当前 OnPaint 的全部内容（提取为方法）。
`PaintEdgeStrip` = 新方法。

## PaintEdgeStrip 绘制

**贴左/右（竖条 18×80）：**
```
背景：Mantle #181825 / Latte #e6e9ef，圆角 3px

以贴右边为例（留白在左）：
  进度条区域 X = EdgeMargin = 6, 宽 = EdgeRevealWidth = 12
  进度条区域 Y = EdgeSegPadding = 5, 高 = 80 - 5*2 = 70

  两段，各高 = (70 - EdgeSegGap) / 2 = (70 - 3) / 2 = 33.5px
  上段（MCP）：Y = 5, H = 33.5
  下段（5h）：Y = 5 + 33.5 + 3 = 41.5, H = 33.5

  每段内：
    段背景：Surface 0 #313244 / Latte #ccd0da
    色块：position absolute, bottom=0, left=1px, right=1px
    高度 = 百分比 × 段高
    颜色：按配额状态

贴左边布局镜像（留白在右）。
```

**贴顶（横条 126×18）：**
```
背景：同上

留白在上（Y=0..6）
进度条区域 Y = EdgeMargin = 6, 高 = 12
进度条区域 X = EdgeSegPadding = 5, 宽 = 126 - 10 = 116

两段，各宽 = (116 - EdgeSegGap) / 2 = 56.5px
左段（MCP）：X = 5, W = 56.5
右段（5h）：X = 5 + 56.5 + 3 = 64.5, W = 56.5

每段内：
  色块从左往右填充，top=1px, bottom=1px
  宽度 = 百分比 × 段宽
```

## 颜色

**深色 Mocha（边缘条）：**
- 条背景：Mantle (24,24,37)
- 段背景：Surface 0 (49,50,68)
- MCP 蓝：(137,180,250)
- 5h 青：(148,226,213)
- 警告黄：(249,226,175)
- 临界红：(243,139,168)

**浅色 Latte（边缘条，柔和色调）：**
- 条背景：(230,233,239) — 比卡片 Base 更深，区分层次
- 段背景：(204,208,218)
- MCP 蓝：(106,159,216)
- 5h 青：(95,168,160)
- 警告黄：(201,168,76)
- 临界红：(192,80,80)

注：浅色模式边缘条使用柔和色调，与完整卡片的 Latte 亮色区分。这是有意的设计选择——小条用亮色太抢眼。

## SnapToEdge 修改

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
            Location = new Point(screen.Right - Width, screen.Bottom / 2 - Height / 2);
            break;
        case DockStyle.Left:
            Size = new Size(EdgeMargin + EdgeRevealWidth, EdgeStripVHeight);
            Location = new Point(screen.Left, screen.Bottom / 2 - Height / 2);
            break;
        case DockStyle.Top:
            Size = new Size(EdgeStripHWidth, EdgeMargin + EdgeRevealWidth);
            Location = new Point(screen.Right / 2 - Width / 2, screen.Top);
            break;
    }

    BackColor = _themeService.IsDark
        ? Color.FromArgb(24, 24, 37)
        : Color.FromArgb(230, 233, 239);

    Invalidate();
}
```

## Expand 修改

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

    // BackColor 会在下一次 OnPaint 中自动更新为 Base 色
    Invalidate();
}
```

## CollapseIfSnapped 修改

恢复迷你条尺寸和位置，同 SnapToEdge 的逻辑。

## OnMouseDown 修改（拖动解除贴边）

当前代码清除 snap 标志但不恢复窗口尺寸。新增：
```csharp
if (_isSnapped)
{
    _isSnapped = false;
    _isExpanded = false;
    _snapEdge = DockStyle.None;
    Size = new Size(CardWidth, CardHeight); // 新增：恢复卡片尺寸
}
```

## PersistPosition 修改

贴边时不保存位置（迷你条坐标无意义）：
```csharp
private void PersistPosition()
{
    if (_isSnapped) return; // 新增：贴边时不保存
    try
    {
        var config = _configService.Config;
        config.FloatingBarX = Location.X;
        config.FloatingBarY = Location.Y;
        _configService.Save(config);
    }
    catch { }
}
```

## 删除旧常量

删除 `RevealEdgeWidth = 4`（第 29 行），全部替换为 `EdgeRevealWidth`。

## BackColor 同步

在 `_themeChangedHandler` 中，根据贴边状态设置不同 BackColor：
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

## 修改文件清单

仅修改 `FloatingWidget.cs`：
- 新增 8 个常量，删除 1 个旧常量
- 新增 `PaintEdgeStrip()` 方法
- 提取 `PaintFullCard()` 方法（当前 OnPaint 内容）
- 修改 `OnPaint`：分支绘制
- 修改 `SnapToEdge`：设置迷你条尺寸
- 修改 `Expand`：恢复卡片尺寸
- 修改 `CollapseIfSnapped`：恢复迷你条尺寸
- 修改 `OnMouseDown`：拖动时恢复尺寸
- 修改 `PersistPosition`：贴边时跳过
- 修改 `_themeChangedHandler`：BackColor 按状态切换

不修改其他文件。