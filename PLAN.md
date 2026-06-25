# GLMQuotaMonitor 四项修复方案（v3）

## 实施顺序
1. `Models/AppConfig.cs` — 枚举定义
2. `ConfigService.cs` — 旧配置迁移
3. `QuotaService.cs` — 退避重试
4. `ThemeService.cs` — 去掉系统跟随
5. `TrayApplicationContext.cs` — 主题二选一 + 通知去重
6. `SettingsForm.cs` — UI 调整 + 卸载按钮
7. `Program.cs` — --uninstall 入口

---

## 问题 1：自动刷新不生效

### 根因
`QuotaService.cs:62-63`，连续失败 3 次后直接 `break` 退出轮询循环，用户无感知。

### 修改方案

**QuotaService.cs**

1. 新增 `volatile bool _enteredBackoff` 标志，首次进入退避时触发一次提示
2. 轮询循环失败 3 次后不退出，改为降速重试
3. 成功时重置 `_enteredBackoff = false`
4. 修改 `FetchQuota` 中第 183-184 行的错误提示逻辑：仅在 `_enteredBackoff` 为 false 时触发一次

```csharp
// 新增字段
private volatile bool _enteredBackoff;

// 轮询循环修改
while (!cts.Token.IsCancellationRequested)
{
    await FetchQuota(tokenGetter(), baseUrlGetter(), cts.Token);

    if (_consecutiveFailures >= MaxRetryCount)
    {
        if (!_enteredBackoff)
        {
            _enteredBackoff = true;
            Error?.Invoke("连续失败，已进入退避重试模式");
        }
        int shift = Math.Min(_consecutiveFailures - MaxRetryCount, 20);
        int retryDelay = (int)Math.Min((long)intervalMs * (1L << shift), 30L * 60 * 1000);
        await Task.Delay(retryDelay, cts.Token);
    }
    else
    {
        _enteredBackoff = false;
        await Task.Delay(intervalMs, cts.Token);
    }
}
```

```csharp
// FetchQuota 中修改（第 183-184 行）
// 旧：if (_consecutiveFailures >= MaxRetryCount)
//        Error?.Invoke("连续失败次数过多，已停止自动轮询，请手动刷新");
// 新：删除这两行（退避提示已移至轮询循环中）
```

成功时重置（`FetchQuota` 中 `_consecutiveFailures = 0` 后）：
```csharp
_consecutiveFailures = 0;
_enteredBackoff = false;
```

---

## 问题 2：主题切换去掉"自动"模式

### 根因
`ThemeMode` 枚举有 `Auto/Dark/Light` 三个值，`CycleTheme()` 三循环。

### 修改方案

**Models/AppConfig.cs**
- `ThemeMode` 保留原数值：`Dark = 1, Light = 2`（保持已有配置兼容）
- 去掉 `Auto` 枚举值

```csharp
public enum ThemeMode
{
    Dark = 1,
    Light = 2
}
```

**ConfigService.cs**
- `Load()` 中加迁移逻辑：读取后如果 `config.Theme` 为 0（旧 Auto），映射为 `ThemeMode.Light`
- 直接写文件，不经过 `Save()`（避免触发 ConfigChanged 事件）

```csharp
// Load() 中，反序列化成功后：
if ((int)config.Theme == 0)
{
    config.Theme = ThemeMode.Light;
    // 静默写回，不触发事件
    try
    {
        Directory.CreateDirectory(ConfigDir);
        string migratedJson = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ConfigPath, migratedJson);
    }
    catch { }
}
```

**ThemeService.cs**
- 去掉 `SystemEvents.UserPreferenceChanged` 订阅
- 去掉 `_mode` 字段的 `Auto` 分支
- 构造函数改为 `ThemeService(ThemeMode initialMode)`
- `IsDark` 直接返回 `_mode == ThemeMode.Dark`
- `SetMode` 保留

```csharp
public class ThemeService : IDisposable
{
    private ThemeMode _mode;

    public ThemeService(ThemeMode initialMode)
    {
        _mode = initialMode;
    }

    public bool IsDark => _mode == ThemeMode.Dark;

    public event Action<bool>? ThemeChanged;

    public void SetMode(ThemeMode mode)
    {
        _mode = mode;
        ThemeChanged?.Invoke(IsDark);
    }

    public void Dispose() { }
}
```

**TrayApplicationContext.cs**
- 构造函数调整：先构造 `_configService`，再用 `config.Theme` 构造 `_themeService`
- 删除第 46 行 `_themeService.SetMode(_configService.Config.Theme);`（构造时已设定）
- `CycleTheme()` 改为 `Dark ↔ Light` 二选一

