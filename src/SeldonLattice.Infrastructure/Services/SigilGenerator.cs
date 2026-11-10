using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models;
using SeldonLattice.Domain.Models.Results;
using SeldonLattice.Domain.Models.Storage;

namespace SeldonLattice.Infrastructure.Services;

/// <summary>
/// Deterministic sigil pattern generator. Produces SVG visualizations of CID hashes
/// using geometric algorithms. All patterns are reproducible from the same CID.
/// </summary>
public sealed class SigilGenerator : ISigilGenerator
{
    private static readonly double Tau = Math.PI * 2;

    /// <inheritdoc/>
    public Task<SigilResult> GenerateAsync(string cid, SigilAlgorithm algorithm, int size)
    {
        try
        {
            var hashBytes = DecodeCidHash(cid);
            var svg = algorithm switch
            {
                SigilAlgorithm.Lissajous => GenerateLissajous(hashBytes, size),
                SigilAlgorithm.Mandala => GenerateMandala(hashBytes, size),
                _ => GenerateFractalTree(hashBytes, size)
            };

            return Task.FromResult(new SigilResult
            {
                Algorithm = algorithm,
                Cid = cid,
                Svg = svg,
                Size = size,
                HashBytes = hashBytes
            });
        }
        catch (Exception exception)
        {
            return Task.FromException<SigilResult>(exception);
        }
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<SigilResult>> GenerateAllAsync(string cid, int size)
    {
        var algorithms = Enum.GetValues<SigilAlgorithm>();
        var results = new List<SigilResult>();
        foreach (var algo in algorithms)
        {
            results.Add(await GenerateAsync(cid, algo, size));
        }
        return results;
    }

    /// <summary>
    /// Extracts raw hash bytes from a CID string.
    /// The CID format is "algorithm-hash" where hash is base64url-encoded
    /// (e.g. "sha3-256-AbCd..."). If parsing fails, falls back to hashing the raw string.
    /// </summary>
    private static byte[] DecodeCidHash(string cid)
    {
        if (string.IsNullOrEmpty(cid))
            return new byte[32];

        try
        {
            return Cid.FromString(cid).RawHash;
        }
        catch
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            return sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(cid));
        }
    }

    /// <summary>
    /// Deterministic pseudo-random double generator seeded from hash bytes.
    /// </summary>
    private sealed class HashRng(byte[] bytes)
    {
        private int _pos = 0;
        private ulong _calls = 0;

        public double NextDouble()
        {
            // Cid.FromString accepts arbitrary strings, so the raw hash can be
            // shorter than 8 bytes (e.g. "testcid" -> 5, "abc" -> 2). Reading a
            // UInt64 from those throws regardless of cursor position,
            // so fold the short hash and advance a call counter through a
            // splitmix64 finalizer instead - deterministic per CID, no cursor.
            if (bytes.Length < 8)
            {
                var folded = 0UL;
                foreach (var b in bytes)
                    folded = folded * 31 + b;
                var x = folded ^ (++_calls * 0x9E3779B97F4A7C15UL);
                x ^= x >> 30; x *= 0xBF58476D1CE4E5B9UL;
                x ^= x >> 27; x *= 0x94D049BB133111EBUL;
                x ^= x >> 31;
                return (x & 0xFFFFFFFFFFFF) / (double)(1UL << 48);
            }

            if (_pos + 8 > bytes.Length)
                _pos = 0;
            var ul = BitConverter.ToUInt64(bytes, _pos);
            _pos += 8;
            return (ul & 0xFFFFFFFFFFFF) / (double)(1UL << 48);
        }

        public int NextInt(int min, int max)
        {
            return min + (int)(NextDouble() * (max - min));
        }
    }

    #region SVG Helpers

    private static string SvgRoot(int size, string innerContent)
    {
        return $"""
        <svg xmlns="http://www.w3.org/2000/svg" width="{size}" height="{size}" viewBox="0 0 {size} {size}">
        {innerContent}
        </svg>
        """;
    }

    private static string ToPath(string commands) => $"<path d=\"{commands}\" fill=\"none\" stroke=\"#0f1b2a\" stroke-width=\"1.5\"/>";

    private static string ToCircle(double x, double y, double r, string? fill = null) =>
        $"<circle cx=\"{x:F2}\" cy=\"{y:F2}\" r=\"{r:F2}\" fill=\"{fill ?? "#0f1b2a"}\"/>";

    private static string ToPolyline(double[][] points, string? stroke = null)
    {
        var pts = string.Join(" ", points.Select(p => $"{p[0]:F2},{p[1]:F2}"));
        return $"<polyline points=\"{pts}\" fill=\"none\" stroke=\"{stroke ?? "#0f1b2a"}\" stroke-width=\"1.5\"/>";
    }

    private static string ToLine(double x1, double y1, double x2, double y2, string? stroke = null) =>
        $"<line x1=\"{x1:F2}\" y1=\"{y1:F2}\" x2=\"{x2:F2}\" y2=\"{y2:F2}\" stroke=\"{stroke ?? "#0f1b2a"}\" stroke-width=\"1.5\"/>";

    private static string ToRect(double x, double y, double w, double h, string? fill = null) =>
        $"<rect x=\"{x:F2}\" y=\"{y:F2}\" width=\"{w:F2}\" height=\"{h:F2}\" fill=\"{fill ?? "#0f1b2a"}\"/>";

    private static string ToText(double x, double y, string text) =>
        $"<text x=\"{x:F2}\" y=\"{y:F2}\" font-size=\"8\" fill=\"#64748b\">{text}</text>";

    private static string ColorFromHash(HashRng rng)
    {
        var r = rng.NextInt(40, 200);
        var g = rng.NextInt(40, 200);
        var b = rng.NextInt(100, 240);
        return $"rgb({r},{g},{b})";
    }

    #endregion

    #region Algorithm 1: Lissajous Curves

    private static string GenerateLissajous(byte[] hashBytes, int size)
    {
        var rng = new HashRng(hashBytes);
        var cx = size / 2.0;
        var cy = size / 2.0;
        var amplitude = size * 0.35;
        var freqA = rng.NextInt(1, 5);
        var freqB = rng.NextInt(1, 5);
        var phase = rng.NextDouble() * Tau;
        var pointCount = 100;
        var color = ColorFromHash(rng);

        var pts = new List<double[]>();
        for (int i = 0; i <= pointCount; i++)
        {
            var t = Tau * i / pointCount;
            var x = cx + Math.Cos(freqA * t) * amplitude;
            var y = cy + Math.Sin(freqB * t + phase) * amplitude;
            pts.Add([x, y]);
        }

        var path = new System.Text.StringBuilder();
        path.Append($"M {pts[0][0]:F2},{pts[0][1]:F2}");
        for (int i = 1; i < pts.Count; i++)
        {
            path.Append($" L{pts[i][0]:F2},{pts[i][1]:F2}");
        }

        // Add a second curve with different phase
        var phase2 = phase + Tau / 4;
        var pts2 = new List<double[]>();
        for (int i = 0; i <= pointCount; i++)
        {
            var t = Tau * i / pointCount;
            var x = cx + Math.Cos(freqA * t) * amplitude * 0.6;
            var y = cy + Math.Sin(freqB * t + phase2) * amplitude * 0.6;
            pts2.Add([x, y]);
        }

        var path2 = new System.Text.StringBuilder();
        path2.Append($"M {pts2[0][0]:F2},{pts2[0][1]:F2}");
        for (int i = 1; i < pts2.Count; i++)
        {
            path2.Append($" L{pts2[i][0]:F2},{pts2[i][1]:F2}");
        }

        var inner = new[]
        {
            $"<path d=\"{path}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"1.5\"/>",
            $"<path d=\"{path2}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"1.2\" stroke-opacity=\"0.5\"/>"
        };

        return SvgRoot(size, string.Join("\n", inner));
    }

    #endregion

    #region Algorithm 2: Mandala Pattern

    private static string GenerateMandala(byte[] hashBytes, int size)
    {
        var rng = new HashRng(hashBytes);
        var cx = size / 2.0;
        var cy = size / 2.0;
        var maxRadius = size * 0.4;
        var layers = rng.NextInt(3, 6);
        var color = ColorFromHash(rng);

        var elements = new List<string>();

        for (int layer = 0; layer < layers; layer++)
        {
            var radius = (double)(layer + 1) / layers * maxRadius;
            var petals = rng.NextInt(6, 16);
            var petalDepth = rng.NextDouble() * 0.3 + 0.2;
            var petalSize = radius * 0.3;

            for (int p = 0; p < petals; p++)
            {
                var angle = Tau * p / petals;
                var tipX = cx + Math.Cos(angle) * radius;
                var tipY = cy + Math.Sin(angle) * radius;
                var baseAngle = angle + Tau / 4;
                var baseDist = radius * petalDepth;
                var leftX = cx + Math.Cos(baseAngle) * baseDist;
                var leftY = cy + Math.Sin(baseAngle) * baseDist;
                var rightX = cx + Math.Cos(baseAngle + Tau / 2) * baseDist;
                var rightY = cy + Math.Sin(baseAngle + Tau / 2) * baseDist;

                // Petal as a path
                var d = $"M{leftX:F2},{leftY:F2} Q{tipX:F2},{tipY:F2} {cx:F2},{cy:F2} Q{rightX:F2},{rightY:F2} {leftX:F2},{leftY:F2}";
                elements.Add($"<path d=\"{d}\" fill=\"{color}\" fill-opacity=\"0.2\" stroke=\"{color}\" stroke-width=\"0.5\"/>");
            }

            // Center circle for this layer
            elements.Add($"<circle cx=\"{cx:F2}\" cy=\"{cy:F2}\" r=\"{radius * (0.3 + rng.NextDouble() * 0.2):F2}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"1\" stroke-dasharray=\"2,2\"/>");
        }

        elements.Add(ToCircle(cx, cy, 3, "#0f1b2a"));
        return SvgRoot(size, string.Join("\n", elements));
    }

    #endregion

    #region Algorithm 3: Fractal Tree

    private static string GenerateFractalTree(byte[] hashBytes, int size)
    {
        var rng = new HashRng(hashBytes);
        var color = ColorFromHash(rng);
        var maxDepth = rng.NextInt(5, 8);
        var baseLength = size * 0.2;

        var elements = new List<string>();
        var sb = new System.Text.StringBuilder();

        void DrawBranch(double x, double y, double angle, double length, int depth)
        {
            var endX = x + Math.Cos(angle) * length;
            var endY = y + Math.Sin(angle) * length;
            sb.Append($"M{x:F2},{y:F2} L{endX:F2},{endY:F2}\n");

            if (depth > 0)
            {
                var leftAngle = angle + Tau / 8 + (rng.NextDouble() - 0.5) * 0.3;
                var rightAngle = angle - Tau / 8 + (rng.NextDouble() - 0.5) * 0.3;
                var leftLen = length * (0.6 + rng.NextDouble() * 0.2);
                var rightLen = length * (0.6 + rng.NextDouble() * 0.2);

                var leftBranchCount = rng.NextInt(1, 3);
                for (int lb = 0; lb < leftBranchCount; lb++)
                {
                    DrawBranch(endX, endY, leftAngle + (rng.NextDouble() - 0.5) * 0.4, leftLen * (0.7 + rng.NextDouble() * 0.3), depth - 1);
                }

                var rightBranchCount = rng.NextInt(1, 3);
                for (int rb = 0; rb < rightBranchCount; rb++)
                {
                    DrawBranch(endX, endY, rightAngle + (rng.NextDouble() - 0.5) * 0.4, rightLen * (0.7 + rng.NextDouble() * 0.3), depth - 1);
                }
            }
        }

        // Start from bottom center
        var startX = size / 2.0;
        var startY = size * 0.85;
        var startAngle = -Tau / 4; // Point upward

        DrawBranch(startX, startY, startAngle, baseLength, maxDepth);
        elements.Add($"<path d=\"{sb.ToString().Trim()}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"1.5\"/>");
        elements.Add(ToCircle(startX, startY, 3, "#0f1b2a"));

        return SvgRoot(size, string.Join("\n", elements));
    }

    #endregion
}
