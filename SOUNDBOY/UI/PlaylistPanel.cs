using SOUNDBOY.Audio;
using SOUNDBOY.Model;

namespace SOUNDBOY.UI;

/// <summary>Figma frame: "SOUNDBOY / Main window" › Playlist.</summary>
sealed class PlaylistPanel : SkinPanel
{
    public const float W = 560, MinH = 200;

    readonly MainForm app;
    readonly PlScrollBar scroll;
    readonly PushButton add, rem, sel, sort, lists;
    bool resizing;
    float resizeOffset;

    public TrackList List { get; }

    float H => LogicalSize.Height;
    RectangleF Card => new(12, 40, 536, H - 92);
    RectangleF Grip => new(532, H - 16, 14, 14);
    RectangleF InfoRect => new(380, H - 38, 140, 28);

    public PlaylistPanel(MainForm app)
    {
        this.app = app;
        Add(new PushButton(494, 9, 22, 22, "Jump to file (J)") { Style = BtnStyle.Raised, Icon = Skin.Icon.Search, IconSize = 14, Click = app.ShowJump });
        Add(new PushButton(522, 9, 22, 22, "Hide playlist (Alt+E)") { Style = BtnStyle.Raised, Icon = Skin.Icon.Close, IconSize = 14, Click = app.TogglePlaylist });
        List = Add(new TrackList(app));
        scroll = Add(new PlScrollBar(List));
        add = Add(new PushButton(0, 0, 60, 28, "Add files, folders or URLs") { Style = BtnStyle.Pill, Text = "+ Add", Emphasis = true });
        rem = Add(new PushButton(0, 0, 70, 28, "Remove tracks") { Style = BtnStyle.Pill, Text = "Remove" });
        sel = Add(new PushButton(0, 0, 62, 28, "Selection") { Style = BtnStyle.Pill, Text = "Select" });
        sort = Add(new PushButton(0, 0, 52, 28, "Sort, randomize, file info") { Style = BtnStyle.Pill, Text = "Sort" });
        lists = Add(new PushButton(0, 0, 54, 28, "New / open / save playlist") { Style = BtnStyle.Pill, Text = "Lists" });
        foreach (var (b, kind) in new[] { (add, "add"), (rem, "rem"), (sel, "sel"), (sort, "misc"), (lists, "list") })
            b.Click = () => app.ShowPlaylistMenu(kind, this, new PointF(b.Bounds.X, b.Bounds.Y - 4));
    }

    protected override void OnLayoutChanged()
    {
        var card = Card;
        List.Bounds = new RectangleF(card.X + 8, card.Y + 8, card.Width - 24, card.Height - 16);
        scroll.Bounds = new RectangleF(card.Right - 10, card.Y + 12, 4, card.Height - 24);
        float by = H - 38;
        add.Bounds = new RectangleF(12, by, 60, 28);
        rem.Bounds = new RectangleF(78, by, 70, 28);
        sel.Bounds = new RectangleF(154, by, 62, 28);
        sort.Bounds = new RectangleF(222, by, 52, 28);
        lists.Bounds = new RectangleF(280, by, 54, 28);
        List.ClampScroll();
    }

    public void OnPlaylistChanged()
    {
        List.ClampScroll();
        Invalidate();
    }

    public void ShowCurrent()
    {
        int i = app.Playlist.CurrentIndex;
        if (i >= 0) List.EnsureVisible(i);
        Invalidate();
    }

    public void InvalidateInfo()
    {
        InvalidateLogical(InfoRect);
        List.InvalidateCurrentRow();
    }

    public int InsertRowAt(PointF p)
    {
        if (!List.Bounds.Contains(p)) return app.Playlist.Count;
        return Math.Clamp(List.RowAt(p), 0, app.Playlist.Count);
    }

    protected override void PaintBackground(Graphics g)
    {
        base.PaintBackground(g);
        var pl = app.Playlist;
        Skin.Label(g, "Playlist", Skin.Heading, Skin.Text, new RectangleF(16, 10, 70, 20), Skin.Left);
        bool unknown = pl.Items.Any(t => t.Duration == null);
        int selCount = pl.Items.Count(t => t.Selected);
        string meta = $"{pl.Count} {(pl.Count == 1 ? "track" : "tracks")} · {Fmt.Time(pl.TotalDuration)}{(unknown && pl.Count > 0 ? "+" : "")}"
                      + (selCount > 1 ? $" · {selCount} selected" : "");
        Skin.Label(g, meta, Skin.Small, Skin.Muted, new RectangleF(78, 10, 300, 20), Skin.Left);

        Skin.Round(g, Skin.Surface, Card, 12);

        var eng = app.Engine;
        if (eng.IsLoaded && eng.State != PlayerState.Stopped)
        {
            string info = $"{Fmt.Time(eng.Position)} / {(eng.Duration > TimeSpan.Zero ? Fmt.Length(eng.Duration) : "--:--")}";
            Skin.Label(g, info, Skin.Mono, Skin.Muted, InfoRect, Skin.Right);
        }

        var gr = Grip;
        foreach (var (x, y) in new[] { (10, 2), (6, 6), (10, 6), (2, 10), (6, 10), (10, 10) })
            Skin.Round(g, Skin.Dim, new RectangleF(gr.X + x, gr.Y + y, 2, 2), 1);
    }

