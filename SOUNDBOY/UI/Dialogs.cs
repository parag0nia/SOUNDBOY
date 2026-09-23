using System.Diagnostics;
using SOUNDBOY.Model;

namespace SOUNDBOY.UI;

static class DarkTheme
{
    public static readonly Color Back = Skin.Surface;
    public static readonly Color Fore = Skin.Text;
    public static readonly Color FieldFore = Skin.Text;
    public static readonly Color Field = Skin.Window;

    public static void Apply(Form f)
    {
        f.AutoScaleDimensions = new SizeF(96f, 96f);
        f.AutoScaleMode = AutoScaleMode.Dpi;
        f.BackColor = Back;
        f.ForeColor = Fore;
        f.Font = new Font("Segoe UI", 9f);
        f.StartPosition = FormStartPosition.CenterParent;
        f.ShowInTaskbar = false;
        f.MinimizeBox = false;
        f.MaximizeBox = false;
    }

    public static Button Button(string text, DialogResult result, int x, int y) => new()
    {
        Text = text,
        DialogResult = result,
        Location = new Point(x, y),
        Size = new Size(80, 28),
        FlatStyle = FlatStyle.Flat,
        BackColor = Skin.Raised,
        ForeColor = Fore,
    };
}

sealed class InputDialog : Form
{
    readonly TextBox box;

    InputDialog(string title, string prompt, string value)
    {
        DarkTheme.Apply(this);
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ClientSize = new Size(480, 118);
        var label = new Label { Text = prompt, Location = new Point(12, 12), Size = new Size(456, 20) };
        box = new TextBox
        {
            Text = value,
            Location = new Point(12, 38),
            Size = new Size(456, 24),
            BackColor = DarkTheme.Field,
            ForeColor = DarkTheme.FieldFore,
            BorderStyle = BorderStyle.FixedSingle,
        };
        var ok = DarkTheme.Button("OK", DialogResult.OK, 302, 78);
        var cancel = DarkTheme.Button("Cancel", DialogResult.Cancel, 388, 78);
        AcceptButton = ok;
        CancelButton = cancel;
        Controls.AddRange(new Control[] { label, box, ok, cancel });
        Shown += (_, _) => { box.Focus(); box.SelectAll(); };
    }

    public static string? Ask(IWin32Window owner, string title, string prompt, string value = "")
    {
        using var d = new InputDialog(title, prompt, value);
        return d.ShowDialog(owner) == DialogResult.OK ? d.box.Text : null;
    }
}

/// <summary>Winamp's "Jump to file" (J): type to filter the playlist, Enter to play.</summary>
sealed class JumpDialog : Form
{
    readonly TextBox box;
    readonly ListBox list;
    readonly IReadOnlyList<Track> all;
    readonly Dictionary<Track, int> index = new();
    List<Track> shown = new();

    public Track? Selected { get; private set; }

