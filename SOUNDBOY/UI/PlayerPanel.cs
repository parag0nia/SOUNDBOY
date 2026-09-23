using System.Drawing.Drawing2D;
using SOUNDBOY.Audio;

namespace SOUNDBOY.UI;

static class Fmt
{
    public static string Time(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return Clock(t);
    }

    /// <summary>For track lengths: a 0.9 s clip reads "0:01", not "0:00".</summary>
    public static string Length(TimeSpan t) => Clock(t > TimeSpan.Zero && t < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : t);

    static string Clock(TimeSpan t)
    {
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}";
    }
}

/// <summary>Figma frames: "SOUNDBOY / Main window" › Player, and "SOUNDBOY / Windowshade".</summary>
sealed class PlayerPanel : SkinPanel
{
    public const float W = 560, FullH = 252, ShadeH = 44;
    const int BarCount = 44;
    const string Sep = "     •     ";

    static readonly RectangleF Card = new(12, 44, 536, 128);
    static readonly RectangleF Art = new(24, 56, 104, 104);
    static readonly RectangleF TitleRect = new(144, 56, 392, 26);
    static readonly RectangleF SubRect = new(144, 84, 392, 18);
    const float ChipY = 108;
    static readonly RectangleF VisRect = new(144, 132, 392, 28);
    static readonly RectangleF ElapsedRect = new(16, 179, 46, 18);
    static readonly RectangleF RemainRect = new(498, 179, 46, 18);
    static readonly RectangleF ShadeTitle = new(44, 13, 248, 18);
    static readonly RectangleF ShadeTime = new(296, 13, 52, 18);

    readonly MainForm app;
    readonly List<Widget> full = new(), shade = new();
    readonly Slider seek, volume, balance, miniSeek;
    readonly Analyzer analyzer = new(BarCount);
    Image? artwork;

    string marqueeText = "";
    float marqueeW, marqueeX, marqueeHold;
    Font marqueeFont = Skin.TitleFont;
    string? transient;
    double transientLeft, blink;
    DateTime lastBgClick;

    public bool Shaded { get; private set; }
    protected override IEnumerable<Widget> ActiveWidgets => Shaded ? shade : full;

