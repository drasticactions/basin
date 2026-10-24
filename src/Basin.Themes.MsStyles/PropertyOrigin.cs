namespace Basin.Themes.MsStyles;

/// <summary>
/// Identifies the block that a theme property value came from. The values are the uxtheme <c>PROPERTYORIGIN</c> values.
/// </summary>
public enum PropertyOrigin
{
    /// <summary>
    /// The value is set on the part and state.
    /// </summary>
    State,
    /// <summary>
    /// The value is set on the part with state 0.
    /// </summary>
    Part,
    /// <summary>
    /// The value is set on the class with part 0 and state 0.
    /// </summary>
    Class,
    /// <summary>
    /// The value is set in the <c>globals</c> class or in the system metrics.
    /// </summary>
    Global,
    /// <summary>
    /// No block in the lookup chain sets the value.
    /// </summary>
    NotFound,
}
