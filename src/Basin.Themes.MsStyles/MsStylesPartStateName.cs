namespace Basin.Themes.MsStyles;

/// <summary>
/// Names one part or one state of a theme class.
/// </summary>
/// <param name="Part">The part ID. It is 0 for a state that applies to every part.</param>
/// <param name="State">The state ID. It is 0 when the entry names a part.</param>
/// <param name="Name">The name in the INI section text, for example <c>CAPTION</c>.</param>
public readonly record struct MsStylesPartStateName(int Part, int State, string Name);
