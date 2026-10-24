namespace Basin.Effects;

/// <summary>
/// Receives the dim of each window from a <see cref="DimInactiveEffect{TWindow}"/>. The consumer puts the values on the window content, for example on <c>SceneBuffer.Brightness</c> and <c>SceneBuffer.Saturation</c>.
/// </summary>
/// <typeparam name="TWindow">The window type of the consumer.</typeparam>
public interface IDimInactiveTarget<in TWindow>
    where TWindow : class
{
    /// <summary>
    /// Applies the dim of a window. The effect calls it when the dim changes, on every step of a fade.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="brightness">The factor for the brightness of the window, 1 for none.</param>
    /// <param name="saturation">The factor for the saturation of the window, 1 for none.</param>
    void Apply(TWindow window, float brightness, float saturation);
}
