namespace SOUNDBOY.UI;

/// <summary>A lightweight painted element inside a <see cref="SkinPanel"/>. All geometry is logical pixels.</summary>
abstract class Widget
{
    public RectangleF Bounds;
    public float HitPad;           // extra hit area around Bounds
    public bool Visible = true;
    public bool Hover, Down;
    public string? Tooltip;
    public SkinPanel? Owner;

    public abstract void Paint(Graphics g);
    public virtual void OnMouseDown(PointF p, MouseEventArgs e) { }
    public virtual void OnMouseMove(PointF p, MouseEventArgs e) { }
    public virtual void OnMouseUp(PointF p, MouseEventArgs e) { }
    public virtual void OnDoubleClick(PointF p) { }
    public virtual void OnWheel(int delta) { }
    public virtual Cursor? Cursor => null;
    public bool Contains(PointF p) => RectangleF.Inflate(Bounds, HitPad, HitPad).Contains(p);
    public void Invalidate() => Owner?.InvalidateLogical(RectangleF.Inflate(Bounds, HitPad + 4, HitPad + 4));
}

enum BtnStyle
{
    Ghost,    // icon only, background on hover (transport side buttons)
    Raised,   // rounded square on bg/raised (title bar buttons)
    Pill,     // text pill on bg/raised
    Primary,  // accent circle (play/pause)
}

sealed class PushButton : Widget
{
    public BtnStyle Style = BtnStyle.Ghost;
    public Skin.Icon? Icon;
    public Func<Skin.Icon>? IconFunc;
    public float IconSize = 18;
    public string? Text;
    public Font Font = Skin.Button;
    public Func<bool>? Active;     // accent-colored content when true
    public bool Emphasis;          // primary text color instead of muted
    public float Radius = 6;
    public Action? Click;

    public PushButton(float x, float y, float w, float h, string? tooltip = null)
    {
        Bounds = new RectangleF(x, y, w, h);
        Tooltip = tooltip;
    }

    public override void Paint(Graphics g)
    {
        var r = Bounds;
        bool pressed = Down && Hover;
        bool active = Active?.Invoke() == true;
        Color fg;

        switch (Style)
        {
            case BtnStyle.Primary:
                var bg = pressed ? Skin.Lighten(Skin.Accent, -30) : Hover ? Skin.Lighten(Skin.Accent, 20) : Skin.Accent;
                Skin.Circle(g, bg, r.X + r.Width / 2, r.Y + r.Height / 2, r.Width);
                fg = Skin.Ink;
                break;
            case BtnStyle.Raised:
            case BtnStyle.Pill:
                var fill = pressed ? Skin.Selected : Hover ? Skin.Lighten(Skin.Raised, 12) : Skin.Raised;
                Skin.Round(g, fill, r, Style == BtnStyle.Pill ? r.Height / 2 : Radius);
                fg = active ? Skin.Accent : Hover || Emphasis ? Skin.Text : Skin.Muted;
                break;
            default:
                if (Hover || pressed) Skin.Round(g, pressed ? Skin.Selected : Skin.Raised, r, Radius);
                fg = active ? Skin.Accent : Hover || Emphasis ? Skin.Text : Skin.Muted;
                break;
        }

        var icon = IconFunc?.Invoke() ?? Icon;
        if (icon is { } ic)
        {
            float s = IconSize;
            Skin.DrawIcon(g, ic, new RectangleF(r.X + (r.Width - s) / 2, r.Y + (r.Height - s) / 2, s, s), fg);
        }
        var text = Text;
        if (text != null) Skin.Label(g, text, Font, fg, r, Skin.Center);
    }

    public override void OnMouseUp(PointF p, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && Contains(p)) Click?.Invoke();
    }
}

/// <summary>On/off pill switch (Figma: EnableSwitch).</summary>
sealed class Switch : Widget
{
    public Func<bool> IsOn = () => false;
    public Action? Click;

    public Switch(float x, float y, float w, float h, string? tooltip = null)
    {
        Bounds = new RectangleF(x, y, w, h);
        Tooltip = tooltip;
    }

    public override void Paint(Graphics g)
    {
        bool on = IsOn();
        var r = Bounds;
        Skin.Round(g, on ? Skin.Accent : Skin.Raised, r, r.Height / 2);
        float d = r.Height - 4;
        float cx = on ? r.Right - 2 - d / 2 : r.X + 2 + d / 2;
        Skin.Circle(g, on ? Skin.Ink : Skin.Muted, cx, r.Y + r.Height / 2, d);
    }

    public override void OnMouseUp(PointF p, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && Contains(p)) Click?.Invoke();
    }
}

enum SliderStyle { Seek, Volume, Balance, Eq, Mini }

/// <summary>
/// Slim slider. Bounds is the track extent along the axis (the thumb center travels its full length);
/// HitPad widens the clickable area.
/// </summary>
sealed class Slider : Widget
{
    public bool Vertical;
    public SliderStyle Style;
    public float Value;              // 0..1; vertical sliders: 1 = top
    public float? ResetValue;        // double-click target
    public float WheelStep = 0.04f;
    public bool Enabled = true;
    public bool Dragging { get; private set; }
    public event Action<float>? Changing;
    public event Action<float>? Committed;

    public Slider() => HitPad = 6;

    float ValueAt(PointF p) => Vertical
        ? Math.Clamp(1 - (p.Y - Bounds.Y) / Bounds.Height, 0, 1)
        : Math.Clamp((p.X - Bounds.X) / Bounds.Width, 0, 1);

    void Set(float v)
    {
        Value = v;
        Changing?.Invoke(v);
        Invalidate();
    }

    public override void OnMouseDown(PointF p, MouseEventArgs e)
    {
        if (!Enabled || e.Button != MouseButtons.Left) return;
        Dragging = true;
        Set(ValueAt(p));
    }

    public override void OnMouseMove(PointF p, MouseEventArgs e)
    {
        if (Dragging) Set(ValueAt(p));
    }

    public override void OnMouseUp(PointF p, MouseEventArgs e)
    {
        if (!Dragging) return;
        Dragging = false;
        Committed?.Invoke(Value);
        Invalidate();
    }

    public override void OnDoubleClick(PointF p)
    {
        if (!Enabled || ResetValue is not float v) return;
        Set(v);
        Committed?.Invoke(Value);
    }

    public override void OnWheel(int delta)
    {
        if (!Enabled) return;
        Set(Math.Clamp(Value + Math.Sign(delta) * WheelStep, 0, 1));
        Committed?.Invoke(Value);
    }

    public override void Paint(Graphics g) => Skin.PaintSlider(g, this);
}
