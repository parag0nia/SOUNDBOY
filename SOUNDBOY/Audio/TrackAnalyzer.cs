using NAudio.Wave;

namespace SOUNDBOY.Audio;

/// <summary>Result of musical analysis. Any field may be null when it can't be determined reliably.</summary>
public sealed record AnalysisResult(double? Bpm, string? Key, string? Camelot);

/// <summary>
/// Offline tempo and key detection. Decodes up to <see cref="MaxSeconds"/> of audio, downmixes to mono
/// at ~11 kHz, then:
///  • BPM — spectral-flux onset envelope → autocorrelation over 60–200 BPM with a log-normal prior
///    centered on 120 BPM, refined with a multi-beat comb.
///  • Key — chromagram (65 Hz–2 kHz) correlated against the Krumhansl–Schmuckler major/minor profiles.
/// </summary>
public static class TrackAnalyzer
{
    const int TargetRate = 11025;
    const double MaxSeconds = 150;
    const int OnsetFft = 1024, OnsetHop = 128;
    const int ChromaFft = 8192, ChromaHop = 2048;

    static readonly double[] MajorProfile = { 6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88 };
    static readonly double[] MinorProfile = { 6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17 };
    static readonly string[] MajorNames = { "C", "Db", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B" };
    static readonly string[] MinorNames = { "Cm", "C#m", "Dm", "Ebm", "Em", "Fm", "F#m", "Gm", "G#m", "Am", "Bbm", "Bm" };
    // Camelot wheel number by pitch class (C = 0).
    static readonly int[] CamelotMajor = { 8, 3, 10, 5, 12, 7, 2, 9, 4, 11, 6, 1 };
    static readonly int[] CamelotMinor = { 5, 12, 7, 2, 9, 4, 11, 6, 1, 8, 3, 10 };

    public static AnalysisResult Analyze(string path, CancellationToken ct)
    {
        var mono = DecodeMono(path, ct, out int rate);
        double seconds = mono.Length / (double)rate;
        if (seconds < 3) return new AnalysisResult(null, null, null);

        double? bpm = seconds >= 8 ? DetectBpm(mono, rate, ct) : null;
        var (key, camelot) = DetectKey(mono, rate, ct);
        return new AnalysisResult(bpm, key, camelot);
    }

    // ------------------------------------------------------------------ decoding

    static float[] DecodeMono(string path, CancellationToken ct, out int rate)
    {
        var (stream, samples) = AudioEngine.OpenReader(path);
        using (stream)
        {
            int ch = samples.WaveFormat.Channels;
            int sr = samples.WaveFormat.SampleRate;
            int factor = Math.Max(1, (int)Math.Round(sr / (double)TargetRate));
            rate = sr / factor;
            int maxOut = (int)(MaxSeconds * rate);
            var outBuf = new List<float>(Math.Min(maxOut, rate * 60));
            var buf = new float[sr * ch]; // ~1 s per read
            double acc = 0;
            int accN = 0;
            int read;
            while (outBuf.Count < maxOut && (read = samples.Read(buf, 0, buf.Length - buf.Length % ch)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                for (int i = 0; i + ch <= read; i += ch)
                {
                    float m = 0;
                    for (int c = 0; c < ch; c++) m += buf[i + c];
                    acc += m / ch;
                    if (++accN == factor) // boxcar low-pass + decimate
                    {
                        outBuf.Add((float)(acc / factor));
                        acc = 0;
                        accN = 0;
                    }
                }
            }
            return outBuf.ToArray();
        }
    }

    // ------------------------------------------------------------------ tempo

    static double? DetectBpm(float[] x, int rate, CancellationToken ct)
    {
        double fps = rate / (double)OnsetHop;
        var window = Hann(OnsetFft);
        var re = new double[OnsetFft];
        var im = new double[OnsetFft];
        int bins = OnsetFft / 2;
        var prev = new double[bins];
        int frames = (x.Length - OnsetFft) / OnsetHop;
        if (frames < fps * 6) return null;
        var onset = new double[frames];

        for (int f = 0; f < frames; f++)
        {
            if ((f & 255) == 0) ct.ThrowIfCancellationRequested();
            int o = f * OnsetHop;
            for (int i = 0; i < OnsetFft; i++) { re[i] = x[o + i] * window[i]; im[i] = 0; }
            Dsp.Fft(re, im);
            double flux = 0;
            for (int k = 1; k < bins; k++)
            {
                double mag = Math.Log(1 + 100 * Math.Sqrt(re[k] * re[k] + im[k] * im[k]));
                double d = mag - prev[k];
                if (d > 0) flux += d;
                prev[k] = mag;
            }
            onset[f] = flux;
        }

        // Remove the slowly varying loudness trend so the autocorrelation sees beats, not dynamics.
        int w = (int)(fps * 0.5);
        var detr = new double[frames];
        double run = 0;
        for (int i = 0; i < frames; i++)
        {
            run += onset[i];
            if (i >= w) run -= onset[i - w];
            double mean = run / Math.Min(i + 1, w);
            detr[i] = Math.Max(0, onset[i] - mean);
        }

        double Ac(double lag)
        {
            int l0 = (int)Math.Floor(lag);
            double t = lag - l0;
            double s0 = 0, s1 = 0;
            for (int i = 0; i + l0 + 1 < frames; i++)
            {
                s0 += detr[i] * detr[i + l0];
                s1 += detr[i] * detr[i + l0 + 1];
            }
            return (s0 * (1 - t) + s1 * t) / Math.Max(1, frames - l0);
        }

        double energy = Ac(0);
        if (energy <= 1e-9) return null;

        // Coarse search on integer lags with a tempo prior centered on 120 BPM (1 octave std).
        int minLag = (int)Math.Floor(60 * fps / 200), maxLag = (int)Math.Ceiling(60 * fps / 60);
        double bestScore = double.MinValue, bestLag = 0;
        var acCache = new Dictionary<int, double>();
        double AcInt(int l) => acCache.TryGetValue(l, out var v) ? v : acCache[l] = Ac(l);
        for (int lag = minLag; lag <= maxLag; lag++)
        {
            double bpm = 60 * fps / lag;
            double prior = Math.Exp(-0.5 * Math.Pow(Math.Log2(bpm / 120.0), 2));
            double score = (AcInt(lag) + 0.5 * AcInt(lag * 2) + 0.25 * AcInt(lag * 4)) * prior;
            if (score > bestScore) { bestScore = score; bestLag = lag; }
        }
        if (AcInt((int)bestLag) / energy < 0.02) return null; // no periodicity at all

        // Fine search around the winner: comb over several beats for sub-frame accuracy.
        double bestBpm = 60 * fps / bestLag, fineScore = double.MinValue, fineBpm = bestBpm;
        for (double b = bestBpm - 3; b <= bestBpm + 3; b += 0.05)
        {
            double lag = 60 * fps / b, s = 0;
            for (int k = 1; k <= 4; k++) s += Ac(lag * k) / k;
            if (s > fineScore) { fineScore = s; fineBpm = b; }
        }
        return Math.Round(fineBpm, 1);
    }

    // ------------------------------------------------------------------ key

    static (string?, string?) DetectKey(float[] x, int rate, CancellationToken ct)
    {
        var window = Hann(ChromaFft);
        var re = new double[ChromaFft];
        var im = new double[ChromaFft];
        var chroma = new double[12];
        int kMin = (int)Math.Ceiling(65.0 * ChromaFft / rate), kMax = (int)(2000.0 * ChromaFft / rate);
        var pcOfBin = new int[kMax + 1];
        for (int k = kMin; k <= kMax; k++)
        {
            double f = k * (double)rate / ChromaFft;
            int midi = (int)Math.Round(69 + 12 * Math.Log2(f / 440.0));
            pcOfBin[k] = ((midi % 12) + 12) % 12;
        }

        int frames = Math.Max(0, (x.Length - ChromaFft) / ChromaHop + 1);
        if (frames < 2) return (null, null);
        for (int f = 0; f < frames; f++)
        {
            if ((f & 31) == 0) ct.ThrowIfCancellationRequested();
            int o = f * ChromaHop;
            for (int i = 0; i < ChromaFft; i++) { re[i] = x[o + i] * window[i]; im[i] = 0; }
            Dsp.Fft(re, im);
            var frame = new double[12];
            double total = 0;
            for (int k = kMin; k <= kMax; k++)
            {
                double mag = Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
                frame[pcOfBin[k]] += mag;
                total += mag;
            }
            if (total <= 1e-9) continue;
            for (int p = 0; p < 12; p++) chroma[p] += frame[p] / total; // per-frame normalization: loudness-independent
        }
        if (chroma.Sum() <= 1e-9) return (null, null);

        double best = double.MinValue, second = double.MinValue;
        int bestRoot = 0;
        bool bestMinor = false;
        for (int root = 0; root < 12; root++)
        {
            foreach (bool minor in new[] { false, true })
            {
                var prof = minor ? MinorProfile : MajorProfile;
                double r = Pearson(chroma, i => prof[((i - root) % 12 + 12) % 12]);
                if (r > best) { second = best; best = r; bestRoot = root; bestMinor = minor; }
                else if (r > second) second = r;
            }
        }
        if (best < 0.3) return (null, null); // atonal / noise / speech

        return bestMinor
            ? (MinorNames[bestRoot], CamelotMinor[bestRoot] + "A")
            : (MajorNames[bestRoot], CamelotMajor[bestRoot] + "B");
    }

    static double Pearson(double[] a, Func<int, double> b)
    {
        double ma = a.Average(), mb = 0;
        for (int i = 0; i < 12; i++) mb += b(i);
        mb /= 12;
        double num = 0, da = 0, db = 0;
        for (int i = 0; i < 12; i++)
        {
            double x = a[i] - ma, y = b(i) - mb;
            num += x * y;
            da += x * x;
            db += y * y;
        }
        return num / Math.Sqrt(da * db + 1e-12);
    }

    static double[] Hann(int n)
    {
        var w = new double[n];
        for (int i = 0; i < n; i++) w[i] = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (n - 1)));
        return w;
    }
}

static class Dsp
{
    /// <summary>In-place iterative radix-2 complex FFT. Length must be a power of two.</summary>
    public static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len;
            double wr = Math.Cos(ang), wi = Math.Sin(ang);
            int half = len / 2;
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (int k = 0; k < half; k++)
                {
                    int a = i + k, b = a + half;
                    double tr = re[b] * cr - im[b] * ci;
                    double ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr; im[b] = im[a] - ti;
                    re[a] += tr; im[a] += ti;
                    double ncr = cr * wr - ci * wi;
                    ci = cr * wi + ci * wr;
                    cr = ncr;
                }
            }
        }
    }
}
