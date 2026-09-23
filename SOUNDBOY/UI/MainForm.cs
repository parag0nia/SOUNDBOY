using System.Diagnostics;
using System.Runtime.InteropServices;
using SOUNDBOY.Audio;
using SOUNDBOY.Model;

namespace SOUNDBOY.UI;

sealed class MainForm : Form
{
    public AudioEngine Engine { get; } = new();
    public Playlist Playlist { get; } = new();
    public AppSettings Settings { get; }
    public PlayerPanel Player { get; }
    public EqPanel EqView { get; }
    public PlaylistPanel PlaylistView { get; }
    public SkinPanel? FocusPanel { get; private set; }
    public bool IsActive { get; private set; }
    public Track? LoadedTrack { get; private set; }
    public bool StopAfterCurrent;

    readonly System.Windows.Forms.Timer timer = new() { Interval = 15 };
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly NotifyIcon tray;
    readonly TagLoader tagLoader = new();
    readonly float dpiScale;
    double lastTick, infoRefresh;
    int failStreak;
    readonly AnalysisCache analysisCache = new();
    CancellationTokenSource? analysisCts;
    readonly MediaSession? media;
    PlayerState reportedState = (PlayerState)(-1);
    double timelineRefresh;
    long lastMediaCommand;
    bool hotkeysRegistered;
    float volumeBeforeMute = 0.8f;

    // Fallback only (if Windows media controls are unavailable): Play/Pause, Next, Previous, Stop.
    static readonly (int id, uint vk)[] MediaKeys = { (1, 0xB3), (2, 0xB0), (3, 0xB1), (4, 0xB2) };