    public PlayerPanel(MainForm app)
    {
        this.app = app;

        // --- title bar ---
        Add(new PushButton(360, 9, 34, 22, "Equalizer (Alt+G)") { Style = BtnStyle.Pill, Text = "EQ", Font = Skin.Chip, Active = () => app.Settings.ShowEq, Click = app.ToggleEq }, full);
        Add(new PushButton(398, 9, 34, 22, "Playlist (Alt+E)") { Style = BtnStyle.Pill, Text = "PL", Font = Skin.Chip, Active = () => app.Settings.ShowPlaylist, Click = app.TogglePlaylist }, full);
        Add(TitleButton(444, 9, Skin.Icon.Menu, "Menu", () => app.ShowMainMenu(this, new PointF(444, 33))), full);
        Add(TitleButton(470, 9, Skin.Icon.Minimize, "Minimize", app.MinimizeApp), full);
        Add(TitleButton(496, 9, Skin.Icon.Shade, "Compact mode (Ctrl+W)", app.ToggleShade), full);
        Add(TitleButton(522, 9, Skin.Icon.Close, "Close", app.Close), full);

        // --- progress ---
        seek = Add(new Slider { Bounds = new(64, 180, 432, 16), Style = SliderStyle.Seek, Tooltip = "Seek (Left/Right: 5 s)" }, full);
        seek.Changing += SeekPreview;
        seek.Committed += app.SeekTo;

        // --- controls ---
        Add(new PushButton(16, 210, 32, 32, "Shuffle (S)") { Icon = Skin.Icon.Shuffle, Active = () => app.Settings.Shuffle, Click = app.ToggleShuffle }, full);
        Add(new PushButton(52, 210, 32, 32, "Repeat: off / all / one (R)")
        {
            IconFunc = () => app.Settings.Repeat == RepeatMode.One ? Skin.Icon.RepeatOne : Skin.Icon.Repeat,
            Active = () => app.Settings.Repeat != RepeatMode.Off,
            Click = app.CycleRepeat,
        }, full);
        Add(new PushButton(172, 210, 32, 32, "Stop (V)") { Icon = Skin.Icon.Stop, Click = app.Stop }, full);
        Add(new PushButton(212, 208, 36, 36, "Previous (Z)") { Icon = Skin.Icon.Prev, IconSize = 20, Emphasis = true, Click = app.Prev }, full);
        Add(new PushButton(256, 202, 48, 48, "Play / Pause (X, C, Space)")
        {
            Style = BtnStyle.Primary,
            IconSize = 22,
            IconFunc = () => app.Engine.State == PlayerState.Playing ? Skin.Icon.Pause : Skin.Icon.Play,
            Click = app.PlayPause,
        }, full);
        Add(new PushButton(312, 208, 36, 36, "Next (B)") { Icon = Skin.Icon.Next, IconSize = 20, Emphasis = true, Click = app.Next }, full);
        Add(new PushButton(356, 210, 32, 32, "Open file(s) (L)") { Icon = Skin.Icon.Eject, Click = app.OpenFilesPlay }, full);
        Add(new PushButton(408, 202, 28, 24, "Mute")
        {
            IconSize = 16,
            IconFunc = () => app.Settings.Volume <= 0.001f ? Skin.Icon.Mute : Skin.Icon.Volume,
            Click = app.ToggleMute,
        }, full);

        volume = Add(new Slider { Bounds = new(440, 206, 104, 16), Style = SliderStyle.Volume, Tooltip = "Volume (Up/Down, mouse wheel)", WheelStep = 0.03f }, full);
        volume.Changing += v => { app.SetVolume(v); ShowTransient($"Volume {v * 100:0}%"); };

        balance = Add(new Slider { Bounds = new(440, 224, 104, 16), Style = SliderStyle.Balance, Tooltip = "Balance (double-click to center)", ResetValue = 0.5f, WheelStep = 0.05f }, full);
        balance.Changing += v =>
        {
            float b = v * 2 - 1;
            if (Math.Abs(b) < 0.07f) { b = 0; balance.Value = 0.5f; }
            app.SetBalance(b);
            ShowTransient(b == 0 ? "Balance: center" : $"Balance: {Math.Abs(b) * 100:0}% {(b < 0 ? "left" : "right")}");
        };

        // --- windowshade ---
        Add(new PushButton(356, 11, 22, 22, "Previous (Z)") { Icon = Skin.Icon.Prev, IconSize = 14, Emphasis = true, Click = app.Prev }, shade);
        Add(new PushButton(382, 10, 24, 24, "Play / Pause")
        {
            Style = BtnStyle.Primary,
            IconSize = 12,
            IconFunc = () => app.Engine.State == PlayerState.Playing ? Skin.Icon.Pause : Skin.Icon.Play,
            Click = app.PlayPause,
        }, shade);
        Add(new PushButton(410, 11, 22, 22, "Next (B)") { Icon = Skin.Icon.Next, IconSize = 14, Emphasis = true, Click = app.Next }, shade);
        miniSeek = Add(new Slider { Bounds = new(440, 14, 48, 16), Style = SliderStyle.Mini, Tooltip = "Seek" }, shade);
        miniSeek.Changing += SeekPreview;
        miniSeek.Committed += app.SeekTo;
        Add(TitleButton(496, 11, Skin.Icon.Shade, "Full mode (Ctrl+W)", app.ToggleShade), shade);
        Add(TitleButton(522, 11, Skin.Icon.Close, "Close", app.Close), shade);
    }

    static PushButton TitleButton(float x, float y, Skin.Icon icon, string tip, Action click) =>
        new(x, y, 22, 22, tip) { Style = BtnStyle.Raised, Icon = icon, IconSize = 14, Click = click };

    void SeekPreview(float v)
    {
        var d = app.Engine.Duration;
        ShowTransient($"Seek to {Fmt.Time(d * v)} / {Fmt.Time(d)}");
    }

    public void SetShaded(bool on)
    {
        Shaded = on;
        marqueeText = "";
        Invalidate();
    }

    public void SetArtwork(Image? img)
    {
        artwork?.Dispose();
        artwork = img;
        InvalidateLogical(Art);
    }

    public void SyncFromSettings()
    {
        volume.Value = app.Settings.Volume;
        balance.Value = app.Settings.Balance / 2 + 0.5f;
        Invalidate();
    }

    public void ShowTransient(string text, double seconds = 1.4)
    {
        transient = text;
        transientLeft = seconds;
    }

    public void ResetMarquee()
    {
        marqueeX = 0;
        marqueeHold = 2f;
    }

