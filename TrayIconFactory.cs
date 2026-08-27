using System.Drawing;
using System.Drawing.Drawing2D;
using GLMQuotaMonitor.Models;

namespace GLMQuotaMonitor;

/// <summary>
/// 托盘图标工厂
/// 程序化生成彩色状态圆点图标
/// </summary>
public static class TrayIconFactory
{
    private static readonly Dictionary<(QuotaStatus, int), Icon> IconCache = new();

    /// <summary>
    /// 根据配额状态获取对应的图标（缓存键含像素尺寸：运行期 DPI 变化后自动重建）
    /// </summary>
    public static Icon GetIcon(QuotaStatus status)
    {
        int px = Math.Max(16, SystemInformation.SmallIconSize.Width);
        var key = (status, px);
        if (!IconCache.TryGetValue(key, out var icon))
        {
            icon = CreateStatusIcon(status, px);
            IconCache[key] = icon;
        }
        return icon;
    }

    /// <summary>
    /// 生成状态图标（圆点；尺寸取系统小图标规格，几何按比例缩放）
    /// 配色与卡片同源（Catppuccin 中间调），不再使用刺眼的系统纯色
    /// </summary>
    private static Icon CreateStatusIcon(QuotaStatus status, int px)
    {
        Color color = status switch
        {
            QuotaStatus.Normal => BlendBlack(Color.FromArgb(124, 196, 133), 0.30f),   // 绿
            QuotaStatus.Warning => BlendBlack(Color.FromArgb(233, 186, 100), 0.30f),  // 黄
            QuotaStatus.Critical => BlendBlack(Color.FromArgb(226, 98, 98), 0.30f),   // 红
            _ => BlendBlack(Color.FromArgb(140, 145, 165), 0.30f)                     // 灰
        };

        using var bmp = new Bitmap(px, px);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        float f = px / 16f;
        float inset = 1.2f * f;
        float d = px - inset * 2;

        using var fill = new SolidBrush(color);
        using var border = new Pen(BlendBlack(color, 0.35f), Math.Max(1f, 1.1f * f));
        g.FillEllipse(fill, inset, inset, d, d);
        g.DrawEllipse(border, inset, inset, d, d);

        IntPtr hIcon = bmp.GetHicon();
        var result = Icon.FromHandle(hIcon);

        // 复制 Icon 后销毁 HICON，避免泄漏
        var cloned = (Icon)result.Clone();
        DestroyIcon(hIcon);
        return cloned;
    }

    /// <summary>向黑色方向混合（替代 ControlPaint.Dark 的死黑描边）</summary>
    private static Color BlendBlack(Color c, float amount)
    {
        byte Mix(byte ch) => (byte)(ch * (1 - amount));
        return Color.FromArgb(Mix(c.R), Mix(c.G), Mix(c.B));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>
    /// 释放缓存的图标资源
    /// </summary>
    public static void ClearCache()
    {
        foreach (var icon in IconCache.Values)
            icon.Dispose();
        IconCache.Clear();
    }
}
