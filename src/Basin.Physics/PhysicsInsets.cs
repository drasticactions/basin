namespace Basin.Physics;

/// <summary>
/// Grows a bound body past the toplevel geometry, in logical pixels. A consumer uses it when its model geometry does not include the decorations.
/// </summary>
/// <param name="Left">The pixels added on the left side.</param>
/// <param name="Top">The pixels added above the top edge.</param>
/// <param name="Right">The pixels added on the right side.</param>
/// <param name="Bottom">The pixels added below the bottom edge.</param>
public readonly record struct PhysicsInsets(int Left, int Top, int Right, int Bottom)
{
    /// <summary>
    /// Gets the sum of <see cref="Left"/> and <see cref="Right"/>, which the insets add to the body width.
    /// </summary>
    public int Horizontal => Left + Right;

    /// <summary>
    /// Gets the sum of <see cref="Top"/> and <see cref="Bottom"/>, which the insets add to the body height.
    /// </summary>
    public int Vertical => Top + Bottom;
}