    public void Tick(double dt)
    {
        var eng = app.Engine;
        if (!Shaded && app.Settings.Vis != VisMode.Off)
            analyzer.Update(eng.Tap, AudioEngine.OutputLatencyMs, eng.State == PlayerState.Playing, (float)dt);

        var dur = eng.Duration;
        bool canSeek = eng.CanSeek && eng.State != PlayerState.Stopped;
        float frac = dur > TimeSpan.Zero ? (float)Math.Clamp(eng.Position / dur, 0, 1) : 0;
        seek.Enabled = miniSeek.Enabled = canSeek;
        if (!seek.Dragging) seek.Value = frac;
        if (!miniSeek.Dragging) miniSeek.Value = frac;

        if (transient != null && (transientLeft -= dt) <= 0) transient = null;

        var text = app.NowPlayingTitle;
        var font = Shaded ? Skin.BodyStrong : Skin.TitleFont;
        if (text != marqueeText || font != marqueeFont)
        {
            marqueeText = text;
            marqueeFont = font;
            marqueeW = Skin.MeasureWidth(text + Sep, font);
            ResetMarquee();
        }
        if (marqueeHold > 0) marqueeHold -= (float)dt;
        else
        {
            marqueeX += (float)dt * 32;
            if (marqueeX > marqueeW) { marqueeX -= marqueeW; marqueeHold = 2f; }
        }
        blink += dt;

        if (Shaded)
        {
            Invalidate();
        }
        else
        {
            InvalidateLogical(Card);
            InvalidateLogical(new RectangleF(0, 174, W, 78));
        }
    }

    // ------------------------------------------------------------------ painting

    protected override void PaintBackground(Graphics g)
    {
        base.PaintBackground(g);
        if (Shaded) { PaintShaded(g); return; }

        Skin.Logo(g, new RectangleF(14, 7, 26, 26));
        Skin.Label(g, "SOUNDBOY", Skin.AppName, app.IsActive ? Skin.Text : Skin.Muted, new RectangleF(44, 10, 150, 20), Skin.Left);

        Skin.Round(g, Skin.Surface, Card, 12);
        if (artwork != null)
        {
            using var clip = Skin.RoundPath(Art, 8);
            var st = g.Save();
            g.SetClip(clip, CombineMode.Intersect);
            var im = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(artwork, Cover(artwork.Size, Art));
            g.InterpolationMode = im;
            g.Restore(st);
        }
        else
        {
            Skin.DefaultArtwork(g, Art, 8);
        }

        DrawMarquee(g, TitleRect, Skin.Text);
        if (transient != null) Skin.Label(g, transient, Skin.Body, Skin.Accent, SubRect, Skin.Left);
        else Skin.Label(g, app.NowPlayingSubtitle, Skin.Body, Skin.Muted, SubRect, Skin.Left);
        DrawChips(g);
        DrawVis(g);
        DrawTimes(g);
        Skin.Label(g, "BAL", Skin.Tiny, Skin.Dim, new RectangleF(408, 226, 28, 12), Skin.Center);
    }

