namespace Basin.WindowManager;

/// <summary>
/// The decoration styles that a window supports and prefers, as its client reports them to the window manager.
/// </summary>
public enum DecorationHint
{
    /// <summary>The window draws its own decorations and cannot use server-side decorations.</summary>
    OnlySupportsClientSide = 0,

    /// <summary>The window supports both styles and prefers to draw its own decorations.</summary>
    PrefersClientSide = 1,

    /// <summary>The window supports both styles and prefers decorations from the window manager.</summary>
    PrefersServerSide = 2,

    /// <summary>The window supports both styles and has no preference. This is the value before the client sends a hint.</summary>
    NoPreference = 3,
}
