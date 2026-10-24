namespace Basin.Capabilities;

/// <summary>A device setting that configuration and <see cref="IInputDeviceConfiguration"/> can read and change.</summary>
public enum InputSetting
{
    /// <summary>Whether the device sends events, as an <see cref="InputSendEventsMode"/>.</summary>
    SendEvents,

    /// <summary>Whether a tap on a touchpad clicks.</summary>
    Tap,

    /// <summary>The buttons for a tap with one, two or three fingers, as an <see cref="InputTapButtonMap"/>.</summary>
    TapButtonMap,

    /// <summary>Whether tap-and-drag is on.</summary>
    Drag,

    /// <summary>How a drag continues after the finger lifts, as an <see cref="InputDragLock"/>.</summary>
    DragLock,

    /// <summary>The number of fingers that drag, as an <see cref="InputThreeFingerDrag"/>.</summary>
    ThreeFingerDrag,

    /// <summary>The six numbers of the touch calibration matrix.</summary>
    CalibrationMatrix,

    /// <summary>The pointer acceleration profile, as an <see cref="InputAccelProfile"/>.</summary>
    AccelProfile,

    /// <summary>The pointer acceleration speed, from -1 to 1.</summary>
    AccelSpeed,

    /// <summary>Whether the scroll direction is reversed.</summary>
    NaturalScroll,

    /// <summary>Whether the primary and secondary buttons swap.</summary>
    LeftHanded,

    /// <summary>How a touchpad click selects a button, as an <see cref="InputClickMethod"/>.</summary>
    ClickMethod,

    /// <summary>The buttons for a clickfinger press, as an <see cref="InputClickfingerButtonMap"/>.</summary>
    ClickfingerButtonMap,

    /// <summary>Whether a press of left and right together gives a middle click.</summary>
    MiddleEmulation,

    /// <summary>How the device scrolls, as an <see cref="InputScrollMethod"/>.</summary>
    ScrollMethod,

    /// <summary>The evdev button code that scrolls with <see cref="InputScrollMethod.OnButtonDown"/>.</summary>
    ScrollButton,

    /// <summary>Whether a click of the scroll button toggles scrolling, so that the button need not be held.</summary>
    ScrollButtonLock,

    /// <summary>Whether a touchpad ignores input while the user types.</summary>
    DisableWhileTyping,

    /// <summary>Whether a touchpad ignores input while the user moves a trackpoint.</summary>
    DisableWhileTrackpointing,

    /// <summary>The clockwise rotation of the device in degrees, from 0 to 359.</summary>
    Rotation,

    /// <summary>The factor that the compositor applies to scroll distances. It must be greater than 0.</summary>
    ScrollFactor,

    /// <summary>The name of the output that the device maps to. The compositor applies it.</summary>
    MapToOutput,

    /// <summary>The XKB rules for the keyboard keymap.</summary>
    KeymapRules,

    /// <summary>The XKB model for the keyboard keymap.</summary>
    KeymapModel,

    /// <summary>The XKB layout for the keyboard keymap.</summary>
    KeymapLayout,

    /// <summary>The XKB variant for the keyboard keymap.</summary>
    KeymapVariant,

    /// <summary>The XKB options for the keyboard keymap.</summary>
    KeymapOptions,

    /// <summary>The key repeat rate in characters per second.</summary>
    RepeatRate,

    /// <summary>The delay before key repeat starts, in milliseconds.</summary>
    RepeatDelay,
}
