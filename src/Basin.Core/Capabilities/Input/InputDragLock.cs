namespace Basin.Capabilities;

/// <summary>How a tap-and-drag continues after the finger lifts. The values match libinput.</summary>
public enum InputDragLock : uint
{
    /// <summary>The drag ends when the finger lifts.</summary>
    Disabled = 0,

    /// <summary>The drag continues for a short timeout after the finger lifts.</summary>
    EnabledTimeout = 1,

    /// <summary>The drag continues until a tap ends it.</summary>
    EnabledSticky = 2,
}
