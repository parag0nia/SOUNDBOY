using SOUNDBOY.Audio;

namespace SOUNDBOY.UI;

/// <summary>Figma frame: "SOUNDBOY / Main window" › Equalizer.</summary>
sealed class EqPanel : SkinPanel
{
    public const float W = 560, H = 228;
    static readonly RectangleF Card = new(12, 40, 536, 180);
    const float TrackTop = 60, TrackH = 128, LabelY = 196;
    const float PreampX = 42, FirstBandX = 116, BandStep = 44;

    readonly MainForm app;
    readonly Slider preamp;
    readonly Slider[] bands = new Slider[EqSettings.BandCount];
    DateTime lastBgClick;

    public EqPanel(MainForm app)
    {
        this.app = app;
        var eq = app.Engine.Eq;

        Add(new Switch(100, 10, 34, 20, "Turn equalizer on/off") { IsOn = () => eq.Enabled, Click = app.ToggleEqEnabled });
        Add(new PushButton(358, 8, 60, 24, "Reset all sliders to 0 dB") { Style = BtnStyle.Pill, Text = "Reset", Click = () => app.ApplyEq(new float[EqSettings.BandCount], 0) });
        var presets = Add(new PushButton(424, 8, 90, 24, "Load, save, import and export presets") { Style = BtnStyle.Pill, Text = "Presets ▾", Emphasis = true });
        presets.Click = () => app.ShowPresetMenu(this, new PointF(presets.Bounds.X, presets.Bounds.Bottom + 4));
        Add(new PushButton(522, 9, 22, 22, "Hide equalizer (Alt+G)") { Style = BtnStyle.Raised, Icon = Skin.Icon.Close, IconSize = 14, Click = app.ToggleEq });

        preamp = Add(NewSlider(PreampX, "Preamp (double-click: 0 dB)"));
        preamp.Changing += v => app.SetPreamp(ToDb(v));

        for (int i = 0; i < EqSettings.BandCount; i++)
        {
            int band = i;
            bands[i] = Add(NewSlider(FirstBandX + i * BandStep, $"{EqSettings.Labels[i]}Hz (double-click: 0 dB)"));
            bands[i].Changing += v => app.SetBand(band, ToDb(v));
        }
    }

    static Slider NewSlider(float cx, string tip) => new()
    {
        Vertical = true,
        Style = SliderStyle.Eq,
        Bounds = new RectangleF(cx - 10, TrackTop, 20, TrackH),
        ResetValue = 0.5f,
        WheelStep = 1 / 24f,
        Tooltip = tip,
        HitPad = 6,
    };

    static float ToDb(float v) => (float)Math.Round((v * 2 - 1) * EqSettings.MaxDb, 1);
    static float ToValue(float db) => db / (2 * EqSettings.MaxDb) + 0.5f;

    public void SyncFromEq()
    {
        var eq = app.Engine.Eq;
        preamp.Value = ToValue(eq.Preamp);
        for (int i = 0; i < EqSettings.BandCount; i++) bands[i].Value = ToValue(eq[i]);
        Invalidate();
    }

    public void InvalidateGraph() => InvalidateLogical(Card);

    protected override void PaintBackground(Graphics g)
    {
        base.PaintBackground(g);
        var eq = app.Engine.Eq;
        Skin.Label(g, "Equalizer", Skin.Heading, Skin.Text, new RectangleF(16, 10, 90, 20), Skin.Left);
        Skin.Round(g, Skin.Surface, Card, 12);

        float cy = TrackTop + TrackH / 2;
        Skin.Label(g, "+12", Skin.Chip, Skin.Dim, new RectangleF(52, TrackTop - 6, 28, 12), Skin.Right);
        Skin.Label(g, "0", Skin.Chip, Skin.Dim, new RectangleF(52, cy - 6, 28, 12), Skin.Right);
        Skin.Label(g, "−12", Skin.Chip, Skin.Dim, new RectangleF(52, TrackTop + TrackH - 6, 28, 12), Skin.Right);
        Skin.Fill(g, Skin.Raised, new RectangleF(88, TrackTop, 1, TrackH));

        // Response curve behind the band thumbs (Figma: ResponseCurve).
        var pts = new PointF[EqSettings.BandCount];
        for (int i = 0; i < pts.Length; i++)
            pts[i] = new PointF(FirstBandX + i * BandStep, cy - eq[i] / EqSettings.MaxDb * (TrackH / 2));
        using (new Skin.Aa(g))
        using (var pen = new Pen(Skin.Alpha(Skin.Accent, eq.Enabled ? 90 : 35), 2f))
            g.DrawCurve(pen, pts, 0.5f);

        Skin.Label(g, "PRE", Skin.Chip, Skin.Muted, new RectangleF(PreampX - 22, LabelY, 44, 14), Skin.Center);
        for (int i = 0; i < EqSettings.BandCount; i++)
            Skin.Label(g, EqSettings.Labels[i], Skin.Chip, Skin.Muted, new RectangleF(FirstBandX + i * BandStep - 22, LabelY, 44, 14), Skin.Center);

        if (!eq.Enabled)
            Skin.Label(g, "Off", Skin.Small, Skin.Dim, new RectangleF(140, 10, 40, 20), Skin.Left);
    }

    protected override void OnBackgroundDown(PointF p, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) { app.ShowPresetMenu(this, p); return; }
        if (e.Button != MouseButtons.Left) return;
        var now = DateTime.UtcNow;
        bool dbl = (now - lastBgClick).TotalMilliseconds < SystemInformation.DoubleClickTime;
        lastBgClick = now;
        if (dbl && p.Y < 40) { app.ToggleEqEnabled(); return; }
        app.BeginWindowDrag();
    }
}
