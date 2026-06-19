using System.Drawing;
using System.Drawing.Drawing2D;

namespace GLMQuotaMonitor;

/// <summary>
/// Graphics 绘图扩展方法
/// </summary>
public static class GraphicsExtensions
{
    /// <summary>
    /// 填充圆角矩形
    /// </summary>
    public static void FillRoundedRectangle(this Graphics g, Brush brush, float x, float y, float w, float h, float radius)
    {
        using var path = MakeRoundRect(x, y, w, h, radius);
        g.FillPath(brush, path);
    }

    /// <summary>
    /// 绘制圆角矩形边框
    /// </summary>
    public static void DrawRoundedRectangle(this Graphics g, Pen pen, float x, float y, float w, float h, float radius)
    {
        using var path = MakeRoundRect(x, y, w, h, radius);
        g.DrawPath(pen, path);
    }

    /// <summary>
    /// 创建圆角矩形路径
    /// </summary>
    public static GraphicsPath MakeRoundRect(float x, float y, float w, float h, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
