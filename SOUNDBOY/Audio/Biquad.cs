namespace SOUNDBOY.Audio;

/// <summary>RBJ peaking-EQ biquad. Coefficients can be updated without resetting state (no clicks).</summary>
sealed class Biquad
{
    double b0 = 1, b1, b2, a1, a2;
    double x1, x2, y1, y2;

    public void SetPeaking(double sampleRate, double freq, double q, double gainDb)
    {
        double a = Math.Pow(10, gainDb / 40.0);
        double w0 = 2 * Math.PI * freq / sampleRate;
        double cos = Math.Cos(w0);
        double alpha = Math.Sin(w0) / (2 * q);
        double a0 = 1 + alpha / a;
        b0 = (1 + alpha * a) / a0;
        b1 = -2 * cos / a0;
        b2 = (1 - alpha * a) / a0;
        a1 = -2 * cos / a0;
        a2 = (1 - alpha / a) / a0;
    }

    public void Reset() => x1 = x2 = y1 = y2 = 0;

    public float Process(float x)
    {
        double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
        x2 = x1; x1 = x;
        y2 = y1; y1 = y;
        // Flush denormals so quiet passages don't stall the CPU.
        if (Math.Abs(y1) < 1e-20) y1 = 0;
        return (float)y;
    }
}
