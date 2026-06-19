# 实现计划：主题修复 + 菜单调整

## 1. 新建 ToolStripDarkRenderer.cs

```csharp
namespace GLMQuotaMonitor;

/// <summary>
/// 深色模式菜单渲染器（Catppuccin Mocha 配色）
/// </summary>
public class ToolStripDarkRenderer : ToolStripProfessionalRenderer
{
    public ToolStripDarkRenderer() : base(new DarkColorTable()) { }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled
            ? Color.FromArgb(205, 214, 244)   // Text
            : Color.FromArgb(108, 112, 134);  // Overlay 1 (disabled)
        base.OnRenderItemText(e);
    }
}

internal class DarkColorTable : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Color.FromArgb(30, 30, 46);       // Base
    public override Color MenuBorder => Color.FromArgb(49, 50, 68);                        // Surface 0
    public override Color MenuItemBorder => Color.FromArgb(49, 50, 68);                    // Surface 0
    public override Color MenuItemSelected => Color.FromArgb(49, 50, 68);                  // Surface 0
    public override Color MenuItemSelectedGradientBegin => Color.FromArgb(49, 50, 68);
    public override Color MenuItemSelectedGradientEnd => Color.FromArgb(49, 50, 68);
    public override Color MenuItemPressedGradientBegin => Color.FromArgb(49, 50, 68);
    public override Color MenuItemPressedGradientEnd => Color.FromArgb(49, 50, 68);
    public override Color MenuStripGradientBegin => Color.FromArgb(30, 30, 46);            // Base
    public override Color MenuStripGradientEnd => Color.FromArgb(30, 30, 46);
    public override Color ImageMarginGradientBegin => Color.FromArgb(30, 30, 46);
    public override Color ImageMarginGradientMiddle => Color.FromArgb(30, 30, 46);
    public override Color ImageMarginGradientEnd => Color.FromArgb(30, 30, 46);
    public override Color SeparatorDark => Color.FromArgb(69, 71, 90);                     // Surface 1
    public override Color SeparatorLight => Color.FromArgb(69, 71, 90);
    public override Color CheckBackground => Color.FromArgb(49, 50, 68);
    public override Color CheckSelectedBackground => Color.FromArgb(49, 50, 68);
    public override Color CheckPressedBackground => Color.FromArgb(49, 50, 68);
    public override Color ButtonSelectedHighlight => Color.FromArgb(49, 50, 68);
}
```

## 2. FloatingWidget.cs 修改

### 2a. 菜单渲染器

构造函数中创建菜单后，根据当前主题设置渲染器：
```csharp
if (_themeService.IsDark)
    menu.Renderer = new ToolStripDarkRenderer();
```

在 `_themeChangedHandler` 中更新菜单渲染器：
```csharp
_themeChangedHandler = (isDark) =>
{
    void Update()
    {
        // 更新菜单渲染器
        if (ContextMenuStrip != null)
            ContextMenuStrip.Renderer = isDark
                ? new ToolStripDarkRenderer()
                : new ToolStripProfessionalRenderer();
        // ... BackColor + Invalidate
    }
    ...
};
```

### 2b. "显示浮动条" → "定位浮动条"

构造函数菜单代码修改：
```csharp
// 旧：
var floatToggle = new ToolStripMenuItem("显示浮动条") { CheckOnClick = true, Checked = true };
floatToggle.Click += (_, _) => _onToggleFloat();
menu.Items.Add(floatToggle);

// 新：
var locateItem = new ToolStripMenuItem("📍 定位浮动条");
locateItem.Click += (_, _) => _onLocate();
menu.Items.Add(locateItem);
```

构造函数参数修改：
```csharp
// 旧：Action onToggleFloat
// 新：Action onLocate
```

字段修改：
```csharp
// 旧：private readonly Action _onToggleFloat;
// 新：private readonly Action _onLocate;
```

### 2c. 新增 Locate() 方法

```csharp
/// <summary>
/// 定位浮动条：从贴边位置展开 3 秒后收回
/// </summary>
public void Locate()
{
    if (!_isSnapped || _isExpanded) return;

    Expand();

    var locateTimer = new System.Windows.Forms.Timer { Interval = 3000 };
    locateTimer.Tick += (_, _) =>
    {
        locateTimer.Stop();
        locateTimer.Dispose();
        CollapseIfSnapped();
    };
    locateTimer.Start();
}
```

