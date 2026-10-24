namespace Basin.WindowManager;

/// <summary>
/// A window manager's own wrapper around a <see cref="WmWindow"/>, as <see cref="WmSession{TWindow}"/> stores it.
/// </summary>
public interface IWmManagedWindow
{
    /// <summary>
    /// Gets the library window that this wrapper manages. The value must not change for the life of the wrapper.
    /// </summary>
    WmWindow Window { get; }
}
