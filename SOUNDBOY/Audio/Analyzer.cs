namespace SOUNDBOY.Audio;

/// <summary>FFT spectrum bars with falloff + peak caps, and an oscilloscope snapshot.</summary>
public sealed class Analyzer
{
    const int N = 2048;

    readonly float[] buf = new float[N];
    readonly double[] re = new double[N];
    readonly double[] im = new double[N];
    readonly float[] window = new float[N];
    readonly float[] peakHold;

    public float[] Bars { get; }
    public float[] Peaks { get; }
    public float[] Scope { get; } = new float[576];

    public Analyzer(int bars)
    {
        Bars = new float[bars];
        Peaks = new float[bars];
        peakHold = new float[bars];
        for (int i = 0; i < N; i++)
            window[i] = 0.5f * (1 - MathF.Cos(2 * MathF.PI * i / (N - 1)));
    }

    public void Update(SampleTap tap, int latencyMs, bool active, float dt)
    {
        if (active) tap.Read(buf, latencyMs);
        else Array.Clear(buf);

        Array.Copy(buf, N - Scope.Length, Scope, 0, Scope.Length);

        for (int i = 0; i < N; i++) { re[i] = buf[i] * window[i]; im[i] = 0; }
        Dsp.Fft(re, im);

        int sr = Math.Max(8000, tap.SampleRate);
        double fMin = 40, fMax = Math.Min(16000, sr / 2.0);
        double binHz = (double)sr / N;
        int nb = Bars.Length;

        for (int b = 0; b < nb; b++)
        {
            double f0 = fMin * Math.Pow(fMax / fMin, (double)b / nb);
            double f1 = fMin * Math.Pow(fMax / fMin, (double)(b + 1) / nb);
            int k0 = Math.Clamp((int)Math.Floor(f0 / binHz), 1, N / 2 - 1);
            int k1 = Math.Clamp((int)Math.Ceiling(f1 / binHz), k0 + 1, N / 2);
            double max = 0;
            for (int k = k0; k < k1; k++)
                max = Math.Max(max, re[k] * re[k] + im[k] * im[k]);

            double mag = Math.Sqrt(max) / (N / 4.0);
            double db = 20 * Math.Log10(mag + 1e-9);
            db += 2.4 * Math.Log2(Math.Sqrt(f0 * f1) / 1000.0); // tilt so treble isn't always flat
            float level = (float)Math.Clamp((db + 64) / 60, 0, 1);

            if (level > Bars[b]) Bars[b] += (level - Bars[b]) * 0.8f;
            else Bars[b] = Math.Max(level, Bars[b] - dt * 1.7f);

            if (Bars[b] >= Peaks[b]) { Peaks[b] = Bars[b]; peakHold[b] = 0.35f; }
            else if (peakHold[b] > 0) peakHold[b] -= dt;
            else Peaks[b] = Math.Max(Bars[b], Peaks[b] - dt * 0.9f);
        }
    }

}
