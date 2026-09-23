using System.Text;

namespace SOUNDBOY.Audio;

public sealed record EqPreset(string Name, float[] Bands, float Preamp);

public static class EqPresets
{
    public static readonly EqPreset[] BuiltIn =
    {
        new("Classical",          new[] { 0f, 0, 0, 0, 0, 0, -7.2f, -7.2f, -7.2f, -9.6f }, 0),
        new("Club",               new[] { 0f, 0, 8, 5.6f, 5.6f, 5.6f, 3.2f, 0, 0, 0 }, 0),
        new("Dance",              new[] { 9.6f, 7.2f, 2.4f, 0, 0, -5.6f, -7.2f, -7.2f, 0, 0 }, 0),
        new("Full Bass",          new[] { -8f, 9.6f, 9.6f, 5.6f, 1.6f, -4, -8, -10.4f, -11.2f, -11.2f }, -3),
        new("Full Bass & Treble", new[] { 7.2f, 5.6f, 0, -7.2f, -4.8f, 1.6f, 8, 11.2f, 12, 12 }, -3),
        new("Full Treble",        new[] { -9.6f, -9.6f, -9.6f, -4, 2.4f, 11.2f, 12, 12, 12, 12 }, -3),
        new("Laptop Speakers",    new[] { 4.8f, 11.2f, 5.6f, -3.2f, -2.4f, 1.6f, 4.8f, 9.6f, 12, 12 }, -3),
        new("Large Hall",         new[] { 10.4f, 10.4f, 5.6f, 5.6f, 0, -4.8f, -4.8f, -4.8f, 0, 0 }, -2),
        new("Live",               new[] { -4.8f, 0, 4, 5.6f, 5.6f, 5.6f, 4, 2.4f, 2.4f, 2.4f }, 0),
        new("Party",              new[] { 7.2f, 7.2f, 0, 0, 0, 0, 0, 0, 7.2f, 7.2f }, 0),
        new("Pop",                new[] { -1.6f, 4.8f, 7.2f, 8, 5.6f, 0, -2.4f, -2.4f, -1.6f, -1.6f }, 0),
        new("Reggae",             new[] { 0f, 0, 0, -5.6f, 0, 6.4f, 6.4f, 0, 0, 0 }, 0),
        new("Rock",               new[] { 8f, 4.8f, -5.6f, -8, -3.2f, 4, 8.8f, 11.2f, 11.2f, 11.2f }, -3),
        new("Ska",                new[] { -2.4f, -4.8f, -4, 0, 4, 5.6f, 8.8f, 9.6f, 11.2f, 9.6f }, -2),
        new("Soft",               new[] { 4.8f, 1.6f, 0, -2.4f, 0, 4, 8, 9.6f, 11.2f, 12 }, -2),
        new("Soft Rock",          new[] { 4f, 4, 2.4f, 0, -4, -5.6f, -3.2f, 0, 2.4f, 8.8f }, 0),
        new("Techno",             new[] { 8f, 5.6f, 0, -5.6f, -4.8f, 0, 8, 9.6f, 9.6f, 8.8f }, -2),
    };

    // Winamp .EQF: 31-byte header, then records of 257-byte name + 11 bytes (10 bands, preamp).
    // Each byte is a slider position 0..63 where 0 is the top (+12 dB) and 31 is 0 dB.
    static readonly byte[] Header = Encoding.ASCII.GetBytes("Winamp EQ library file v1.1\u001a!--");
    const int NameLen = 257;

    static float ToDb(byte b) => Math.Clamp((31 - b) / 31f * 12f, -12f, 12f);
    static byte ToByte(float db) => (byte)Math.Clamp((int)Math.Round(31 - db / 12f * 31), 0, 63);

    public static List<EqPreset> ReadEqf(string path)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length < Header.Length || !data.AsSpan(0, 27).SequenceEqual(Header.AsSpan(0, 27)))
            throw new InvalidDataException("Not a Winamp EQ library (.eqf) file.");
        var list = new List<EqPreset>();
        for (int pos = Header.Length; pos + NameLen + 11 <= data.Length; pos += NameLen + 11)
        {
            int end = Array.IndexOf(data, (byte)0, pos, NameLen);
            string name = Encoding.Latin1.GetString(data, pos, (end < 0 ? pos + NameLen : end) - pos).Trim();
            var bands = new float[10];
            for (int i = 0; i < 10; i++) bands[i] = ToDb(data[pos + NameLen + i]);
            list.Add(new EqPreset(name.Length > 0 ? name : "Imported", bands, ToDb(data[pos + NameLen + 10])));
        }
        return list;
    }

    public static void WriteEqf(string path, IEnumerable<EqPreset> presets)
    {
        using var fs = File.Create(path);
        fs.Write(Header);
        foreach (var p in presets)
        {
            var name = new byte[NameLen];
            var raw = Encoding.Latin1.GetBytes(p.Name);
            Array.Copy(raw, name, Math.Min(raw.Length, NameLen - 1));
            fs.Write(name);
            for (int i = 0; i < 10; i++) fs.WriteByte(ToByte(p.Bands[i]));
            fs.WriteByte(ToByte(p.Preamp));
        }
    }
}
