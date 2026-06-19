# GLM Quota Monitor — 设计文档

## 1. 概述

GLM Quota Monitor 是一个 Windows 桌面常驻工具，用于实时监控智谱 AI (GLM) 和 Z.ai 的 API 使用配额。采用系统托盘 + 可选浮动条的显示方式，轻量、不打扰。

### 目标用户

使用 GLM Coding Plan / Z.ai API 的开发者，需要随时了解配额消耗情况。

### 设计原则

- **安静**：正常时不打扰，异常时才通知
- **轻量**：内存占用小，CPU 几乎为零
- **即装即用**：读环境变量自动配置，也支持手动填写

## 2. 架构

### 2.1 技术选型

| 层 | 技术 | 理由 |
|----|------|------|
| 语言 | C# | Windows 原生开发，WinForms 托盘支持成熟 |
| 框架 | .NET 8 LTS | 长期支持，Win10/11 兼容 |
| UI | WinForms | NotifyIcon 原生支持，轻量 |
| HTTP | HttpClient | .NET 内置，无需额外依赖 |
| 配置 | System.Text.Json | .NET 内置 JSON 序列化 |
| 通知 | NotifyIcon.ShowBalloonTip | Win10/11 均可用，无需额外 NuGet |

### 2.2 模块划分

```
┌─────────────────────────────────────────┐
│              Program.cs                 │
│  (Mutex 单实例, Application.Run)        │
└──────────────┬──────────────────────────┘
               │
┌──────────────▼──────────────────────────┐
│         TrayApplicationContext           │
│  ┌──────────┐  ┌──────────┐  ┌────────┐│
│  │ NotifyIcon│  │PopupMenu │  │Settings││
│  │ (托盘图标)│  │(详情面板) │  │ Form   ││
│  └────┬─────┘  └──────────┘  └────────┘│
│       │                                 │
│  ┌────▼─────────────────────────────────┐│
│  │         QuotaService                 ││
│  │  (定时轮询 API, 数据解析)            ││
│  └────┬─────────────────────────────────┘│
│       │                                 │
│  ┌────▼──────┐  ┌───────────┐           │
│  │ConfigService│ │ThemeService│           │
│  │(配置读写)  │ │(主题检测)  │           │
│  └───────────┘  └───────────┘           │
└─────────────────────────────────────────┘
               │
       ┌───────▼───────┐
       │  FloatingBar   │
       │ (浮动监控条)   │
       └───────────────┘
```

### 2.3 生命周期

```
启动
  │
  ├─ Mutex 检查 → 已有实例？→ 退出
  │
  ├─ 加载配置 (ConfigService)
  │   ├─ 读 config.json
  │   └─ 读环境变量 (合并, 手动配置优先)
  │
  ├─ 检测主题 (ThemeService)
  │   └─ 读注册表 HKCU\...\AppsUseLightTheme
  │
  ├─ 创建托盘图标 (TrayApplicationContext)
  │   ├─ NotifyIcon (初始灰色)
  │   └─ ContextMenuStrip (设置/浮动条/自启/刷新/退出)
  │
  ├─ 启动轮询 (QuotaService)
  │   ├─ 首次立即请求
  │   └─ Timer 每 N 分钟刷新
  │
  └─ Token 未配置？→ 弹设置窗口
```

## 3. 核心模块设计

### 3.1 QuotaService — API 调用

**接口**

```
GET https://{domain}/api/monitor/usage/quota/limit
Authorization: {raw_token}
Accept-Language: en-US,en
```

**域名映射**

| 平台 | domain |
|------|--------|
| 智谱 AI | `open.bigmodel.cn` |
| 智谱 AI (dev) | `dev.bigmodel.cn` |
| Z.ai | `api.z.ai` |

**响应解析**

```json
{
  "data": {
    "limits": [
      {
        "type": "TIME_LIMIT",      // MCP 月度配额
        "usage": 1000,             // 总量
        "currentValue": 250,       // 已用
        "remaining": 750           // 剩余
      },
      {
        "type": "TOKENS_LIMIT",    // 5h Token 流控
        "usage": 500000,
        "currentValue": 120000,
        "remaining": 380000
      }
    ]
  }
}
```

**容错**

- 字段兼容：`usage` / `limit_value` / `limitValue`，`currentValue` / `used_value` / `usedValue`
- 请求超时：10 秒
- 重试：连续失败 3 次后停止轮询，等用户手动刷新
- 无网络：显示上次成功数据 + "离线" 标记

**轮询**

- 默认间隔：3 分钟（180,000ms）
- 可配置范围：1-30 分钟
- 启动延迟：3 秒后首次请求（避免启动时网络未就绪）

### 3.2 TrayApplicationContext — 托盘与弹窗

**托盘图标状态**

| 状态 | 图标 | 触发条件 |
|------|------|----------|
| 正常 | 🟢 绿色圆点 | 所有配额 < 50% |
| 警告 | 🟡 黄色圆点 | 任一配额 ≥ 50% |
| 临界 | 🔴 红色圆点 | 任一配额 ≥ 80% |
| 离线/错误 | ⚪ 灰色圆点 | API 请求失败 / Token 未配置 |

**Tooltip**

```
GLM: MCP 35% | 5h 22% | 802次
```

最多约 120 字符（Win10/11 Unicode 模式下 Shell_NotifyIcon 支持 127 字符）。

