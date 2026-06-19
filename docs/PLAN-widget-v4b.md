# Widget V4b：刷新行优化

## 问题

1. 刷新按钮和更新时间与统计行紧贴，无视觉分隔
2. 更新时间精确到秒，不需要
3. 刷新按钮字号和颜色太抢眼
4. 时间字号应更小更淡

## 修改清单

### FloatingWidget.cs

#### 1. 字体

删除 `_refreshFont`（8.5pt），新增 `_tinyFont`（7.5pt）：

```csharp
// 删除：
private readonly Font _refreshFont = new("Segoe UI", 8.5f);

// 新增：
private readonly Font _tinyFont = new("Segoe UI", 7.5f);
```

#### 2. Dispose

```csharp
// 删除：
_refreshFont.Dispose();

// 新增：
_tinyFont.Dispose();
```

#### 3. OnPaint 刷新行区域

当前代码（统计行后）：
```csharp
y += 16;
// 直接画刷新行
```

改为：
```csharp
y += 12;
// 画分隔线（使用 sepColor，同其他分隔线）
using (var sepPen = new Pen(sepColor))
    g.DrawLine(sepPen, x, y, x + cw, y);
y += 8;
// 画刷新行
```

#### 4. 刷新按钮样式

当前：
```csharp
Color refreshColor = isDark ? Color.FromArgb(123, 140, 222) : Color.FromArgb(60, 80, 180);
g.DrawString(refreshText, _refreshFont, refreshBrush, x, y);
```

改为：
```csharp
Color refreshColor = isDark ? Color.FromArgb(100, 110, 150) : Color.FromArgb(80, 90, 120);
g.DrawString(refreshText, _labelFont, refreshBrush, x, y);  // 复用 _labelFont (9pt)
```

#### 5. 更新时间样式

当前：
```csharp
Color timeColor = isDark ? Color.FromArgb(80, 90, 115) : Color.FromArgb(140, 140, 160);
string timeText = _snapshot.IsOffline ? "离线" : $"{_snapshot.Timestamp:HH:mm:ss} 更新";
g.DrawString(timeText, _detailFont, timeBrush, ...);
```

改为：
```csharp
Color timeColor = isDark ? Color.FromArgb(70, 80, 100) : Color.FromArgb(150, 150, 165);
string timeText = _snapshot.IsOffline ? "离线" : $"{_snapshot.Timestamp:HH:mm} 更新";
g.DrawString(timeText, _tinyFont, timeBrush, ...);  // 7.5pt
```

#### 6. sepColor 变量

sepColor 已在 OnPaint 中声明（用于其他分隔线）。确认刷新行的分隔线可以复用同一个 sepColor 变量。

### TrayApplicationContext.cs

无改动。

## 空间验证

```
y=14   MCP 行 (24px) → y=38
y=46   5h 行 (24px) → y=70
y=80   统计行
y=92   y+=12 → 分隔线
y=100  y+=8 → 刷新行（按钮左 + 时间右）
y=~114 刷新行底部（9pt字高约14px）
y=130  窗口底部（余量16px）✅
```