    protected override Cursor? BackgroundCursor(PointF p) => Grip.Contains(p) ? Cursors.SizeNS : null;

    protected override void OnBackgroundDown(PointF p, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) { app.ShowListContextMenu(this, p); return; }
        if (e.Button != MouseButtons.Left) return;
        if (RectangleF.Inflate(Grip, 3, 3).Contains(p)) { resizing = true; resizeOffset = H - p.Y; return; }
        app.BeginWindowDrag();
    }

    protected override void OnBackgroundMove(PointF p, MouseEventArgs e)
    {
        if (resizing) app.SetPlaylistHeight(p.Y + resizeOffset);
    }

    protected override void OnBackgroundUp(PointF p, MouseEventArgs e) => resizing = false;

    protected override void OnBackgroundWheel(PointF p, int delta) => List.OnWheel(delta);
}

sealed class TrackList : Widget
{
    public const float RowH = 32;
    readonly MainForm app;
    int anchor = -1, dragRow, clickRow;
    bool dragging, moved;

    public int TopRow { get; set; }
    public int FocusRow { get; private set; } = -1;

    public TrackList(MainForm app) => this.app = app;

    Playlist Pl => app.Playlist;
    public int Count => Pl.Count;
    public int PageRows => Math.Max(1, (int)(Bounds.Height / RowH));
    public int MaxTop => Math.Max(0, Pl.Count - PageRows);

    public void ClampScroll()
    {
        TopRow = Math.Clamp(TopRow, 0, MaxTop);
        if (FocusRow >= Pl.Count) FocusRow = Pl.Count - 1;
    }

    public int RowAt(PointF p) => TopRow + (int)Math.Floor((p.Y - Bounds.Y) / RowH);

    public void EnsureVisible(int row)
    {
        if (row < TopRow) TopRow = row;
        else if (row >= TopRow + PageRows) TopRow = row - PageRows + 1;
        ClampScroll();
    }

    public void InvalidateCurrentRow()
    {
        int i = Pl.CurrentIndex - TopRow;
        if (i >= 0 && i < PageRows) Owner?.InvalidateLogical(new RectangleF(Bounds.X, Bounds.Y + i * RowH, 40, RowH));
    }

    public void MoveFocus(int row, bool extend)
    {
        if (Pl.Count == 0) return;
        row = Math.Clamp(row, 0, Pl.Count - 1);
        if (extend && anchor >= 0) Pl.SelectRange(anchor, row);
        else { Pl.SelectOnly(row); anchor = row; }
        FocusRow = row;
        EnsureVisible(row);
        Owner?.Invalidate();
    }

    public void MoveSelection(int delta)
    {
        int applied = Pl.MoveSelected(delta);
        if (applied == 0) return;
        FocusRow += applied;
        anchor += applied;
        EnsureVisible(Math.Clamp(FocusRow, 0, Pl.Count - 1));
        Owner?.Invalidate();
    }

    public override void Paint(Graphics g)
    {
        var st = g.Save();
        g.IntersectClip(Bounds);
        var pl = Pl;
        var cur = pl.Current;
        int end = Math.Min(pl.Count, TopRow + PageRows + 1);
        bool focused = app.FocusPanel == Owner;
        bool playing = app.Engine.State == PlayerState.Playing;

        if (pl.Count == 0)
            Skin.Label(g, "Drop music here, or click + Add", Skin.Body, Skin.Dim, Bounds, Skin.Center);

        for (int i = TopRow; i < end; i++)
        {
            var t = pl.Items[i];
            float y = Bounds.Y + (i - TopRow) * RowH;
            var row = new RectangleF(Bounds.X, y, Bounds.Width, RowH);
            if (t.Selected) Skin.Round(g, Skin.Selected, row, 8);
            else if (focused && i == FocusRow) Skin.Round(g, Skin.Alpha(Skin.Selected, 110), row, 8);

            bool isCur = t == cur;
            var color = isCur ? Skin.Accent : t.Missing ? Skin.VisHigh : Skin.Text;
            if (isCur)
            {
                // Playing indicator (Figma: PlayingIndicator); bars bounce while playing.
                double ph = playing ? Environment.TickCount64 / 180.0 : 0;
                float[] hs = playing
                    ? new[] { (float)(6 + 5 * Math.Abs(Math.Sin(ph))), (float)(6 + 6 * Math.Abs(Math.Sin(ph + 1.3))), (float)(5 + 5 * Math.Abs(Math.Sin(ph + 2.1))) }
                    : new[] { 8f, 12f, 6f };
                for (int k = 0; k < 3; k++)
                    Skin.Round(g, Skin.Accent, new RectangleF(row.X + 12 + k * 5, y + 22 - hs[k], 3, hs[k]), 1);
            }
            else
            {
                Skin.Label(g, (i + 1).ToString(), Skin.Mono, Skin.Dim, new RectangleF(row.X + 8, y, 30, RowH), Skin.Left);
            }
            Skin.Label(g, t.DisplayTitle, isCur ? Skin.BodyStrong : Skin.Body, color, new RectangleF(row.X + 40, y, row.Width - 40 - 68, RowH), Skin.Left);
            string dur = t.Duration is { } d ? Fmt.Length(d) : "";
            Skin.Label(g, dur, Skin.Mono, isCur ? Skin.Accent : Skin.Muted, new RectangleF(row.Right - 68, y, 56, RowH), Skin.Right);
        }
        g.Restore(st);
    }

