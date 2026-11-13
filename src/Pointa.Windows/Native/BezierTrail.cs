using SkiaSharp;

namespace Pointa.Windows.Native;

// Port of lan-laser-pen/src/index.ts (1e643ed), with bezier-js 5.1.0 quadrature. MIT notices are in docs/third-party.
internal static class BezierTrail
{
    internal readonly record struct Point(double X, double Y);
    internal readonly record struct Sample(Point Position, double Time);
    internal readonly record struct Segment(Point Start, Point Control1, Point Control2, Point End, double Width = 0, double Opacity = 0);
    private static readonly double[] Nodes = [.064056892862605626, .19111886747361631, .31504267969616337, .43379350762604514,
        .54542147138883954, .64809365193697557, .74012419157855436, .82000198597390292, .88641552700440103, .93827455200273276,
        .97472855597130950, .99518721999702136];
    private static readonly double[] Weights = [.12793819534675216, .12583745634682830, .12167047292780339, .11550566805372560,
        .10744427011596563, .09761865210411389, .08619016153195328, .07334648141108031, .05929858491543678, .04427743881741981,
        .02853138862893366, .01234122979998720];

    internal static void Drain(List<Sample> points, double now, int duration)
    {
        points.RemoveAll(point => duration <= 0 || point.Time < now - duration);
        for (var i = points.Count - 1; i > 0; i--)
            if (points[i].Position == points[i - 1].Position) points.RemoveAt(i);
        for (var i = points.Count - 1; i > 1; i--)
            if (points[i].Position == points[i - 2].Position) { points.RemoveRange(i - 1, 2); i--; }
    }

