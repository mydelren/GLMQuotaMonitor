# 浮动 Widget V3 重构计划

## 设计目标

- 每个配额区只占一行：标签 | 进度条 | 百分比，三列居中
- 去掉详情行（"133 / 1.0K"）
- 进度条加高（10px）
- 统计行居中
- 卡片尺寸匹配内容

## 新布局

```
┌──────────────────────────────────┐
│                                  │
│  MCP 配额    [████████░░░░]  13% │  ← 三列同一行
│             ────────────────     │  ← 分隔线
│  5h Token    [████████████]  94% │  ← 三列同一行
│             ────────────────     │  ← 分隔线
│      1.2K 次调用 | 164M Token    │  ← 居中
│                                  │
└──────────────────────────────────┘
```

## 三列布局计算

内容区宽度：200px（240 - 20×2）

| 列 | 宽度 | X 起点 | 说明 |
|----|------|--------|------|
| 标签 | 60px | x | 左对齐 |
| 间距 | 5px | | |
| 进度条 | 80px | x+65 | 高度 10px |
| 间距 | 5px | | |
| 百分比 | 50px | x+150 | 右对齐 |
| 合计 | 200px | | ✅ |

## 垂直尺寸（精确计算）

OnPaint 流程：
```
y = 14 (CardVPadding)

DrawQuotaSection(第1行) → 返回 y+20 = 34
分隔线间距 y+=6 → y=40
分隔线画在 y=40
y+=6 → y=46

DrawQuotaSection(第2行) → 返回 y+20 = 66
分隔线间距 y+=6 → y=72
分隔线画在 y=72
y+=6 → y=78

统计行画在 y=78, 底部约 y=92
padding底部 14px → 窗口高度 106px
```

窗口尺寸：**240 × 106**

## 需要修改的代码

### 1. FloatingWidget.cs — 常量

| 常量 | 当前值 | 改为 |
|------|--------|------|
| `CardHeight` | 190 | 106 |
| `BarWidth` | 120 | 80 |
| `BarHeight` | 6 | 10 |
| `BarPctGap` | 10 | 删除（用 ColGap 替代） |
| 新增 `LabelColWidth` | — | 60 |
| 新增 `PctColWidth` | — | 50 |
| 新增 `ColGap` | — | 5 |

### 2. FloatingWidget.cs — DrawQuotaSection 完全重写

```
输入：item, config, isDark, x, y, cw
逻辑：
  计算三列 X 位置：
    labelX = x
    barX = x + LabelColWidth + ColGap
    pctX = x + LabelColWidth + ColGap + BarWidth + ColGap

  绘制标签（左列，垂直居中于行高）
  绘制进度条（中列，高度 10px，垂直居中于行高）
  绘制百分比（右列，垂直居中于行高）

  返回 y + 20（单行高度）
```

### 3. FloatingWidget.cs — OnPaint 间距调整

当前：
```csharp
y = DrawQuotaSection(...);
y += 10;  // 分隔线前间距
// 画分隔线
y += 10;  // 分隔线后间距
```

改为：
```csharp
y = DrawQuotaSection(...);
y += 6;   // 分隔线前间距
// 画分隔线
y += 6;   // 分隔线后间距
```

涉及行：120, 126, 130, 135（共4处 `y += 10` 改为 `y += 6`）

### 4. FloatingWidget.cs — 统计行居中

当前：左对齐绘制
改为：
```csharp
string statsLine = $"...";
var textSize = g.MeasureString(statsLine, _detailFont);
float statsX = (w - textSize.Width) / 2;
g.DrawString(statsLine, _detailFont, statBrush, statsX, y);
```

### 5. TrayApplicationContext.cs — tooltip 加重置时间

当前 tooltip 格式：
```
GLM: MCP 13% | 5h 94% | 1.2K
```

改为（单行，不换行，NotifyIcon.Text 不支持可靠换行）：
```
GLM: MCP 13% | 5h 94% | 1.2K次 | 重置13:29
```

只显示 5h Token 的重置时间（如果有）。

### 6. 删除常量 `BarPctGap`

新的列布局用 `ColGap` 统一管理间距，`BarPctGap` 不再使用，删除。

## 不修改的文件

- QuotaService.cs
- QuotaData.cs
- ConfigService.cs
- ThemeService.cs
- SettingsForm.cs
- Program.cs
- GraphicsExtensions.cs

## 不修改的设计

- 字体大小（label 9pt, value 11pt bold, detail 8pt）
- 配色方案
- 圆角 4px
- 边框样式
- 贴边吸附逻辑
