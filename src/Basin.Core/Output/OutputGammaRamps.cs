using Pixman;

namespace Basin;

/// <summary>
/// Holds one gamma lookup table per channel, as a gamma-control client sets it. Each entry maps an input level to a 16-bit output level.
/// </summary>
/// <param name="Red">The table for the red channel.</param>
/// <param name="Green">The table for the green channel. It has the same length as <paramref name="Red"/>.</param>
/// <param name="Blue">The table for the blue channel. It has the same length as <paramref name="Red"/>.</param>
public readonly record struct OutputGammaRamps(ushort[] Red, ushort[] Green, ushort[] Blue);
