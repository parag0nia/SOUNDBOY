using System.Globalization;
using System.Text;

namespace SOUNDBOY.Model;

public static class MediaFiles
{
    public static readonly string[] AudioExtensions =
    {
        ".mp3", ".mp2", ".wav", ".flac", ".ogg", ".oga", ".m4a", ".m4b", ".aac", ".mp4", ".wma", ".aif", ".aiff", ".ac3",
    };

    public static readonly string[] PlaylistExtensions = { ".m3u", ".m3u8", ".pls" };

    public static bool IsUrl(string s) =>
        s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    public static bool IsAudio(string path) =>
        AudioExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static bool IsPlaylist(string path) =>
        PlaylistExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Turns files, folders (recursive), playlists and URLs into tracks.</summary>
    public static IEnumerable<Track> Expand(IEnumerable<string> paths)
    {
        foreach (var raw in paths)
        {
            var p = raw.Trim().Trim('"');
            if (p.Length == 0) continue;
            if (IsUrl(p)) { yield return new Track(p); continue; }

            if (Directory.Exists(p))
            {
                List<string> files;
                try
                {
                    files = Directory
                        .EnumerateFiles(p, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                        .Where(IsAudio)
                        .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
                }
                catch { continue; }
                foreach (var f in files) yield return new Track(f);
                continue;
            }

            if (IsPlaylist(p) && File.Exists(p))
            {
                List<Track> list;
                try { list = PlaylistIO.Load(p); } catch { continue; }
                foreach (var t in list) yield return t;
                continue;
            }

            if (File.Exists(p)) yield return new Track(p);
        }
    }
}

public static class PlaylistIO
{
    public static List<Track> Load(string path)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full)!;
        var lines = ReadLines(full);
        return Path.GetExtension(full).Equals(".pls", StringComparison.OrdinalIgnoreCase)
            ? LoadPls(lines, dir)
            : LoadM3u(lines, dir);
    }

    static string[] ReadLines(string path)
    {
        var bytes = File.ReadAllBytes(path);
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { text = Encoding.Latin1.GetString(bytes); }
        return text.TrimStart('﻿').Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
    }

    static List<Track> LoadM3u(string[] lines, string dir)
    {
        var list = new List<Track>();
        string? title = null;
        TimeSpan? dur = null;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                var body = line[8..];
                int comma = body.IndexOf(',');
                var secs = (comma >= 0 ? body[..comma] : body).Trim().Split(' ')[0];
                if (double.TryParse(secs, NumberStyles.Float, CultureInfo.InvariantCulture, out var s) && s > 0)
                    dur = TimeSpan.FromSeconds(s);
                title = comma >= 0 ? body[(comma + 1)..].Trim() : null;
                continue;
            }
            if (line.StartsWith('#')) continue;
            list.Add(MakeTrack(line, dir, title, dur));
            title = null;
            dur = null;
        }
        return list;
    }

    static List<Track> LoadPls(string[] lines, string dir)
    {
        var files = new SortedDictionary<int, string>();
        var titles = new Dictionary<int, string>();
        var lengths = new Dictionary<int, double>();
        foreach (var raw in lines)
        {
            int eq = raw.IndexOf('=');
            if (eq < 0) continue;
            var key = raw[..eq].Trim();
            var val = raw[(eq + 1)..].Trim();
            if (TryKey(key, "File", out int n)) files[n] = val;
            else if (TryKey(key, "Title", out n)) titles[n] = val;
            else if (TryKey(key, "Length", out n) && double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var l)) lengths[n] = l;
        }
        return files.Select(kv => MakeTrack(kv.Value, dir,
            titles.GetValueOrDefault(kv.Key),
            lengths.TryGetValue(kv.Key, out var l) && l > 0 ? TimeSpan.FromSeconds(l) : null)).ToList();
    }

    static bool TryKey(string key, string prefix, out int n)
    {
        n = 0;
        return key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && int.TryParse(key[prefix.Length..], out n);
    }

    static Track MakeTrack(string entry, string dir, string? title, TimeSpan? dur)
    {
        string p = entry;
        if (!MediaFiles.IsUrl(p))
        {
            try
            {
                if (p.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) p = new Uri(p).LocalPath;
                else if (!Path.IsPathRooted(p)) p = Path.GetFullPath(Path.Combine(dir, p));
            }
            catch { }
        }
        var t = new Track(p) { Duration = dur };
        if (!string.IsNullOrWhiteSpace(title)) t.Title = title;
        return t;
    }

    public static void Save(string path, IEnumerable<Track> tracks)
    {
        var sb = new StringBuilder();
        var list = tracks.ToList();
        static string Secs(Track t) => t.Duration is { } d ? ((int)d.TotalSeconds).ToString(CultureInfo.InvariantCulture) : "-1";

        if (Path.GetExtension(path).Equals(".pls", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine("[playlist]");
            for (int i = 0; i < list.Count; i++)
            {
                sb.AppendLine($"File{i + 1}={list[i].Path}");
                sb.AppendLine($"Title{i + 1}={list[i].DisplayTitle}");
                sb.AppendLine($"Length{i + 1}={Secs(list[i])}");
            }
            sb.AppendLine($"NumberOfEntries={list.Count}");
            sb.AppendLine("Version=2");
        }
        else
        {
            sb.AppendLine("#EXTM3U");
            foreach (var t in list)
            {
                sb.AppendLine($"#EXTINF:{Secs(t)},{t.DisplayTitle}");
                sb.AppendLine(t.Path);
            }
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }
}
