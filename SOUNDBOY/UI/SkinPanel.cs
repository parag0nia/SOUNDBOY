using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace SOUNDBOY.UI;

/// <summary>
/// A custom-painted, zoomable surface. Subclasses lay out and paint in logical pixels;
/// the panel scales everything and routes mouse input to widgets.
/// </summary>
class SkinPanel : Control
{
    protected readonly List<Widget> Widgets = new();
    readonly ToolTip tip = new() { InitialDelay = 700, ReshowDelay = 200 };
    Widget? capture, hover;
    bool bgCapture;

    public float UiScale { get; private set; } = 1f;
    public SizeF LogicalSize { get; private set; }
    public event Action<SkinPanel>? GotActive;

    protected virtual IEnumerable<Widget> ActiveWidgets => Widgets;

    public SkinPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Skin.Window;
    }

    protected T Add<T>(T w, List<Widget>? list = null) where T : Widget
    {
        w.Owner = this;
        (list ?? Widgets).Add(w);
        return w;
    }

    public void SetLayout(float scale, float logicalW, float logicalH)
    {
        UiScale = scale;
        LogicalSize = new SizeF(logicalW, logicalH);
        Size = new Size((int)Math.Ceiling(logicalW * scale), (int)Math.Ceiling(logicalH * scale));
        OnLayoutChanged();
        Invalidate();
    }

    protected virtual void OnLayoutChanged() { }

    public PointF ToLogical(Point p) => new(p.X / UiScale, p.Y / UiScale);
    public Point ToDevice(PointF p) => new((int)(p.X * UiScale), (int)(p.Y * UiScale));

    public void InvalidateLogical(RectangleF r)
    {
        Invalidate(Rectangle.FromLTRB(
            (int)Math.Floor(r.Left * UiScale) - 2, (int)Math.Floor(r.Top * UiScale) - 2,
            (int)Math.Ceiling(r.Right * UiScale) + 2, (int)Math.Ceiling(r.Bottom * UiScale) + 2));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.ScaleTransform(UiScale, UiScale);
        g.SmoothingMode = SmoothingMode.None;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        PaintBackground(g);
        foreach (var w in ActiveWidgets)
            if (w.Visible) w.Paint(g);
        PaintOverlay(g);
    }

    protected virtual void PaintBackground(Graphics g) =>
        Skin.Fill(g, Skin.Window, new RectangleF(0, 0, LogicalSize.Width, LogicalSize.Height));

    protected virtual void PaintOverlay(Graphics g) { }

    Widget? HitTest(PointF p) => ActiveWidgets.Reverse().FirstOrDefault(w => w.Visible && w.Contains(p));

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        GotActive?.Invoke(this);
        var p = ToLogical(e.Location);
        var w = HitTest(p);
        if (w != null)
        {
            capture = w;
            w.Down = true;
            w.Hover = true;
            w.Invalidate();
            w.OnMouseDown(p, e);
        }
        else
        {
            bgCapture = true;
            OnBackgroundDown(p, e);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = ToLogical(e.Location);
        if (capture != null)
        {
            bool h = capture.Contains(p);
            if (h != capture.Hover) { capture.Hover = h; capture.Invalidate(); }
            capture.OnMouseMove(p, e);
            return;
        }
        if (bgCapture) { OnBackgroundMove(p, e); return; }

        var w = HitTest(p);
        if (w != hover)
        {
            if (hover != null) { hover.Hover = false; hover.Invalidate(); }
            hover = w;
            if (w != null) { w.Hover = true; w.Invalidate(); }
            tip.SetToolTip(this, w?.Tooltip);
        }
        Cursor = w?.Cursor ?? BackgroundCursor(p) ?? Cursors.Default;
        if (w != null) w.OnMouseMove(p, e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var p = ToLogical(e.Location);
        if (capture != null)
        {
            var c = capture;
            capture = null;
            c.OnMouseUp(p, e);
            c.Down = false;
            c.Hover = c.Contains(p);
            c.Invalidate();
        }
        else if (bgCapture)
        {
            bgCapture = false;
            OnBackgroundUp(p, e);
        }
    }

    // Capture is lost when a window drag starts (the move loop eats the mouse-up), a menu opens,
    // or the user Alt+Tabs mid-drag: end any in-progress interaction.
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (Capture) return;
        bgCapture = false;
        if (capture == null) return;
        var c = capture;
        capture = null;
        var p = ToLogical(PointToClient(MousePosition));
        c.OnMouseUp(p, new MouseEventArgs(MouseButtons.None, 0, 0, 0, 0));
        c.Down = false;
        c.Hover = false;
        c.Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hover != null && capture == null)
        {
            hover.Hover = false;
            hover.Invalidate();
            hover = null;
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        var p = ToLogical(e.Location);
        HitTest(p)?.OnDoubleClick(p);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var p = ToLogical(e.Location);
        var w = HitTest(p);
        if (w != null) w.OnWheel(e.Delta);
        else OnBackgroundWheel(p, e.Delta);
    }

    protected virtual void OnBackgroundDown(PointF p, MouseEventArgs e) { }
    protected virtual void OnBackgroundMove(PointF p, MouseEventArgs e) { }
    protected virtual void OnBackgroundUp(PointF p, MouseEventArgs e) { }
    protected virtual void OnBackgroundWheel(PointF p, int delta) { }
    protected virtual Cursor? BackgroundCursor(PointF p) => null;

    protected override void Dispose(bool disposing)
    {
        if (disposing) tip.Dispose();
        base.Dispose(disposing);
    }
}
