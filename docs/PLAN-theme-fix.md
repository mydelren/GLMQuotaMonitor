# 主题全面修复 + 菜单功能调整

## 修复清单

### 1. ContextMenuStrip 深色主题

**问题**：Widget 和托盘的右键菜单在深色模式下显示浅色背景。

**方案**：新建 `ToolStripDarkRenderer` 类，继承 `ToolStripProfessionalRenderer`。

深色菜单颜色（Catppuccin Mocha）：
- 背景：Base #1e1e2e (30,30,46)
- 选中项背景：Surface 0 #313244 (49,50,68)
- 文字：Text #cdd6f4 (205,214,244)
- 次要文字：Subtext 0 #a6adc8 (166,173,200)
- 分隔线：Surface 1 #45475a (69,71,90)
- 边框：Surface 0 #313244 (49,50,68)

浅色模式使用默认渲染器。

**修改文件**：
- 新建 `ToolStripDarkRenderer.cs`
- `FloatingWidget.cs`：构造函数中根据主题设置 `menu.Renderer`
- `TrayApplicationContext.cs`：`CreateContextMenu` 中根据主题设置 `menu.Renderer`
- 主题切换时更新两处菜单的 Renderer

### 2. SettingsForm 主题化补全

**问题**：只主题化了 TextBox 和 NumericUpDown，ComboBox/CheckBox/Button/Label 未处理。

**方案**：在 `ApplyTheme` 方法中补全：
- ComboBox：BackColor + ForeColor
- CheckBox：ForeColor（背景透明）
- Button：FlatStyle + ForeColor + BackColor
- Label：ForeColor（继承自 Form，已处理）
- 分隔线 Panel：改用 Catppuccin Surface 1

**修改文件**：`SettingsForm.cs`

### 3. "显示浮动条" → "定位浮动条"

**问题**：功能过时，现在只有浮动条，没有隐藏的概念。

**方案**：
- 菜单项文本改为 "📍 定位浮动条"
- 去掉 CheckOnClick，改为普通菜单项
- 点击行为：如果 widget 贴边收起 → 展开 3 秒后收回
- 新增 `LocateFloatingBar()` 方法

**修改文件**：
- `FloatingWidget.cs`：菜单项改名，回调改为 `_onLocate`
- `TrayApplicationContext.cs`：`ToggleFloatingBar` → `LocateFloatingBar`
- 构造函数参数 `onToggleFloat` → `onLocate`

### 4. 轮询默认值

**问题**：默认 3 分钟，应为 5。

**方案**：`AppConfig.cs` 中 `PollingIntervalMinutes` 默认值 3 → 5。

**修改文件**：`Models/AppConfig.cs`

### 5. 托盘图标颜色

**结论**：不改。绿/黄/红/灰作为状态指示色在深色和浅色模式下都可辨识。

## 修改文件清单

| 文件 | 改动 |
|------|------|
| 新建 `ToolStripDarkRenderer.cs` | 深色菜单渲染器 |
| `FloatingWidget.cs` | 菜单渲染器、"定位浮动条"、_onLocate 回调 |
| `TrayApplicationContext.cs` | 菜单渲染器、LocateFloatingBar 方法、构造函数参数 |
| `SettingsForm.cs` | 补全主题化、分隔线颜色 |
| `Models/AppConfig.cs` | 轮询默认值 3→5 |

## 不修改

- QuotaService.cs、QuotaData.cs、ConfigService.cs、ThemeService.cs、Program.cs、GraphicsExtensions.cs、TrayIconFactory.cs