    public MainForm(string[] args)
    {
        Settings = AppSettings.Load();

        Text = "SOUNDBOY";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Skin.Window;
        KeyPreview = true;
        Icon = AppIcon.Load();
        dpiScale = DeviceDpi / 96f;
        ToolStripManager.Renderer = new DarkRenderer();

        Player = new PlayerPanel(this);
        EqView = new EqPanel(this);
        PlaylistView = new PlaylistPanel(this);
        foreach (var p in new SkinPanel[] { Player, EqView, PlaylistView })
        {
            p.GotActive += s => { FocusPanel = s; PlaylistView.Invalidate(); };
            p.AllowDrop = true;
            p.DragEnter += OnDragEnter;
            p.DragDrop += OnDragDrop;
            Controls.Add(p);
        }

        Engine.TrackEnded += (_, _) => OnTrackEnded();
        Engine.PlaybackError += (_, ex) => { Player.ShowTransient("Playback error: " + ex.Message, 4); OnTrackEnded(); };

        try
        {
            media = new MediaSession();
            media.ButtonPressed += b => { try { BeginInvoke(() => OnMediaButton(b)); } catch { } };
            media.SeekRequested += t => { try { BeginInvoke(() => { if (Engine.CanSeek) Engine.Seek(t); }); } catch { } };
        }
        catch
        {
            media = null; // very old Windows builds: fall back to global hotkeys in OnHandleCreated
        }
        Engine.Volume = Settings.Volume;
        Engine.Balance = Settings.Balance;
        Engine.Eq.Set(Settings.EqBands, Settings.EqPreamp);
        Engine.Eq.Enabled = Settings.EqEnabled;
        Player.SyncFromSettings();
        EqView.SyncFromEq();
        Player.SetShaded(Settings.Shaded);
        TopMost = Settings.AlwaysOnTop;

        Playlist.Changed += () => { PlaylistView.OnPlaylistChanged(); UpdateTitle(); };

        tray = new NotifyIcon { Icon = Icon, Text = "SOUNDBOY", Visible = true, ContextMenuStrip = Menus.New() };
        tray.ContextMenuStrip.Opening += (_, e) => { BuildTrayMenu(tray.ContextMenuStrip); e.Cancel = false; };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) RestoreApp(); };

        Relayout();
        PlaceWindow();
        LoadAutosave();
        EnsureSample();
        if (args.Length > 0)
            OpenPaths(args.Where(a => !a.StartsWith('/')), play: !args.Contains("/add", StringComparer.OrdinalIgnoreCase), replace: false);

        timer.Tick += (_, _) => OnTick();
        timer.Start();
    }

    // ------------------------------------------------------------------ window plumbing

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style |= 0x00020000 | 0x00080000; // WS_MINIMIZEBOX | WS_SYSMENU: taskbar click minimizes/restores
            cp.ClassStyle |= 0x00020000;         // CS_DROPSHADOW
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (media == null)
        {
            foreach (var (id, vk) in MediaKeys) Native.RegisterHotKey(Handle, id, Native.MOD_NOREPEAT, vk);
            hotkeysRegistered = true;
        }
        SingleInstance.StartServer(lines =>
        {
            try { BeginInvoke(() => OnExternalMessage(lines)); } catch { }
        });
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY)
        {
            switch ((int)m.WParam)
            {
                case 1: PlayPause(); break;
                case 2: Next(); break;
                case 3: Prev(); break;
                case 4: Stop(); break;
            }
            return;
        }
        if (m.Msg == Native.WM_MOVING)
        {
            var r = Marshal.PtrToStructure<Native.RECT>(m.LParam);
            SnapToEdges(ref r);
            Marshal.StructureToPtr(r, m.LParam, false);
            m.Result = 1;
            return;
        }
        base.WndProc(ref m);
    }

    static void SnapToEdges(ref Native.RECT r)
    {
        const int d = 14;
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        var wa = Screen.FromRectangle(Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom)).WorkingArea;
        if (Math.Abs(r.Left - wa.Left) < d) r.Left = wa.Left;
        else if (Math.Abs(r.Left + w - wa.Right) < d) r.Left = wa.Right - w;
        if (Math.Abs(r.Top - wa.Top) < d) r.Top = wa.Top;
        else if (Math.Abs(r.Top + h - wa.Bottom) < d) r.Top = wa.Bottom - h;
        r.Right = r.Left + w;
        r.Bottom = r.Top + h;
    }

    public void BeginWindowDrag()
    {
        Native.ReleaseCapture();
        Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, Native.HTCAPTION, IntPtr.Zero);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        IsActive = true;
        InvalidatePanels();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        IsActive = false;
        InvalidatePanels();
    }

    void InvalidatePanels()
    {
        Player.Invalidate();
        EqView.Invalidate();
        PlaylistView.Invalidate();
    }

    public float UiScale => dpiScale * Settings.Zoom;

    public void Relayout()
    {
        float s = UiScale;
        SuspendLayout();
        Player.SetLayout(s, PlayerPanel.W, Player.Shaded ? PlayerPanel.ShadeH : PlayerPanel.FullH);
        Player.Location = Point.Empty;
        int y = Player.Height;

        // Note: read the settings, not Control.Visible, which is false until the form itself is shown.
        EqView.Visible = Settings.ShowEq;
        if (Settings.ShowEq)
        {
            EqView.SetLayout(s, EqPanel.W, EqPanel.H);
            EqView.Location = new Point(0, y);
            y += EqView.Height;
        }

        PlaylistView.Visible = Settings.ShowPlaylist;
        if (Settings.ShowPlaylist)
        {
            PlaylistView.SetLayout(s, PlaylistPanel.W, Math.Max(PlaylistPanel.MinH, Settings.PlaylistHeight));
            PlaylistView.Location = new Point(0, y);
            y += PlaylistView.Height;
        }

        ClientSize = new Size(Player.Width, y);
        ResumeLayout();
    }

    void PlaceWindow()
    {
        var rect = new Rectangle(Settings.X, Settings.Y, Width, Math.Min(Height, 200));
        if (Settings.X != int.MinValue && Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(rect)))
        {
            Location = new Point(Settings.X, Settings.Y);
        }
        else
        {
            var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + Math.Max(0, (wa.Height - Height) / 2));
        }
    }

    public void MinimizeApp()
    {
        if (Settings.MinimizeToTray) Hide();
        else WindowState = FormWindowState.Minimized;
    }

    public void RestoreApp()
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    // ------------------------------------------------------------------ main loop

    void OnTick()
    {
        if (media != null && Engine.State != reportedState)
        {
            reportedState = Engine.State;
            media.SetState(reportedState);
            timelineRefresh = 1;
        }
        if (media != null && (timelineRefresh += 0.015) >= 1 && Engine.IsLoaded)
        {
            timelineRefresh = 0;
            media.SetTimeline(Engine.Position, Engine.Duration);
        }

        double now = clock.Elapsed.TotalSeconds;
        double dt = Math.Min(0.1, now - lastTick);
        lastTick = now;
        if (!Visible || WindowState == FormWindowState.Minimized) return;

        Player.Tick(dt);
        if (Settings.ShowPlaylist && Engine.State == PlayerState.Playing) PlaylistView.List.InvalidateCurrentRow();
        if (tagLoader.TakeDirty()) PlaylistView.OnPlaylistChanged();
        if ((infoRefresh += dt) > 0.25)
        {
            infoRefresh = 0;
            if (Settings.ShowPlaylist) PlaylistView.InvalidateInfo();
        }
    }

    public string NowPlayingTitle => Playlist.Current?.DisplayTitleOnly ?? "SOUNDBOY";

    public string NowPlayingSubtitle
    {
        get
        {
            var t = Playlist.Current;
            if (t == null) return Playlist.Count == 0 ? "Drop music here or press L to open files" : "Press play to start";
            var parts = new List<string>
            {
                string.IsNullOrWhiteSpace(t.Artist) ? (t.IsUrl ? "Internet stream" : "Unknown artist") : t.Artist!,
            };
            if (!string.IsNullOrWhiteSpace(t.Album)) parts.Add(t.Album!);
            if (t.Year > 0) parts.Add(t.Year.ToString());
            return string.Join(" · ", parts);
        }
    }

    /// <summary>Format chips, then the musical ones (BPM, key) which are drawn in the accent color.</summary>
    public IEnumerable<(string Text, bool Musical)> NowPlayingChips
    {
        get
        {
            var t = LoadedTrack;
            if (t == null || !Engine.IsLoaded) yield break;
            yield return (t.IsUrl ? "STREAM" : Path.GetExtension(t.Path).TrimStart('.').ToUpperInvariant(), false);
            if (t.Bitrate > 0) yield return ($"{t.Bitrate} kbps", false);
            int sr = t.SampleRate > 0 ? t.SampleRate : Engine.SourceSampleRate;
            if (sr > 0) yield return ($"{sr / 1000.0:0.#} kHz", false);
            int ch = t.Channels > 0 ? t.Channels : Engine.SourceChannels;
            if (ch > 0) yield return (ch == 1 ? "MONO" : ch == 2 ? "STEREO" : $"{ch} CH", false);

            if (t.Analyzing && t.DisplayBpm == null) yield return ("··· BPM", true);
            else if (t.DisplayBpm is double bpm) yield return ($"{Math.Round(bpm):0} BPM", true);
            if (t.Key != null) yield return ($"{t.Key} · {t.Camelot}", true);
            else if (t.Analyzing) yield return ("KEY ···", true);
        }
    }

    // ------------------------------------------------------------------ BPM / key detection

    void StartAnalysis(Track t)
    {
        analysisCts?.Cancel();
        if (t.IsUrl || t.Analyzed) return;
        if (analysisCache.Get(t.Path) is { } cached)
        {
            ApplyAnalysis(t, cached);
            return;
        }
        var cts = analysisCts = new CancellationTokenSource();
        t.Analyzing = true;
        string path = t.Path;
        Task.Run(() => TrackAnalyzer.Analyze(path, cts.Token), cts.Token).ContinueWith(task =>
        {
            if (IsDisposed) return;
            try
            {
                BeginInvoke(() =>
                {
                    t.Analyzing = false;
                    if (task.Status != TaskStatus.RanToCompletion) { Player.Invalidate(); return; }
                    analysisCache.Put(path, task.Result);
                    ApplyAnalysis(t, task.Result);
                });
            }
            catch { }
        }, TaskScheduler.Default);
    }

    void ApplyAnalysis(Track t, AnalysisResult r)
    {
        t.Bpm = r.Bpm;
        t.Key = r.Key;
        t.Camelot = r.Camelot;
        t.Analyzing = false;
        t.Analyzed = true;
        Player.Invalidate();
    }

    void UpdateTitle()
    {
        var t = Playlist.Current;
        int i = Playlist.CurrentIndex;
        Text = t == null ? "SOUNDBOY" : $"{(i >= 0 ? $"{i + 1}. " : "")}{t.DisplayTitle} - SOUNDBOY";
        tray.Text = Text.Length > 63 ? Text[..60] + "..." : Text;
    }

    // ------------------------------------------------------------------ playback

    public bool PlayTrack(Track t, bool start = true)
    {
        Playlist.Current = t;
        LoadedTrack = null;
        try
        {
            if (t.IsUrl) Cursor = Cursors.WaitCursor;
            Engine.Load(t.Path);
            LoadedTrack = t;
            if (!t.InfoLoaded || t.Bitrate == 0) t.LoadInfo();
            // Trust the decoder over tag headers (VBR files without a Xing header often report 0).
            var decoded = Engine.Duration;
            if (decoded > TimeSpan.Zero && (t.Duration is not { } td || Math.Abs((td - decoded).TotalSeconds) > 1))
                t.Duration = decoded;
            t.Missing = false;
            if (start) Engine.Play();
            failStreak = 0;
            UpdateArtworkAndSession(t);
            StartAnalysis(t);
            return true;
        }
        catch (Exception ex)
        {
            Engine.Unload();
            t.Missing = !t.IsUrl && !File.Exists(t.Path);
            Player.ShowTransient($"Can't play {t.FileNameTitle}: {ex.Message}", 4);
            // Skip unplayable files like Winamp does, but never loop forever.
            if (start && ++failStreak < Playlist.Count)
            {
                var next = Playlist.GetNext(Settings.Shuffle, Settings.Repeat != RepeatMode.Off);
                if (next != null && next != t) BeginInvoke(() => PlayTrack(next));
            }
            return false;
        }
        finally
        {
            Cursor = Cursors.Default;
            UpdateTitle();
            PlaylistView.ShowCurrent();
            Player.ResetMarquee();
        }
    }

    public void PlayPressed()
    {
        switch (Engine.State)
        {
            case PlayerState.Paused:
                Engine.Play();
                break;
            case PlayerState.Playing:
                Engine.Seek(TimeSpan.Zero);
                break;
            default:
                var t = Playlist.Current ?? Playlist.Selected.FirstOrDefault() ?? (Playlist.Count > 0 ? Playlist.Items[0] : null);
                if (t == null) { OpenFilesPlay(); return; }
                if (t == LoadedTrack && Engine.IsLoaded) Engine.Play();
                else PlayTrack(t);
                break;
        }
    }

    public void PauseToggle()
    {
        if (Engine.State == PlayerState.Playing) Engine.Pause();
        else if (Engine.State == PlayerState.Paused) Engine.Play();
    }

    public void PlayPause()
    {
        if (Engine.State == PlayerState.Playing) Engine.Pause();
        else PlayPressed();
    }

    public void Stop() => Engine.Stop();

    public void Next()
    {
        var n = Playlist.GetNext(Settings.Shuffle, wrap: true);
        if (n != null) PlayTrack(n, Engine.State != PlayerState.Stopped);
    }

    public void Prev()
    {
        var p = Playlist.GetPrevious(Settings.Shuffle, wrap: true);
        if (p != null) PlayTrack(p, Engine.State != PlayerState.Stopped);
    }

    void OnTrackEnded()
    {
        if (StopAfterCurrent)
        {
            StopAfterCurrent = false;
            return;
        }
        if (Settings.Repeat == RepeatMode.One && Playlist.Current != null)
        {
            PlayTrack(Playlist.Current);
            return;
        }
        var n = Playlist.GetNext(Settings.Shuffle, Settings.Repeat == RepeatMode.All);
        if (n != null) PlayTrack(n);
    }

    public void SeekTo(float fraction)
    {
        if (!Engine.CanSeek || Engine.State == PlayerState.Stopped) return;
        Engine.Seek(Engine.Duration * fraction);
    }

    public void SeekBy(double seconds)
    {
        if (!Engine.CanSeek || Engine.State == PlayerState.Stopped) return;
        Engine.Seek(Engine.Position + TimeSpan.FromSeconds(seconds));
    }

    public void SetVolume(float v)
    {
        Settings.Volume = Math.Clamp(v, 0, 1);
        Engine.Volume = Settings.Volume;
    }

    public void VolumeBy(float delta)
    {
        SetVolume(Settings.Volume + delta);
        Player.SyncFromSettings();
        Player.ShowTransient($"Volume {Settings.Volume * 100:0}%");
    }

    public void ToggleMute()
    {
        if (Settings.Volume > 0.001f)
        {
            volumeBeforeMute = Settings.Volume;
            SetVolume(0);
        }
        else
        {
            SetVolume(volumeBeforeMute > 0.01f ? volumeBeforeMute : 0.8f);
        }
        Player.SyncFromSettings();
        Player.ShowTransient(Settings.Volume <= 0.001f ? "Muted" : $"Volume {Settings.Volume * 100:0}%");
    }

    public void SetBalance(float b)
    {
        Settings.Balance = b;
        Engine.Balance = b;
    }

    // ------------------------------------------------------------------ equalizer

    public void SetBand(int band, float db)
    {
        Engine.Eq[band] = db;
        Settings.EqBands[band] = db;
        EqView.InvalidateGraph();
        Player.ShowTransient($"EQ {EqSettings.Labels[band]}Hz: {db:+0.0;-0.0;0.0} dB");
    }

    public void SetPreamp(float db)
    {
        Engine.Eq.Preamp = db;
        Settings.EqPreamp = db;
        EqView.InvalidateGraph();
        Player.ShowTransient($"EQ preamp: {db:+0.0;-0.0;0.0} dB");
    }

    public void ApplyEq(float[] bands, float preamp)
    {
        Engine.Eq.Set(bands, preamp);
        Settings.EqBands = Engine.Eq.Gains;
        Settings.EqPreamp = Engine.Eq.Preamp;
        EqView.SyncFromEq();
    }

    public void ToggleEqEnabled()
    {
        Engine.Eq.Enabled = !Engine.Eq.Enabled;
        Settings.EqEnabled = Engine.Eq.Enabled;
        EqView.Invalidate();
        Player.ShowTransient(Engine.Eq.Enabled ? "Equalizer on" : "Equalizer off");
    }

    public void ShowPresetMenu(SkinPanel panel, PointF at)
    {
        var m = Menus.New();
        var load = Menus.Sub("Load preset");
        foreach (var p in EqPresets.BuiltIn)
        {
            var preset = p;
            load.DropDownItems.Add(Menus.Item(preset.Name, () => ApplyEq(preset.Bands, preset.Preamp)));
        }
        if (Settings.UserPresets.Count > 0)
        {
            load.DropDownItems.Add(Menus.Sep());
            foreach (var (name, values) in Settings.UserPresets.OrderBy(kv => kv.Key))
            {
                var v = values;
                load.DropDownItems.Add(Menus.Item(name, () => ApplyEq(v.Take(10).ToArray(), v.Length > 10 ? v[10] : 0)));
            }
        }
        m.Items.Add(load);
        m.Items.Add(Menus.Item("Save preset...", SavePreset));

        var del = Menus.Sub("Delete preset");
        foreach (var name in Settings.UserPresets.Keys.OrderBy(k => k).ToList())
            del.DropDownItems.Add(Menus.Item(name, () => Settings.UserPresets.Remove(name)));
        del.Enabled = Settings.UserPresets.Count > 0;
        m.Items.Add(del);

        m.Items.Add(Menus.Sep());
        m.Items.Add(Menus.Item("Import Winamp presets (.eqf)...", ImportEqf));
        m.Items.Add(Menus.Item("Export my presets (.eqf)...", ExportEqf, enabled: Settings.UserPresets.Count > 0));
        m.Items.Add(Menus.Sep());
        m.Items.Add(Menus.Item("Equalizer enabled", ToggleEqEnabled, isChecked: Engine.Eq.Enabled));
        m.Items.Add(Menus.Item("Reset to flat", () => ApplyEq(new float[10], 0)));
        ShowMenu(m, panel, at);
    }

    void SavePreset()
    {
        var name = InputDialog.Ask(this, "Save EQ preset", "Preset name:")?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        Settings.UserPresets[name] = Engine.Eq.Gains.Append(Engine.Eq.Preamp).ToArray();
        Player.ShowTransient($"Preset saved: {name}");
    }

    void ImportEqf()
    {
        using var d = new OpenFileDialog { Filter = "Winamp EQ presets (*.eqf)|*.eqf|All files|*.*" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var list = EqPresets.ReadEqf(d.FileName);
            foreach (var p in list) Settings.UserPresets[p.Name] = p.Bands.Append(p.Preamp).ToArray();
            if (list.Count == 1) ApplyEq(list[0].Bands, list[0].Preamp);
            Player.ShowTransient($"Imported {list.Count} preset(s)", 2.5);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Import failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    void ExportEqf()
    {
        using var d = new SaveFileDialog { Filter = "Winamp EQ presets (*.eqf)|*.eqf", FileName = "neoamp.eqf" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            EqPresets.WriteEqf(d.FileName, Settings.UserPresets.Select(kv =>
                new EqPreset(kv.Key, kv.Value.Take(10).ToArray(), kv.Value.Length > 10 ? kv.Value[10] : 0)));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ------------------------------------------------------------------ toggles

    public void ToggleEq() { Settings.ShowEq = !Settings.ShowEq; Relayout(); Player.Invalidate(); }
    public void TogglePlaylist() { Settings.ShowPlaylist = !Settings.ShowPlaylist; Relayout(); Player.Invalidate(); }

    public void ToggleShade()
    {
        Settings.Shaded = !Settings.Shaded;
        Player.SetShaded(Settings.Shaded);
        Relayout();
    }

    public void ToggleShuffle()
    {
        Settings.Shuffle = !Settings.Shuffle;
        Playlist.ResetShuffle();
        Player.Invalidate();
        Player.ShowTransient(Settings.Shuffle ? "Shuffle on" : "Shuffle off");
    }

    public void CycleRepeat()
    {
        Settings.Repeat = (RepeatMode)(((int)Settings.Repeat + 1) % 3);
        Player.Invalidate();
        Player.ShowTransient(Settings.Repeat switch
        {
            RepeatMode.All => "Repeat: playlist",
            RepeatMode.One => "Repeat: current track",
            _ => "Repeat off",
        });
    }

    public void ToggleTimeMode()
    {
        Settings.RemainingTime = !Settings.RemainingTime;
        Player.Invalidate();
    }

    public void CycleVis()
    {
        Settings.Vis = (VisMode)(((int)Settings.Vis + 1) % 3);
        Player.Invalidate();
    }

    public void ToggleOnTop()
    {
        Settings.AlwaysOnTop = !Settings.AlwaysOnTop;
        TopMost = Settings.AlwaysOnTop;
    }

    void SetZoom(float z)
    {
        Settings.Zoom = z;
        Relayout();
    }

    public void SetPlaylistHeight(float h)
    {
        int ih = (int)Math.Max(PlaylistPanel.MinH, h);
        if (ih == Settings.PlaylistHeight) return;
        Settings.PlaylistHeight = ih;
        Relayout();
    }

    // ------------------------------------------------------------------ opening files

    static string OpenFilter
    {
        get
        {
            string audio = string.Join(";", MediaFiles.AudioExtensions.Select(e => "*" + e));
            string lists = string.Join(";", MediaFiles.PlaylistExtensions.Select(e => "*" + e));
            return $"All supported|{audio};{lists}|Audio files|{audio}|Playlists|{lists}|All files|*.*";
        }
    }

    public void OpenFilesPlay() => OpenFilesDialog(replace: true);
    public void AddFiles() => OpenFilesDialog(replace: false);

    void OpenFilesDialog(bool replace)
    {
        using var d = new OpenFileDialog
        {
            Multiselect = true,
            Filter = OpenFilter,
            Title = replace ? "Play file(s)" : "Add file(s) to playlist",
            InitialDirectory = Settings.LastFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        Settings.LastFolder = Path.GetDirectoryName(d.FileNames[0]);
        OpenPaths(d.FileNames, play: replace, replace: replace);
    }

    public void OpenFolder(bool replace)
    {
        using var d = new FolderBrowserDialog
        {
            Description = replace ? "Play folder (includes subfolders)" : "Add folder to playlist (includes subfolders)",
            UseDescriptionForTitle = true,
            InitialDirectory = Settings.LastFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        Settings.LastFolder = d.SelectedPath;
        OpenPaths(new[] { d.SelectedPath }, play: replace, replace: replace);
    }

    public void OpenUrl(bool replace)
    {
        var url = InputDialog.Ask(this, replace ? "Play URL" : "Add URL", "Stream or file URL (http:// or https://):", "https://")?.Trim();
        if (string.IsNullOrEmpty(url) || !MediaFiles.IsUrl(url) || url.EndsWith("://")) return;
        OpenPaths(new[] { url }, play: replace, replace: replace);
    }

    public void OpenPaths(IEnumerable<string> paths, bool play, bool replace, int insertAt = -1)
    {
        List<Track> tracks;
        Cursor = Cursors.WaitCursor;
        try { tracks = MediaFiles.Expand(paths).ToList(); }
        finally { Cursor = Cursors.Default; }
        if (tracks.Count == 0) return;

        if (replace)
        {
            Playlist.Clear();
            PlaylistView.List.TopRow = 0;
        }
        Playlist.Add(tracks, insertAt);
        tagLoader.Enqueue(tracks.Where(t => !t.IsUrl));
        if (play) PlayTrack(tracks[0]);
    }

    void OnExternalMessage(string[] lines)
    {
        RestoreApp();
        if (lines.Length < 2) return;
        OpenPaths(lines.Skip(1), play: lines[0] == "OPEN", replace: false);
    }

    void OnDragEnter(object? sender, DragEventArgs e) =>
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;

    void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files) return;
        if (sender == PlaylistView)
        {
            int row = PlaylistView.InsertRowAt(PlaylistView.ToLogical(PlaylistView.PointToClient(new Point(e.X, e.Y))));
            BeginInvoke(() => OpenPaths(files, play: false, replace: false, insertAt: row));
        }
        else
        {
            // Winamp behavior: dropping on the main window replaces the playlist and plays.
            BeginInvoke(() => OpenPaths(files, play: true, replace: true));
        }
    }

    // ------------------------------------------------------------------ playlist commands

    public void RemoveSelected() => Playlist.RemoveSelected();

    void OpenPlaylistFile()
    {
        using var d = new OpenFileDialog { Filter = "Playlists (*.m3u;*.m3u8;*.pls)|*.m3u;*.m3u8;*.pls|All files|*.*", InitialDirectory = Settings.LastFolder };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        OpenPaths(new[] { d.FileName }, play: false, replace: true);
    }

    void SavePlaylistFile()
    {
        using var d = new SaveFileDialog
        {
            Filter = "M3U8 playlist (*.m3u8)|*.m3u8|M3U playlist (*.m3u)|*.m3u|PLS playlist (*.pls)|*.pls",
            FileName = "playlist.m3u8",
            InitialDirectory = Settings.LastFolder,
        };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        try { PlaylistIO.Save(d.FileName, Playlist.Items); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    public void ShowJump()
    {
        if (Playlist.Count == 0) return;
        using var d = new JumpDialog(Playlist.Items);
        if (d.ShowDialog(this) == DialogResult.OK && d.Selected != null) PlayTrack(d.Selected);
    }

    public void ShowFileInfo()
    {
        var t = Playlist.Selected.FirstOrDefault() ?? Playlist.Current;
        if (t == null) return;
        t.LoadInfo();
        using var d = new FileInfoDialog(t);
        d.ShowDialog(this);
        PlaylistView.Invalidate();
    }

    void OpenContainingFolder()
    {
        var t = Playlist.Selected.FirstOrDefault() ?? Playlist.Current;
        if (t == null || t.IsUrl || !File.Exists(t.Path)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{t.Path}\"") { UseShellExecute = true });
    }

    void SortBy(Func<Track, string> key) => Playlist.SortBy(key, StringComparer.CurrentCultureIgnoreCase);

    public void ShowPlaylistMenu(string kind, SkinPanel panel, PointF at)
    {
        var m = Menus.New();
        switch (kind)
        {
            case "add":
                m.Items.Add(Menus.Item("Add file(s)...", AddFiles, "L"));
                m.Items.Add(Menus.Item("Add folder...", () => OpenFolder(false), "Shift+L"));
                m.Items.Add(Menus.Item("Add URL...", () => OpenUrl(false), "Ctrl+L"));
                break;
            case "rem":
                m.Items.Add(Menus.Item("Remove selected", RemoveSelected, "Del"));
                m.Items.Add(Menus.Item("Crop (keep selected)", Playlist.Crop, "Ctrl+Del"));
                m.Items.Add(Menus.Item("Clear playlist", Playlist.Clear, "Ctrl+Shift+Del"));
                m.Items.Add(Menus.Sep());
                m.Items.Add(Menus.Item("Remove missing files", Playlist.RemoveMissing));
                m.Items.Add(Menus.Item("Remove duplicates", Playlist.RemoveDuplicates));
                break;
            case "sel":
                m.Items.Add(Menus.Item("Select all", () => Playlist.SelectAll(true), "Ctrl+A"));
                m.Items.Add(Menus.Item("Select none", () => Playlist.SelectAll(false)));
                m.Items.Add(Menus.Item("Invert selection", Playlist.InvertSelection, "Ctrl+I"));
                break;
            case "misc":
                m.Items.Add(Menus.Sub("Sort list",
                    Menus.Item("By title", () => SortBy(t => t.DisplayTitle)),
                    Menus.Item("By artist", () => SortBy(t => t.Artist ?? t.DisplayTitle)),
                    Menus.Item("By album", () => SortBy(t => t.Album ?? "")),
                    Menus.Item("By file name", () => SortBy(t => Path.GetFileName(t.Path))),
                    Menus.Item("By path and file name", () => SortBy(t => t.Path)),
                    Menus.Item("By length", () => Playlist.SortBy(t => t.Duration ?? TimeSpan.Zero))));
                m.Items.Add(Menus.Item("Reverse list", Playlist.Reverse));
                m.Items.Add(Menus.Item("Randomize list", Playlist.Randomize));
                m.Items.Add(Menus.Sep());
                m.Items.Add(Menus.Item("File info...", ShowFileInfo, "Alt+3"));
                m.Items.Add(Menus.Item("Jump to file...", ShowJump, "J"));
                break;
            case "list":
                m.Items.Add(Menus.Item("New playlist", Playlist.Clear, "Ctrl+N"));
                m.Items.Add(Menus.Item("Open playlist...", OpenPlaylistFile, "Ctrl+O"));
                m.Items.Add(Menus.Item("Save playlist...", SavePlaylistFile, "Ctrl+S"));
                m.Items.Add(Menus.Sep());
                m.Items.Add(Menus.Item("Export playlist to Rekordbox", () => ExportToRekordbox(selectionOnly: false), enabled: !rekordboxBusy && Playlist.Count > 0));
                m.Items.Add(Menus.Item("Rekordbox export file...", ChooseRekordboxFile));
                m.Items.Add(Menus.Item("Rekordbox setup help...", () => ShowRekordboxHelp(null)));
                break;
        }
        m.Closed += (_, _) => BeginInvoke(m.Dispose);
        m.Show(panel, panel.ToDevice(at), ToolStripDropDownDirection.AboveRight);
    }

    public void ShowListContextMenu(SkinPanel panel, PointF at)
    {
        bool any = Playlist.Selected.Any();
        var m = Menus.New();
        m.Items.Add(Menus.Item("Play", () => { var t = Playlist.Selected.FirstOrDefault(); if (t != null) PlayTrack(t); }, "Enter", enabled: any));
        m.Items.Add(Menus.Item("File info...", ShowFileInfo, "Alt+3", enabled: any));
        m.Items.Add(Menus.Item("Show in folder", OpenContainingFolder, enabled: any));
        m.Items.Add(Menus.Sep());
        int selCount = Playlist.Selected.Count();
        m.Items.Add(Menus.Item(any ? $"Export {selCount} track{(selCount == 1 ? "" : "s")} to Rekordbox" : "Export playlist to Rekordbox",
            () => ExportToRekordbox(selectionOnly: any), "Ctrl+E", enabled: !rekordboxBusy && Playlist.Count > 0));
        m.Items.Add(Menus.Sep());
        m.Items.Add(Menus.Item("Remove", RemoveSelected, "Del", enabled: any));
        m.Items.Add(Menus.Item("Crop", Playlist.Crop, "Ctrl+Del", enabled: any));
        m.Items.Add(Menus.Sep());
        m.Items.Add(Menus.Item("Add file(s)...", AddFiles));
        m.Items.Add(Menus.Item("Add folder...", () => OpenFolder(false)));
        m.Items.Add(Menus.Item("Select all", () => Playlist.SelectAll(true), "Ctrl+A"));
        ShowMenu(m, panel, at);
    }

    // ------------------------------------------------------------------ main menu

    public void ShowMainMenu(SkinPanel panel, PointF at)
    {
        var m = Menus.New();
        m.Items.Add(Menus.Item("Play file(s)...", OpenFilesPlay, "L"));
        m.Items.Add(Menus.Item("Play folder...", () => OpenFolder(true), "Shift+L"));
        m.Items.Add(Menus.Item("Play URL...", () => OpenUrl(true), "Ctrl+L"));
        m.Items.Add(Menus.Sep());
        m.Items.Add(Menus.Item("Compact mode", ToggleShade, "Ctrl+W", Settings.Shaded));
        m.Items.Add(Menus.Item("Equalizer", ToggleEq, "Alt+G", Settings.ShowEq));
        m.Items.Add(Menus.Item("Playlist editor", TogglePlaylist, "Alt+E", Settings.ShowPlaylist));
        m.Items.Add(Menus.Sep());
        m.Items.Add(Menus.Sub("Playback",
            Menus.Item("Previous", Prev, "Z"),
            Menus.Item("Play", PlayPressed, "X"),
            Menus.Item("Pause", PauseToggle, "C"),
            Menus.Item("Stop", Stop, "V"),
            Menus.Item("Next", Next, "B"),
            Menus.Sep(),
            Menus.Item("Back 5 seconds", () => SeekBy(-5), "Left"),
            Menus.Item("Forward 5 seconds", () => SeekBy(5), "Right"),
            Menus.Item("Volume up", () => VolumeBy(0.05f), "Up"),
            Menus.Item("Volume down", () => VolumeBy(-0.05f), "Down"),
            Menus.Sep(),
            Menus.Item("Stop after current track", () => StopAfterCurrent = !StopAfterCurrent, "Ctrl+V", StopAfterCurrent),
            Menus.Item("Shuffle", ToggleShuffle, "S", Settings.Shuffle),
            Menus.Sub("Repeat",
                Menus.Item("Off", () => SetRepeat(RepeatMode.Off), null, Settings.Repeat == RepeatMode.Off),
                Menus.Item("Playlist", () => SetRepeat(RepeatMode.All), null, Settings.Repeat == RepeatMode.All),
                Menus.Item("Current track", () => SetRepeat(RepeatMode.One), null, Settings.Repeat == RepeatMode.One))));
        m.Items.Add(Menus.Sub("Visualization",
            Menus.Item("Spectrum analyzer", () => SetVis(VisMode.Spectrum), null, Settings.Vis == VisMode.Spectrum),
            Menus.Item("Oscilloscope", () => SetVis(VisMode.Oscilloscope), null, Settings.Vis == VisMode.Oscilloscope),
            Menus.Item("Off", () => SetVis(VisMode.Off), null, Settings.Vis == VisMode.Off),
            Menus.Sep(),
            Menus.Item("Show peaks", () => Settings.ShowPeaks = !Settings.ShowPeaks, null, Settings.ShowPeaks)));
        var zoom = Menus.Sub("Zoom");
        foreach (var z in new[] { 0.75f, 1f, 1.25f, 1.5f, 2f })
        {
            float zz = z;
            zoom.DropDownItems.Add(Menus.Item($"{z * 100:0}%", () => SetZoom(zz), null, Math.Abs(Settings.Zoom - z) < 0.01f));
        }
        m.Items.Add(Menus.Sub("Options",
            Menus.Item("Always on top", ToggleOnTop, "Ctrl+Alt+A", Settings.AlwaysOnTop),
            Menus.Item("Minimize to tray", () => Settings.MinimizeToTray = !Settings.MinimizeToTray, null, Settings.MinimizeToTray),
            Menus.Item("Show remaining time", ToggleTimeMode, "Ctrl+T", Settings.RemainingTime),
            zoom));
        m.Items.Add(Menus.Sep());
        m.Items.Add(Menus.Item("File info...", ShowFileInfo, "Alt+3"));
        m.Items.Add(Menus.Item("Jump to file...", ShowJump, "J"));
        m.Items.Add(Menus.Sep());
        m.Items.Add(Menus.Item("Keyboard shortcuts && about...", ShowAbout, "F1"));
        m.Items.Add(Menus.Item("Exit", Close));
        ShowMenu(m, panel, at);
    }

    void SetRepeat(RepeatMode r) { Settings.Repeat = r; Player.Invalidate(); }
    void SetVis(VisMode v) { Settings.Vis = v; Player.Invalidate(); }

    void ShowMenu(ContextMenuStrip m, SkinPanel panel, PointF at)
    {
        m.Closed += (_, _) => BeginInvoke(m.Dispose);
        m.Show(panel, panel.ToDevice(at));
    }

    void BuildTrayMenu(ContextMenuStrip m)
    {
        m.Items.Clear();
        m.Items.Add(Menus.Item(Engine.State == PlayerState.Playing ? "Pause" : "Play", PlayPause));
        m.Items.Add(Menus.Item("Stop", Stop));
        m.Items.Add(Menus.Item("Previous", Prev));
        m.Items.Add(Menus.Item("Next", Next));
        m.Items.Add(Menus.Sep());
        m.Items.Add(Menus.Item("Show SOUNDBOY", RestoreApp));
        m.Items.Add(Menus.Item("Exit", Close));
    }

    public void ShowAbout()
    {
        MessageBox.Show(this,
            $"SOUNDBOY {Program.Version}\n\n" +
            "Formats: MP3, WAV, FLAC, OGG Vorbis, M4A/AAC, WMA, AIFF, AC3, plus HTTP streams.\n" +
            "Playlists: M3U, M3U8, PLS. EQ presets: Winamp .EQF import/export.\n\n" +
            "Z / X / C / V / B\tPrevious / Play / Pause / Stop / Next\n" +
            "Space\t\tPlay/Pause\n" +
            "Left / Right\tSeek 5 seconds\n" +
            "Up / Down\tVolume (in playlist: move cursor)\n" +
            "L / Shift+L / Ctrl+L\tOpen file(s) / folder / URL\n" +
            "J\t\tJump to file\n" +
            "S / R\t\tShuffle / Repeat\n" +
            "Ctrl+T\t\tElapsed/remaining time\n" +
            "Ctrl+W\t\tCompact mode\n" +
            "Alt+G / Alt+E\tEqualizer / Playlist\n" +
            "Alt+3\t\tFile info\n" +
            "Ctrl+V\t\tStop after current track\n" +
            "Ctrl+Alt+A\tAlways on top\n" +
            "Playlist: Enter, Del, Ctrl+A, Ctrl+I, Alt+Up/Down (move), Ctrl+N/O/S\n" +
            "Keyboard media keys (Play/Pause, Next, Previous, Stop, Rewind, Fast-forward)\nwork anywhere, and SOUNDBOY shows up in the Windows media flyout.\n\n" +
            "Click the time to toggle total/remaining, the visualizer to change modes,\nand the artwork for file info.",
            "About SOUNDBOY", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ------------------------------------------------------------------ keyboard

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (HandleKey(keyData)) return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    bool HandleKey(Keys k)
    {
        bool inList = FocusPanel == PlaylistView && Settings.ShowPlaylist;
        var list = PlaylistView.List;
        switch (k)
        {
            case Keys.Z: Prev(); return true;
            case Keys.X: PlayPressed(); return true;
            case Keys.C: PauseToggle(); return true;
            case Keys.V: Stop(); return true;
            case Keys.B: Next(); return true;
            case Keys.Space: PlayPause(); return true;
            case Keys.L: OpenFilesPlay(); return true;
            case Keys.Shift | Keys.L: OpenFolder(true); return true;
            case Keys.Control | Keys.L: OpenUrl(true); return true;
            case Keys.J: ShowJump(); return true;
            case Keys.S: ToggleShuffle(); return true;
            case Keys.R: CycleRepeat(); return true;
            case Keys.Control | Keys.T: ToggleTimeMode(); return true;
            case Keys.Control | Keys.W: ToggleShade(); return true;
            case Keys.Control | Keys.V:
                StopAfterCurrent = !StopAfterCurrent;
                Player.ShowTransient(StopAfterCurrent ? "Stop after current track: on" : "Stop after current track: off");
                return true;
            case Keys.Alt | Keys.G: ToggleEq(); return true;
            case Keys.Alt | Keys.E: TogglePlaylist(); return true;
            case Keys.Alt | Keys.D3: ShowFileInfo(); return true;
            case Keys.Control | Keys.Alt | Keys.A: ToggleOnTop(); return true;
            case Keys.F1: ShowAbout(); return true;
            case Keys.Left: SeekBy(-5); return true;
            case Keys.Right: SeekBy(5); return true;
            case Keys.Control | Keys.A: Playlist.SelectAll(true); return true;
            case Keys.Control | Keys.I: Playlist.InvertSelection(); return true;
            case Keys.Control | Keys.N: Playlist.Clear(); return true;
            case Keys.Control | Keys.O: OpenPlaylistFile(); return true;
            case Keys.Control | Keys.S: SavePlaylistFile(); return true;
            case Keys.Control | Keys.E: ExportToRekordbox(selectionOnly: Playlist.Selected.Any()); return true;
        }

        if (!inList)
        {
            switch (k)
            {
                case Keys.Up: VolumeBy(0.03f); return true;
                case Keys.Down: VolumeBy(-0.03f); return true;
            }
            return false;
        }

        int f = list.FocusRow;
        switch (k)
        {
            case Keys.Up: list.MoveFocus(f - 1, false); return true;
            case Keys.Down: list.MoveFocus(f + 1, false); return true;
            case Keys.Shift | Keys.Up: list.MoveFocus(f - 1, true); return true;
            case Keys.Shift | Keys.Down: list.MoveFocus(f + 1, true); return true;
            case Keys.PageUp: list.MoveFocus(f - list.PageRows, false); return true;
            case Keys.PageDown: list.MoveFocus(f + list.PageRows, false); return true;
            case Keys.Home: list.MoveFocus(0, false); return true;
            case Keys.End: list.MoveFocus(Playlist.Count - 1, false); return true;
            case Keys.Shift | Keys.Home: list.MoveFocus(0, true); return true;
            case Keys.Shift | Keys.End: list.MoveFocus(Playlist.Count - 1, true); return true;
            case Keys.Alt | Keys.Up: list.MoveSelection(-1); return true;
            case Keys.Alt | Keys.Down: list.MoveSelection(1); return true;
            case Keys.Enter:
                if (f >= 0 && f < Playlist.Count) PlayTrack(Playlist.Items[f]);
                return true;
            case Keys.Delete: RemoveSelected(); return true;
            case Keys.Control | Keys.Delete: Playlist.Crop(); return true;
            case Keys.Control | Keys.Shift | Keys.Delete: Playlist.Clear(); return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ Rekordbox export

    bool rekordboxBusy;

    string RekordboxXmlPath => Settings.RekordboxXmlPath ?? RekordboxExport.DefaultXmlPath;

    public void ExportToRekordbox(bool selectionOnly)
    {
        if (rekordboxBusy) { Player.ShowTransient("A Rekordbox export is already running…"); return; }
        var tracks = (selectionOnly ? Playlist.Selected : Playlist.Items).ToList();
        var supported = tracks.Where(t => RekordboxExport.IsSupported(t) && File.Exists(t.Path)).ToList();
        int skipped = tracks.Count - supported.Count;
        if (supported.Count == 0)
        {
            MessageBox.Show(this, "Nothing to export. Rekordbox can load MP3, M4A/AAC, WAV, AIFF and FLAC files that exist on disk.",
                "Export to Rekordbox", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string xmlPath = RekordboxXmlPath;
        string name = (selectionOnly ? "Selection" : "Playlist") + " " + DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        rekordboxBusy = true;
        Player.ShowTransient($"Rekordbox: preparing {supported.Count} track(s)…", 3);

        // BPM/key for every track (cached ones are instant), tags where missing; all off the UI thread.
        var todo = supported.Where(t => !t.Analyzed || !t.InfoLoaded).ToList();
        Task.Run(() =>
        {
            var results = new System.Collections.Concurrent.ConcurrentDictionary<Track, AnalysisResult>();
            int done = 0;
            long lastReport = 0;
            Parallel.ForEach(todo, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, t =>
            {
                if (!t.InfoLoaded) t.LoadInfo();
                if (!t.Analyzed)
                {
                    var r = analysisCache.Get(t.Path);
                    if (r == null)
                    {
                        try { r = TrackAnalyzer.Analyze(t.Path, CancellationToken.None); }
                        catch { r = new AnalysisResult(null, null, null); }
                        analysisCache.Put(t.Path, r);
                    }
                    results[t] = r;
                }
                int d = Interlocked.Increment(ref done);
                long now = Environment.TickCount64;
                if (now - Interlocked.Read(ref lastReport) > 250 || d == todo.Count)
                {
                    Interlocked.Exchange(ref lastReport, now);
                    try { BeginInvoke(() => Player.ShowTransient($"Rekordbox: analyzing BPM & key {d}/{todo.Count}…", 3)); } catch { }
                }
            });
            return results;
        }).ContinueWith(task =>
        {
            try
            {
                BeginInvoke(() =>
                {
                    rekordboxBusy = false;
                    if (task.Exception != null)
                    {
                        MessageBox.Show(this, "Rekordbox export failed: " + task.Exception.GetBaseException().Message, "Export to Rekordbox",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    foreach (var (t, r) in task.Result) ApplyAnalysis(t, r);
                    analysisCache.Save();
                    try
                    {
                        var result = RekordboxExport.Export(xmlPath, supported, name);
                        PlaylistView.Invalidate();
                        string note = skipped > 0 ? $" ({skipped} skipped: format rekordbox can't load)" : "";
                        Player.ShowTransient($"Exported {supported.Count} track(s) to Rekordbox{note}", 5);
                        if (!Settings.RekordboxHelpShown || (RekordboxExport.RekordboxInstalled && !RekordboxExport.XmlSidebarEnabled))
                            ShowRekordboxHelp(result);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "Rekordbox export failed: " + ex.Message, "Export to Rekordbox", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                });
            }
            catch { } // window closed mid-export
        }, TaskScheduler.Default);
    }

    void ShowRekordboxHelp(RekordboxExportResult? result)
    {
        string path = result?.XmlPath ?? RekordboxXmlPath;
        using var d = new RekordboxHelpDialog(path, result,
            configuredInRekordbox: string.Equals(RekordboxExport.RekordboxConfiguredXmlPath, path, StringComparison.OrdinalIgnoreCase),
            sidebarEnabled: RekordboxExport.XmlSidebarEnabled);
        d.ShowDialog(this);
        if (d.DontShowAgain) Settings.RekordboxHelpShown = true;
    }

    void ChooseRekordboxFile()
    {
        using var d = new SaveFileDialog
        {
            Title = "Rekordbox XML export file",
            Filter = "rekordbox XML (*.xml)|*.xml",
            OverwritePrompt = false, // existing libraries are merged, not replaced
            FileName = Path.GetFileName(RekordboxXmlPath),
            InitialDirectory = Path.GetDirectoryName(RekordboxXmlPath),
        };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        Settings.RekordboxXmlPath = string.Equals(d.FileName, RekordboxExport.DefaultXmlPath, StringComparison.OrdinalIgnoreCase) ? null : d.FileName;
        Player.ShowTransient("Rekordbox exports go to " + Path.GetFileName(d.FileName), 3);
    }

    // ------------------------------------------------------------------ media keys / Windows media flyout

    void OnMediaButton(Windows.Media.SystemMediaTransportControlsButton b)
    {
        // Some keyboards deliver a key twice (two HID paths); ignore instant repeats.
        long now = Environment.TickCount64;
        if (now - lastMediaCommand < 150) return;
        lastMediaCommand = now;
        switch (b)
        {
            case Windows.Media.SystemMediaTransportControlsButton.Play:
                if (Engine.State != PlayerState.Playing) PlayPressed();
                break;
            case Windows.Media.SystemMediaTransportControlsButton.Pause:
                if (Engine.State == PlayerState.Playing) Engine.Pause();
                break;
            case Windows.Media.SystemMediaTransportControlsButton.Stop: Stop(); break;
            case Windows.Media.SystemMediaTransportControlsButton.Next: Next(); break;
            case Windows.Media.SystemMediaTransportControlsButton.Previous: Prev(); break;
            case Windows.Media.SystemMediaTransportControlsButton.Rewind: SeekBy(-10); break;
            case Windows.Media.SystemMediaTransportControlsButton.FastForward: SeekBy(10); break;
        }
        Player.Invalidate();
    }

    void UpdateArtworkAndSession(Track t)
    {
        byte[]? art = t.ReadArtwork();
        Image? img = null;
        if (art != null)
        {
            try { using var ms = new MemoryStream(art); img = new Bitmap(Image.FromStream(ms)); } catch { img = null; }
        }
        Player.SetArtwork(img);
        if (media == null) return;
        byte[] thumb;
        using (var bmp = new Bitmap(256, 256))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                if (img != null) g.DrawImage(img, 0, 0, 256, 256);
                else Skin.DefaultArtwork(g, new RectangleF(0, 0, 256, 256), 0);
            }
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            thumb = ms.ToArray();
        }
        media.SetTrack(t.DisplayTitleOnly, t.Artist, t.Album, thumb);
        media.SetTimeline(TimeSpan.Zero, Engine.Duration);
    }

    // ------------------------------------------------------------------ bundled sample

    public static string SamplePath => Path.Combine(AppSettings.Dir, "Samples", "Dunno Sample Shout.mp3");

    /// <summary>Makes sure the bundled "Dunno Sample Shout" is in the playlist every time the app opens.</summary>
    void EnsureSample()
    {
        try
        {
            if (!File.Exists(SamplePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SamplePath)!);
                using var res = typeof(MainForm).Assembly.GetManifestResourceStream("SOUNDBOY.Sample.mp3");
                if (res == null) return;
                using var fs = File.Create(SamplePath);
                res.CopyTo(fs);
            }
            var sample = Playlist.Items.FirstOrDefault(t => string.Equals(t.Path, SamplePath, StringComparison.OrdinalIgnoreCase));
            bool isNew = sample == null;
            sample ??= new Track(SamplePath);
            // The file's own tags name a different show; always present it as the SOUNDBOY sample.
            sample.FixedTitle = true;
            sample.Title = "Dunno Sample Shout";
            sample.Artist = "SOUNDBOY";
            sample.Album = "Samples";
            if (!sample.InfoLoaded) sample.LoadInfo();
            if (sample.Duration is not { TotalSeconds: >= 1 }) sample.Duration = ProbeDuration(SamplePath) ?? sample.Duration;
            if (isNew) Playlist.Add(new[] { sample }, 0);
            else Playlist.NotifyChanged();
            PlaylistView.ShowCurrent();
        }
        catch { }
    }

    /// <summary>Exact length from the decoder, for files whose headers don't report it.</summary>
    static TimeSpan? ProbeDuration(string path)
    {
        try
        {
            using var r = new NAudio.Wave.AudioFileReader(path);
            return r.TotalTime > TimeSpan.Zero ? r.TotalTime : null;
        }
        catch
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ persistence

    void LoadAutosave()
    {
        try
        {
            if (!File.Exists(AppSettings.PlaylistFile)) return;
            var tracks = PlaylistIO.Load(AppSettings.PlaylistFile);
            Playlist.Add(tracks);
            if (Settings.CurrentIndex >= 0 && Settings.CurrentIndex < tracks.Count)
                Playlist.Current = tracks[Settings.CurrentIndex];
            tagLoader.Enqueue(tracks.Where(t => !t.IsUrl && t.Duration == null));
            PlaylistView.ShowCurrent();
        }
        catch { }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        timer.Stop();
        if (WindowState == FormWindowState.Normal && Visible)
        {
            Settings.X = Left;
            Settings.Y = Top;
        }
        Settings.CurrentIndex = Playlist.CurrentIndex;
        Settings.Save();
        try
        {
            Directory.CreateDirectory(AppSettings.Dir);
            PlaylistIO.Save(AppSettings.PlaylistFile, Playlist.Items);
        }
        catch { }
        if (hotkeysRegistered) foreach (var (id, _) in MediaKeys) Native.UnregisterHotKey(Handle, id);
        media?.Dispose();
        tray.Visible = false;
        tray.Dispose();
        tagLoader.Dispose();
        analysisCts?.Cancel();
        analysisCache.Save();
        Engine.Dispose();
    }
}

static class AppIcon
{
    public static Icon Load()
    {
        try
        {
            using var s = typeof(AppIcon).Assembly.GetManifestResourceStream("SOUNDBOY.app.ico");
            if (s != null) return new Icon(s);
        }
        catch { }
        return SystemIcons.Application;
    }
}
