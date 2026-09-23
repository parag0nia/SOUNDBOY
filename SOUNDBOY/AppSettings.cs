using System.Text.Json;

namespace SOUNDBOY;

public enum RepeatMode { Off, All, One }
public enum VisMode { Spectrum, Oscilloscope, Off }

public sealed class AppSettings
{
    public float Volume { get; set; } = 0.8f;
    public float Balance { get; set; }
    public bool Shuffle { get; set; }
    public RepeatMode Repeat { get; set; }
    public bool EqEnabled { get; set; } = true;
    public float EqPreamp { get; set; }
    public float[] EqBands { get; set; } = new float[10];
    public Dictionary<string, float[]> UserPresets { get; set; } = new();
    public bool ShowEq { get; set; } = true;
    public bool ShowPlaylist { get; set; } = true;
    public bool Shaded { get; set; }
    public bool AlwaysOnTop { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool ShowPeaks { get; set; } = true;
    public bool RemainingTime { get; set; }
    public VisMode Vis { get; set; }
    public float Zoom { get; set; } = 1f;
    public int PlaylistHeight { get; set; } = 260;
    public int X { get; set; } = int.MinValue;
    public int Y { get; set; } = int.MinValue;
    public int CurrentIndex { get; set; } = -1;
    public string? LastFolder { get; set; }
    /// <summary>Custom rekordbox XML export file; null = the file rekordbox is configured to read.</summary>
    public string? RekordboxXmlPath { get; set; }
    public bool RekordboxHelpShown { get; set; }

    /// <summary>
    /// Optional isolated profile (env var SOUNDBOY_PROFILE): separate settings folder and its own
    /// single-instance lock, so a test/second profile never touches the main one.
    /// </summary>
    public static string Profile { get; } =
        new string((Environment.GetEnvironmentVariable("SOUNDBOY_PROFILE") ?? "").Where(char.IsLetterOrDigit).ToArray());

    public static string Dir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     Profile.Length == 0 ? "SOUNDBOY" : "SOUNDBOY-" + Profile);

    public static string PlaylistFile => Path.Combine(Dir, "playlist.m3u8");
    static string SettingsFile => Path.Combine(Dir, "settings.json");

    /// <summary>The app was previously named DunnoMusic; carry its settings and playlist over once.</summary>
    static void MigrateFromDunnoMusic()
    {
        try
        {
            var old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DunnoMusic");
            if (Directory.Exists(Dir) || !Directory.Exists(old)) return;
            Directory.CreateDirectory(Dir);
            foreach (var name in new[] { "settings.json", "playlist.m3u8" })
            {
                var src = Path.Combine(old, name);
                if (!File.Exists(src)) continue;
                var text = File.ReadAllText(src).Replace(old, Dir, StringComparison.OrdinalIgnoreCase);
                File.WriteAllText(Path.Combine(Dir, name), text);
            }
        }
        catch { }
    }

    public static AppSettings Load()
    {
        if (Profile.Length == 0) MigrateFromDunnoMusic();
        AppSettings s;
        try
        {
            s = File.Exists(SettingsFile)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile)) ?? new()
                : new();
        }
        catch
        {
            s = new();
        }
        if (s.EqBands is not { Length: 10 }) s.EqBands = new float[10];
        s.UserPresets ??= new();
        s.Zoom = Math.Clamp(s.Zoom, 0.5f, 3f);
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
