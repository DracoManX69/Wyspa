using System.Numerics;

namespace Wyspa.Core.Services;

// Compact phonetic trajectories for query-by-example matching. No waveform is persisted.
public static class WakeAcoustics
{
    public static float[][] Extract(float[] samples)
    {
        if (samples.Length < 400) return [];
        var max = samples.Select(Math.Abs).Max(); if (max < .005) return [];
        var first = Array.FindIndex(samples, value => Math.Abs(value) > Math.Max(.003, max * .08));
        var last = Array.FindLastIndex(samples, value => Math.Abs(value) > Math.Max(.003, max * .08));
        first = Math.Max(0, first - 320); last = Math.Min(samples.Length, last + 320);
        if (last - first < 1600) return [];
        var frames = new List<float[]>();
        for (var offset = first; offset + 400 <= last && frames.Count < 320; offset += 160)
        {
            var fft = new Complex[512];
            for (var i = 0; i < 400; i++) fft[i] = new Complex(samples[offset + i] * (.54 - .46 * Math.Cos(2 * Math.PI * i / 399)), 0);
            Transform(fft);
            var mel = new double[24];
            for (var band = 0; band < 24; band++)
            {
                int Bin(int point) => (int)Math.Round((700 * (Math.Exp(point / 25d * Math.Log(1 + 7600d / 700)) - 1)) / 16000 * 512);
                var a = Bin(band); var b = Math.Max(a + 1, Bin(band + 1)); var c = Math.Max(b + 1, Bin(band + 2));
                double energy = 0;
                for (var bin = a; bin < Math.Min(257, c); bin++)
                {
                    var weight = bin <= b ? (bin - a) / (double)(b - a) : (c - bin) / (double)(c - b);
                    energy += fft[bin].Magnitude * fft[bin].Magnitude * weight;
                }
                mel[band] = Math.Log(Math.Max(1e-9, energy));
            }
            var row = new float[12];
            for (var coefficient = 1; coefficient <= 12; coefficient++)
                row[coefficient - 1] = (float)Enumerable.Range(0, 24).Sum(b => mel[b] * Math.Cos(Math.PI * coefficient * (b + .5) / 24));
            frames.Add(row);
        }
        if (frames.Count < 10) return [];
        for (var c = 0; c < 12; c++)
        {
            var mean = frames.Average(row => row[c]); var variance = Math.Sqrt(frames.Average(row => Math.Pow(row[c] - mean, 2)) + 1e-6);
            foreach (var row in frames) row[c] = (float)((row[c] - mean) / variance);
        }
        foreach (var row in frames)
        {
            var norm = Math.Sqrt(row.Sum(v => v * v) + 1e-9); for (var c = 0; c < 12; c++) row[c] /= (float)norm;
        }
        return frames.ToArray();
    }
    public static double Distance(float[][] left, float[][] right)
    {
        if (left.Length < 10 || right.Length < 10 || left.Length > 320 || right.Length > 320 ||
            left.Any(r => r.Length != 12 || r.Any(v => !float.IsFinite(v))) || right.Any(r => r.Length != 12 || r.Any(v => !float.IsFinite(v)))) return double.PositiveInfinity;
        var previous = Enumerable.Repeat(double.PositiveInfinity, right.Length + 1).ToArray(); previous[0] = 0;
        for (var i = 1; i <= left.Length; i++)
        {
            var current = Enumerable.Repeat(double.PositiveInfinity, right.Length + 1).ToArray();
            for (var j = 1; j <= right.Length; j++)
            {
                if (Math.Abs(i / (double)left.Length - j / (double)right.Length) > .3) continue;
                var cost = 1 - left[i - 1].Zip(right[j - 1]).Sum(pair => pair.First * pair.Second);
                current[j] = Math.Max(0, cost) + Math.Min(current[j - 1], Math.Min(previous[j], previous[j - 1]));
            }
            previous = current;
        }
        return previous[^1] / Math.Max(left.Length, right.Length);
    }
    public static double Best(float[][] sample, IEnumerable<float[][]> templates) => templates.Select(t => Distance(sample, t)).DefaultIfEmpty(double.PositiveInfinity).Min();
    private static void Transform(Complex[] values)
    {
        for (int i = 1, j = 0; i < values.Length; i++)
        {
            var bit = values.Length >> 1; for (; (j & bit) != 0; bit >>= 1) j ^= bit; j ^= bit;
            if (i < j) (values[i], values[j]) = (values[j], values[i]);
        }
        for (var length = 2; length <= values.Length; length <<= 1)
        {
            var step = Complex.FromPolarCoordinates(1, -2 * Math.PI / length);
            for (var offset = 0; offset < values.Length; offset += length)
            {
                var phase = Complex.One;
                for (var j = 0; j < length / 2; j++)
                { var even = values[offset + j]; var odd = values[offset + j + length / 2] * phase; values[offset + j] = even + odd; values[offset + j + length / 2] = even - odd; phase *= step; }
            }
        }
    }
}
