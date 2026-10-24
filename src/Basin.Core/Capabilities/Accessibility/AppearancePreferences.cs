namespace Basin.Capabilities;

/// <summary>
/// The appearance preferences of a session.
/// </summary>
/// <param name="ColorScheme">The preferred color scheme.</param>
/// <param name="Accent">The accent color, or <see langword="null"/> for none.</param>
/// <param name="HighContrast"><see langword="true"/> when the user asks for high contrast.</param>
/// <param name="ReducedMotion"><see langword="true"/> when the user asks for less motion.</param>
public readonly record struct AppearancePreferences(ColorSchemePreference ColorScheme, AccentColor? Accent, bool HighContrast, bool ReducedMotion);
