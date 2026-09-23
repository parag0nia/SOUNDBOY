using System.Text.Json;
using SOUNDBOY.Audio;

namespace SOUNDBOY.Model;

/// <summary>
/// Remembers BPM/key results per file (keyed by path + size + modified time) in
/// %APPDATA%\SOUNDBOY\analysis.json so each file is analyzed only once.
/// </summary>
public sealed class AnalysisCache
{
    sealed record Entry(double? Bpm, string? Key, string? Camelot);

    static string FilePath => Path.Combine(AppSettings.Dir, "analysis.json");
    readonly Dictionary<string, Entry> entries;
    readonly object sync = new();

    public AnalysisCache()
    {
        try
        {
            entries = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(FilePath)) ?? new()
                : new();
        }
        catch
        {
            entries = new();
        }
    }

    static string? KeyFor(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            return fi.Exists ? $"{fi.FullName.ToLowerInvariant()}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}" : null;
        }
        catch
        {
            return null;
        }
    }

    public AnalysisResult? Get(string path)
    {
        var k = KeyFor(path);
        lock (sync)
            return k != null && entries.TryGetValue(k, out var e) ? new AnalysisResult(e.Bpm, e.Key, e.Camelot) : null;
    }

    public void Put(string path, AnalysisResult r)
    {
        var k = KeyFor(path);
        if (k == null) return;
        lock (sync) entries[k] = new Entry(r.Bpm, r.Key, r.Camelot);
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppSettings.Dir);
            string json;
            lock (sync) json = JsonSerializer.Serialize(entries);
            File.WriteAllText(FilePath, json);
        }
        catch { }
    }
}
