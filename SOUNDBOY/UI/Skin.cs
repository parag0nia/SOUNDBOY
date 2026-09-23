using System.Drawing.Drawing2D;

namespace SOUNDBOY.UI;

/// <summary>
/// SOUNDBOY design system. Colors mirror the "SOUNDBOY Colors" variables in the Figma file
/// (SOUNDBOY — Player UI); all geometry is in logical pixels matching the Figma frames 1:1.
/// </summary>
static class Skin
{
    // --- tokens (Figma: SOUNDBOY Colors / Dark) ---
    public static readonly Color Window = Hex("#0F0F14");     // bg/window
    public static readonly Color Surface = Hex("#17171F");    // bg/surface
    public static readonly Color Raised = Hex("#22222D");     // bg/raised
    public static readonly Color Selected = Hex("#2C2C3C");   // bg/selected
    public static readonly Color Border = Hex("#2A2A38");     // border/subtle
    public static readonly Color Text = Hex("#F4F4F8");       // text/primary
    public static readonly Color Muted = Hex("#8B8BA3");      // text/muted
    public static readonly Color Dim = Hex("#55556B");        // text/dim
    public static readonly Color Accent = Hex("#C8FF3D");     // accent/primary
    public static readonly Color Ink = Hex("#0F0F14");        // accent/ink
    public static readonly Color VisMid = Hex("#FFD23D");     // vis/mid
    public static readonly Color VisHigh = Hex("#FF6B3D");    // vis/high

