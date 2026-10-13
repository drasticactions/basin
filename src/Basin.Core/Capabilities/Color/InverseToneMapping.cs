namespace Basin.Capabilities;

public readonly record struct InverseToneMapping
{
    public InverseToneMapping()
    {
    }

    public InverseToneMapping(double sdrNits, double targetNits)
    {
        SdrNits = sdrNits;
        TargetNits = targetNits;
    }

    public double SdrNits { get; init; } = 100;

    public double TargetNits { get; init; } = 1000;
}
