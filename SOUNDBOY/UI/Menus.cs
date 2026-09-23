using System.Drawing.Drawing2D;

namespace SOUNDBOY.UI;

sealed class DarkColors : ProfessionalColorTable
{
    static readonly Color Bg = Skin.Surface;
    static readonly Color Sel = Skin.Selected;
    static readonly Color Border = Skin.Border;

    public override Color ToolStripDropDownBackground => Bg;
    public override Color ImageMarginGradientBegin => Bg;
    public override Color ImageMarginGradientMiddle => Bg;
    public override Color ImageMarginGradientEnd => Bg;
    public override Color MenuBorder => Border;
    public override Color MenuItemBorder => Sel;
    public override Color MenuItemSelected => Sel;
    public override Color MenuItemSelectedGradientBegin => Sel;
    public override Color MenuItemSelectedGradientEnd => Sel;
    public override Color MenuItemPressedGradientBegin => Sel;
    public override Color MenuItemPressedGradientMiddle => Sel;
    public override Color MenuItemPressedGradientEnd => Sel;
    public override Color SeparatorDark => Border;
    public override Color SeparatorLight => Bg;
    public override Color CheckBackground => Color.Transparent;
    public override Color CheckSelectedBackground => Color.Transparent;
    public override Color CheckPressedBackground => Color.Transparent;
}

sealed class DarkRenderer : ToolStripProfessionalRenderer
{
    public DarkRenderer() : base(new DarkColors()) => RoundedEdges = false;

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Skin.Text : Skin.Dim;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Skin.Muted;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r = e.ImageRectangle;
        var g = e.Graphics;
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Skin.Accent, 2f);
        g.DrawLines(pen, new PointF[]
        {
            new(r.Left + 3, r.Top + r.Height / 2f),
            new(r.Left + r.Width * 0.42f, r.Bottom - 4),
            new(r.Right - 3, r.Top + 3),
        });
        g.SmoothingMode = old;
    }
}

static class Menus
{
    public static ContextMenuStrip New() => new()
    {
        ShowCheckMargin = true,
        ShowImageMargin = false,
        BackColor = Skin.Surface,
        ForeColor = Skin.Text,
    };

    public static ToolStripMenuItem Item(string text, Action? onClick, string? keys = null, bool isChecked = false, bool enabled = true)
    {
        var item = new ToolStripMenuItem(text) { ShortcutKeyDisplayString = keys, Checked = isChecked, Enabled = enabled };
        if (onClick != null) item.Click += (_, _) => onClick();
        return item;
    }

    public static ToolStripMenuItem Sub(string text, params ToolStripItem[] items)
    {
        var item = new ToolStripMenuItem(text);
        item.DropDownItems.AddRange(items);
        if (item.DropDown is ToolStripDropDownMenu dd) { dd.ShowCheckMargin = true; dd.ShowImageMargin = false; }
        return item;
    }

    public static ToolStripSeparator Sep() => new();
}
