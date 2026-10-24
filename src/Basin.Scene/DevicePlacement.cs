namespace Basin.Scene;

/// <summary>
/// Converts logical coordinates to device pixels and back with the rounding that the scene uses, so that adjacent spans meet without gaps.
/// </summary>
public static class DevicePlacement
{
    /// <summary>
    /// Rounds a fractional output scale to the integer scale that chrome assets use.
    /// </summary>
    /// <param name="scale">The output scale.</param>
    /// <returns>The rounded scale. It is at least 1.</returns>
    public static int ChromeScale(double scale) => Math.Max(1, (int)Math.Round(scale));

    /// <summary>
    /// Converts a logical coordinate to a device pixel coordinate by rounding to the nearest pixel.
    /// </summary>
    /// <param name="logical">The coordinate in logical pixels.</param>
    /// <param name="scale">The output scale.</param>
    /// <returns>The coordinate in device pixels.</returns>
    public static int ToDevice(int logical, double scale) => (int)Math.Round(logical * scale);

    /// <summary>
    /// Converts a logical span to device pixels from its rounded end points. Two adjacent spans thus cover the device pixels with no gap or overlap.
    /// </summary>
    /// <param name="logical">The start of the span, in logical pixels.</param>
    /// <param name="length">The length of the span, in logical pixels.</param>
    /// <param name="scale">The output scale.</param>
    /// <returns>The length of the span in device pixels.</returns>
    public static int ToDeviceSpan(int logical, int length, double scale) =>
        ToDevice(logical + length, scale) - ToDevice(logical, scale);

    /// <summary>
    /// Converts a device pixel coordinate to the logical coordinate at or below it, plus the device pixels that rounding leaves over.
    /// </summary>
    /// <param name="device">The coordinate in device pixels.</param>
    /// <param name="scale">The output scale.</param>
    /// <returns>The logical coordinate, and the device pixel offset from <see cref="ToDevice(int, double)"/> of that coordinate.</returns>
    public static (int Logical, int Nudge) FromDevice(int device, double scale)
    {
        var logical = (int)Math.Floor(device / scale);
        return (logical, device - ToDevice(logical, scale));
    }

    /// <summary>
    /// Converts a device pixel length to the smallest logical length that covers it.
    /// </summary>
    /// <param name="device">The length in device pixels.</param>
    /// <param name="scale">The output scale.</param>
    /// <returns>The length in logical pixels, rounded up.</returns>
    public static int ToLogicalSpan(int device, double scale) => (int)Math.Ceiling(device / scale);

    /// <summary>
    /// Gets the size of the current mode of an output after its transform. A 90 or 270 degree transform swaps the width and the height.
    /// </summary>
    /// <param name="output">The output to measure.</param>
    /// <returns>The transformed mode size, in device pixels.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    public static (int Width, int Height) OutputSize(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return OutputTransforms.SwapsAxes(output.Transform)
            ? (output.CurrentMode.Height, output.CurrentMode.Width)
            : (output.CurrentMode.Width, output.CurrentMode.Height);
    }
}
