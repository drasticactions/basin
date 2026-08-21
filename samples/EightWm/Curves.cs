namespace EightWm;

internal static class Curves
{
    public static double Evaluate(AnimationCurve curve, double t)
    {
        if (t <= 0)
        {
            return 0;
        }

        if (t >= 1)
        {
            return 1;
        }

        return curve switch
        {
            AnimationCurve.Deceleration => Bezier(t, 0.1, 0.9, 0.2, 1.0),
            AnimationCurve.Departure => Bezier(t, 0.11, 0.5, 0.24, 0.96),
            _ => t,
        };
    }

    private static double Bezier(double t, double x1, double y1, double x2, double y2)
    {
        var guess = t;
        for (var i = 0; i < 8; i++)
        {
            var x = CubicAt(guess, x1, x2) - t;
            if (Math.Abs(x) < 1e-6)
            {
                return CubicAt(guess, y1, y2);
            }

            var slope = CubicSlope(guess, x1, x2);
            if (Math.Abs(slope) < 1e-6)
            {
                break;
            }

            guess -= x / slope;
        }

        double low = 0;
        double high = 1;
        guess = t;
        for (var i = 0; i < 24; i++)
        {
            var x = CubicAt(guess, x1, x2);
            if (Math.Abs(x - t) < 1e-6)
            {
                break;
            }

            if (x > t)
            {
                high = guess;
            }
            else
            {
                low = guess;
            }

            guess = (low + high) / 2;
        }

        return CubicAt(guess, y1, y2);
    }

    private static double CubicAt(double t, double a, double b)
    {
        var inverse = 1 - t;
        return (3 * inverse * inverse * t * a) + (3 * inverse * t * t * b) + (t * t * t);
    }

    private static double CubicSlope(double t, double a, double b)
    {
        var inverse = 1 - t;
        return (3 * inverse * inverse * a) +
               (6 * inverse * t * (b - a)) +
               (3 * t * t * (1 - b));
    }
}