    public static readonly Font AppName = new("Segoe UI", 14f, FontStyle.Bold, GraphicsUnit.Pixel);
    public static readonly Font Heading = new("Segoe UI Semibold", 13f, FontStyle.Regular, GraphicsUnit.Pixel);
    public static readonly Font TitleFont = new("Segoe UI Semibold", 18f, FontStyle.Regular, GraphicsUnit.Pixel);
    public static readonly Font Body = new("Segoe UI", 13f, FontStyle.Regular, GraphicsUnit.Pixel);
    public static readonly Font BodyStrong = new("Segoe UI Semibold", 13f, FontStyle.Regular, GraphicsUnit.Pixel);
    public static readonly Font Small = new("Segoe UI", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
    public static readonly Font Button = new("Segoe UI Semibold", 11f, FontStyle.Regular, GraphicsUnit.Pixel);
    public static readonly Font Chip = new("Segoe UI", 10f, FontStyle.Bold, GraphicsUnit.Pixel);
    public static readonly Font Tiny = new("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Pixel);
    public static readonly Font Mono = new("Consolas", 12.5f, FontStyle.Regular, GraphicsUnit.Pixel);

    public static readonly StringFormat Center = Fmt(StringAlignment.Center);
    public static readonly StringFormat Left = Fmt(StringAlignment.Near);
    public static readonly StringFormat Right = Fmt(StringAlignment.Far);
    public static readonly StringFormat Typo = CreateTypo();

    static StringFormat Fmt(StringAlignment a) => new()
    {
        Alignment = a,
        LineAlignment = StringAlignment.Center,
        FormatFlags = StringFormatFlags.NoWrap,
        Trimming = StringTrimming.EllipsisCharacter,
    };

    static StringFormat CreateTypo()
    {
        var f = (StringFormat)StringFormat.GenericTypographic.Clone();
        f.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
        return f;
    }

    static Color Hex(string h) => Color.FromArgb(Convert.ToInt32(h[1..3], 16), Convert.ToInt32(h[3..5], 16), Convert.ToInt32(h[5..7], 16));

    static readonly Graphics MeasureG = Graphics.FromImage(new Bitmap(1, 1));

    public static float MeasureWidth(string s, Font f)
    {
        lock (MeasureG) return MeasureG.MeasureString(s, f, PointF.Empty, Typo).Width;
    }

    public static Color Lighten(Color c, int d) => Color.FromArgb(c.A, Cl(c.R + d), Cl(c.G + d), Cl(c.B + d));
    public static Color Alpha(Color c, int a) => Color.FromArgb(a, c);
    static int Cl(int v) => Math.Clamp(v, 0, 255);

    // --- shapes ---

    public sealed class Aa : IDisposable
    {
        readonly Graphics g;
        readonly SmoothingMode old;
        public Aa(Graphics g) { this.g = g; old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias; }
        public void Dispose() => g.SmoothingMode = old;
    }

    public static GraphicsPath RoundPath(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void Round(Graphics g, Color c, RectangleF r, float radius)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        using var aa = new Aa(g);
        using var b = new SolidBrush(c);
        using var p = RoundPath(r, radius);
        g.FillPath(b, p);
    }

    public static void Round(Graphics g, Brush b, RectangleF r, float radius)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        using var aa = new Aa(g);
        using var p = RoundPath(r, radius);
        g.FillPath(b, p);
    }

    public static void Circle(Graphics g, Color c, float cx, float cy, float d)
    {
        using var aa = new Aa(g);
        using var b = new SolidBrush(c);
        g.FillEllipse(b, cx - d / 2, cy - d / 2, d, d);
    }

    public static void Fill(Graphics g, Color c, RectangleF r)
    {
        using var b = new SolidBrush(c);
        g.FillRectangle(b, r);
    }

    public static void Label(Graphics g, string s, Font f, Color c, RectangleF r, StringFormat fmt)
    {
        using var b = new SolidBrush(c);
        g.DrawString(s, f, b, r, fmt);
    }

    static readonly Image? LogoImage = LoadLogo();

    static Image? LoadLogo()
    {
        try
        {
            using var s = typeof(Skin).Assembly.GetManifestResourceStream("SOUNDBOY.logo.png");
            return s == null ? null : new Bitmap(Image.FromStream(s));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The SOUNDBOY mark (Assets/logo.png), scaled to fit and centered in <paramref name="r"/> (Figma: LogoMark).</summary>
    public static void Logo(Graphics g, RectangleF r)
    {
        if (LogoImage == null) { Circle(g, Muted, r.X + r.Width / 2, r.Y + r.Height / 2, Math.Min(r.Width, r.Height)); return; }
        float s = Math.Min(r.Width / LogoImage.Width, r.Height / LogoImage.Height);
        float w = LogoImage.Width * s, h = LogoImage.Height * s;
        var old = g.InterpolationMode;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(LogoImage, r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h);
        g.InterpolationMode = old;
    }

    /// <summary>Default artwork when a track has no embedded cover (Figma: Artwork).</summary>
    public static void DefaultArtwork(Graphics g, RectangleF r, float radius)
    {
        using (var lg = new LinearGradientBrush(r, Lighten(Raised, 14), Window, 45f))
            Round(g, lg, r, radius);
        Logo(g, RectangleF.Inflate(r, -r.Width * 0.16f, -r.Height * 0.16f));
    }

    // --- icons: 24x24 artboards, mirroring the SVGs in the Figma file ---

    public enum Icon { Play, Pause, Stop, Prev, Next, Eject, Shuffle, Repeat, RepeatOne, Volume, Mute, Menu, Minimize, Shade, Close, Search }

    public static void DrawIcon(Graphics g, Icon icon, RectangleF r, Color c)
    {
        using var aa = new Aa(g);
        var st = g.Save();
        g.TranslateTransform(r.X, r.Y);
        g.ScaleTransform(r.Width / 24f, r.Height / 24f);
        using var b = new SolidBrush(c);
        using var pen = new Pen(c, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        switch (icon)
        {
            case Icon.Play: g.FillPolygon(b, Pts(8, 5, 19, 12, 8, 19)); break;
            case Icon.Pause: g.FillRectangle(b, 6, 5, 4, 14); g.FillRectangle(b, 14, 5, 4, 14); break;
            case Icon.Stop: using (var p = RoundPath(new RectangleF(6, 6, 12, 12), 1.5f)) g.FillPath(b, p); break;
            case Icon.Prev: g.FillRectangle(b, 6, 5, 2, 14); g.FillPolygon(b, Pts(20, 5, 9, 12, 20, 19)); break;
            case Icon.Next: g.FillRectangle(b, 16, 5, 2, 14); g.FillPolygon(b, Pts(4, 5, 15, 12, 4, 19)); break;
            case Icon.Eject: g.FillPolygon(b, Pts(12, 5, 19, 14, 5, 14)); g.FillRectangle(b, 5, 16, 14, 3); break;
            case Icon.Shuffle:
                using (var p = new GraphicsPath())
                {
                    p.AddLine(4, 7, 7, 7); p.AddBezier(7, 7, 11, 7, 11, 17, 15, 17); p.AddLine(15, 17, 20, 17);
                    p.StartFigure();
                    p.AddLine(4, 17, 7, 17); p.AddBezier(7, 17, 11, 17, 11, 7, 15, 7); p.AddLine(15, 7, 20, 7);
                    g.DrawPath(pen, p);
                }
                g.DrawLines(pen, Pts(17.5f, 4.5f, 20, 7, 17.5f, 9.5f));
                g.DrawLines(pen, Pts(17.5f, 14.5f, 20, 17, 17.5f, 19.5f));
                break;
            case Icon.Repeat:
            case Icon.RepeatOne:
                g.DrawLines(pen, Pts(17, 3, 20, 6, 17, 9));
                using (var p = new GraphicsPath())
                {
                    p.AddLine(4, 11, 4, 9); p.AddArc(4, 6, 6, 6, 180, 90); p.AddLine(7, 6, 20, 6);
                    g.DrawPath(pen, p);
                }
                g.DrawLines(pen, Pts(7, 21, 4, 18, 7, 15));
                using (var p = new GraphicsPath())
                {
                    p.AddLine(20, 13, 20, 15); p.AddArc(14, 12, 6, 6, 0, 90); p.AddLine(17, 18, 4, 18);
                    g.DrawPath(pen, p);
                }
                if (icon == Icon.RepeatOne)
                {
                    using var f = new Font("Segoe UI", 7.5f, FontStyle.Bold, GraphicsUnit.Pixel);
                    g.DrawString("1", f, b, new RectangleF(7, 6.5f, 10, 11), Center);
                }
                break;
            case Icon.Volume:
            case Icon.Mute:
                g.FillPolygon(b, Pts(4, 9, 8, 9, 13, 5, 13, 19, 8, 15, 4, 15));
                if (icon == Icon.Volume)
                {
                    g.DrawArc(pen, 12.5f - 5, 12 - 5, 10, 10, -45, 90);
                    g.DrawArc(pen, 12.5f - 8.5f, 12 - 8.5f, 17, 17, -45, 90);
                }
                else
                {
                    g.DrawLine(pen, 16, 9.5f, 21, 14.5f);
                    g.DrawLine(pen, 21, 9.5f, 16, 14.5f);
                }
                break;
            case Icon.Menu: g.DrawLine(pen, 5, 7, 19, 7); g.DrawLine(pen, 5, 12, 19, 12); g.DrawLine(pen, 5, 17, 19, 17); break;
            case Icon.Minimize: g.DrawLine(pen, 6, 12, 18, 12); break;
            case Icon.Shade:
                using (var p = RoundPath(new RectangleF(5, 6, 14, 12), 2)) g.DrawPath(pen, p);
                g.DrawLine(pen, 5, 10.5f, 19, 10.5f);
                break;
            case Icon.Close: g.DrawLine(pen, 7, 7, 17, 17); g.DrawLine(pen, 17, 7, 7, 17); break;
            case Icon.Search: g.DrawEllipse(pen, 5, 5, 12, 12); g.DrawLine(pen, 15.5f, 15.5f, 20, 20); break;
        }
        g.Restore(st);
    }

    static PointF[] Pts(params float[] v)
    {
        var p = new PointF[v.Length / 2];
        for (int i = 0; i < p.Length; i++) p[i] = new PointF(v[i * 2], v[i * 2 + 1]);
        return p;
    }

    // --- sliders ---

    public static void PaintSlider(Graphics g, Slider s)
    {
        var r = s.Bounds;
        bool hot = s.Hover || s.Dragging;
        if (s.Vertical)
        {
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            float ty = r.Y + (1 - s.Value) * r.Height;
            Round(g, Raised, new RectangleF(cx - 2, r.Y, 4, r.Height), 2);
            Round(g, Accent, RectangleF.FromLTRB(cx - 2, Math.Min(cy, ty), cx + 2, Math.Max(cy, ty)), 2);
            Round(g, hot ? Color.White : Text, new RectangleF(cx - 10, ty - 5, 20, 10), 5);
            return;
        }

        float y = r.Y + r.Height / 2;
        float tx = r.X + s.Value * r.Width;
        Round(g, Raised, new RectangleF(r.X, y - 2, r.Width, 4), 2);
        if (!s.Enabled) return;
        if (s.Style == SliderStyle.Balance)
        {
            float mid = r.X + r.Width / 2;
            Fill(g, Dim, new RectangleF(mid - 1, y - 4, 2, 8));
            Round(g, Accent, RectangleF.FromLTRB(Math.Min(mid, tx), y - 2, Math.Max(mid, tx), y + 2), 2);
        }
        else
        {
            Round(g, Accent, new RectangleF(r.X, y - 2, tx - r.X, 4), 2);
        }
        float d = s.Style == SliderStyle.Seek ? 12 : s.Style == SliderStyle.Mini ? 0 : 10;
        if (hot) d += 2;
        if (d > 0) Circle(g, s.Style == SliderStyle.Balance && !hot ? Muted : Text, tx, y, d);
    }
}
