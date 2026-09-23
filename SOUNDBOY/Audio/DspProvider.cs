using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SOUNDBOY.Audio;

/// <summary>
/// Decoder -> stereo -> preamp + 10-band EQ -> visualizer tap -> volume/balance -> clamp.
/// Also owns the decoder stream and serializes seeks against the audio thread.
/// </summary>
sealed class DspProvider : ISampleProvider, IDisposable
{
    readonly object sync = new();
    readonly WaveStream stream;
    readonly ISampleProvider source;
    readonly EqSettings eq;
    readonly SampleTap tap;
    readonly Biquad[] left = new Biquad[EqSettings.BandCount];
    readonly Biquad[] right = new Biquad[EqSettings.BandCount];
    readonly bool[] active = new bool[EqSettings.BandCount];
    float preampGain = 1f;
    bool eqOn;
    int eqVersion = -1;
    float[] mono = new float[8192];
    volatile bool disposed;
    bool streamDisposed;

    public float Volume { get; set; } = 1f;
    public float Balance { get; set; }
    public WaveFormat WaveFormat { get; }
    public int SourceChannels { get; }
    public int SourceSampleRate { get; }

    public DspProvider(WaveStream stream, ISampleProvider samples, EqSettings eq, SampleTap tap)
    {
        this.stream = stream;
        this.eq = eq;
        this.tap = tap;
        SourceChannels = samples.WaveFormat.Channels;
        SourceSampleRate = samples.WaveFormat.SampleRate;
        source = SourceChannels switch
        {
            1 => new MonoToStereoSampleProvider(samples),
            2 => samples,
            _ => new DownmixToStereo(samples),
        };
        WaveFormat = source.WaveFormat;
        for (int i = 0; i < EqSettings.BandCount; i++)
        {
            left[i] = new Biquad();
            right[i] = new Biquad();
        }
    }

    public TimeSpan Position
    {
        get { try { return streamDisposed ? TimeSpan.Zero : stream.CurrentTime; } catch { return TimeSpan.Zero; } }
    }

    public TimeSpan Duration
    {
        get
        {
            try { return !streamDisposed && stream.CanSeek && stream.Length > 0 ? stream.TotalTime : TimeSpan.Zero; }
            catch { return TimeSpan.Zero; }
        }
    }

    public bool CanSeek => Duration > TimeSpan.Zero;

    public void Seek(TimeSpan t)
    {
        lock (sync)
        {
            if (disposed || !CanSeek) return;
            var dur = Duration;
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            if (t > dur) t = dur;
            try { stream.CurrentTime = t; } catch { }
            for (int i = 0; i < EqSettings.BandCount; i++) { left[i].Reset(); right[i].Reset(); }
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        lock (sync)
        {
            if (disposed) { DisposeStream(); return 0; }
            int n;
            try { n = source.Read(buffer, offset, count); }
            catch { n = 0; } // corrupt frame / dropped stream: treat as end of track
            if (disposed) { DisposeStream(); return 0; }
            n &= ~1;
            if (n > 0) Process(buffer, offset, n);
            return n;
        }
    }

    void Process(float[] buf, int offset, int n)
    {
        int v = eq.Version;
        if (v != eqVersion) { eqVersion = v; RebuildFilters(); }

        float vol = Volume;
        float gain = vol * vol; // perceptual taper
        float bal = Balance;
        float gl = gain * (bal > 0 ? 1 - bal : 1);
        float gr = gain * (bal < 0 ? 1 + bal : 1);

        int frames = n / 2;
        if (mono.Length < frames) mono = new float[frames];

        for (int f = 0, i = offset; f < frames; f++, i += 2)
        {
            float l = buf[i], r = buf[i + 1];
            if (eqOn)
            {
                l *= preampGain;
                r *= preampGain;
                for (int b = 0; b < EqSettings.BandCount; b++)
                {
                    if (!active[b]) continue;
                    l = left[b].Process(l);
                    r = right[b].Process(r);
                }
            }
            mono[f] = (l + r) * 0.5f;
            buf[i] = Math.Clamp(l * gl, -1f, 1f);
            buf[i + 1] = Math.Clamp(r * gr, -1f, 1f);
        }
        tap.Write(mono, frames, WaveFormat.SampleRate);
    }

    void RebuildFilters()
    {
        eqOn = eq.Enabled;
        preampGain = (float)Math.Pow(10, eq.Preamp / 20.0);
        int sr = WaveFormat.SampleRate;
        for (int b = 0; b < EqSettings.BandCount; b++)
        {
            float g = eq[b];
            float f = EqSettings.Frequencies[b];
            active[b] = eqOn && Math.Abs(g) > 0.05f && f < sr * 0.45f;
            if (active[b])
            {
                left[b].SetPeaking(sr, f, 1.1, g);
                right[b].SetPeaking(sr, f, 1.1, g);
            }
            else
            {
                left[b].Reset();
                right[b].Reset();
            }
        }
    }

    public void Dispose()
    {
        disposed = true;
        // If the audio thread is stuck inside a (network) read, it disposes the stream itself when it returns.
        if (Monitor.TryEnter(sync, 300))
        {
            try { DisposeStream(); }
            finally { Monitor.Exit(sync); }
        }
    }

    void DisposeStream()
    {
        if (streamDisposed) return;
        streamDisposed = true;
        try { stream.Dispose(); } catch { }
    }
}

/// <summary>Folds 3+ channel audio (e.g. 5.1 FLAC) down to stereo.</summary>
sealed class DownmixToStereo : ISampleProvider
{
    readonly ISampleProvider src;
    readonly int ch;
    float[] buf = Array.Empty<float>();

    public WaveFormat WaveFormat { get; }

    public DownmixToStereo(ISampleProvider s)
    {
        src = s;
        ch = s.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(s.WaveFormat.SampleRate, 2);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int frames = count / 2;
        int need = frames * ch;
        if (buf.Length < need) buf = new float[need];
        int got = src.Read(buf, 0, need) / ch;
        for (int f = 0; f < got; f++)
        {
            int b = f * ch;
            float l = buf[b], r = buf[b + 1];
            if (ch >= 3) { float c = buf[b + 2] * 0.707f; l += c; r += c; }
            if (ch >= 6) { l += buf[b + 4] * 0.707f; r += buf[b + 5] * 0.707f; }
            buffer[offset + f * 2] = l * 0.6f;
            buffer[offset + f * 2 + 1] = r * 0.6f;
        }
        return got * 2;
    }
}
