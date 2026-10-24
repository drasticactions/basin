namespace Basin.Capabilities;

/// <summary>The buttons that a tap with one, two or three fingers produces.</summary>
public enum InputTapButtonMap : uint
{
    /// <summary>One finger gives left, two give right and three give middle.</summary>
    LeftRightMiddle = 0,

    /// <summary>One finger gives left, two give middle and three give right.</summary>
    LeftMiddleRight = 1,
}
