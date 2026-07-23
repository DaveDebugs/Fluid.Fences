using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace DesktopFences.Core.Theming
{

    public sealed record PaletteSwatch(Color Color, double Share, double Chroma, double Score)
    {
        public override string ToString() =>
            $"{ColorSpace.ToHex(Color)}  {Share * 100:F1}%  chroma {Chroma:F3}  score {Score:F3}";
    }

    public static class PaletteExtractor
    {

        public static IReadOnlyList<PaletteSwatch> Extract(
            byte[] bgraPixels, int width, int height, int clusters = 6, int maxIterations = 24)
        {
            if (bgraPixels is null || width <= 0 || height <= 0) return Array.Empty<PaletteSwatch>();

            var points = SampleToOklab(bgraPixels, width, height);
            if (points.Count < clusters) return Array.Empty<PaletteSwatch>();

            var centroids = InitialiseCentroids(points, clusters);
            var assignment = new int[points.Count];

            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                bool moved = false;

                for (int i = 0; i < points.Count; i++)
                {
                    int best = 0;
                    double bestDistance = double.MaxValue;
                    for (int c = 0; c < clusters; c++)
                    {
                        double d = SquaredDistance(points[i], centroids[c]);
                        if (d < bestDistance) { bestDistance = d; best = c; }
                    }
                    if (assignment[i] != best) { assignment[i] = best; moved = true; }
                }

                if (!moved && iteration > 0) break;

                var sums = new (double L, double a, double b, int n)[clusters];
                for (int i = 0; i < points.Count; i++)
                {
                    int c = assignment[i];
                    sums[c] = (sums[c].L + points[i].L, sums[c].a + points[i].a,
                               sums[c].b + points[i].b, sums[c].n + 1);
                }
                for (int c = 0; c < clusters; c++)
                    if (sums[c].n > 0)
                        centroids[c] = (sums[c].L / sums[c].n, sums[c].a / sums[c].n, sums[c].b / sums[c].n);
            }

            var counts = new int[clusters];
            foreach (int c in assignment) counts[c]++;
            double total = points.Count;

            var result = new List<PaletteSwatch>(clusters);
            for (int c = 0; c < clusters; c++)
            {
                if (counts[c] == 0) continue;

                var (L, a, b) = centroids[c];
                double chroma = Math.Sqrt(a * a + b * b);
                double share = counts[c] / total;

                double extremity = 1.0 - Math.Pow(Math.Abs(L - 0.5) * 2.0, 3);
                double score = share * (0.25 + chroma * 14.0) * Math.Max(0.15, extremity);

                result.Add(new PaletteSwatch(ColorSpace.OklabToRgb(L, a, b), share, chroma, score));
            }

            return result.OrderByDescending(s => s.Score).ToList();
        }

        public static Color? SelectSeed(IReadOnlyList<PaletteSwatch> palette)
        {
            if (palette is null || palette.Count == 0) return null;

            var usable = palette.FirstOrDefault(s => s.Chroma >= 0.02);
            return (usable ?? palette[0]).Color;
        }

        private static List<(double L, double a, double b)> SampleToOklab(byte[] bgra, int width, int height)
        {
            const int TargetSamples = 40_000;
            int totalPixels = width * height;
            int step = Math.Max(1, (int)Math.Sqrt(totalPixels / (double)TargetSamples));

            var points = new List<(double, double, double)>(TargetSamples);

            for (int y = 0; y < height; y += step)
            {
                int row = y * width * 4;
                for (int x = 0; x < width; x += step)
                {
                    int i = row + x * 4;
                    if (i + 3 >= bgra.Length) continue;
                    if (bgra[i + 3] < 8) continue;

                    points.Add(ColorSpace.RgbToOklab(bgra[i + 2], bgra[i + 1], bgra[i]));
                }
            }
            return points;
        }

        private static (double L, double a, double b)[] InitialiseCentroids(
            List<(double L, double a, double b)> points, int k)
        {
            var rng = new Random(20260720);
            var centroids = new (double, double, double)[k];
            centroids[0] = points[rng.Next(points.Count)];

            var nearest = new double[points.Count];
            for (int i = 0; i < points.Count; i++) nearest[i] = SquaredDistance(points[i], centroids[0]);

            for (int c = 1; c < k; c++)
            {
                double sum = 0;
                for (int i = 0; i < nearest.Length; i++) sum += nearest[i];

                double target = rng.NextDouble() * sum;
                int chosen = nearest.Length - 1;
                double running = 0;
                for (int i = 0; i < nearest.Length; i++)
                {
                    running += nearest[i];
                    if (running >= target) { chosen = i; break; }
                }

                centroids[c] = points[chosen];
                for (int i = 0; i < points.Count; i++)
                    nearest[i] = Math.Min(nearest[i], SquaredDistance(points[i], centroids[c]));
            }
            return centroids;
        }

        private static double SquaredDistance(
            (double L, double a, double b) p, (double L, double a, double b) q)
        {
            double dL = p.L - q.L, da = p.a - q.a, db = p.b - q.b;
            return dL * dL + da * da + db * db;
        }
    }
}
