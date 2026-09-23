using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SOUNDBOY.Model;

namespace SOUNDBOY.Audio;

public enum PlayerState { Stopped, Playing, Paused }

/// <summary>
/// Playback engine. Must be driven from the UI thread: WaveOutEvent captures the creating thread's
/// SynchronizationContext, so TrackEnded / PlaybackError are raised on the UI thread.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    WaveOutEvent? output;
    DspProvider? dsp;
    float volume = 0.8f;
    float balance;

    public const int OutputLatencyMs = 120;

    public EqSettings Eq { get; } = new();
    public SampleTap Tap { get; } = new();
    public PlayerState State { get; private set; }
    public bool IsLoaded => dsp != null;
    public string? LoadedSource { get; private set; }

    public event EventHandler? TrackEnded;
    public event EventHandler<Exception>? PlaybackError;

    public float Volume
    {
        get => volume;
        set { volume = Math.Clamp(value, 0f, 1f); if (dsp != null) dsp.Volume = volume; }
    }

    /// <summary>-1 = full left, 0 = center, 1 = full right.</summary>
    public float Balance
    {
        get => balance;
        set { balance = Math.Clamp(value, -1f, 1f); if (dsp != null) dsp.Balance = balance; }
    }

    public TimeSpan Position => dsp?.Position ?? TimeSpan.Zero;
    public TimeSpan Duration => dsp?.Duration ?? TimeSpan.Zero;
    public bool CanSeek => dsp?.CanSeek ?? false;
    public int SourceChannels => dsp?.SourceChannels ?? 0;
    public int SourceSampleRate => dsp?.SourceSampleRate ?? 0;

    public void Load(string source)
    {
        Unload();
        var (stream, samples) = OpenReader(source);
        dsp = new DspProvider(stream, samples, Eq, Tap) { Volume = volume, Balance = balance };
        LoadedSource = source;
        State = PlayerState.Stopped;
    }

    public void Play()
    {
        if (dsp == null) return;
        if (State == PlayerState.Playing) return;
        if (State == PlayerState.Paused && output != null)
        {
            output.Play();
            State = PlayerState.Playing;
            return;
        }
        if (output == null)
        {
            output = new WaveOutEvent { DesiredLatency = OutputLatencyMs, NumberOfBuffers = 3 };
            output.PlaybackStopped += OnPlaybackStopped;
            output.Init(new SampleToWaveProvider16(dsp));
        }
        Tap.Clear();
        output.Play();
        State = PlayerState.Playing;
    }

    public void Pause()
    {
        if (State != PlayerState.Playing || output == null) return;
        output.Pause();
        State = PlayerState.Paused;
    }

    public void Stop()
    {
        KillOutput();
        dsp?.Seek(TimeSpan.Zero);
        State = PlayerState.Stopped;
        Tap.Clear();
    }

    public void Seek(TimeSpan t) => dsp?.Seek(t);

    public void Unload()
    {
        KillOutput();
        dsp?.Dispose();
        dsp = null;
        LoadedSource = null;
        State = PlayerState.Stopped;
        Tap.Clear();
    }

    void KillOutput()
    {
        if (output == null) return;
        output.PlaybackStopped -= OnPlaybackStopped;
        try { output.Stop(); } catch { }
        output.Dispose();
        output = null;
    }

    void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (!ReferenceEquals(sender, output)) return;
        KillOutput();
        dsp?.Seek(TimeSpan.Zero);
        State = PlayerState.Stopped;
        if (e.Exception != null) PlaybackError?.Invoke(this, e.Exception);
        else TrackEnded?.Invoke(this, EventArgs.Empty);
    }

    public static (WaveStream, ISampleProvider) OpenReader(string source)
    {
        if (MediaFiles.IsUrl(source))
        {
            var mf = new MediaFoundationReader(source);
            return (mf, mf.ToSampleProvider());
        }

        var ext = Path.GetExtension(source).ToLowerInvariant();
        if (ext is ".ogg" or ".oga")
        {
            var v = new VorbisWaveReader(source);
            return (v, v);
        }

        try
        {
            var a = new AudioFileReader(source);
            return (a, a);
        }
        catch when (File.Exists(source))
        {
            // Some MP3s trip up the ACM decoder; Media Foundation is more forgiving.
            var mf = new MediaFoundationReader(source);
            return (mf, mf.ToSampleProvider());
        }
    }

    public void Dispose() => Unload();
}
