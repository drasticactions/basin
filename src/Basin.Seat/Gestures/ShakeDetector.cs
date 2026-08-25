using Basin.Diagnostics;

namespace Basin.Seat;

public sealed class ShakeDetector
{
    private const double Tolerance = 1.0;

    private const double MinimumDiagonal = 100.0;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly List<(double X, double Y, long Nanos)> _history = [];

    public double IntervalMillis { get; set; } = 1000;

    public double Sensitivity { get; set; } = 4;

    public void Reset()
    {
        _thread.Assert();
        _history.Clear();
    }

    public bool Update(double x, double y, long nanos)
    {
        _thread.Assert();
        var interval = (long)(Math.Max(0, IntervalMillis) * 1_000_000);
        var keep = 0;
        while (keep < _history.Count && nanos - _history[keep].Nanos >= interval)
        {
            keep++;
        }

        if (keep > 0)
        {
            _history.RemoveRange(0, keep);
        }

        if (_history.Count >= 2)
        {
            var last = _history[^1];
            var previous = _history[^2];
            if (SameSign(last.X - previous.X, x - last.X) && SameSign(last.Y - previous.Y, y - last.Y))
            {
                _history[^1] = (x, y, nanos);
                return false;
            }
        }

        _history.Add((x, y, nanos));

        var left = _history[0].X;
        var top = _history[0].Y;
        var right = left;
        var bottom = top;
        var distance = 0.0;
        for (var i = 1; i < _history.Count; i++)
        {
            var dx = _history[i].X - _history[i - 1].X;
            var dy = _history[i].Y - _history[i - 1].Y;
            distance += Math.Sqrt((dx * dx) + (dy * dy));
            left = Math.Min(left, _history[i].X);
            top = Math.Min(top, _history[i].Y);
            right = Math.Max(right, _history[i].X);
            bottom = Math.Max(bottom, _history[i].Y);
        }

        var width = right - left;
        var height = bottom - top;
        var diagonal = Math.Sqrt((width * width) + (height * height));
        if (diagonal < MinimumDiagonal)
        {
            return false;
        }

        if (distance / diagonal > Sensitivity)
        {
            _history.Clear();
            return true;
        }

        return false;
    }

    private static bool SameSign(double a, double b) =>
        (a >= -Tolerance && b >= -Tolerance) || (a <= Tolerance && b <= Tolerance);
}
