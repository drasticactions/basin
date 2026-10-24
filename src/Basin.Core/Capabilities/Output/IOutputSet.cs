namespace Basin.Capabilities;

/// <summary>
/// The outputs that output-management clients can see and configure, including disabled ones.
/// </summary>
public interface IOutputSet
{
    /// <summary>
    /// Gets the outputs. An output stays in the list until it is destroyed.
    /// </summary>
    IReadOnlyList<IOutput> Outputs { get; }

    /// <summary>
    /// Occurs when an output is added to or removed from <see cref="Outputs"/>.
    /// </summary>
    event Action? Changed;
}