    internal static List<Segment> Build(IReadOnlyList<Sample> points, double maxWidth = 8, double tension = .3, double pixelScale = 1)
    {
        if (points.Count < 3) return [];
        var original = Curves(points.Select(sample => sample.Position).ToArray(), tension);
        var lengths = original.Select(Length).ToArray();
        var total = lengths.Sum();
        if (total <= 0 || !double.IsFinite(total)) return [];
        var split = new List<Point>();
        for (var i = 0; i < original.Count; i++)
        {
            var steps = (int)Math.Ceiling(lengths[i] / (total / 50)) + 2;
            for (var j = 0; j <= steps; j++)
                if (i == original.Count - 1 || j < steps) split.Add(At(original[i], (double)j / steps));
        }
        var segments = Curves(split, tension);
        var past = 0d;
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            past += Length(segment);
            segments[i] = segment with { Start = Extend(segment.Start, segment.Control1, true, pixelScale),
                End = Extend(segment.End, segment.Control2, false, pixelScale), Width = past / total * maxWidth, Opacity = past / total };
        }
        return segments;
    }

    private static List<Segment> Curves(IReadOnlyList<Point> points, double tension)
    {
        var before = new Point[points.Count]; var after = new Point[points.Count];
        for (var i = points.Count - 2; i > 0; i--)
        {
            var current = points[i]; var next = points[i + 1]; var previous = points[i - 1];
            var distance = Distance(previous, next);
            if (distance == 0) { before[i] = after[i] = current; continue; }
            var dx = (previous.X - next.X) / distance; var dy = (previous.Y - next.Y) / distance;
            var forward = Distance(current, next) * tension; var backward = Distance(current, previous) * tension;
            after[i] = new(current.X - dx * forward, current.Y - dy * forward);
            before[i] = new(current.X + dx * backward, current.Y + dy * backward);
        }
        after[0] = Midpoint(points[0], points[1]);
        before[^1] = Midpoint(points[^1], points[^2]);
        var curves = new List<Segment>(points.Count - 1);
        for (var i = 1; i < points.Count; i++) curves.Add(new(points[i - 1], after[i - 1], before[i], points[i]));
        return curves;
    }

    private static Point At(Segment s, double t)
    {
        if (t == 0) return s.Start;
        if (t == 1) return s.End;
        var m = 1 - t; var a = m*m*m; var b = m*m*t*3; var c = m*t*t*3; var d = t*t*t;
        return new(a*s.Start.X+b*s.Control1.X+c*s.Control2.X+d*s.End.X, a*s.Start.Y+b*s.Control1.Y+c*s.Control2.Y+d*s.End.Y);
    }

    private static double Length(Segment s)
    {
        double Speed(double t)
        {
            var m = 1 - t;
            var x = m*m*(3*(s.Control1.X-s.Start.X)) + 2*m*t*(3*(s.Control2.X-s.Control1.X)) + t*t*(3*(s.End.X-s.Control2.X));
            var y = m*m*(3*(s.Control1.Y-s.Start.Y)) + 2*m*t*(3*(s.Control2.Y-s.Control1.Y)) + t*t*(3*(s.End.Y-s.Control2.Y));
            return Math.Sqrt(x*x+y*y);
        }
        var sum = 0d;
        for (var i = 0; i < Nodes.Length; i++) { sum += Weights[i] * Speed(.5-.5*Nodes[i]); sum += Weights[i] * Speed(.5+.5*Nodes[i]); }
        return .5*sum;
    }

    private static Point Extend(Point end, Point control, bool start, double scale)
    {
        if (end.X != control.X)
        {
            var slope = (end.Y-control.Y)/(end.X-control.X); var intercept = end.Y-slope*end.X;
            var offset = Math.Sqrt(.005/(1+slope*slope))*scale;
            var x = end.X + (control.X > end.X ? -offset : offset);
            return new(x, slope*x+intercept);
        }
        return new(end.X, end.Y + (start ? (end.Y > control.Y ? .005 : -.005) : (control.Y > end.Y ? -.005 : .005))*scale);
    }

    internal static double Fade(IReadOnlyList<Sample> points, double now, int duration) => points.Count == 0 || duration <= 0 ? 0 : Math.Clamp(1-(now-points[^1].Time)/duration, 0, 1);
    private static double Distance(Point a, Point b) => Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y));
    private static Point Midpoint(Point a, Point b) => new((a.X+b.X)/2, (a.Y+b.Y)/2);

    internal static void Draw(SKCanvas canvas, IReadOnlyList<Sample> points, IReadOnlyList<Segment> segments, double fade, SKColor color, float scale, float size)
    {
        if (points.Count < 2 || fade <= 0) return;
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Butt };
        if (points.Count == 2)
        {
            paint.StrokeCap = SKStrokeCap.Round; paint.StrokeWidth = 5 * scale * size / 12;
            paint.ColorF = new SKColorF(color.Red/255f, color.Green/255f, color.Blue/255f, (float)fade);
            canvas.DrawLine((float)points[0].Position.X, (float)points[0].Position.Y, (float)points[1].Position.X, (float)points[1].Position.Y, paint);
            return;
        }
        using var path = new SKPathBuilder();
        foreach (var segment in segments)
        {
            paint.StrokeWidth = (float)segment.Width;
            paint.ColorF = new SKColorF(color.Red/255f, color.Green/255f, color.Blue/255f, (float)(Math.Clamp(segment.Opacity,0,1)*fade));
            path.Reset(); path.MoveTo((float)segment.Start.X, (float)segment.Start.Y);
            path.CubicTo((float)segment.Control1.X, (float)segment.Control1.Y, (float)segment.Control2.X, (float)segment.Control2.Y, (float)segment.End.X, (float)segment.End.Y);
            using var stroke = path.Detach();
            canvas.DrawPath(stroke, paint);
        }
    }

    internal static void DrawHead(SKCanvas canvas, Point point, SKColor color, float scale, float size)
    {
        var sigma = size / 2;
        using var shadow = SKImageFilter.CreateDropShadowOnly(0, 0, sigma, sigma, color);
        using var paint = new SKPaint { Color = color, IsAntialias = true, ImageFilter = shadow };
        canvas.DrawCircle((float)point.X, (float)point.Y, size*scale/2, paint);
        paint.ImageFilter = null;
        canvas.DrawCircle((float)point.X, (float)point.Y, size*scale/2, paint);
    }
}
