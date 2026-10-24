namespace Basin.Capabilities;

/// <summary>The kind of movement that a custom acceleration curve applies to. The values match libinput.</summary>
public enum InputAccelCurveType : uint
{
    /// <summary>The curve for each kind of movement that has no curve of its own.</summary>
    Fallback = 0,

    /// <summary>The curve for pointer motion.</summary>
    Motion = 1,

    /// <summary>The curve for scrolling.</summary>
    Scroll = 2,
}