**左键点击 — 详情面板**

使用 `ToolStripDropDown` + `ToolStripControlHost` 托管一个 `UserControl`。

面板内容（参考 popup-demo.html）：
- 标题栏：GLM 配额监控 + 平台标识
- MCP 配额进度条 + 百分比
- 5h Token 进度条 + 百分比
- 统计数据：调用次数、Token 用量、配额重置倒计时
- 底栏：上次刷新时间 + 立即刷新按钮

**右键菜单**

```
⚙ 设置
──────────
☐ 显示浮动条
☐ 开机自启动
──────────
⟳ 立即刷新
──────────
✕ 退出
```

### 3.3 FloatingBar — 浮动监控条

**外观**

- 无边框、半透明背景（深色模式 rgba(22,33,62,0.85)，浅色模式 rgba(255,255,255,0.9)）
- 置顶（`TopMost = true`）
- 圆角（通过 `Region` 实现）
- 内容：`MCP: 35% | 5h: 22% | 802次`
- 高度：约 28px，宽度自适应

**交互**

- **拖动**：鼠标按下拖动移动位置
- **贴边吸附**：
  - 拖到屏幕左/右/上边缘 10px 范围内 → 松手后自动贴边
  - 贴边后大部分隐藏，只露出 2-3px 边缘
  - 鼠标悬停在露出的边缘 → 平滑滑出显示完整内容
  - 鼠标离开 → 延迟 0.5 秒后滑回隐藏
- **右键菜单**：取消贴边 / 关闭浮动条

**数据更新**

- 复用 QuotaService 的数据，不需要额外请求
- 配额数据变化时自动刷新显示

### 3.4 ConfigService — 配置管理

**存储位置**

```
%AppData%\GLMQuotaMonitor\config.json
```

**配置模型 (AppConfig)**

```json
{
  "authToken": "",
  "platform": "auto",
  "pollingIntervalMinutes": 3,
  "autoStart": false,
  "theme": "auto",
  "showFloatingBar": false,
  "warningThreshold": 50,
  "criticalThreshold": 80,
  "floatingBarX": null,
  "floatingBarY": null
}
```

- `platform`: `"auto"` | `"zhipu"` | `"zai"`
- `theme`: `"auto"` | `"dark"` | `"light"`
- `floatingBarX/Y`: `null` 表示使用默认位置（屏幕右下角），整数值为具体屏幕坐标

**优先级**

手动配置 > 环境变量 > 默认值

**读取时机**

- 启动时读一次
- 设置窗口保存后立即重载

### 3.5 ThemeService — 主题检测

**检测方法**

读注册表：
```
HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize
  AppsUseLightTheme: DWORD (0=深色, 1=浅色)
```

**自动模式监听**

- 订阅 `Microsoft.Win32.SystemEvents.UserPreferenceChanged` 事件
- 当用户切换系统主题时自动触发，无需轮询
- 变化时触发事件，UI 模块响应切换主题

### 3.6 TrayIconFactory — 图标生成

程序化生成 16x16 图标，避免嵌入 .ico 资源文件：

```csharp
// 伪代码
Bitmap(16,16) → FillEllipse(颜色) → GetHicon() → Icon.FromHandle()
```

缓存每种状态的 Icon 实例，避免 GDI 句柄泄漏。

### 3.7 SettingsForm — 设置窗口

标准 WinForms 窗口，包含：

- API Key 输入框（PasswordChar 掩码，右侧"显示/隐藏"按钮）
- 平台下拉框（自动 / 智谱 AI / Z.ai）
- 轮询间隔数字框（1-30 分钟）
- 开机自启复选框
- 主题单选（自动 / 深色 / 浅色）
- 浮动条开关复选框
- 警告/临界阈值滑块
- 保存 / 取消 按钮

## 4. 通知策略

| 事件 | 通知方式 |
|------|----------|
| 配额 ≥ 警告阈值 (50%) | 托盘图标变黄，无弹窗 |
| 配额 ≥ 临界阈值 (80%) | 托盘图标变红 + BalloonTip 弹窗 |
| API 请求失败（连续3次） | 托盘图标变灰 + BalloonTip |
| Token 未配置 | 首次启动弹设置窗口 |

## 5. 单实例保证

```csharp
const string MutexName = "Global\\GLMQuotaMonitor_{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}";
Mutex mutex = new Mutex(true, MutexName, out bool createdNew);
if (!createdNew) return; // 已有实例，静默退出

// ... Application.Run(...) ...

GC.KeepAlive(mutex); // 防止 GC 回收导致 mutex 提前释放
```

## 6. 开机自启

写入注册表：
```
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
  GLMQuotaMonitor = "C:\path\to\GLMQuotaMonitor.exe"
```

- 只写当前用户（HKCU），不需要管理员权限
- 设置窗口中的复选框控制开关

## 7. 发布策略

当前阶段：**框架依赖**

```bash
dotnet publish -c Release -r win-x64 --self-contained false
```

- 体积 ~5-15MB
- 需要目标机器安装 .NET 8 运行时
- 后续可切换为自包含发布

## 8. 后续扩展（暂不实现）

- 24 小时调用趋势图（需要 `/api/monitor/usage/model-usage` 接口）
- 多账号监控
- 系统资源监控（CPU/内存/网速）整合
- 自包含发布 + 安装包（Inno Setup / NSIS）
- 自动更新检查
