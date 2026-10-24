namespace Basin.Freedesktop;

/// <summary>
/// The registered main categories of the Desktop Menu specification, in the order that grouping uses.
/// </summary>
public enum DesktopMainCategory
{
    /// <summary>Multimedia. It also covers the <c>Audio</c> and <c>Video</c> categories.</summary>
    AudioVideo,
    /// <summary>Software development tools.</summary>
    Development,
    /// <summary>Educational software.</summary>
    Education,
    /// <summary>Games.</summary>
    Game,
    /// <summary>Graphics viewers and editors.</summary>
    Graphics,
    /// <summary>Network applications, for example web browsers and mail clients.</summary>
    Network,
    /// <summary>Office applications.</summary>
    Office,
    /// <summary>Scientific software.</summary>
    Science,
    /// <summary>Settings applications.</summary>
    Settings,
    /// <summary>System tools.</summary>
    System,
    /// <summary>Small utilities, which a default menu calls accessories.</summary>
    Utility,
    /// <summary>An entry that names no registered main category.</summary>
    Other,
}