    public JumpDialog(IReadOnlyList<Track> tracks)
    {
        all = tracks;
        for (int i = 0; i < tracks.Count; i++) index[tracks[i]] = i;
        DarkTheme.Apply(this);
        Text = "Jump to file";
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        ClientSize = new Size(560, 420);
        KeyPreview = true;

        box = new TextBox { Dock = DockStyle.Top, BackColor = DarkTheme.Field, ForeColor = DarkTheme.FieldFore, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 11f) };
        list = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = DarkTheme.Field,
            ForeColor = DarkTheme.FieldFore,
            BorderStyle = BorderStyle.None,
            IntegralHeight = false,
            Font = new Font("Segoe UI", 10f),
        };
        Controls.Add(list);
        Controls.Add(box);

        box.TextChanged += (_, _) => Filter();
        list.DoubleClick += (_, _) => Accept();
        KeyDown += OnKey;
        Filter();
    }

    void OnKey(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Enter: Accept(); e.Handled = e.SuppressKeyPress = true; break;
            case Keys.Escape: Close(); break;
            case Keys.Down when box.Focused && list.Items.Count > 0:
                list.SelectedIndex = Math.Min(list.Items.Count - 1, list.SelectedIndex + 1);
                e.Handled = true;
                break;
            case Keys.Up when box.Focused && list.Items.Count > 0:
                list.SelectedIndex = Math.Max(0, list.SelectedIndex - 1);
                e.Handled = true;
                break;
        }
    }

    void Filter()
    {
        var words = box.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        shown = all.Where(t => words.All(w =>
            t.DisplayTitle.Contains(w, StringComparison.CurrentCultureIgnoreCase) ||
            t.Path.Contains(w, StringComparison.CurrentCultureIgnoreCase))).ToList();
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var t in shown) list.Items.Add($"{index[t] + 1}. {t.DisplayTitle}");
        list.EndUpdate();
        if (shown.Count > 0) list.SelectedIndex = 0;
    }

    void Accept()
    {
        if (list.SelectedIndex < 0 || list.SelectedIndex >= shown.Count) return;
        Selected = shown[list.SelectedIndex];
        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>One-time guide for getting SOUNDBOY exports into rekordbox (steps adapt to its current settings).</summary>
sealed class RekordboxHelpDialog : Form
{
    readonly CheckBox dontShow;
    public bool DontShowAgain => dontShow.Checked;

    public RekordboxHelpDialog(string xmlPath, RekordboxExportResult? result, bool configuredInRekordbox, bool sidebarEnabled)
    {
        DarkTheme.Apply(this);
        Text = "Export to Rekordbox";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ClientSize = new Size(560, 400);

        var steps = new List<string>();
        if (!sidebarEnabled)
            steps.Add("In rekordbox, open Preferences (gear icon) › View › Layout and tick “rekordbox xml”.");
        if (!configuredInRekordbox)
            steps.Add("Preferences › Advanced › Database › rekordbox xml › Imported Library: click Browse and pick the file below.");
        else
            steps.Add("rekordbox is already set to read this file, so there's nothing to browse for.");
        steps.Add("In rekordbox's left sidebar open rekordbox xml › Playlists › SOUNDBOY (click the refresh icon next to “rekordbox xml” after each new export).");
        steps.Add("Right-click a playlist › Import Playlist, or drag tracks into your Collection. BPM and key come along; rekordbox analyzes beat grids and waveforms on import.");

        string headline = result == null
            ? "SOUNDBOY exports to a rekordbox XML library."
            : $"Exported to rekordbox: {result.Added} new, {result.Updated} updated, playlist “{result.PlaylistName}”.";

        var title = new Label { Text = headline, Font = new Font("Segoe UI Semibold", 10.5f), Location = new Point(18, 16), Size = new Size(524, 44) };
        var body = new Label
        {
            Text = "To see it in rekordbox" + (result?.CreatedFile == false ? ":" : " (one-time setup):") + "\n\n" +
                   string.Join("\n\n", steps.Select((s, i) => $"{i + 1}.  {s}")),
            Location = new Point(18, 64),
            Size = new Size(524, 216),
            ForeColor = DarkTheme.Fore,
        };
        var pathBox = new TextBox
        {
            Text = xmlPath,
            ReadOnly = true,
            Location = new Point(18, 288),
            Size = new Size(524, 24),
            BackColor = DarkTheme.Field,
            ForeColor = DarkTheme.FieldFore,
            BorderStyle = BorderStyle.FixedSingle,
        };
        dontShow = new CheckBox { Text = "Don't show this after every export", Location = new Point(18, 324), AutoSize = true, ForeColor = DarkTheme.Fore };

        var copy = DarkTheme.Button("Copy path", DialogResult.None, 18, 356);
        copy.Width = 100;
        copy.Click += (_, _) => { try { Clipboard.SetText(xmlPath); } catch { } };
        var open = DarkTheme.Button("Show file", DialogResult.None, 126, 356);
        open.Width = 100;
        open.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", File.Exists(xmlPath) ? $"/select,\"{xmlPath}\"" : $"\"{Path.GetDirectoryName(xmlPath)}\"") { UseShellExecute = true });
            }
            catch { }
        };
        var ok = DarkTheme.Button("OK", DialogResult.OK, 462, 356);
        AcceptButton = ok;
        CancelButton = ok;
        Controls.AddRange(new Control[] { title, body, pathBox, dontShow, copy, open, ok });
        ActiveControl = ok;
    }
}

sealed class FileInfoDialog : Form
{
    public FileInfoDialog(Track t)
    {
        DarkTheme.Apply(this);
        Text = "File info";
        FormBorderStyle = FormBorderStyle.Sizable;
        ClientSize = new Size(560, 380);

        var lv = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HeaderStyle = ColumnHeaderStyle.None,
            BackColor = DarkTheme.Field,
            ForeColor = DarkTheme.FieldFore,
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 9.5f),
        };
        lv.Columns.Add("Field", 110);
        lv.Columns.Add("Value", 420);

        long size = 0;
        try { if (!t.IsUrl) size = new FileInfo(t.Path).Length; } catch { }
        var rows = new (string, string?)[]
        {
            ("Title", t.Title),
            ("Artist", t.Artist),
            ("Album", t.Album),
            ("Year", t.Year > 0 ? t.Year.ToString() : null),
            ("Genre", t.Genre),
            ("Length", t.Duration is { } d ? Fmt.Length(d) : null),
            ("Bitrate", t.Bitrate > 0 ? $"{t.Bitrate} kbps" : null),
            ("Sample rate", t.SampleRate > 0 ? $"{t.SampleRate} Hz" : null),
            ("Channels", t.Channels switch { 0 => null, 1 => "Mono", 2 => "Stereo", var n => $"{n} channels" }),
            ("Format", t.Codec),
            ("BPM", t.DisplayBpm is double bpm ? $"{bpm:0.#}{(t.TagBpm != null ? " (from tags)" : "")}" : null),
            ("Key", t.Key != null ? $"{t.Key} ({t.Camelot} Camelot)" : null),
            ("File size", size > 0 ? $"{size / 1048576.0:0.00} MB ({size:N0} bytes)" : null),
            ("Location", t.Path),
        };
        foreach (var (k, v) in rows)
            lv.Items.Add(new ListViewItem(new[] { k, v ?? "" }));
        lv.Resize += (_, _) => lv.Columns[1].Width = Math.Max(100, lv.ClientSize.Width - lv.Columns[0].Width - 4);

        var bar = new Panel { Dock = DockStyle.Bottom, Height = 44 };
        var folder = DarkTheme.Button("Show in folder", DialogResult.None, 12, 8);
        folder.Width = 120;
        folder.Enabled = !t.IsUrl && File.Exists(t.Path);
        folder.Click += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{t.Path}\"") { UseShellExecute = true });
        var close = DarkTheme.Button("Close", DialogResult.OK, 0, 8);
        close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        close.Left = bar.Width - close.Width - 12;
        bar.Controls.Add(folder);
        bar.Controls.Add(close);
        AcceptButton = close;
        CancelButton = close;

        Controls.Add(lv);
        Controls.Add(bar);
    }
}