    static RectangleF Cover(Size img, RectangleF box)
    {
        float s = Math.Max(box.Width / img.Width, box.Height / img.Height);
        float w = img.Width * s, h = img.Height * s;
        return new RectangleF(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
    }

    void PaintShaded(Graphics g)
    {
        Skin.Logo(g, new RectangleF(14, 9, 26, 26));
        DrawMarquee(g, ShadeTitle, Skin.Text);
        var eng = app.Engine;
        if (eng.State != PlayerState.Stopped && (eng.State != PlayerState.Paused || blink % 1.0 < 0.5))
            Skin.Label(g, Fmt.Time(eng.Position), Skin.Mono, Skin.Accent, ShadeTime, Skin.Right);
    }

    void DrawChips(Graphics g)
    {
        float x = TitleRect.X;
        foreach (var (chip, musical) in app.NowPlayingChips)
        {
            float w = Skin.MeasureWidth(chip, Skin.Chip) + 18;
            if (x + w > TitleRect.Right) break; // never spill outside the card
            var r = new RectangleF(x, ChipY, w, 18);
            // Figma: Chip/* (format) and Chip/BPM, Chip/Key (musical: accent-tinted)
            Skin.Round(g, musical ? Skin.Alpha(Skin.Accent, 28) : Skin.Raised, r, 9);
            Skin.Label(g, chip, Skin.Chip, musical ? Skin.Accent : Skin.Muted, r, Skin.Center);
            x += w + 4;
        }
    }

    void DrawTimes(Graphics g)
    {
        var eng = app.Engine;
        bool stopped = eng.State == PlayerState.Stopped;
        var pos = stopped ? TimeSpan.Zero : eng.Position;
        var dur = eng.Duration;
        bool visible = eng.State != PlayerState.Paused || blink % 1.0 < 0.5;
        if (visible) Skin.Label(g, Fmt.Time(pos), Skin.Mono, stopped ? Skin.Muted : Skin.Text, ElapsedRect, Skin.Left);
        string right = dur <= TimeSpan.Zero ? "--:--" : app.Settings.RemainingTime ? "-" + Fmt.Time(dur - pos) : Fmt.Length(dur);
        Skin.Label(g, right, Skin.Mono, Skin.Muted, RemainRect, Skin.Right);
    }

    void DrawVis(Graphics g)
    {
        var r = VisRect;
        switch (app.Settings.Vis)
        {
            case VisMode.Spectrum:
            {
                const float bw = 6, gap = 3;
                using var lg = new LinearGradientBrush(r, Skin.VisHigh, Skin.Accent, 90f)
                {
                    InterpolationColors = new ColorBlend
                    {
                        Colors = new[] { Skin.VisHigh, Skin.VisMid, Skin.Accent, Skin.Accent },
                        Positions = new[] { 0f, 0.3f, 0.7f, 1f },
                    },
                };
                for (int i = 0; i < BarCount; i++)
                {
                    float x = r.X + i * (bw + gap);
                    float h = analyzer.Bars[i] * r.Height;
                    if (h < 3) Skin.Round(g, Skin.Raised, new RectangleF(x, r.Bottom - 3, bw, 3), 1); // resting bar
                    else Skin.Round(g, lg, new RectangleF(x, r.Bottom - h, bw, h), 1);
                    if (app.Settings.ShowPeaks && analyzer.Peaks[i] > 0.04f)
                    {
                        float py = Math.Max(r.Top, r.Bottom - analyzer.Peaks[i] * r.Height - 3);
                        Skin.Fill(g, Skin.Alpha(Skin.Text, 170), new RectangleF(x, py, bw, 1.5f));
                    }
                }
                break;
            }
            case VisMode.Oscilloscope:
            {
                var s = analyzer.Scope;
                var pts = new PointF[(int)r.Width / 2];
                for (int i = 0; i < pts.Length; i++)
                {
                    float v = s[i * s.Length / pts.Length];
                    pts[i] = new PointF(r.X + i * 2, r.Y + r.Height / 2 - Math.Clamp(v, -1, 1) * (r.Height / 2 - 1));
                }
                using var aa = new Skin.Aa(g);
                using var pen = new Pen(Skin.Accent, 1.5f);
                g.DrawLines(pen, pts);
                break;
            }
            default:
                Skin.Fill(g, Skin.Raised, new RectangleF(r.X, r.Bottom - 2, r.Width, 2));
                break;
        }
    }

    void DrawMarquee(Graphics g, RectangleF r, Color color)
    {
        var state = g.Save();
        g.IntersectClip(r);
        using var b = new SolidBrush(color);
        float ty = r.Y + (r.Height - marqueeFont.GetHeight()) / 2;
        bool fits = marqueeW - Skin.MeasureWidth(Sep, marqueeFont) <= r.Width;
        if (fits)
        {
            g.DrawString(marqueeText, marqueeFont, b, r.X, ty, Skin.Typo);
        }
        else
        {
            g.DrawString(marqueeText + Sep, marqueeFont, b, r.X - marqueeX, ty, Skin.Typo);
            g.DrawString(marqueeText + Sep, marqueeFont, b, r.X - marqueeX + marqueeW, ty, Skin.Typo);
        }
        g.Restore(state);
    }

    // ------------------------------------------------------------------ input

    protected override void OnBackgroundDown(PointF p, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) { app.ShowMainMenu(this, p); return; }
        if (e.Button != MouseButtons.Left) return;

        if (Shaded ? ShadeTime.Contains(p) : ElapsedRect.Contains(p) || RemainRect.Contains(p)) { app.ToggleTimeMode(); return; }
        if (!Shaded && VisRect.Contains(p)) { app.CycleVis(); return; }
        if (!Shaded && Art.Contains(p)) { app.ShowFileInfo(); return; }

        var now = DateTime.UtcNow;
        bool dbl = (now - lastBgClick).TotalMilliseconds < SystemInformation.DoubleClickTime;
        lastBgClick = now;
        if (dbl && (Shaded || p.Y < 40)) { lastBgClick = default; app.ToggleShade(); return; }
        app.BeginWindowDrag();
    }

    protected override Cursor? BackgroundCursor(PointF p) =>
        !Shaded && (VisRect.Contains(p) || Art.Contains(p) || ElapsedRect.Contains(p) || RemainRect.Contains(p)) ? Cursors.Hand : null;

    protected override void OnBackgroundWheel(PointF p, int delta) => app.VolumeBy(Math.Sign(delta) * 0.03f);

    protected override void Dispose(bool disposing)
    {
        if (disposing) artwork?.Dispose();
        base.Dispose(disposing);
    }
}
