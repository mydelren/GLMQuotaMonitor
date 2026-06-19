# Widget V5：Catppuccin 配色 + 控件重构

## 目标

按 catppuccin-demo.html 效果实现：
1. 全面换用 Catppuccin Mocha 深色 / Latte 浅色
2. 进度条缩短到 65px，标签和进度条间距 10px
3. 底部行改为：重置时间（左）+ 更新时间（右）
4. Widget 右键菜单：刷新、浮动条开关、主题切换、设置、退出
5. 刷新按钮从底部行移到右键菜单

## 修改清单

### FloatingWidget.cs

#### 1. 常量

| 常量 | 当前 | 改为 | 说明 |
|------|------|------|------|
| BarWidth | 70 | 65 | 进度条缩短 |
| ColGap | 5 | 10 | 标签和进度条间距 |
| BarPctGap | (不存在) | 5 | 新增：进度条和百分比间距 |
| CardHeight | 130 | 134 | 配合新布局 |

三列：70 + 10 + 65 + 5 + 50 = 200 ✅

DrawQuotaRow 中：
```csharp
int barX = x + LabelColWidth + ColGap;       // 70+10=80
int pctX = barX + BarWidth + BarPctGap;      // 80+65+5=150
```

#### 2. CardHeight=134 计算

OnPaint 流程（需同步调整 y 增量）：
```
y=14   MCP 行(24) → y=38
y=46   y+=8 → 5h 行(24) → y=70
y=80   y+=10 → 分隔线 → y=80
y=80   y+=6 → 统计行 → y=86
y=86   y+=12 → 分隔线 → y=98
y=98   y+=8 → 底部行 → y=106
y=106  底部行内容(约14px高) → y=120
y=134  窗口底部(14px padding)
```

需要修改的 y 增量：
- 统计行前的 y+=10 改为 y+=6（5h行后）
- 统计行后的 y+=12 改为 y+=12（不变）
- 统计行和分隔线的顺序：先分隔线，后统计行（匹配 HTML demo）

#### 3. 颜色全面替换为 Catppuccin

**深色模式 (Mocha)：**

| 用途 | 当前 | Catppuccin |
|------|------|-----------|
| 背景 | (235,18,24,42) | Base (30,30,46) |
| 边框 | (40,255,255,255) | Surface 0 (49,50,68) |
| 标签文字 | (120,130,160) | Subtext 0 (166,173,200) |
| 次要文字 | (80,90,115) | Overlay 1 (127,132,156) |
| 分隔线 | (20,255,255,255) | Surface 1 (69,71,90) |
| MCP 蓝 | (70,130,230) | Blue (137,180,250) |
| 5h 青 | (0,210,205) | Teal (148,226,213) |
| 警告黄 | (255,220,100) | Yellow (249,226,175) |
| 临界红 | (255,118,117) | Red (243,139,168) |
| 进度条底色 | (40,255,255,255) | Surface 0 (49,50,68) |
| 统计文字 | (120,130,160) | Overlay 1 (127,132,156) |
| 底部文字 | (70,80,100) | Surface 2 (88,91,112) |

**浅色模式 (Latte)：**

| 用途 | Catppuccin Latte |
|------|-----------------|
| 背景 | (239,241,245) |
| 边框 | (204,208,218) |
| 标签文字 | (108,111,133) |
| 次要文字 | (140,143,161) |
| 分隔线 | (188,192,204) |
| MCP 蓝 | (30,102,245) |
| 5h 青 | (23,146,153) |
| 警告黄 | (223,142,29) |
| 临界红 | (210,15,57) |
| 进度条底色 | (204,208,218) |
| 统计文字 | (140,143,161) |
| 底部文字 | (156,160,176) |

#### 4. 底部行重构

删除刷新按钮绘制和命中检测，改为：

```
左侧：重置时间
  if (snapshot.Token5hQuota.ResetDateTime.HasValue)
    "重置 HH:mm"

右侧：更新时间
  "HH:mm 更新"
```

删除：
- `_refreshBtnRect` 字段
- `OnMouseDown` 中的刷新按钮命中检测（约第 234-239 行）
- OnPaint 中的 refreshColor、refreshBrush、refreshText、refreshSize 变量

保留：
- `_onRefresh` 字段（右键菜单需要）

#### 5. Widget 右键菜单

构造函数新增 4 个回调参数（保留 _onRefresh）：

```csharp
public FloatingWidget(
    ThemeService themeService,
    ConfigService configService,
    Action onRefresh,
    Action onToggleFloat,
    Action onToggleAutoStart,
    Action onCycleTheme,
    Action onShowSettings)
```

菜单项：
1. ⟳ 立即刷新 → onRefresh
2. ──分隔──
3. ☑ 显示浮动条 → onToggleFloat
4. ☐ 开机自启动 → onToggleAutoStart
5. ──分隔──
6. ☀ 切换主题 → onCycleTheme
7. ⚙ 设置 → onShowSettings
8. ──分隔──
9. ✕ 退出 → Application.Exit()

#### 6. 分隔线颜色

```csharp
Color lineColor = isDark ? Color.FromArgb(69, 71, 90) : Color.FromArgb(188, 192, 204);
```

#### 7. OnPaint 元素顺序调整（匹配 HTML demo）

当前：统计行 → y+=12 → 分隔线 → y+=8 → 底部行
改为：分隔线 → y+=6 → 统计行 → y+=12 → 分隔线 → y+=8 → 底部行

即：5h行后 → 分隔线 → 统计行 → 分隔线 → 底部行

### TrayApplicationContext.cs

#### 1. 创建 FloatingWidget 传入所有回调

```csharp
_floatingBar = new FloatingWidget(
    _themeService, _configService,
    RefreshQuota,
    ToggleFloatingBar,
    ToggleAutoStart,
    CycleTheme,
    ShowSettings);
```

#### 2. 新增方法

- `ToggleFloatingBar()` — 切换浮动条
- `ToggleAutoStart()` — 切换自启
- `CycleTheme()` — 循环：Auto→Dark→Light→Auto
- `ShowSettings()` — 已有，不变

#### 3. 状态同步

Widget 右键菜单的 checkbox 状态和托盘菜单的 checkbox 状态需要同步。
方案：在 ToggleFloatingBar/ToggleAutoStart 中同时更新两处。

## 不修改

- DrawQuotaRow 布局逻辑（只改常量和颜色）
- 贴边吸附逻辑
- QuotaService.cs、QuotaData.cs、ConfigService.cs、ThemeService.cs、SettingsForm.cs、Program.cs、GraphicsExtensions.cs
