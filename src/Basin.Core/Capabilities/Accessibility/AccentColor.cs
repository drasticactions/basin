namespace Basin.Capabilities;

/// <summary>
/// An accent color in sRGB, each channel from 0 to 1.
/// </summary>
/// <param name="Red">The red channel.</param>
/// <param name="Green">The green channel.</param>
/// <param name="Blue">The blue channel.</param>
public readonly record struct AccentColor(double Red, double Green, double Blue);
