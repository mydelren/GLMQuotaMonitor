namespace GLMQuotaMonitor;

/// <summary>
/// 深色模式菜单渲染器（Catppuccin Mocha 配色）
/// </summary>
public class ToolStripDarkRenderer : ToolStripProfessionalRenderer
{
    public ToolStripDarkRenderer() : base(new DarkColorTable()) { }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled
            ? Color.FromArgb(205, 214, 244)   // Text
            : Color.FromArgb(108, 112, 134);  // Overlay 1 (disabled)
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Color.FromArgb(205, 214, 244); // Text
        base.OnRenderArrow(e);
    }
}

/// <summary>
/// 深色菜单颜色表
/// </summary>
internal class DarkColorTable : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Color.FromArgb(30, 30, 46);       // Base
    public override Color MenuBorder => Color.FromArgb(49, 50, 68);                        // Surface 0
    public override Color MenuItemBorder => Color.FromArgb(49, 50, 68);
    public override Color MenuItemSelected => Color.FromArgb(49, 50, 68);                  // Surface 0
    public override Color MenuItemSelectedGradientBegin => Color.FromArgb(49, 50, 68);
    public override Color MenuItemSelectedGradientEnd => Color.FromArgb(49, 50, 68);
    public override Color MenuItemPressedGradientBegin => Color.FromArgb(49, 50, 68);
    public override Color MenuItemPressedGradientEnd => Color.FromArgb(49, 50, 68);
    public override Color MenuStripGradientBegin => Color.FromArgb(30, 30, 46);
    public override Color MenuStripGradientEnd => Color.FromArgb(30, 30, 46);
    public override Color ImageMarginGradientBegin => Color.FromArgb(30, 30, 46);
    public override Color ImageMarginGradientMiddle => Color.FromArgb(30, 30, 46);
    public override Color ImageMarginGradientEnd => Color.FromArgb(30, 30, 46);
    public override Color SeparatorDark => Color.FromArgb(69, 71, 90);                     // Surface 1
    public override Color SeparatorLight => Color.FromArgb(69, 71, 90);
    public override Color CheckBackground => Color.FromArgb(49, 50, 68);
    public override Color CheckSelectedBackground => Color.FromArgb(49, 50, 68);
    public override Color CheckPressedBackground => Color.FromArgb(49, 50, 68);
    public override Color ButtonSelectedHighlight => Color.FromArgb(49, 50, 68);
}
