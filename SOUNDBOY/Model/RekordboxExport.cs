using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace SOUNDBOY.Model;

public sealed record RekordboxExportResult(string XmlPath, int Added, int Updated, string PlaylistName, bool CreatedFile);

/// <summary>
/// Writes tracks into a rekordbox XML library (the DJ_PLAYLISTS format rekordbox imports via its
/// "rekordbox xml" sidebar). Existing content is merged, never discarded: tracks are matched by file
/// location, and SOUNDBOY only touches its own "SOUNDBOY" playlist folder.
/// </summary>
public static class RekordboxExport
{
    const string FolderName = "SOUNDBOY";
    const string AllPlaylistName = "All SOUNDBOY exports";

    static readonly Dictionary<string, string> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp3"] = "MP3 File",
        [".m4a"] = "M4A File",
        [".aac"] = "M4A File",
        [".wav"] = "WAV File",
        [".aif"] = "AIFF File",
        [".aiff"] = "AIFF File",
        [".flac"] = "FLAC File",
    };

    /// <summary>Formats rekordbox can play (it can't load OGG, WMA, MP2, AC3 or streams).</summary>
    public static bool IsSupported(Track t) => !t.IsUrl && Kinds.ContainsKey(Path.GetExtension(t.Path));

    static string RekordboxSettingsFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pioneer", "rekordbox6", "rekordbox3.settings");

    static string? ReadRekordboxSetting(string name)
    {
        try
        {
            if (!File.Exists(RekordboxSettingsFile)) return null;
            var doc = XDocument.Load(RekordboxSettingsFile);
            return doc.Descendants("VALUE").FirstOrDefault(v => (string?)v.Attribute("name") == name)?.Attribute("val")?.Value;
        }
        catch
        {
            return null;
        }
    }

    public static bool RekordboxInstalled => File.Exists(RekordboxSettingsFile);

    /// <summary>The XML file rekordbox is set to read (Preferences › Advanced › Database › rekordbox xml).</summary>
    public static string? RekordboxConfiguredXmlPath
    {
        get
        {
            var v = ReadRekordboxSetting("bridgeImportedLibraryFile");
            return string.IsNullOrWhiteSpace(v) ? null : Path.GetFullPath(v.Replace('/', Path.DirectorySeparatorChar));
        }
    }

    /// <summary>Whether the "rekordbox xml" entry is shown in rekordbox's sidebar (Preferences › View › Layout).</summary>
    public static bool XmlSidebarEnabled => ReadRekordboxSetting("showRbXml") == "1";

    public static string DefaultXmlPath =>
        RekordboxConfiguredXmlPath ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "SOUNDBOY", "SOUNDBOY-rekordbox.xml");

    public static RekordboxExportResult Export(string xmlPath, IReadOnlyList<Track> tracks, string playlistName)
    {
        bool existed = File.Exists(xmlPath);
        XDocument doc;
        if (existed)
        {
            try { doc = XDocument.Load(xmlPath); }
            catch (Exception ex) { throw new InvalidDataException($"The existing rekordbox XML file can't be read ({ex.Message}). It was left untouched.", ex); }
            if (doc.Root?.Name != "DJ_PLAYLISTS") throw new InvalidDataException("The existing file isn't a rekordbox XML library. It was left untouched.");

            // Back up a library written by another app before SOUNDBOY first modifies it.
            var product = (string?)doc.Root.Element("PRODUCT")?.Attribute("Name");
            string backup = xmlPath + ".before-soundboy.bak";
            if (product != "SOUNDBOY" && !File.Exists(backup)) File.Copy(xmlPath, backup);
        }
        else
        {
            doc = new XDocument(new XDeclaration("1.0", "UTF-8", null),
                new XElement("DJ_PLAYLISTS", new XAttribute("Version", "1.0.0"),
                    new XElement("PRODUCT", new XAttribute("Name", "SOUNDBOY"), new XAttribute("Version", Program.Version), new XAttribute("Company", "SOUNDBOY"))));
        }

        var root = doc.Root!;
        var collection = root.Element("COLLECTION") ?? AddAfter(root, new XElement("COLLECTION"), "PRODUCT");
        var playlists = root.Element("PLAYLISTS") ?? AddAfter(root, new XElement("PLAYLISTS"), "COLLECTION");
        var rootNode = playlists.Elements("NODE").FirstOrDefault(n => (string?)n.Attribute("Name") == "ROOT");
        if (rootNode == null)
        {
            rootNode = new XElement("NODE", new XAttribute("Type", "0"), new XAttribute("Name", "ROOT"), new XAttribute("Count", "0"));
            playlists.Add(rootNode);
        }

        // Existing tracks by location, and the next free TrackID.
        var byLocation = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        int nextId = 1;
        foreach (var t in collection.Elements("TRACK"))
        {
            var loc = (string?)t.Attribute("Location");
            if (loc != null) byLocation[loc] = t;
            if (int.TryParse((string?)t.Attribute("TrackID"), out int id)) nextId = Math.Max(nextId, id + 1);
        }

        int added = 0, updated = 0;
        var ids = new List<string>();
        foreach (var t in tracks.Where(IsSupported).DistinctBy(t => t.Path, StringComparer.OrdinalIgnoreCase))
        {
            string loc = ToLocation(t.Path);
            if (byLocation.TryGetValue(loc, out var el))
            {
                Fill(el, t, isNew: false);
                updated++;
            }
            else
            {
                el = new XElement("TRACK", new XAttribute("TrackID", nextId++));
                Fill(el, t, isNew: true);
                collection.Add(el);
                byLocation[loc] = el;
                added++;
            }
            ids.Add((string)el.Attribute("TrackID")!);
        }
        collection.SetAttributeValue("Entries", collection.Elements("TRACK").Count());

        // ROOT › SOUNDBOY › [this export] and [All SOUNDBOY exports]
        var folder = rootNode.Elements("NODE").FirstOrDefault(n => (string?)n.Attribute("Type") == "0" && (string?)n.Attribute("Name") == FolderName);
        if (folder == null)
        {
            folder = new XElement("NODE", new XAttribute("Type", "0"), new XAttribute("Name", FolderName), new XAttribute("Count", "0"));
            rootNode.Add(folder);
        }
        var all = Playlist(folder, AllPlaylistName, keepExisting: true);
        var existing = new HashSet<string>(all.Elements("TRACK").Select(e => (string?)e.Attribute("Key") ?? ""));
        foreach (var id in ids.Where(existing.Add)) all.Add(new XElement("TRACK", new XAttribute("Key", id)));
        all.SetAttributeValue("Entries", all.Elements("TRACK").Count());

        var list = Playlist(folder, playlistName, keepExisting: false);
        foreach (var id in ids) list.Add(new XElement("TRACK", new XAttribute("Key", id)));
        list.SetAttributeValue("Entries", ids.Count);

        folder.SetAttributeValue("Count", folder.Elements("NODE").Count());
        rootNode.SetAttributeValue("Count", rootNode.Elements("NODE").Count());

        Directory.CreateDirectory(Path.GetDirectoryName(xmlPath)!);
        string tmp = xmlPath + ".tmp";
        using (var w = new StreamWriter(tmp, false, new UTF8Encoding(false)))
            doc.Save(w);
        File.Move(tmp, xmlPath, overwrite: true);
        return new RekordboxExportResult(xmlPath, added, updated, playlistName, !existed);
    }

    static XElement AddAfter(XElement root, XElement el, string previous)
    {
        var prev = root.Element(previous);
        if (prev != null) prev.AddAfterSelf(el);
        else root.Add(el);
        return el;
    }

    static XElement Playlist(XElement folder, string name, bool keepExisting)
    {
        var node = folder.Elements("NODE").FirstOrDefault(n => (string?)n.Attribute("Type") == "1" && (string?)n.Attribute("Name") == name);
        if (node != null && !keepExisting) { node.Remove(); node = null; }
        if (node == null)
        {
            node = new XElement("NODE", new XAttribute("Name", name), new XAttribute("Type", "1"),
                new XAttribute("KeyType", "0"), new XAttribute("Entries", "0"));
            // Keep "All SOUNDBOY exports" first, then exports newest-first.
            if (name == AllPlaylistName) folder.AddFirst(node);
            else if (folder.Elements("NODE").FirstOrDefault() is { } first && (string?)first.Attribute("Name") == AllPlaylistName) first.AddAfterSelf(node);
            else folder.AddFirst(node);
        }
        return node;
    }

    static void Fill(XElement el, Track t, bool isNew)
    {
        var inv = CultureInfo.InvariantCulture;
        void Set(string name, object? value) { if (value != null) el.SetAttributeValue(name, Convert.ToString(value, inv)); }

        long size = 0;
        try { size = new FileInfo(t.Path).Length; } catch { }

        Set("Name", string.IsNullOrWhiteSpace(t.Title) ? t.FileNameTitle : t.Title);
        Set("Artist", t.Artist ?? "");
        Set("Album", t.Album ?? "");
        Set("Genre", t.Genre ?? "");
        Set("Kind", Kinds[Path.GetExtension(t.Path)]);
        Set("Size", size);
        Set("TotalTime", t.Duration is { } d ? (int)Math.Round(d.TotalSeconds) : 0);
        Set("Year", t.Year > 0 ? t.Year : 0);
        if (t.DisplayBpm is double bpm) Set("AverageBpm", bpm.ToString("0.00", inv));
        if (t.Key != null) Set("Tonality", t.Key);
        if (t.Bitrate > 0) Set("BitRate", t.Bitrate);
        if (t.SampleRate > 0) Set("SampleRate", t.SampleRate);
        if (isNew) Set("DateAdded", DateTime.Now.ToString("yyyy-MM-dd", inv));
        Set("Location", ToLocation(t.Path));
    }

    /// <summary>rekordbox location format: file://localhost/C:/Music/My%20Track.mp3</summary>
    public static string ToLocation(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.StartsWith(@"\\")) return new Uri(full).AbsoluteUri; // UNC: file://server/share/...
        var parts = full.Split('\\');
        return "file://localhost/" + parts[0] + "/" + string.Join("/", parts.Skip(1).Select(Uri.EscapeDataString));
    }
}
