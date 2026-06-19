# 实现计划：Widget V4b

## FloatingWidget.cs 修改

### 1. 字体声明（第 49 行）

```
删除：private readonly Font _refreshFont = new("Segoe UI", 8.5f);
新增：private readonly Font _tinyFont = new("Segoe UI", 7.5f);
```

### 2. OnPaint 分隔线颜色

当前代码中没有 sepColor 变量（v4 已删除分隔线）。需要新增一个淡色用于分隔线：

在 OnPaint 的统计行前（约第 135 行），新增：
```csharp
Color lineColor = isDark ? Color.FromArgb(20, 255, 255, 255) : Color.FromArgb(15, 0, 0, 0);
```

### 3. OnPaint 统计行后（第 146 行）

当前：
```csharp
y += 16;

// ═══ 刷新行 ═══
```

改为：
```csharp
y += 12;

// ═══ 分隔线 ═══
using (var linePen = new Pen(lineColor))
    g.DrawLine(linePen, x, y, x + cw, y);
y += 8;

// ═══ 刷新行 ═══
```

### 4. 刷新按钮颜色和字体（第 150-154 行）

当前：
```csharp
Color refreshColor = isDark ? Color.FromArgb(123, 140, 222) : Color.FromArgb(60, 80, 180);
using var refreshBrush = new SolidBrush(refreshColor);
string refreshText = "⟳ 刷新";
g.DrawString(refreshText, _refreshFont, refreshBrush, x, y);
var refreshSize = g.MeasureString(refreshText, _refreshFont);
```

改为：
```csharp
Color refreshColor = isDark ? Color.FromArgb(100, 110, 150) : Color.FromArgb(80, 90, 120);
using var refreshBrush = new SolidBrush(refreshColor);
string refreshText = "⟳ 刷新";
g.DrawString(refreshText, _labelFont, refreshBrush, x, y);
var refreshSize = g.MeasureString(refreshText, _labelFont);
```

注意：第 153 和 154 行都从 `_refreshFont` 改为 `_labelFont`。

### 5. 更新时间颜色、字号、格式（第 158-162 行）

当前：
```csharp
Color timeColor = isDark ? Color.FromArgb(80, 90, 115) : Color.FromArgb(140, 140, 160);
using var timeBrush = new SolidBrush(timeColor);
string timeText = _snapshot.IsOffline ? "离线" : $"{_snapshot.Timestamp:HH:mm:ss} 更新";
var timeSize = g.MeasureString(timeText, _detailFont);
g.DrawString(timeText, _detailFont, timeBrush, x + cw - timeSize.Width, y + 1);
```

改为：
```csharp
Color timeColor = isDark ? Color.FromArgb(70, 80, 100) : Color.FromArgb(150, 150, 165);
using var timeBrush = new SolidBrush(timeColor);
string timeText = _snapshot.IsOffline ? "离线" : $"{_snapshot.Timestamp:HH:mm} 更新";
var timeSize = g.MeasureString(timeText, _tinyFont);
g.DrawString(timeText, _tinyFont, timeBrush, x + cw - timeSize.Width, y + 1);
```

### 6. Dispose（第 356 行）

```
删除：_refreshFont.Dispose();
新增：_tinyFont.Dispose();
```

## 不修改

- 常量、DrawQuotaRow、贴边吸附逻辑
- TrayApplicationContext.cs
- 其他文件
