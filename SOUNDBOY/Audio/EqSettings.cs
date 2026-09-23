namespace SOUNDBOY.Audio;

/// <summary>
/// Shared 10-band equalizer state. The UI writes it; the audio thread polls <see cref="Version"/>
/// and rebuilds its filters when it changes.
/// </summary>
public sealed class EqSettings
{
    public const int BandCount = 10;
    public const float MaxDb = 12f;

    public static readonly float[] Frequencies = { 60, 170, 310, 600, 1000, 3000, 6000, 12000, 14000, 16000 };
    public static readonly string[] Labels = { "60", "170", "310", "600", "1K", "3K", "6K", "12K", "14K", "16K" };

    readonly float[] gains = new float[BandCount];
    float preamp;
    bool enabled = true;
    int version;

    public event Action? Changed;

    public int Version => Volatile.Read(ref version);

    public bool Enabled
    {
        get => enabled;
        set { if (enabled == value) return; enabled = value; Bump(); }
    }

    public float Preamp
    {
        get => preamp;
        set { preamp = Clamp(value); Bump(); }
    }

    public float this[int band]
    {
        get => gains[band];
        set { gains[band] = Clamp(value); Bump(); }
    }

    public float[] Gains => (float[])gains.Clone();

    public void Set(float[] bands, float pre)
    {
        for (int i = 0; i < BandCount; i++)
            gains[i] = i < bands.Length ? Clamp(bands[i]) : 0f;
        preamp = Clamp(pre);
        Bump();
    }

    static float Clamp(float v) => Math.Clamp(v, -MaxDb, MaxDb);

    void Bump()
    {
        Interlocked.Increment(ref version);
        Changed?.Invoke();
    }
}
