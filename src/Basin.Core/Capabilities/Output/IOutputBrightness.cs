namespace Basin.Capabilities;

/// <summary>
/// Controls the hardware brightness of a display, for example a backlight. Values are in the device's own range.
/// </summary>
public interface IOutputBrightness
{
    /// <summary>
    /// Tells whether the brightness of an output can be controlled.
    /// </summary>
    /// <param name="output">The output to check.</param>
    /// <returns><see langword="true"/> when the output has a brightness control.</returns>
    bool Supports(IOutput output);

    /// <summary>
    /// Gives the largest brightness value of an output.
    /// </summary>
    /// <param name="output">The output to read.</param>
    /// <returns>The maximum in device units, or 0 when the output has no control.</returns>
    uint Max(IOutput output);

    /// <summary>
    /// Reads the current brightness of an output.
    /// </summary>
    /// <param name="output">The output to read.</param>
    /// <param name="value">The brightness from 0 to <see cref="Max"/> on success.</param>
    /// <returns><see langword="false"/> when the value cannot be read.</returns>
    bool TryGet(IOutput output, out uint value);

    /// <summary>
    /// Sets the brightness of an output.
    /// </summary>
    /// <param name="output">The output to change.</param>
    /// <param name="value">The brightness from 0 to <see cref="Max"/>.</param>
    /// <returns><see langword="true"/> when the device took the value.</returns>
    bool Set(IOutput output, uint value);

    /// <summary>
    /// Tells whether the control for an output goes over DDC/CI. The compositor does not use such a control when the
    /// configuration does not allow DDC/CI. The default is false.
    /// </summary>
    /// <param name="output">The output to check.</param>
    /// <returns><see langword="true"/> when brightness changes go to the monitor over DDC/CI.</returns>
    bool UsesDdcCi(IOutput output) => false;

    /// <summary>
    /// Occurs when the brightness of an output changes, also when something other than the compositor changed it.
    /// </summary>
    event Action<IOutput>? BrightnessChanged;
}
