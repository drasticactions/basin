namespace Basin.UI.Paper;

/// <summary>
/// One setting that a settings widget edits, with the state that the row shows next to it.
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
public interface ISettingValue<T>
{
    /// <summary>
    /// Gets or sets the value. A widget sets it each time the user changes the control.
    /// </summary>
    T Value { get; set; }

    /// <summary>
    /// Gets a value indicating whether the value differs from the saved value. The row then shows an accent dot.
    /// </summary>
    bool IsDirty { get; }

    /// <summary>
    /// Gets a short warning text that the row shows as a pill, or <see langword="null"/>.
    /// </summary>
    string? Badge { get; }

    /// <summary>
    /// Gets an error text that the row shows below the control, or <see langword="null"/>.
    /// </summary>
    string? Error { get; }

    /// <summary>
    /// Gets a warning text that the row shows below the control when there is no error. The default is <see langword="null"/>.
    /// </summary>
    string? Hint => null;
}
