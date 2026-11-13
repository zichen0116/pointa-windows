using System.Text.Json;
using SkiaSharp;

namespace Pointa.Windows.Native;

internal static class TrailReferenceChecks
{
    internal static void Run(string directory)
    {
        using var source = typeof(BezierTrail).Assembly.GetManifestResourceStream("Pointa.Windows.Native.ReferenceTrails.json")!;
        using var document = JsonDocument.Parse(source);
        var assertions = 0;
        var comparisons = new List<object>();
        var worstAlphaDifference = 0;
        foreach (var fixture in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            var name = fixture.GetProperty("name").GetString()!;
            var now = fixture.GetProperty("now").GetDouble();
            var raw = fixture.GetProperty("points").EnumerateArray().Select(p => new BezierTrail.Sample(new(p[0].GetDouble(),p[1].GetDouble()),p[2].GetDouble())).ToList();
            var points = raw.ToList();
            BezierTrail.Drain(points, now, 500);
            var kept = fixture.GetProperty("kept");
            Check(points.Count == kept.GetArrayLength(), name + " retained points");
            for (var i = 0; i < points.Count; i++) { Near(points[i].Position.X, kept[i][0].GetDouble()); Near(points[i].Position.Y, kept[i][1].GetDouble()); Near(points[i].Time, kept[i][2].GetDouble()); }
            var segments = BezierTrail.Build(points);
            var expected = fixture.GetProperty("strokes");
            Check(segments.Count == expected.GetArrayLength(), name + " segment count");
            for (var i = 0; i < segments.Count; i++)
            {
                var s = segments[i]; var values = new[] { s.Start.X,s.Start.Y,s.Control1.X,s.Control1.Y,s.Control2.X,s.Control2.Y,s.End.X,s.End.Y };
                for (var j = 0; j < values.Length; j++) Near(values[j], expected[i].GetProperty("p")[j].GetDouble());
                Near(s.Width, expected[i].GetProperty("width").GetDouble()); Near(s.Opacity, expected[i].GetProperty("opacity").GetDouble());
            }
            var fade = BezierTrail.Fade(points, now, 500);
            Near(fade, fixture.GetProperty("fade").GetDouble());
            using var surface = SKSurface.Create(new SKImageInfo(640, 360, SKColorType.Bgra8888, SKAlphaType.Premul));
            surface.Canvas.Clear(SKColors.Transparent);
            BezierTrail.Draw(surface.Canvas, points, segments, fade, SKColors.Red, 1, 12);
            BezierTrail.DrawHead(surface.Canvas, raw[^1].Position, SKColors.Red, 1, 12);
            using var snapshot = surface.Snapshot(); using var png = snapshot.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(directory, $"trail-{name}.png"), png.ToArray());
            var referencePath = Path.Combine(Directory.GetParent(directory)!.FullName, "browser-reference", name + ".png");
            if (File.Exists(referencePath))
            {
                using var expectedBitmap = SKBitmap.Decode(referencePath);
                using var actualBitmap = SKBitmap.FromImage(snapshot);
                var affected = 0; var exact = 0; var maxDelta = 0; long sumDelta = 0;
                for (var y = 0; y < 360; y++) for (var x = 0; x < 640; x++)
                {
                    var a = actualBitmap.GetPixel(x,y); var b = expectedBitmap.GetPixel(x,y);
                    if (a.Alpha == 0 && b.Alpha == 0) continue;
                    affected++; var delta = Math.Abs(a.Alpha - b.Alpha);
                    if (delta == 0) exact++;
                    sumDelta += delta; maxDelta = Math.Max(maxDelta, delta);
                }
                worstAlphaDifference = Math.Max(worstAlphaDifference, maxDelta);
                comparisons.Add(new { name, affected, exact, maxDelta, meanAlphaDifference = affected == 0 ? 0 : (double)sumDelta / affected });
                using var comparison = SKSurface.Create(new SKImageInfo(1280,360));
                comparison.Canvas.Clear(SKColors.White);
                comparison.Canvas.DrawBitmap(expectedBitmap, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
                comparison.Canvas.DrawBitmap(actualBitmap, 640, 0, new SKSamplingOptions(SKFilterMode.Nearest));
                using var combined = comparison.Snapshot(); using var comparisonPng = combined.Encode(SKEncodedImageFormat.Png,100);
                File.WriteAllBytes(Path.Combine(directory, $"comparison-{name}.png"), comparisonPng.ToArray());
            }
            void Near(double actual, double wanted) { Check(Math.Abs(actual-wanted) <= .00001, $"{name}: {actual} != {wanted}"); }
        }
        File.WriteAllText(Path.Combine(directory,"trail-reference-result.txt"), $"PASS: {assertions} upstream geometry/fade assertions across 12 cases; Skia snapshots saved.");
        File.WriteAllText(Path.Combine(directory,"trail-raster-result.json"), JsonSerializer.Serialize(comparisons, new JsonSerializerOptions { WriteIndented = true }));
        if (comparisons.Count > 0) Check(worstAlphaDifference <= 2, $"browser pixel alpha delta {worstAlphaDifference} exceeds rounding tolerance");
        void Check(bool passed, string message) { assertions++; if (!passed) throw new InvalidOperationException("Trail reference mismatch: " + message); }
    }
}
