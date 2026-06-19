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
    private static readonly Dictionary<QuotaStatus, Icon> IconCache = new();

    /// <summary>
    /// 根据配额状态获取对应的图标
    /// </summary>
    public static Icon GetIcon(QuotaStatus status)
    {
        if (!IconCache.TryGetValue(status, out var icon))
        {
            icon = CreateStatusIcon(status);
            IconCache[status] = icon;
        }
        return icon;
    }

    /// <summary>
    /// 生成状态图标（16x16 彩色圆点）
    /// </summary>
    private static Icon CreateStatusIcon(QuotaStatus status)
    {
        Color color = status switch
        {
            QuotaStatus.Normal => Color.FromArgb(0, 180, 0),     // 绿色
            QuotaStatus.Warning => Color.FromArgb(230, 180, 0),  // 黄色
            QuotaStatus.Critical => Color.FromArgb(200, 0, 0),   // 红色
            _ => Color.FromArgb(128, 128, 128)                   // 灰色
        };

        using var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        // 填充圆
        using var fill = new SolidBrush(color);
        using var border = new Pen(ControlPaint.Dark(color), 1);
        g.FillEllipse(fill, 1, 1, 14, 14);
        g.DrawEllipse(border, 1, 1, 14, 14);

        IntPtr hIcon = bmp.GetHicon();
        var result = Icon.FromHandle(hIcon);

        // 复制 Icon 后销毁 HICON，避免泄漏
        var cloned = (Icon)result.Clone();
        DestroyIcon(hIcon);
        return cloned;
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
