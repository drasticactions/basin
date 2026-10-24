namespace Basin.Capabilities;

/// <summary>
/// Tests and applies output configurations as one transaction, for the output-management protocols and for the consumer.
/// </summary>
public interface IOutputConfiguration
{
    /// <summary>
    /// Checks a configuration without changing anything.
    /// </summary>
    /// <param name="entries">One entry for each output to change.</param>
    /// <returns><see langword="true"/> when every entry can commit. <see cref="LastFailureReason"/> tells why one cannot.</returns>
    bool Test(IReadOnlyList<OutputConfigurationEntry> entries);

    /// <summary>
    /// Applies a configuration. An implementation must raise <see cref="Applied"/> once when it succeeds.
    /// </summary>
    /// <param name="entries">One entry for each output to change.</param>
    /// <returns><see langword="true"/> when the configuration is in effect. <see cref="LastFailureReason"/> can say why it is not.</returns>
    bool Apply(IReadOnlyList<OutputConfigurationEntry> entries);

    /// <summary>
    /// Occurs once for each applied configuration, with the entries that went in, whoever asked for it.
    /// </summary>
    event Action<IReadOnlyList<OutputConfigurationEntry>>? Applied;

    /// <summary>
    /// Gives the optional properties that an output can set. A bit is set only when the hardware has the property and
    /// the implementation drives it. The default is <see cref="OutputConfigurationFeatures.None"/>.
    /// </summary>
    /// <param name="output">The output to check.</param>
    /// <returns>The available properties.</returns>
    OutputConfigurationFeatures Supported(IOutput output) => OutputConfigurationFeatures.None;

    /// <summary>
    /// Reads the committed configuration of an output, including state that <see cref="IOutput"/> does not model.
    /// A property that <see cref="Apply"/> accepted must be reported from then on.
    /// </summary>
    /// <param name="output">The output to read.</param>
    /// <param name="state">The committed state on success, otherwise the default value.</param>
    /// <returns><see langword="false"/> when there is nothing to report beyond the layout. The default returns false.</returns>
    bool TryRead(IOutput output, out OutputConfigurationEntry state)
    {
        state = default;
        return false;
    }

    /// <summary>
    /// Gets a readable reason for the last failed <see cref="Test"/> or <see cref="Apply"/>, or <see langword="null"/>.
    /// It is only meaningful directly after the failure.
    /// </summary>
    string? LastFailureReason => null;
}
