namespace SOUNDBOY.Model;

public sealed class Track
{
    public string Path { get; }
    public bool IsUrl { get; }
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public string? Genre { get; set; }
    public int Year { get; set; }
    public TimeSpan? Duration { get; set; }
    public int Bitrate { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public string? Codec { get; set; }
    public bool Selected { get; set; }
    public bool Missing { get; set; }
    public volatile bool InfoLoaded;

    /// <summary>BPM stored in the file's tags (e.g. by DJ software); preferred over detection when present.</summary>
    public double? TagBpm { get; set; }
    public double? Bpm { get; set; }
    public string? Key { get; set; }
    public string? Camelot { get; set; }
    /// <summary>True while tempo/key detection is running for this track.</summary>
    public bool Analyzing { get; set; }
    public bool Analyzed { get; set; }

    public double? DisplayBpm => TagBpm ?? Bpm;

    /// <summary>When set, embedded tags never override Title/Artist (used for the bundled sample).</summary>
    public bool FixedTitle { get; set; }

    public Track(string path)
    {
        Path = path;
        IsUrl = MediaFiles.IsUrl(path);
    }

    public string FileNameTitle => IsUrl ? Path : System.IO.Path.GetFileNameWithoutExtension(Path);

    public string DisplayTitle
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Title)) return FileNameTitle;
            return string.IsNullOrWhiteSpace(Artist) ? Title! : $"{Artist} - {Title}";
        }
    }

    /// <summary>Title without the artist (the player shows the artist on its own line).</summary>
    public string DisplayTitleOnly => string.IsNullOrWhiteSpace(Title) ? FileNameTitle : Title!;

    /// <summary>Embedded cover art (front cover preferred), or null.</summary>
    public byte[]? ReadArtwork()
    {
        if (IsUrl) return null;
        try
        {
            using var f = TagLib.File.Create(Path);
            var pics = f.Tag.Pictures;
            if (pics == null || pics.Length == 0) return null;
            var pic = pics.FirstOrDefault(p => p.Type == TagLib.PictureType.FrontCover) ?? pics[0];
            return pic.Data?.Data;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Reads tags and stream properties. Safe to call from a background thread.</summary>
    public void LoadInfo()
    {
        if (IsUrl) { InfoLoaded = true; return; }
        try
        {
            using var f = TagLib.File.Create(Path);
            var t = f.Tag;
            if (!FixedTitle && !string.IsNullOrWhiteSpace(t.Title))
            {
                Title = t.Title.Trim();
                Artist = (t.JoinedPerformers ?? t.FirstAlbumArtist)?.Trim();
            }
            if (!FixedTitle)
            {
                Album = t.Album;
                Genre = t.FirstGenre;
                Year = (int)t.Year;
            }
            if (t.BeatsPerMinute > 0) TagBpm = t.BeatsPerMinute;
            var p = f.Properties;
            if (p != null)
            {
                if (p.Duration > TimeSpan.Zero) Duration = p.Duration;
                Bitrate = p.AudioBitrate;
                SampleRate = p.AudioSampleRate;
                Channels = p.AudioChannels;
                Codec = p.Description;
            }
            Missing = false;
        }
        catch
        {
            Missing = !File.Exists(Path);
        }
        InfoLoaded = true;
    }
}
