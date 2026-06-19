# 浮动 Widget V4 计划

## 问题清单

1. **标签/进度条/百分比重叠** — LabelColWidth=60 太窄，"MCP 配额"约 65px，溢出
2. **进度条太矮、底色不明显** — 当前 10px，BarBg 透明度太高
3. **缺少手动刷新按钮和更新时间** — 需要在底部增加 UI 元素
4. **MCP 和 5h Token 无法区分** — 颜色完全相同，视觉上分不清
5. **边框只显示左/上，右/下缺失** — TransparencyKey 抗锯齿问题

## 解决方案

### 1. 标签列加宽

| 列 | 当前 | 改为 |
|----|------|------|
| LabelColWidth | 60 | 70 |
| BarWidth | 80 | 70 |
| PctColWidth | 50 | 50 |
| ColGap | 5 | 5 |
| 合计 | 200 | 200 ✅ |

标签列 70px 足够放 "MCP 配额"（约 65px）。

### 2. 进度条加高 + 底色更明显

| 属性 | 当前 | 改为 |
|------|------|------|
| BarHeight | 10 | 14 |
| BarBg 深色模式 | rgba(25,255,255,255) ~10% | rgba(255,255,255,15%) 更明显 |
| BarBg 浅色模式 | rgba(15,0,0,0) ~6% | rgba(0,0,0,10%) 更明显 |

行高从 20px 改为 24px（容纳 14px 进度条 + 上下居中）。

### 3. 底部增加刷新按钮和更新时间

在统计行下方增加：
```
  [⟳ 刷新]    14:32:05 更新
```

- 刷新按钮：左侧，可点击触发 RefreshQuota()
- 更新时间：右侧，显示上次数据刷新时间

需要在 FloatingWidget 中接收刷新回调和时间戳。

高度增加：统计行 14px + 间距 8px + 底部行 20px + padding = 约 42px

新 CardHeight：14(pad) + 24 + 6 + 24 + 6 + 14 + 8 + 20 + 14(pad) = 130px → 取 **130**

### 4. MCP 和 5h Token 视觉区分

**方案：进度条颜色区分**

| 配额 | 正常色 | 警告色 | 临界色 |
|------|--------|--------|--------|
| MCP 配额 | 蓝色 rgb(70,130,230) | 黄色 | 红色 |
| 5h Token | 青色 rgb(0,210,205) | 黄色 | 红色 |

MCP 用蓝色系，5h 用青色系。正常状态下一眼可区分。超限时统一变黄/红（警告语义一致）。

### 5. 边框内缩修复

将背景填充和边框绘制从 (0, 0, w, h) 改为 (2, 2, w-4, h-4)。

窗口边缘 2px 保持 TransparencyKey 颜色（品红色），被系统透明化。抗锯齿像素完全在窗口内部，不会出现半透明边框伪影。

```csharp
// 背景
using (var path = GraphicsExtensions.MakeRoundRect(2, 2, w - 4, h - 4, 4))
    g.FillPath(bgBrush, path);

// 边框
using (var path = GraphicsExtensions.MakeRoundRect(2, 2, w - 5, h - 5, 4))
    g.DrawPath(borderPen, path);
```

### 6. FloatingWidget 需要接收的新依赖

- 刷新回调：`Action onRefresh`，由 TrayApplicationContext 传入
- 更新时间：从 QuotaSnapshot.Timestamp 获取

构造函数改为：
```csharp
public FloatingWidget(ThemeService themeService, ConfigService configService, Action onRefresh)
```

### 7. TrayApplicationContext 适配

- 创建 FloatingWidget 时传入刷新回调
- 传递给 FloatingWidget 的 UpdateData 已包含 Timestamp

## 修改文件

| 文件 | 改动 |
|------|------|
| FloatingWidget.cs | 常量调整、DrawQuotaRow 颜色区分、边框内缩、底部刷新+时间、构造函数加 onRefresh |
| TrayApplicationContext.cs | 创建 FloatingWidget 时传入回调 |

## 不修改

- QuotaService.cs、QuotaData.cs、ConfigService.cs、ThemeService.cs、SettingsForm.cs、Program.cs、GraphicsExtensions.cs
- 字体大小不变
- 贴边吸附逻辑不变
