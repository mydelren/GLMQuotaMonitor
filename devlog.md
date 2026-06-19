# 开发日志（不入 git）

## 2026-06-19

### 项目启动
- 调研了两个 GitHub 仓库：CowanNath/GLMQuotaWatcher 和 Safphere/glm-usage-vscode
- 确定技术方案：C# + WinForms + .NET 8 LTS，系统托盘 + 浮动卡片
- 创建项目文件夹 GLMQuotaMonitor，初始化 git 仓库
- 配置了 GitHub CLI（gh），建仓库 mydelren/GLMQuotaMonitor
- 安装了 .NET 8 SDK

### 环境配置
- Git 用户：mydelren+kwangao@foxmail.com
- GitHub 代理：http://127.0.0.1:7897（Clash-Verge）
- 写了 README.md、docs/DESIGN.md、LICENSE、.gitignore

### 核心功能开发
- **QuotaService**：调用 GLM/Z.ai 监控 API（quota/limit + model-usage）
- **TrayApplicationContext**：系统托盘图标、右键菜单、tooltip
- **ConfigService**：JSON 配置读写（%AppData%\GLMQuotaMonitor\config.json）
- **ThemeService**：深色/浅色主题检测（注册表 SystemEvents）
- **TrayIconFactory**：程序化生成彩色状态图标
- **Program.cs**：Mutex 单实例

### API 调研
- quota/limit 接口：返回 MCP 配额（TIME_LIMIT）和 5h Token（TOKENS_LIMIT）
- model-usage 接口：返回调用次数和 Token 用量（24小时窗口）
- 认证方式：Authorization 头，无 Bearer 前缀
- API 支持 percentage 字段直接返回百分比

### UI 迭代（经历了很多轮）

#### V1-V4：弹窗面板
- 从 ToolStripDropDown 弹窗 → 自绘面板 → 全自绘 GDI+
- 经历了文字重叠、进度条太短、渐变移除、间距调整等多轮修复
- 最终去掉了弹窗，改为 widget 右键菜单

#### 浮动条演变
- FloatingBar → FloatingWidget
- 从横条 → 垂直卡片
- 从两列 → 三列（标签 | 进度条 | 百分比）
- 去掉了详情行，改为底部统计行 + 刷新时间
- 加了右键菜单（刷新、定位、自启、主题、设置、退出）

#### 配色
- 从自定义色 → Catppuccin Mocha 深色 / Latte 浅色
- 浅色模式进度条用柔和色调（降低饱和度）

#### 贴边吸附
- 实现了左/右/顶三个方向的贴边
- 贴边后显示迷你进度条（12px 宽，分两段）
- 从下往上填充（左/右），从左往右填充（顶）
- 边距：桌面侧 3px + 进度条 10px + 屏幕边 5px
- 位置自由（不固定居中）

### 三道门禁流程
- 建立了计划 → 实现计划 → 代码审查的三道门禁制度
- 每道门禁都需要子代理全部 PASS
- 写入 .constraints 文件

### Bug 修复记录
- TransparencyKey 抗锯齿导致紫红色边框 → 去掉 TransparencyKey
- OnPaint 中设置 BackColor 导致无限重绘循环 → 移到主题变更事件
- 分隔线和统计行重叠 → 去掉分隔线
- model-usage API 数据为 0 → 修复 Task 异常处理
- 5h Token 百分比为 0 → 支持 API 直接返回的 percentage 字段
- 右键菜单深色模式下为浅色 → ToolStripDarkRenderer

### 发布
- 版本 v0.1.0
- GitHub Actions 自动构建（轻量版 + 完整版）
- 轻量版：210KB（框架依赖）
- 完整版：68MB（自包含）
- Release: https://github.com/mydelren/GLMQuotaMonitor/releases/tag/v0.1.0

### 待办
- 用户反馈后继续迭代 UI
- 可能需要添加周额度（如果 API 支持）
- 考虑添加历史趋势图（需要 model-usage 的 x_time 数据）