```csharp
// 构造函数中：
_configService = new ConfigService();
_themeService = new ThemeService(_configService.Config.Theme);  // 改为有参构造
// 删除：_themeService.SetMode(_configService.Config.Theme);

// CycleTheme 修改：
private void CycleTheme()
{
    var config = _configService.Config;
    config.Theme = _themeService.IsDark ? ThemeMode.Light : ThemeMode.Dark;
    _configService.Save(config);
}
```

**SettingsForm.cs**
- `_cmbTheme` 下拉框去掉"跟随系统"，只保留"深色"/"浅色"
- 索引映射：`Dark(1) ↔ SelectedIndex 0`，`Light(2) ↔ SelectedIndex 1`

```csharp
// InitializeUI 中：
_cmbTheme.Items.AddRange(new object[] { "深色", "浅色" });

// LoadConfigToUI 中：
_cmbTheme.SelectedIndex = (int)config.Theme - 1;  // Dark(1)->0, Light(2)->1

// OnSave 中：
Theme = (ThemeMode)(_cmbTheme.SelectedIndex + 1),  // 0->Dark(1), 1->Light(2)
```

---

## 问题 3：卸载流程——直接删 exe 后的残留

### 残留分析
1. 注册表 `HKCU\...\Run\GLMQuotaMonitor`
2. `%AppData%\GLMQuotaMonitor\` 目录

### 修改方案

**Program.cs**
- `Main` 签名改为 `static void Main(string[] args)`
- `--uninstall` 参数解析放在 **Mutex 检查之前**
- 清理流程：删注册表 → 删配置目录 → 延迟自删 exe → 退出
- 注意：命令行 `--uninstall` 不会停止已运行的实例（文档说明限制）

```csharp
static void Main(string[] args)
{
    if (args.Contains("--uninstall"))
    {
        CleanupAndExit();
        return;
    }
    // ... 原有 Mutex + Application.Run 逻辑
}

static void CleanupAndExit()
{
    // 1. 删注册表
    const string regPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    try
    {
        using var key = Registry.CurrentUser.OpenSubKey(regPath, true);
        key?.DeleteValue("GLMQuotaMonitor", false);
    }
    catch { }

    // 2. 删配置目录
    string configDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GLMQuotaMonitor");
    try { Directory.Delete(configDir, true); } catch { }

    // 3. 延迟自删 exe
    string exePath = Environment.ProcessPath ?? "";
    if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd",
            Arguments = $"/c ping 127.0.0.1 -n 3 > nul & del \"{exePath}\"",
            CreateNoWindow = true,
            UseShellExecute = false
        });
    }
}
```

**SettingsForm.cs**
- 设置界面底部加"卸载"按钮（与"保存/取消"用分隔线隔离）
- `ClientSize` 高度从 430 增加到 480
- 卸载按钮用红色文字警示

```csharp
// 分隔线
Controls.Add(new Panel { ... Location = new Point(16, y), Size = new Size(388, 1) });
y += 16;

