# GLM Quota Monitor

Windows 桌面常驻工具，实时监控智谱 AI (GLM) / Z.ai 的 API 使用配额。

## 功能

- **系统托盘常驻** — 图标颜色直观反映配额状态（绿/黄/红）
- **配额详情面板** — 点击托盘图标查看 MCP 配额、5h Token 流控、调用次数、Token 用量
- **浮动监控条** — 可选的半透明置顶小条，贴边吸附，不用点击就能看到关键数据
- **智能预警** — 配额超限时弹出 Windows 通知
- **深色/浅色主题** — 支持跟随系统主题自动切换
- **开机自启** — 可选开关

## 截图

> TODO: 添加截图

## 快速开始

### 环境要求

- Windows 10 (1903+) / Windows 11
- .NET 8 运行时（[下载](https://dotnet.microsoft.com/download/dotnet/8.0)）

### 安装

1. 下载最新 Release 的 exe 文件
2. 双击运行
3. 首次启动时，程序会尝试读取环境变量 `ANTHROPIC_AUTH_TOKEN`
4. 如未配置，会弹出设置窗口，手动填入 API Key

### 从源码构建

```bash
# 克隆仓库
git clone https://github.com/mydelren/GLMQuotaMonitor.git
cd GLMQuotaMonitor

# 构建
dotnet build -c Release

# 发布（框架依赖，单文件）
dotnet publish -c Release -r win-x64 --self-contained false
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
| 轮询间隔 | 3 分钟 | 数据刷新频率（可配置 1-30 分钟） |
| 开机自启 | 关闭 | 写入注册表 Run 键 |
| 主题 | 自动 | 深色/浅色/跟随系统 |
| 浮动条 | 关闭 | 是否显示浮动监控条 |
| 警告阈值 | 50% | 图标变黄 |
| 临界阈值 | 80% | 图标变红，弹通知 |

### 配置文件位置

```
%AppData%\GLMQuotaMonitor\config.json
```

## 错误处理

- **请求超时**：10 秒
- **重试策略**：连续失败 3 次后停止自动轮询，托盘图标变灰，需手动点击"立即刷新"重试
- **离线显示**：网络不可用时显示最后一次成功获取的数据，图标标记为灰色
- **启动延迟**：首次请求在启动 3 秒后执行（等待网络就绪）

## 技术栈

- C# / .NET 8
- Windows Forms
- 目标框架：`net8.0-windows`
- 运行时：`win-x64`

## API 说明

本工具调用智谱/Z.ai 的监控 API：

```
GET https://{domain}/api/monitor/usage/quota/limit
Authorization: {your_api_key}
```

响应格式：

```json
{
  "data": {
    "limits": [
      {
        "type": "TIME_LIMIT",
        "usage": 1000,
        "currentValue": 250,
        "remaining": 750
      },
      {
        "type": "TOKENS_LIMIT",
        "usage": 500000,
        "currentValue": 120000,
        "remaining": 380000
      }
    ]
  }
}
```

- `TIME_LIMIT` — MCP 月度配额
- `TOKENS_LIMIT` — 5 小时 Token 流控窗口

## 项目结构

```
GLMQuotaMonitor/
├── Program.cs                  # 入口，单实例互斥
├── TrayApplicationContext.cs   # 托盘图标、上下文菜单、弹窗
├── FloatingBar.cs              # 浮动监控条（贴边吸附）
├── QuotaService.cs             # API 调用与数据解析
├── ConfigService.cs            # 配置读写
├── ThemeService.cs             # 主题检测（深色/浅色/自动）
├── TrayIconFactory.cs          # 程序生成托盘图标（彩色圆点）
├── SettingsForm.cs             # 设置窗口
├── Models/
│   ├── QuotaData.cs            # 配额数据模型
│   └── AppConfig.cs            # 配置模型
├── GLMQuotaMonitor.csproj      # 项目文件
├── popup-demo.html             # 弹窗面板视觉稿
├── README.md
├── LICENSE
└── docs/
    └── DESIGN.md               # 设计文档
```

## 致谢

灵感来源：

- [CowanNath/GLMQuotaWatcher](https://github.com/CowanNath/GLMQuotaWatcher) — VS Code 版配额监控
- [Safphere/glm-usage-vscode](https://github.com/Safphere/glm-usage-vscode) — VS Code 版实时用量监控

## 许可证

MIT License
