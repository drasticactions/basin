namespace Basin.Capabilities;

/// <summary>
/// Resolves a cursor name or a <see cref="CursorShape"/> to a cursor image at a given scale.
/// </summary>
public interface ICursorTheme
{
    /// <summary>
    /// Finds the image for an X cursor theme name, for example <c>left_ptr</c>.
    /// </summary>
    /// <param name="shapeName">The cursor name in the theme.</param>
    /// <param name="scale">The output scale factor that the image is for.</param>
    /// <param name="image">The image when the method returns <see langword="true"/>, otherwise the default value.</param>
    /// <returns><see langword="true"/> when the theme has an image for the name.</returns>
    bool TryResolve(string shapeName, double scale, out CursorImage image);

    /// <summary>
    /// Finds the image for a shape. The default implementation maps the shape through <see cref="CursorShapeNames.NameOf"/>.
    /// </summary>
    /// <param name="shape">The cursor shape.</param>
    /// <param name="scale">The output scale factor that the image is for.</param>
    /// <param name="image">The image when the method returns <see langword="true"/>, otherwise the default value.</param>
    /// <returns><see langword="true"/> when the theme has an image for the shape.</returns>
    bool TryResolve(CursorShape shape, double scale, out CursorImage image) =>
        TryResolve(CursorShapeNames.NameOf(shape), scale, out image);
}
