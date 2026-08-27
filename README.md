# 智谱配额监控 / GLM Quota Monitor

Windows 桌面常驻工具，实时监控智谱 AI (GLM) / Z.ai 的 API 使用配额。

## 截图

### 深色模式

| 浮动卡片 | 贴边吸附 | 右键菜单 |
|:--------:|:--------:|:--------:|
| ![深色卡片](images/widget-dark.png) | ![深色贴边](images/edge-dock-dark.png) | ![深色菜单](images/menu-dark.png) |

### 浅色模式

| 浮动卡片 | 贴边吸附 |
|:--------:|:--------:|
| ![浅色卡片](images/widget-light.png) | ![浅色贴边](images/edge-dock-light.png) |

### 设置

![设置窗口](images/settings-dark.png)

## 功能

- **浮动卡片** — 置顶显示 MCP 配额和 5h Token 流控，实时刷新；<10% 显示一位小数，避免四舍五入误导
- **贴边吸附** — 拖到屏幕边缘自动收起为迷你进度条，悬停展开；贴边状态跨重启记忆
- **悬停气泡** — 迷你条上直接看两行数字与重置倒计时
- **右键菜单** — 刷新、定位、开机自启、主题切换、设置
- **智能预警** — 配额超限时弹 Windows 通知（同一窗口期内只提醒一次，不重复轰炸）
- **重置倒计时** — 卡片左下角显示 5h Token 的剩余重置时间（如 `3h46m 后重置`）
- **深色/浅色主题** — Catppuccin 配色，跟随系统自动切换；设置窗口全控件深色适配
- **多显示器** — 吸附/展开按窗口所在屏幕计算，显示器拔插后可从托盘一键拉回
- **纯绿色** — 零注册表、零残留，删掉 exe 即完成卸载

## 启动参数

| 参数 | 说明 |
|------|------|
| `--demo` | 演示模式：跳过网络输出固定示例数据，用于截图/宣传/UI 调试 |
| `--instance=名称` | 启动并行实例（独立互斥体），配合 `--config-dir` 做隔离调试 |
| `--config-dir=路径` | 指定配置目录（便携模式）。注意目录下直接放 `config.json` |

环境变量 `GLMQM_DEBUG=1` 时向 `%TEMP%\glmqm-debug.log` 写运行诊断日志。

## 更新记录

### v0.3.0（2026-08-27）

- **合并双线开发成果**：v0.2.x 分支的退避重试（指数封顶 30 分钟）、SafeCancellationTokenSource、纯绿色改造，与本线的视觉与体验增强合流
- 修复关键数据丢失：后台线程事件因同步上下文捕获过早为 null 而被静默丢弃——现在卡片与托盘可正常刷新
- 卡片视觉重制：药丸进度条 + 内高光、窗口 Region 真实圆角（消除四角色块毛刺）、页脚重置倒计时、<10% 一位小数
- 贴边状态跨重启记忆；迷你条拖出时光标比例重锚定；迷你条悬停气泡；多显示器吸附与屏幕外恢复
- 设置窗分区化排版，ComboBox/NumericUpDown 深色全控件适配
- 稳定性：轮询串行化防并发、SafeCTS 换新防 ODE、指数退避不再永久停摆、预警弹窗去抖、离线态倒计时保护
- 托盘图标 Catppuccin 配色 + 尺寸自适应
- 新增 `--demo` / `--instance=` / `--config-dir=` 启动参数与 `GLMQM_DEBUG` 诊断日志

### v0.2.0 – v0.2.4（2026-06-25）

- 退避重试、通知去重、SafeCancellationTokenSource、纯绿色改造（见对应 Tag）

## 快速开始

### 下载

从 [Releases](https://github.com/mydelren/GLMQuotaMonitor/releases) 下载：

| 文件 | 说明 | 体积 |
|------|------|------|
| `GLMQuotaMonitor-light.exe` | 轻量版（框架依赖） | ~200KB |
| `GLMQuotaMonitor-full.exe` | 完整版（自包含） | ~68MB |

**轻量版** 需要已安装 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)。如果不确定，请下载**完整版**。

### 首次运行

1. 双击 exe 运行
2. 首次启动时，程序会尝试读取环境变量 `ANTHROPIC_AUTH_TOKEN`
3. 如未配置，会弹出设置窗口，手动填入 API Key

### 从源码构建

```bash
git clone https://github.com/mydelren/GLMQuotaMonitor.git
cd GLMQuotaMonitor

# 构建
dotnet build -c Release

# 发布轻量版
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true

# 发布完整版
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## 配置

### API Key

程序按以下优先级获取 API Key：

1. 手动配置（设置窗口中填写，存储于 `%AppData%\GLMQuotaMonitor\config.json`）
2. 环境变量 `ANTHROPIC_AUTH_TOKEN`

### 平台

程序自动从环境变量 `ANTHROPIC_BASE_URL` 识别平台：

| 平台 | 域名 |
|------|------|
| 智谱 AI | `open.bigmodel.cn` |
| 智谱 AI (dev) | `dev.bigmodel.cn` |
| Z.ai | `api.z.ai` |

如未设置环境变量，可在设置窗口中手动选择。

### 设置项

| 项目 | 默认值 | 说明 |
|------|--------|------|
| API Key | — | 智谱/Z.ai 的 API Key |
| 平台 | 自动检测 | 智谱 AI / Z.ai |
| 轮询间隔 | 5 分钟 | 数据刷新频率（可配置 1-30 分钟） |
| 开机自启 | 关闭 | 写入注册表 Run 键 |
| 主题 | 自动 | 深色/浅色/跟随系统 |
| 警告阈值 | 50% | 图标变黄 |
| 临界阈值 | 80% | 图标变红，弹通知 |

## 错误处理

- **请求超时**：10 秒
- **重试策略**：连续失败 3 次后停止自动轮询，需手动刷新
- **离线显示**：网络不可用时显示最后一次成功获取的数据
- **启动延迟**：首次请求在启动 3 秒后执行

## 技术栈

- C# / .NET 8 LTS
- Windows Forms
- 目标框架：`net8.0-windows`
- 运行时：`win-x64`
- 配色：[Catppuccin](https://catppuccin.com/) Mocha (深色) / Latte (浅色)

## API 说明

本工具调用智谱/Z.ai 的监控 API：

```
GET https://{domain}/api/monitor/usage/quota/limit
Authorization: {your_api_key}
```

- `TIME_LIMIT` — MCP 月度配额
- `TOKENS_LIMIT` — 5 小时 Token 流控窗口

## 致谢

灵感来源：

- [CowanNath/GLMQuotaWatcher](https://github.com/CowanNath/GLMQuotaWatcher) — VS Code 版配额监控
- [Safphere/glm-usage-vscode](https://github.com/Safphere/glm-usage-vscode) — VS Code 版实时用量监控
- [Catppuccin](https://catppuccin.com/) — 配色方案

## 关键词

智谱 AI、GLM、Z.ai、配额监控、用量监控、API 监控、Coding Plan、MCP 配额、Token 限流、桌面悬浮窗、系统托盘、Windows 小工具、深色模式、Catppuccin

## 许可证

MIT License
