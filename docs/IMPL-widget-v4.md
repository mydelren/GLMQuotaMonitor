# 实现计划：Widget V4

## FloatingWidget.cs 修改清单

### 1. 常量区

```
LabelColWidth: 60 → 70
BarWidth: 80 → 70
BarHeight: 10 → 14
CardHeight: 106 → 130
```

新增常量：
```csharp
private const int RowHeight = 24;
```

删除 `RefreshRowY` 常量（OnPaint 中通过 y 增量动态计算位置，不需要常量）。

### 2. 构造函数加 onRefresh 参数

当前：
```csharp
public FloatingWidget(ThemeService themeService, ConfigService configService)
```

改为：
```csharp
public FloatingWidget(ThemeService themeService, ConfigService configService, Action onRefresh)
```

新增字段：
```csharp
private readonly Action _onRefresh;
```

构造函数内赋值：`_onRefresh = onRefresh;`

### 3. OnPaint 重写

删除当前 OnPaint 全部内容，重写为：

```
y = 14 (CardVPadding)

// MCP 行
DrawQuotaRow(..., "mcp") → y += RowHeight → y = 38
y += 6 → y = 44

// 5h Token 行
DrawQuotaRow(..., "token") → y += RowHeight → y = 68
y += 6 → y = 74

// 统计行（居中，使用 " | " 分隔）
DrawString("1.2K 次调用  |  164M Token") → y += 14 → y = 88
y += 8 → y = 96

// 刷新行（左侧按钮，右侧时间）
DrawString("⟳ 刷新", _labelFont, 刷新色, 位置) at (x, y)        // 9pt, 可点击
DrawString("14:32:05 更新", _detailFont, 次要色, 位置) at 右侧  // 8pt
```

### 4. DrawQuotaRow 改动

a) rowHeight 局部变量：20 → 24（使用 RowHeight 常量）

b) 颜色区分：
```csharp
// 当前：所有配额用同一颜色
// 改为：根据 item.Type 选择不同颜色
Color normalColor;
if (item.Type == "TIME_LIMIT")
    normalColor = isDark ? Color.FromArgb(70, 130, 230) : Color.FromArgb(50, 100, 200);  // 蓝色
else
    normalColor = isDark ? Color.FromArgb(0, 210, 205) : Color.FromArgb(0, 160, 140);    // 青色

// 警告和临界色保持统一
Color pctColor;
if (pct >= config.CriticalThreshold) pctColor = Color.FromArgb(255, 118, 117);
else if (pct >= config.WarningThreshold) pctColor = Color.FromArgb(255, 220, 100);
else pctColor = normalColor;
```

### 5. 边框内缩

OnPaint 中的两处 MakeRoundRect 调用：

当前：
```csharp
MakeRoundRect(0, 0, w, h, 4)       // 背景
MakeRoundRect(0, 0, w-1, h-1, 4)   // 边框
```

改为：
```csharp
MakeRoundRect(2, 2, w-4, h-4, 4)   // 背景
MakeRoundRect(2, 2, w-5, h-5, 4)   // 边框
```

### 6. 刷新按钮点击检测

OnMouseDown 中增加命中检测：

```csharp
private Rectangle _refreshBtnRect;  // 刷新按钮区域，在 OnPaint 中计算

private void OnMouseDown(object? sender, MouseEventArgs e)
{
    if (e.Button == MouseButtons.Left)
    {
        if (_refreshBtnRect.Contains(e.Location))
        {
            _onRefresh?.Invoke();
            return;
        }
        // 原有拖动逻辑...
    }
}
```

在 OnPaint 的刷新行绘制时，记录按钮区域：
```csharp
_refreshBtnRect = new Rectangle(x, y, 刷新文字宽度, 20);
```

### 7. 进度条底色加深

当前：
```csharp
Color barBg = isDark ? Color.FromArgb(25, 255, 255, 255) : Color.FromArgb(15, 0, 0, 0);
```

改为：
```csharp
Color barBg = isDark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(25, 0, 0, 0);
```

### 8. 删除分隔线

删除 OnPaint 中的两段分隔线代码：
```csharp
// 删除这两段：
using (var sepPen = new Pen(sepColor))
    g.DrawLine(sepPen, x, y, x + cw, y);
```

## TrayApplicationContext.cs 修改清单

### 1. 创建 FloatingWidget 时传入回调

当前（ShowFloatingWidget 方法内）：
```csharp
_floatingBar = new FloatingWidget(_themeService, _configService);
```

改为：
```csharp
_floatingBar = new FloatingWidget(_themeService, _configService, RefreshQuota);
```

### 2. Dispose 中的 _floatingBar 不变

已有 `_floatingBar?.Dispose()`，无需改动。
