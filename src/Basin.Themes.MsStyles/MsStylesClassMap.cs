namespace Basin.Themes.MsStyles;

/// <summary>
/// Lists the part and state names of one theme class, as the uxtheme style map defines them.
/// </summary>
/// <param name="Name">The class name without an application prefix, for example <c>WINDOW</c>.</param>
/// <param name="Entries">The part and state names. An entry with state 0 names a part.</param>
public sealed record MsStylesClassMap(string Name, MsStylesPartStateName[] Entries);