    public override void OnMouseDown(PointF p, MouseEventArgs e)
    {
        var pl = Pl;
        int row = RowAt(p);
        var mods = Control.ModifierKeys;
        if (row < 0 || row >= pl.Count)
        {
            if (e.Button == MouseButtons.Left && (mods & Keys.Control) == 0) pl.SelectAll(false);
            if (e.Button == MouseButtons.Right) app.ShowListContextMenu(Owner!, p);
            return;
        }

        FocusRow = row;
        var t = pl.Items[row];
        if (e.Button == MouseButtons.Right)
        {
            if (!t.Selected) { pl.SelectOnly(row); anchor = row; }
            app.ShowListContextMenu(Owner!, p);
            return;
        }
        if (e.Button != MouseButtons.Left) return;

        if ((mods & Keys.Control) != 0)
        {
            t.Selected = !t.Selected;
            anchor = row;
            pl.NotifyChanged();
        }
        else if ((mods & Keys.Shift) != 0 && anchor >= 0)
        {
            pl.SelectRange(anchor, row);
        }
        else
        {
            if (!t.Selected) pl.SelectOnly(row);
            anchor = row;
            dragging = true;
            moved = false;
            dragRow = clickRow = row;
        }
    }

    public override void OnMouseMove(PointF p, MouseEventArgs e)
    {
        if (!dragging || Pl.Count == 0) return;
        if (p.Y < Bounds.Y && TopRow > 0) TopRow--;
        else if (p.Y > Bounds.Bottom && TopRow < MaxTop) TopRow++;
        int row = Math.Clamp(RowAt(p), 0, Pl.Count - 1);
        int delta = row - dragRow;
        if (delta == 0) return;
        int applied = Pl.MoveSelected(delta);
        if (applied == 0) return;
        dragRow += applied;
        FocusRow += applied;
        anchor += applied;
        moved = true;
        Owner?.Invalidate();
    }

    public override void OnMouseUp(PointF p, MouseEventArgs e)
    {
        if (dragging && !moved && clickRow < Pl.Count && e.Button == MouseButtons.Left) Pl.SelectOnly(clickRow);
        dragging = false;
    }

    public override void OnDoubleClick(PointF p)
    {
        int row = RowAt(p);
        if (row >= 0 && row < Pl.Count) app.PlayTrack(Pl.Items[row]);
    }

    public override void OnWheel(int delta)
    {
        TopRow -= Math.Sign(delta) * 3;
        ClampScroll();
        Owner?.Invalidate();
    }
}

sealed class PlScrollBar : Widget
{
    readonly TrackList list;
    bool dragging;
    float grab;

    public PlScrollBar(TrackList list)
    {
        this.list = list;
        HitPad = 4;
    }

    RectangleF Thumb
    {
        get
        {
            int total = list.Count, page = list.PageRows;
            if (total <= page) return RectangleF.Empty;
            float h = Math.Max(28, Bounds.Height * page / total);
            float y = Bounds.Y + (Bounds.Height - h) * list.TopRow / list.MaxTop;
            return new RectangleF(Bounds.X, y, Bounds.Width, h);
        }
    }

    public override void Paint(Graphics g)
    {
        var th = Thumb;
        if (!th.IsEmpty) Skin.Round(g, dragging || Hover ? Skin.Muted : Skin.Raised, th, 2);
    }

    public override void OnMouseDown(PointF p, MouseEventArgs e)
    {
        var th = Thumb;
        if (th.IsEmpty || e.Button != MouseButtons.Left) return;
        if (RectangleF.Inflate(th, 4, 0).Contains(p)) { dragging = true; grab = p.Y - th.Y; return; }
        list.TopRow += p.Y < th.Y ? -list.PageRows : list.PageRows;
        list.ClampScroll();
        Owner?.Invalidate();
    }

    public override void OnMouseMove(PointF p, MouseEventArgs e)
    {
        if (!dragging) return;
        var th = Thumb;
        float range = Bounds.Height - th.Height;
        if (range <= 0) return;
        list.TopRow = (int)Math.Round((p.Y - grab - Bounds.Y) / range * list.MaxTop);
        list.ClampScroll();
        Owner?.Invalidate();
    }

    public override void OnMouseUp(PointF p, MouseEventArgs e)
    {
        dragging = false;
        Invalidate();
    }

    public override void OnWheel(int delta) => list.OnWheel(delta);
}