### 2d. 删除不再需要的 _floatingBarToggle 字段

FloatingWidget 中没有这个字段（已在之前的改动中删除）。

## 3. TrayApplicationContext.cs 修改

### 3a. 菜单渲染器

`CreateContextMenu` 中根据主题设置渲染器：
```csharp
if (_themeService.IsDark)
    menu.Renderer = new ToolStripDarkRenderer();
```

新增主题变更订阅（当前 TrayApplicationContext 没有订阅 ThemeChanged）：
```csharp
// 在构造函数中
_themeService.ThemeChanged += OnThemeChanged;

// 新增方法
private void OnThemeChanged(bool isDark)
{
    _contextMenu.Renderer = isDark
        ? new ToolStripDarkRenderer()
        : new ToolStripProfessionalRenderer();
}
```

### 3b. "显示浮动条" → "定位浮动条"

```csharp
// 旧：
_floatingBarToggle = new ToolStripMenuItem("显示浮动条")
{
    CheckOnClick = true,
    Checked = _configService.Config.ShowFloatingBar
};
_floatingBarToggle.Click += OnToggleFloatingWidget;
menu.Items.Add(_floatingBarToggle);

// 新：
var locateItem = new ToolStripMenuItem("📍 定位浮动条");
locateItem.Click += (_, _) => LocateFloatingBar();
menu.Items.Add(locateItem);
```

删除 `_floatingBarToggle` 字段和 `OnToggleFloatingWidget` 方法。

### 3c. 新增 LocateFloatingBar 方法

```csharp
private void LocateFloatingBar()
{
    _floatingBar?.Locate();
}
```

### 3d. 删除 ToggleFloatingBar 方法

旧的 `ToggleFloatingBar()` 方法（切换显示/隐藏）不再需要。

### 3e. 构造函数参数修改

FloatingWidget 创建时传 `LocateFloatingBar` 而不是 `ToggleFloatingBar`：
```csharp
_floatingBar = new FloatingWidget(
    _themeService, _configService,
    RefreshQuota,
    LocateFloatingBar,  // 替换 ToggleFloatingBar
    ToggleAutoStart,
    CycleTheme,
    ShowSettings);
```

### 3f. Dispose 中取消订阅

```csharp
_themeService.ThemeChanged -= OnThemeChanged;
```

## 4. SettingsForm.cs 修改

### 4a. ApplyTheme 补全

```csharp
private void ApplyTheme(bool isDark)
{
    Color bg = isDark ? Color.FromArgb(30, 30, 46) : Color.FromArgb(245, 245, 250);
    Color fg = isDark ? Color.FromArgb(224, 224, 224) : Color.FromArgb(30, 30, 30);

    BackColor = bg;
    ForeColor = fg;

    foreach (Control ctrl in Controls)
    {
        switch (ctrl)
        {
            case TextBox tb:
                tb.BackColor = isDark ? Color.FromArgb(40, 40, 60) : Color.White;
                tb.ForeColor = fg;
                break;
            case NumericUpDown nud:
                nud.BackColor = isDark ? Color.FromArgb(40, 40, 60) : Color.White;
                nud.ForeColor = fg;
                break;
            case ComboBox cmb:
                cmb.BackColor = isDark ? Color.FromArgb(40, 40, 60) : Color.White;
                cmb.ForeColor = fg;
                break;
            case Button btn:
                btn.FlatStyle = FlatStyle.Flat;
                btn.BackColor = isDark ? Color.FromArgb(49, 50, 68) : Color.FromArgb(230, 230, 235);
                btn.ForeColor = fg;
                break;
            case CheckBox chk:
                chk.ForeColor = fg;
                break;
            case Panel p when p.Height == 1:
                // 分隔线
                p.BackColor = isDark ? Color.FromArgb(69, 71, 90) : Color.FromArgb(200, 200, 210);
                break;
        }
    }
}
```

### 4b. 分隔线颜色

两处分隔线 Panel 的初始 BackColor 保持不变（在 ApplyTheme 中统一处理）。

## 5. AppConfig.cs 修改

```csharp
// 旧：
public int PollingIntervalMinutes { get; set; } = 3;

// 新：
public int PollingIntervalMinutes { get; set; } = 5;
```

## 不修改

- QuotaService.cs、QuotaData.cs、ConfigService.cs、ThemeService.cs、Program.cs、GraphicsExtensions.cs、TrayIconFactory.cs