// 卸载按钮
var btnUninstall = new Button
{
    Text = "卸载（删除所有数据）",
    Location = new Point(16, y),
    Size = new Size(180, 30),
    FlatStyle = FlatStyle.Flat,
    ForeColor = Color.FromArgb(210, 15, 57)
};
btnUninstall.Click += OnUninstall;
Controls.Add(btnUninstall);
```

```csharp
private void OnUninstall(object? sender, EventArgs e)
{
    var result = MessageBox.Show(
        "确定卸载？将删除注册表键值、配置目录，并自删 exe。", "确认卸载",
        MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
    if (result != DialogResult.Yes) return;

    // 先清理开机自启
    SetAutoStart(false);
    Close();
    Program.CleanupAndExit();
}
```

---

## 问题 4：Critical 通知频繁弹出

### 根因
`TrayApplicationContext.cs:152-158`，每次刷新只要处于 Critical 就弹通知。

### 修改方案

**TrayApplicationContext.cs**
- 新增字段 `_lastNotifiedResetTime` 初始值 `long.MinValue`
- 新增字段 `_lastNotifiedTime` 初始值 `DateTime.MinValue`
- 双策略去重：有 resetTime 按周期去重，无 resetTime 按 1 小时窗口去重

```csharp
private long _lastNotifiedResetTime = long.MinValue;
private DateTime _lastNotifiedTime = DateTime.MinValue;

// OnQuotaUpdated 中替换原有通知逻辑：
if (status == QuotaStatus.Critical && !snapshot.IsOffline)
{
    long resetTime = snapshot.Token5hQuota.NextResetTime;
    bool shouldNotify;

    if (resetTime > 0)
        shouldNotify = resetTime != _lastNotifiedResetTime;
    else
        shouldNotify = (DateTime.Now - _lastNotifiedTime).TotalHours >= 1;

    if (shouldNotify)
    {
        _lastNotifiedResetTime = resetTime;
        _lastNotifiedTime = DateTime.Now;
        _notifyIcon.ShowBalloonTip(5000, "GLM 配额预警",
            $"MCP 配额: {mcpPct}\n5h Token: {tokenPct}",
            ToolTipIcon.Warning);
    }
}
```

---

## 文件变更汇总

| 文件 | 变更类型 | 说明 |
|------|----------|------|
| `Models/AppConfig.cs` | 修改 | ThemeMode 去掉 Auto，保留 Dark=1, Light=2 |
| `ConfigService.cs` | 修改 | Load() 加旧配置迁移（Auto→Light），静默写回 |
| `QuotaService.cs` | 修改 | 退避重试 + _enteredBackoff 标志 |
| `ThemeService.cs` | 修改 | 去掉系统跟随，构造函数接受 ThemeMode |
| `TrayApplicationContext.cs` | 修改 | 主题二选一 + 通知去重 + 删冗余 SetMode 调用 |
| `SettingsForm.cs` | 修改 | 下拉框去掉"跟随系统" + 索引映射修正 + 卸载按钮 |
| `Program.cs` | 修改 | Main 加 args 参数 + --uninstall 入口 |

---

## Hermes 审查意见（2026-06-25）

### 总体评价：方案合理，可以直接实施

四个问题的根因分析准确，代码定位行号与实际一致，修改方案可行。

### 问题 1：退避重试 ✅ 合理

- 根因确认：`QuotaService.cs:62-63` 确实是 `_consecutiveFailures >= MaxRetryCount` 后 `break` 退出
- 退避公式 `(1L << shift)` 正确，5分钟间隔 × shift=20 = 上限 30 分钟，与 `30L * 60 * 1000` 一致
- `_enteredBackoff` 标志保证只提示一次，合理
- 第 181 行的 `"请求失败 ({_consecutiveFailures}/{MaxRetryCount})"` 仍在，用户在进入退避前能看到逐步升级的失败提示，进入退避后只看到一次 "已进入退避重试模式"，设计 OK

### 问题 2：去掉 Auto 主题 ✅ 合理

- `ThemeMode` 枚举当前是 `Auto=0, Dark=1, Light=2`，去掉 Auto 保留 `Dark=1, Light=2`，**数值不变，已保存的 Dark/Light 配置不受影响**
- ConfigService 迁移时机正确：在 `Load()` 中完成，`SettingsForm` 打开前已迁完，不会出现 `SelectedIndex = -1`
- 索引映射 `(int)config.Theme - 1` 和反向 `(_cmbTheme.SelectedIndex + 1)` 正确对应 `Dark(1)↔0, Light(2)↔1`
- `FloatingWidget.cs` 第 118 行的 `☀ 切换主题` 菜单项不需要改（只是调用 `_onCycleTheme()`）

### 问题 3：卸载流程 ✅ 合理

- `CleanupAndExit()` 在 Mutex 检查之前执行，正确——避免卸载时被单实例拦截
- 清理顺序：注册表 → 配置目录 → 延迟自删 exe，合理
- 延迟自删用 `cmd /c ping 127.0.0.1 -n 3 > nul & del`，经典方案

**已知限制**：`--uninstall` 不会停止已运行的实例，对个人工具可接受。

### 问题 4：通知去重 ✅ 合理

- 根因确认：`TrayApplicationContext.cs:152-158` 每次 Critical 都弹通知
- 双策略去重设计合理：有 resetTime 按重置周期去重，无 resetTime 按 1 小时窗口兜底
- `long _lastNotifiedResetTime` 初始值 `long.MinValue` 保证首次一定触发

### 建议的实施顺序

1. `Models/AppConfig.cs` — 枚举定义（其他文件依赖）
2. `ConfigService.cs` — 迁移逻辑（SettingsForm 依赖）
3. `ThemeService.cs` — 简化（TrayApplicationContext 依赖）
4. `QuotaService.cs` — 退避重试（独立）
5. `TrayApplicationContext.cs` — 主题 + 通知（依赖 1-3）
6. `SettingsForm.cs` — UI 调整 + 卸载按钮（依赖 1-2）
7. `Program.cs` — --uninstall 入口（独立）

**没有发现需要改方案的问题。**
