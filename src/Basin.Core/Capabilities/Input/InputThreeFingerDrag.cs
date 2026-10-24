namespace Basin.Capabilities;

/// <summary>The number of fingers that start a drag on a touchpad without a click.</summary>
public enum InputThreeFingerDrag : uint
{
    /// <summary>No finger drag.</summary>
    Disabled = 0,

    /// <summary>Three fingers drag.</summary>
    EnabledThreeFingers = 1,

    /// <summary>Four fingers drag.</summary>
    EnabledFourFingers = 2,
}
