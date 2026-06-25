# GLMQuotaMonitor 修复执行方案

基于 PLAN.md v3 + 审查修正，共 6 个文件变更。

---

## 变更 1：QuotaService.cs — 退避重试

### 1.1 新增字段（第 25 行 `_consecutiveFailures` 之后）

```csharp
private volatile bool _enteredBackoff;
```

### 1.2 修改轮询循环（第 58-66 行）

将：
```csharp
while (!cts.Token.IsCancellationRequested)
{
    await FetchQuota(tokenGetter(), baseUrlGetter(), cts.Token);

    if (_consecutiveFailures >= MaxRetryCount)
        break;

    await Task.Delay(intervalMs, cts.Token);
}
```

改为：
```csharp
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

### 1.3 删除 FetchQuota 中的停止提示（第 183-184 行）

删除：
```csharp
if (_consecutiveFailures >= MaxRetryCount)
    Error?.Invoke("连续失败次数过多，已停止自动轮询，请手动刷新");
```

### 1.4 成功时重置 _enteredBackoff（第 159 行）

在 `_consecutiveFailures = 0;` 之后加：
```csharp
_enteredBackoff = false;
```

---

## 变更 2：TrayApplicationContext.cs — 主题快捷切换

### 2.1 修改 CycleTheme()（第 281-292 行）

将：
```csharp
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

改为：
```csharp
private void CycleTheme()
{
    var config = _configService.Config;
    config.Theme = _themeService.IsDark ? ThemeMode.Light : ThemeMode.Dark;
    _configService.Save(config);
}
```

**逻辑**：Auto 模式下根据当前实际深浅状态切换到对应的手动模式，Dark↔Light 直接互切。设置界面仍保留 Auto/Dark/Light 三选项。

---

## 变更 3：TrayApplicationContext.cs — 通知去重

### 3.1 新增字段（类字段区，`_floatingBar` 之后）

```csharp
private long _lastNotifiedResetTime = long.MinValue;
private DateTime _lastNotifiedTime = DateTime.MinValue;
```

### 3.2 修改 OnQuotaUpdated 中的通知逻辑（第 152-158 行）

将：
```csharp
if (status == QuotaStatus.Critical && !snapshot.IsOffline)
{
    _notifyIcon.ShowBalloonTip(5000,
        "GLM 配额预警",
        $"MCP 配额: {mcpPct}\n5h Token: {tokenPct}",
        ToolTipIcon.Warning);
}
```

改为：
```csharp
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
        _notifyIcon.ShowBalloonTip(5000,
            "GLM 配额预警",
            $"MCP 配额: {mcpPct}\n5h Token: {tokenPct}",
            ToolTipIcon.Warning);
    }
}
```

---

## 变更 4：Program.cs — 卸载入口 + 自删加固

### 4.1 修改 Main 签名 + 添加 --uninstall 处理

将：
```csharp
[STAThread]
static void Main()
{
    // 单实例保证
    using var mutex = new Mutex(true, MutexName, out bool createdNew);
    if (!createdNew)
    {
        return;
    }

    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);
    Application.SetHighDpiMode(HighDpiMode.SystemAware);

    Application.Run(new TrayApplicationContext());

    GC.KeepAlive(mutex);
}
```

改为：
```csharp
[STAThread]
static void Main(string[] args)
{
    if (args.Contains("--uninstall"))
    {
        CleanupAndExit();
        return;
    }

    // 单实例保证
    using var mutex = new Mutex(true, MutexName, out bool createdNew);
    if (!createdNew)
    {
        return;
    }

    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);
    Application.SetHighDpiMode(HighDpiMode.SystemAware);

    Application.Run(new TrayApplicationContext());

    GC.KeepAlive(mutex);
}
```

### 4.2 新增 CleanupAndExit 方法

```csharp
static void CleanupAndExit()
{
    // 1. 删注册表
    const string regPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    try
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(regPath, true);
        key?.DeleteValue("GLMQuotaMonitor", false);
    }
    catch { }

    // 2. 删配置目录
    string configDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GLMQuotaMonitor");
    try { Directory.Delete(configDir, true); } catch { }

    // 3. 延迟自删 exe（taskkill + del，确保进程已退出后删）
    string exePath = Environment.ProcessPath ?? "";
    if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd",
            Arguments = $"/c ping 127.0.0.1 -n 3 > nul & taskkill /f /im \"{Path.GetFileName(exePath)}\" > nul 2>&1 & del \"{exePath}\"",
            CreateNoWindow = true,
            UseShellExecute = false
        });
    }
}
```

需要在文件顶部添加 `using System.Diagnostics;`（当前文件无任何 using，.NET 8 ImplicitUsings 不包含 System.Diagnostics）。

---

## 变更 5：SettingsForm.cs — 卸载按钮

### 5.1 调整窗口高度（第 44 行）

将 `ClientSize = new Size(420, 430);` 改为 `ClientSize = new Size(420, 480);`

### 5.2 在保存/取消按钮之后追加卸载区（第 208 行 CancelButton 之后）

在 `CancelButton = btnCancel;` 之后，`InitializeUI` 方法结束之前，追加：

```csharp
// 分隔线
y += 46;
Controls.Add(new Panel
{
    BackColor = Color.FromArgb(200, 200, 210),
    Location = new Point(16, y),
    Size = new Size(388, 1)
});
y += 12;

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

### 5.3 新增 OnUninstall 方法

```csharp
private void OnUninstall(object? sender, EventArgs e)
{
    var result = MessageBox.Show(
        "确定卸载？将删除注册表键值、配置目录，并自删 exe。", "确认卸载",
        MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
    if (result != DialogResult.Yes) return;

    SetAutoStart(false);
    Close();
    Program.CleanupAndExit();
    Application.Exit();
}
```

---

## 变更汇总

| 文件 | 变更类型 | 说明 |
|------|----------|------|
| `QuotaService.cs` | 修改 | 退避重试替代 break 退出 |
| `TrayApplicationContext.cs` | 修改 | CycleTheme 一行改 + 通知去重 |
| `Program.cs` | 修改 | --uninstall 入口 + CleanupAndExit |
| `SettingsForm.cs` | 修改 | 卸载按钮 + 窗口高度调整 |

**不改动的文件**：
- `Models/AppConfig.cs` — ThemeMode 枚举保留 Auto/Dark/Light
- `ConfigService.cs` — 无需迁移逻辑
- `ThemeService.cs` — 保留 Auto 跟随系统
- `FloatingWidget.cs` — 仅调用回调，无需改动
- `Models/QuotaData.cs` — 无影响
- `GraphicsExtensions.cs` / `TrayIconFactory.cs` / `ToolStripDarkRenderer.cs` — 无影响

---

## 实施顺序

1. `QuotaService.cs` — 独立，无依赖
2. `TrayApplicationContext.cs` — CycleTheme + 通知去重
3. `Program.cs` — CleanupAndExit（SettingsForm 依赖此方法）
4. `SettingsForm.cs` — 卸载按钮（依赖 Program.CleanupAndExit）

## 验证要点

1. **退避重试**：启动后断网，观察 3 次失败后是否进入退避（日志/Debug 输出），恢复网络后是否自动恢复
2. **主题切换**：Auto 模式下点击"切换主题"，确认切到 Dark/Light 而非循环回 Auto
3. **通知去重**：配额 Critical 后连续刷新，确认只弹一次通知
4. **卸载**：运行 `GLMQuotaMonitor.exe --uninstall`，确认注册表、配置目录、exe 三项清理
