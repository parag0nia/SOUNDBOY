using System.Diagnostics;

namespace SOUNDBOY.Audio;

/// <summary>
/// Ring buffer of recent mono samples for the visualizer. Reads are shifted back by the output
/// latency (and forward by the time since the last write) so the display lines up with what is audible.
/// </summary>
public sealed class SampleTap
{
    const int Size = 1 << 17;
    const int Mask = Size - 1;

    readonly float[] ring = new float[Size];
    readonly object sync = new();
    long written;
    long lastWriteTicks;
    int sampleRate = 44100;

    public int SampleRate => sampleRate;

    public void Write(float[] mono, int count, int rate)
    {
        lock (sync)
        {
            for (int i = 0; i < count; i++)
                ring[(written + i) & Mask] = mono[i];
            written += count;
            sampleRate = rate;
            lastWriteTicks = Stopwatch.GetTimestamp();
        }
    }

    public void Clear()
    {
        lock (sync)
        {
            Array.Clear(ring);
            written = 0;
        }
    }

    public void Read(float[] dest, int latencyMs)
    {
        lock (sync)
        {
            if (written == 0) { Array.Clear(dest); return; }
            double elapsed = (Stopwatch.GetTimestamp() - lastWriteTicks) / (double)Stopwatch.Frequency;
            long end = written - (long)(latencyMs / 1000.0 * sampleRate) + (long)(elapsed * sampleRate);
            end = Math.Min(end, written);
            long start = end - dest.Length;
            long oldest = written - Size;
            for (int i = 0; i < dest.Length; i++)
            {
                long idx = start + i;
                dest[i] = idx < 0 || idx < oldest || idx >= written ? 0f : ring[idx & Mask];
            }
        }
    }
}
