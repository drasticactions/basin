namespace Basin.Capabilities;

/// <summary>
/// Holds the parameters of BT.2446 Method A inverse tone mapping, which expands SDR content on a PQ output.
/// </summary>
public readonly record struct InverseToneMapping
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InverseToneMapping"/> struct with 100 cd/m² SDR white and a 1000 cd/m² target.
    /// </summary>
    public InverseToneMapping()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InverseToneMapping"/> struct with the given luminances.
    /// </summary>
    /// <param name="sdrNits">The luminance of SDR white in cd/m².</param>
    /// <param name="targetNits">The luminance that SDR white expands to, in cd/m².</param>
    public InverseToneMapping(double sdrNits, double targetNits)
    {
        SdrNits = sdrNits;
        TargetNits = targetNits;
    }

    /// <summary>
    /// Gets the luminance of SDR white in cd/m². The default is 100.
    /// </summary>
    public double SdrNits { get; init; } = 100;

    /// <summary>
    /// Gets the luminance that SDR white expands to, in cd/m². The default is 1000.
    /// </summary>
    public double TargetNits { get; init; } = 1000;
}
