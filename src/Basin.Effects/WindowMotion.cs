namespace Basin.Effects;

public struct WindowMotion
{
    public WindowMotion(double initial, double strength = 0.08, double smoothness = 4.0)
    {
        Value = initial;
        Start = initial;
        Target = initial;
        Velocity = 0;
        Strength = strength;
        Smoothness = smoothness;
    }

    public double Value { get; set; }

    public double Start { get; private set; }

    public double Target { get; private set; }

    public double Velocity { get; set; }

    public double Strength { get; set; }

    public double Smoothness { get; set; }

    public readonly double Distance => Target - Value;

    public void SetTarget(double target)
    {
        Start = Value;
        Target = target;
    }

    public void Calculate(double millis)
    {
        if (Value == Target && Velocity == 0)
        {
            return;
        }

        var steps = Math.Max(1, (int)(millis / 5));
        for (var i = 0; i < steps; i++)
        {
            var strength = (Target - Value) * Strength;
            Velocity = ((Smoothness * Velocity) + strength) / (Smoothness + 1.0);
            Value += Velocity;
        }
    }

    public void Finish()
    {
        Value = Target;
        Velocity = 0;
    }

    public readonly bool IsSettled()
    {
        if (Distance == 0)
        {
            return true;
        }

        var sign = Target <= Start ? -1 : 1;
        return Distance * sign / 0.5 < 1.0 && Velocity * sign / 0.2 < 1.0;
    }
}
