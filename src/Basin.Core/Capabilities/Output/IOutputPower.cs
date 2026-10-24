namespace Basin.Capabilities;

/// <summary>
/// Turns the display of an output on and off without removing the output, for example for DPMS.
/// </summary>
public interface IOutputPower
{
    /// <summary>
    /// Tells whether the display of an output is on.
    /// </summary>
    /// <param name="output">The output to check.</param>
    /// <returns><see langword="true"/> when the display is on.</returns>
    bool IsOn(IOutput output);

    /// <summary>
    /// Turns the display of an output on or off.
    /// </summary>
    /// <param name="output">The output to change.</param>
    /// <param name="on"><see langword="true"/> to turn the display on.</param>
    /// <returns><see langword="true"/> when the output committed the change.</returns>
    bool SetOn(IOutput output, bool on);

    /// <summary>
    /// Occurs when the power state of an output changes.
    /// </summary>
    event Action<IOutput>? PowerChanged;
}
