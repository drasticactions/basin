namespace Basin.Effects;

/// <summary>
/// One blurred layer of a drop shadow texture. Distances are in logical pixels.
/// </summary>
/// <param name="OffsetX">The horizontal offset of the layer from the window box.</param>
/// <param name="OffsetY">The vertical offset of the layer from the window box.</param>
/// <param name="Radius">The CSS blur radius. The standard deviation is half of this value.</param>
/// <param name="Opacity">The layer alpha, from 0 to 1.</param>
public readonly record struct DropShadowLayer(double OffsetX, double OffsetY, double Radius, double Opacity);
