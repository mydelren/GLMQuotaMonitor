# 实现计划：Widget V3

## FloatingWidget.cs 修改清单

### 1. 常量区（第 12-20 行）

删除：
```csharp
private const int BarPctGap = 10;
```

修改：
```csharp
private const int CardHeight = 190;    → 106
private const int BarWidth = 120;      → 80
private const int BarHeight = 6;       → 10
```

新增：
```csharp
private const int LabelColWidth = 60;
private const int PctColWidth = 50;
private const int ColGap = 5;
```

### 2. OnPaint 间距（第 120, 126, 130, 135 行）

4 处 `y += 10;` 全部改为 `y += 6;`

### 3. OnPaint 统计行（第 138-144 行）

当前：
```csharp
g.DrawString(line, _detailFont, statBrush, x, y);
```

改为：
```csharp
var textSize = g.MeasureString(line, _detailFont);
float statsX = (w - textSize.Width) / 2;
g.DrawString(line, _detailFont, statBrush, statsX, y);
```

### 4. DrawQuotaSection 完全重写（第 152-204 行）

新逻辑：
```
labelX = x
barX = x + LabelColWidth + ColGap = x + 65
pctX = barX + BarWidth + ColGap = x + 150

行高 = 20px
barY = y + (20 - BarHeight) / 2 = y + 5  (垂直居中)
pctY = y + (20 - 字高) / 2               (垂直居中)

绘制标签（_labelFont, labelX, 居中Y）
绘制进度条背景（barX, barY, BarWidth, BarHeight）
绘制进度条填充（同上，按百分比裁剪）
绘制百分比（_valueFont, pctX, 居中Y，右对齐在 50px 列内）

return y + 20
```

百分比右对齐实现：
```csharp
string pctText = $"{pct:F0}%";
var pctSize = g.MeasureString(pctText, _valueFont);
float pctDrawX = pctX + PctColWidth - pctSize.Width;  // 右对齐
g.DrawString(pctText, _valueFont, pctBrush, pctDrawX, pctY);
```

### 5. TrayApplicationContext.cs tooltip（约第 141 行）

当前：
```csharp
string tooltip = $"GLM: MCP {mcpPct} | 5h {tokenPct} | {calls}";
```

改为：
```csharp
string resetInfo = "";
if (snapshot.Token5hQuota.ResetDateTime.HasValue)
    resetInfo = $" | 重置{snapshot.Token5hQuota.ResetDateTime.Value:HH:mm}";
string tooltip = $"GLM: MCP {mcpPct} | 5h {tokenPct} | {calls}{resetInfo}";
if (tooltip.Length > 127) tooltip = tooltip[..127];
_notifyIcon.Text = tooltip;
```

注意：用 `snapshot`（方法参数），不是 `_snapshot`。最后必须赋值给 `_notifyIcon.Text`。

## 不修改

- 字体（_labelFont 9pt, _valueFont 11pt bold, _detailFont 8pt）
- 配色
- 其他文件
