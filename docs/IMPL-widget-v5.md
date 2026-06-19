# 实现计划：Widget V5

## FloatingWidget.cs 修改

### 1. 常量区（第 14-25 行）

```
BarWidth: 70 → 65
ColGap: 5 → 10
CardHeight: 130 → 134
```

新增常量：
```csharp
private const int BarPctGap = 5;
```

### 2. 删除字段

```csharp
// 删除：
private Rectangle _refreshBtnRect;
```

保留 `_onRefresh`（右键菜单需要）。

### 3. 构构函数（第 51 行）

当前：
```csharp
public FloatingWidget(ThemeService themeService, ConfigService configService, Action onRefresh)
```

改为：
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

新增字段：
```csharp
private readonly Action _onToggleFloat;
private readonly Action _onToggleAutoStart;
private readonly Action _onCycleTheme;
private readonly Action _onShowSettings;
```

构造函数内赋值，创建 ContextMenuStrip。

### 4. 创建右键菜单（构造函数内）

```csharp
var menu = new ContextMenuStrip();

var refreshItem = new ToolStripMenuItem("⟳ 立即刷新");
refreshItem.Click += (_, _) => _onRefresh();
menu.Items.Add(refreshItem);

menu.Items.Add(new ToolStripSeparator());

var floatToggle = new ToolStripMenuItem("显示浮动条") { CheckOnClick = true, Checked = true };
floatToggle.Click += (_, _) => _onToggleFloat();
menu.Items.Add(floatToggle);

var autoStartToggle = new ToolStripMenuItem("开机自启动") { CheckOnClick = true };
autoStartToggle.Click += (_, _) => _onToggleAutoStart();
menu.Items.Add(autoStartToggle);

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
```

### 5. DrawQuotaRow（第 170-210 行）

a) 常量引用更新：
```csharp
// 当前：
int barX = x + LabelColWidth + ColGap;    // ColGap=5
int pctX = barX + BarWidth + ColGap;      // ColGap=5

// 改为：
int barX = x + LabelColWidth + ColGap;    // ColGap=10
int pctX = barX + BarWidth + BarPctGap;   // BarPctGap=5
```

b) 颜色替换为 Catppuccin：

正常色（按 item.Type 分支）：
```csharp
Color normalColor;
if (item.Type == "TIME_LIMIT")
    normalColor = isDark ? Color.FromArgb(137, 180, 250) : Color.FromArgb(30, 102, 245);  // Blue
else
    normalColor = isDark ? Color.FromArgb(148, 226, 213) : Color.FromArgb(23, 146, 153);   // Teal
```

警告色：
```csharp
pctColor = isDark ? Color.FromArgb(249, 226, 175) : Color.FromArgb(223, 142, 29);  // Yellow
```

临界色：
```csharp
pctColor = isDark ? Color.FromArgb(243, 139, 168) : Color.FromArgb(210, 15, 57);   // Red
```

进度条底色：
```csharp
Color barBg = isDark ? Color.FromArgb(49, 50, 68) : Color.FromArgb(204, 208, 218);  // Surface 0
```

标签色：
```csharp
Color labelColor = isDark ? Color.FromArgb(166, 173, 200) : Color.FromArgb(108, 111, 133);  // Subtext 0
```

### 6. OnPaint 全面重写

颜色变量全部替换为 Catppuccin：

```csharp
// 背景
Color bg = isDark ? Color.FromArgb(30, 30, 46) : Color.FromArgb(239, 241, 245);       // Base
// 边框
Color border = isDark ? Color.FromArgb(49, 50, 68) : Color.FromArgb(204, 208, 218);    // Surface 0
// 分隔线
Color lineColor = isDark ? Color.FromArgb(69, 71, 90) : Color.FromArgb(188, 192, 204); // Surface 1
// 统计文字
Color statColor = isDark ? Color.FromArgb(127, 132, 156) : Color.FromArgb(140, 143, 161); // Overlay 1
// 底部文字
Color footerColor = isDark ? Color.FromArgb(88, 91, 112) : Color.FromArgb(156, 160, 176); // Mocha Surface 2 / Latte Overlay 0
```

元素顺序（匹配 HTML demo）：
```
y=14   MCP 行
y=38   y+=8
y=46   5h 行
y=70   y+=10
y=80   分隔线
y=86   y+=6
y=86   统计行（居中）
y=98   y+=12
y=98   分隔线
y=106  y+=8
y=106  底部行（左：重置时间，右：更新时间）
```

### 7. 删除 OnMouseDown 中的刷新按钮检测

删除：
```csharp
if (_refreshBtnRect.Contains(e.Location))
{
    _onRefresh?.Invoke();
    return;
}
```

### 8. Dispose

不变（_onRefresh 等 Action 字段不需要 dispose）。

## TrayApplicationContext.cs 修改

### 1. 新增字段和方法

```csharp
private void ToggleFloatingBar()
{
    var config = _configService.Config;
    config.ShowFloatingBar = !config.ShowFloatingBar;
    _configService.Save(config);
}

private void ToggleAutoStart()
{
    var config = _configService.Config;
    config.AutoStart = !config.AutoStart;
    _configService.Save(config);
    SetAutoStart(config.AutoStart);
}

private void CycleTheme()
{
    var config = _configService.Config;
    config.Theme = config.Theme switch
    {
        ThemeMode.Auto => ThemeMode.Dark,
        ThemeMode.Dark => ThemeMode.Light,
        ThemeMode.Light => ThemeMode.Auto,
        _ => ThemeMode.Auto
    };
    _configService.Save(config);
}
```

### 2. ShowFloatingBar 方法中创建 FloatingWidget

```csharp
_floatingBar = new FloatingWidget(
    _themeService, _configService,
    RefreshQuota,
    ToggleFloatingBar,
    ToggleAutoStart,
    CycleTheme,
    ShowSettings);
```

### 3. 菜单状态同步

ToggleFloatingBar 和 ToggleAutoStart 中同时更新：
- 托盘菜单的 _floatingBarToggle.Checked / _autoStartToggle.Checked
- Widget 右键菜单的 checkbox（通过 FloatingWidget 公开方法或事件）

简化方案：Widget 右键菜单的 checkbox 不做同步（用户从 Widget 切换后，托盘菜单状态下次打开时自然更新）。
