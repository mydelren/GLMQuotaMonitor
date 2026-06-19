# Widget V5b：边框修复 + 分隔线间距

## 问题

1. TransparencyKey 导致边框出现紫红色抗锯齿伪影
2. 分隔线和统计行重叠（间距仅 1px）

## 方案

### 1. 去掉 TransparencyKey

删除：
```csharp
BackColor = Color.Magenta;
TransparencyKey = Color.Magenta;
```

改为：
```csharp
BackColor = isDark ? Color.FromArgb(30, 30, 46) : Color.FromArgb(239, 241, 245);
```

在构造函数中无法访问 isDark（还没初始化 ThemeService），所以：
- 构造函数中设 `BackColor = Color.FromArgb(30, 30, 46)`（深色默认）
- OnPaint 开头根据当前主题更新 BackColor

同时删除 Inset 相关逻辑：
- 删除 `Inset = 2` 常量
- MakeRoundRect 改回 `(0, 0, w, h, 4)` 和 `(0, 0, w-1, h-1, 4)`

窗口方角问题：4px 圆角在 240×134 窗口上，方角区域极小（约 4×4 像素），视觉上可接受。

### 2. 统计行和分隔线间距

当前 OnPaint 中统计行后：
```csharp
y += 12;  // 第 197 行
```

改为：
```csharp
y += 16;
```

CardHeight 需要相应增加 4px：134 → 138。

## 修改文件

- FloatingWidget.cs：常量、BackColor、OnPaint

## 不修改

- TrayApplicationContext.cs、QuotaService.cs 等其他文件
