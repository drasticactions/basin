namespace Basin;

/// <summary>
/// Linux evdev key and button codes from <c>linux/input-event-codes.h</c> that basin uses. Wayland key events carry these codes.
/// </summary>
public static class InputCodes
{
    /// <summary>
    /// The evdev code of the Escape key.
    /// </summary>
    public const uint KeyEsc = 1;

    /// <summary>
    /// The evdev code of the Backspace key.
    /// </summary>
    public const uint KeyBackspace = 14;

    /// <summary>
    /// The evdev code of the Tab key.
    /// </summary>
    public const uint KeyTab = 15;

    /// <summary>
    /// The evdev code of the Enter key on the main keyboard.
    /// </summary>
    public const uint KeyEnter = 28;

    /// <summary>
    /// The evdev code of the left Control key.
    /// </summary>
    public const uint KeyLeftCtrl = 29;

    /// <summary>
    /// The evdev code of the S key.
    /// </summary>
    public const uint KeyS = 31;

    /// <summary>
    /// The evdev code of the F key.
    /// </summary>
    public const uint KeyF = 33;

    /// <summary>
    /// The evdev code of the K key.
    /// </summary>
    public const uint KeyK = 37;

    /// <summary>
    /// The evdev code of the left Shift key.
    /// </summary>
    public const uint KeyLeftShift = 42;

    /// <summary>
    /// The evdev code of the Space bar.
    /// </summary>
    public const uint KeySpace = 57;

    /// <summary>
    /// The evdev code of the Caps Lock key.
    /// </summary>
    public const uint KeyCapsLock = 58;

    /// <summary>
    /// The evdev code of the N key.
    /// </summary>
    public const uint KeyN = 49;

    /// <summary>
    /// The evdev code of the M key.
    /// </summary>
    public const uint KeyM = 50;

    /// <summary>
    /// The evdev code of the right Shift key.
    /// </summary>
    public const uint KeyRightShift = 54;

    /// <summary>
    /// The evdev code of the left Alt key.
    /// </summary>
    public const uint KeyLeftAlt = 56;

    /// <summary>
    /// The evdev code of the F1 key.
    /// </summary>
    public const uint KeyF1 = 59;

    /// <summary>
    /// The evdev code of the F9 key.
    /// </summary>
    public const uint KeyF9 = 67;

    /// <summary>
    /// The evdev code of the F10 key.
    /// </summary>
    public const uint KeyF10 = 68;

    /// <summary>
    /// The evdev code of the Enter key on the keypad.
    /// </summary>
    public const uint KeyKpEnter = 96;

    /// <summary>
    /// The evdev code of the right Control key.
    /// </summary>
    public const uint KeyRightCtrl = 97;

    /// <summary>
    /// The evdev code of the right Alt key.
    /// </summary>
    public const uint KeyRightAlt = 100;

    /// <summary>
    /// The evdev code of the Up arrow key.
    /// </summary>
    public const uint KeyUp = 103;

    /// <summary>
    /// The evdev code of the Left arrow key.
    /// </summary>
    public const uint KeyLeft = 105;

    /// <summary>
    /// The evdev code of the Right arrow key.
    /// </summary>
    public const uint KeyRight = 106;

    /// <summary>
    /// The evdev code of the Down arrow key.
    /// </summary>
    public const uint KeyDown = 108;

    /// <summary>
    /// The evdev code of the left Meta (Super) key.
    /// </summary>
    public const uint KeyLeftMeta = 125;

    /// <summary>
    /// The evdev code of the right Meta (Super) key.
    /// </summary>
    public const uint KeyRightMeta = 126;

    /// <summary>
    /// The evdev code of the brightness-down key.
    /// </summary>
    public const uint KeyBrightnessDown = 224;

    /// <summary>
    /// The evdev code of the brightness-up key.
    /// </summary>
    public const uint KeyBrightnessUp = 225;

    /// <summary>
    /// The evdev code of the left mouse button.
    /// </summary>
    public const uint BtnLeft = 0x110;

    /// <summary>
    /// The evdev code of the right mouse button.
    /// </summary>
    public const uint BtnRight = 0x111;

    /// <summary>
    /// The evdev code of the middle mouse button.
    /// </summary>
    public const uint BtnMiddle = 0x112;

    /// <summary>
    /// The evdev code of the side mouse button.
    /// </summary>
    public const uint BtnSide = 0x113;

    /// <summary>
    /// The evdev code of the extra mouse button.
    /// </summary>
    public const uint BtnExtra = 0x114;
}
