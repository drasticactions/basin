using Basin.Seat;
using SDL3;

namespace Basin.Sdl;

/// <summary>
/// Maps SDL scancodes and mouse buttons to basin key codes and evdev codes.
/// An SDL scancode is a USB HID usage, so the map is the same on every operating system.
/// </summary>
public static class SdlKeyMap
{
    private static readonly (SDL.Scancode Scancode, HostKeyCode Code)[] Table =
    [
        (SDL.Scancode.A, HostKeyCode.KeyA),
        (SDL.Scancode.B, HostKeyCode.KeyB),
        (SDL.Scancode.C, HostKeyCode.KeyC),
        (SDL.Scancode.D, HostKeyCode.KeyD),
        (SDL.Scancode.E, HostKeyCode.KeyE),
        (SDL.Scancode.F, HostKeyCode.KeyF),
        (SDL.Scancode.G, HostKeyCode.KeyG),
        (SDL.Scancode.H, HostKeyCode.KeyH),
        (SDL.Scancode.I, HostKeyCode.KeyI),
        (SDL.Scancode.J, HostKeyCode.KeyJ),
        (SDL.Scancode.K, HostKeyCode.KeyK),
        (SDL.Scancode.L, HostKeyCode.KeyL),
        (SDL.Scancode.M, HostKeyCode.KeyM),
        (SDL.Scancode.N, HostKeyCode.KeyN),
        (SDL.Scancode.O, HostKeyCode.KeyO),
        (SDL.Scancode.P, HostKeyCode.KeyP),
        (SDL.Scancode.Q, HostKeyCode.KeyQ),
        (SDL.Scancode.R, HostKeyCode.KeyR),
        (SDL.Scancode.S, HostKeyCode.KeyS),
        (SDL.Scancode.T, HostKeyCode.KeyT),
        (SDL.Scancode.U, HostKeyCode.KeyU),
        (SDL.Scancode.V, HostKeyCode.KeyV),
        (SDL.Scancode.W, HostKeyCode.KeyW),
        (SDL.Scancode.X, HostKeyCode.KeyX),
        (SDL.Scancode.Y, HostKeyCode.KeyY),
        (SDL.Scancode.Z, HostKeyCode.KeyZ),
        (SDL.Scancode.Alpha1, HostKeyCode.Digit1),
        (SDL.Scancode.Alpha2, HostKeyCode.Digit2),
        (SDL.Scancode.Alpha3, HostKeyCode.Digit3),
        (SDL.Scancode.Alpha4, HostKeyCode.Digit4),
        (SDL.Scancode.Alpha5, HostKeyCode.Digit5),
        (SDL.Scancode.Alpha6, HostKeyCode.Digit6),
        (SDL.Scancode.Alpha7, HostKeyCode.Digit7),
        (SDL.Scancode.Alpha8, HostKeyCode.Digit8),
        (SDL.Scancode.Alpha9, HostKeyCode.Digit9),
        (SDL.Scancode.Alpha0, HostKeyCode.Digit0),
        (SDL.Scancode.Return, HostKeyCode.Enter),
        (SDL.Scancode.Escape, HostKeyCode.Escape),
        (SDL.Scancode.Backspace, HostKeyCode.Backspace),
        (SDL.Scancode.Tab, HostKeyCode.Tab),
        (SDL.Scancode.Space, HostKeyCode.Space),
        (SDL.Scancode.Minus, HostKeyCode.Minus),
        (SDL.Scancode.Equals, HostKeyCode.Equal),
        (SDL.Scancode.Leftbracket, HostKeyCode.BracketLeft),
        (SDL.Scancode.Rightbracket, HostKeyCode.BracketRight),
        (SDL.Scancode.Backslash, HostKeyCode.Backslash),
        (SDL.Scancode.NonUsHash, HostKeyCode.Backslash),
        (SDL.Scancode.Semicolon, HostKeyCode.Semicolon),
        (SDL.Scancode.Apostrophe, HostKeyCode.Quote),
        (SDL.Scancode.Grave, HostKeyCode.Backquote),
        (SDL.Scancode.Comma, HostKeyCode.Comma),
        (SDL.Scancode.Period, HostKeyCode.Period),
        (SDL.Scancode.Slash, HostKeyCode.Slash),
        (SDL.Scancode.Capslock, HostKeyCode.CapsLock),
        (SDL.Scancode.F1, HostKeyCode.F1),
        (SDL.Scancode.F2, HostKeyCode.F2),
        (SDL.Scancode.F3, HostKeyCode.F3),
        (SDL.Scancode.F4, HostKeyCode.F4),
        (SDL.Scancode.F5, HostKeyCode.F5),
        (SDL.Scancode.F6, HostKeyCode.F6),
        (SDL.Scancode.F7, HostKeyCode.F7),
        (SDL.Scancode.F8, HostKeyCode.F8),
        (SDL.Scancode.F9, HostKeyCode.F9),
        (SDL.Scancode.F10, HostKeyCode.F10),
        (SDL.Scancode.F11, HostKeyCode.F11),
        (SDL.Scancode.F12, HostKeyCode.F12),
        (SDL.Scancode.F13, HostKeyCode.F13),
        (SDL.Scancode.F14, HostKeyCode.F14),
        (SDL.Scancode.F15, HostKeyCode.F15),
        (SDL.Scancode.F16, HostKeyCode.F16),
        (SDL.Scancode.F17, HostKeyCode.F17),
        (SDL.Scancode.F18, HostKeyCode.F18),
        (SDL.Scancode.F19, HostKeyCode.F19),
        (SDL.Scancode.F20, HostKeyCode.F20),
        (SDL.Scancode.F21, HostKeyCode.F21),
        (SDL.Scancode.F22, HostKeyCode.F22),
        (SDL.Scancode.F23, HostKeyCode.F23),
        (SDL.Scancode.F24, HostKeyCode.F24),
        (SDL.Scancode.Printscreen, HostKeyCode.PrintScreen),
        (SDL.Scancode.Scrolllock, HostKeyCode.ScrollLock),
        (SDL.Scancode.Pause, HostKeyCode.Pause),
        (SDL.Scancode.Insert, HostKeyCode.Insert),
        (SDL.Scancode.Home, HostKeyCode.Home),
        (SDL.Scancode.Pageup, HostKeyCode.PageUp),
        (SDL.Scancode.Delete, HostKeyCode.Delete),
        (SDL.Scancode.End, HostKeyCode.End),
        (SDL.Scancode.Pagedown, HostKeyCode.PageDown),
        (SDL.Scancode.Right, HostKeyCode.ArrowRight),
        (SDL.Scancode.Left, HostKeyCode.ArrowLeft),
        (SDL.Scancode.Down, HostKeyCode.ArrowDown),
        (SDL.Scancode.Up, HostKeyCode.ArrowUp),
        (SDL.Scancode.NumLockClear, HostKeyCode.NumLock),
        (SDL.Scancode.KpDivide, HostKeyCode.NumpadDivide),
        (SDL.Scancode.KpMultiply, HostKeyCode.NumpadMultiply),
        (SDL.Scancode.KpMinus, HostKeyCode.NumpadSubtract),
        (SDL.Scancode.KpPlus, HostKeyCode.NumpadAdd),
        (SDL.Scancode.KpEnter, HostKeyCode.NumpadEnter),
        (SDL.Scancode.Kp1, HostKeyCode.Numpad1),
        (SDL.Scancode.Kp2, HostKeyCode.Numpad2),
        (SDL.Scancode.Kp3, HostKeyCode.Numpad3),
        (SDL.Scancode.Kp4, HostKeyCode.Numpad4),
        (SDL.Scancode.Kp5, HostKeyCode.Numpad5),
        (SDL.Scancode.Kp6, HostKeyCode.Numpad6),
        (SDL.Scancode.Kp7, HostKeyCode.Numpad7),
        (SDL.Scancode.Kp8, HostKeyCode.Numpad8),
        (SDL.Scancode.Kp9, HostKeyCode.Numpad9),
        (SDL.Scancode.Kp0, HostKeyCode.Numpad0),
        (SDL.Scancode.KpPeriod, HostKeyCode.NumpadDecimal),
        (SDL.Scancode.NonUsBackSlash, HostKeyCode.IntlBackslash),
        (SDL.Scancode.Application, HostKeyCode.ContextMenu),
        (SDL.Scancode.Power, HostKeyCode.Power),
        (SDL.Scancode.KpEquals, HostKeyCode.NumpadEqual),
        (SDL.Scancode.Help, HostKeyCode.Help),
        (SDL.Scancode.Menu, HostKeyCode.ContextMenu),
        (SDL.Scancode.Stop, HostKeyCode.BrowserStop),
        (SDL.Scancode.Again, HostKeyCode.Again),
        (SDL.Scancode.Undo, HostKeyCode.Undo),
        (SDL.Scancode.Cut, HostKeyCode.Cut),
        (SDL.Scancode.Copy, HostKeyCode.Copy),
        (SDL.Scancode.Paste, HostKeyCode.Paste),
        (SDL.Scancode.Find, HostKeyCode.Find),
        (SDL.Scancode.Mute, HostKeyCode.AudioVolumeMute),
        (SDL.Scancode.VolumeUp, HostKeyCode.AudioVolumeUp),
        (SDL.Scancode.VolumeDown, HostKeyCode.AudioVolumeDown),
        (SDL.Scancode.KpComma, HostKeyCode.NumpadComma),
        (SDL.Scancode.International1, HostKeyCode.IntlRo),
        (SDL.Scancode.International2, HostKeyCode.KanaMode),
        (SDL.Scancode.International3, HostKeyCode.IntlYen),
        (SDL.Scancode.International4, HostKeyCode.Convert),
        (SDL.Scancode.International5, HostKeyCode.NonConvert),
        (SDL.Scancode.Lang1, HostKeyCode.Lang1),
        (SDL.Scancode.Lang2, HostKeyCode.Lang2),
        (SDL.Scancode.Lang3, HostKeyCode.Lang3),
        (SDL.Scancode.Lang4, HostKeyCode.Lang4),
        (SDL.Scancode.Lang5, HostKeyCode.Lang5),
        (SDL.Scancode.KpLeftParen, HostKeyCode.NumpadParenLeft),
        (SDL.Scancode.KpRightParen, HostKeyCode.NumpadParenRight),
        (SDL.Scancode.LCtrl, HostKeyCode.ControlLeft),
        (SDL.Scancode.LShift, HostKeyCode.ShiftLeft),
        (SDL.Scancode.LAlt, HostKeyCode.AltLeft),
        (SDL.Scancode.LGUI, HostKeyCode.MetaLeft),
        (SDL.Scancode.RCtrl, HostKeyCode.ControlRight),
        (SDL.Scancode.RShift, HostKeyCode.ShiftRight),
        (SDL.Scancode.RAlt, HostKeyCode.AltRight),
        (SDL.Scancode.RGUI, HostKeyCode.MetaRight),
        (SDL.Scancode.Sleep, HostKeyCode.Sleep),
        (SDL.Scancode.Wake, HostKeyCode.WakeUp),
        (SDL.Scancode.MediaPlayPause, HostKeyCode.MediaPlayPause),
        (SDL.Scancode.MediaNextTrack, HostKeyCode.MediaTrackNext),
        (SDL.Scancode.MediaPreviousTrack, HostKeyCode.MediaTrackPrevious),
        (SDL.Scancode.MediaStop, HostKeyCode.MediaStop),
        (SDL.Scancode.MediaEject, HostKeyCode.Eject),
        (SDL.Scancode.ACSearch, HostKeyCode.BrowserSearch),
        (SDL.Scancode.ACHome, HostKeyCode.BrowserHome),
        (SDL.Scancode.ACBack, HostKeyCode.BrowserBack),
        (SDL.Scancode.ACForward, HostKeyCode.BrowserForward),
        (SDL.Scancode.ACStop, HostKeyCode.BrowserStop),
        (SDL.Scancode.ACRefresh, HostKeyCode.BrowserRefresh),
        (SDL.Scancode.ACBookmarks, HostKeyCode.BrowserFavorites),
    ];

    private static readonly HostKeyCode[] ByScancode = Build();

    /// <summary>
    /// Gets every scancode pair of the map. When two pairs share a scancode, <see cref="ToHostKey"/> uses the first.
    /// </summary>
    public static IReadOnlyList<(SDL.Scancode Scancode, HostKeyCode Code)> Entries => Table;

    /// <summary>
    /// Maps an SDL scancode to a basin key code.
    /// </summary>
    /// <param name="scancode">The scancode of an SDL keyboard event.</param>
    /// <returns>The key code, or <see cref="HostKeyCode.None"/> when the map has no entry.</returns>
    public static HostKeyCode ToHostKey(SDL.Scancode scancode)
    {
        var index = (int)scancode;
        return index > 0 && index < ByScancode.Length ? ByScancode[index] : HostKeyCode.None;
    }

    /// <summary>
    /// Maps an SDL scancode to an evdev key code.
    /// </summary>
    /// <param name="scancode">The scancode of an SDL keyboard event.</param>
    /// <param name="evdev">The evdev code, or 0 when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="false"/> when the scancode has no evdev code.</returns>
    public static bool TryToEvdev(SDL.Scancode scancode, out uint evdev) =>
        HostKeyMap.TryToEvdev(ToHostKey(scancode), out evdev);

    /// <summary>
    /// Maps an SDL mouse button to an evdev button code. Buttons 1 to 5 are left, middle, right, side and extra.
    /// </summary>
    /// <param name="button">The SDL button number.</param>
    /// <returns>The evdev code, or 0 for a button above 5.</returns>
    public static uint ButtonCode(byte button) => button switch
    {
        1 => InputCodes.BtnLeft,
        2 => InputCodes.BtnMiddle,
        3 => InputCodes.BtnRight,
        4 => InputCodes.BtnSide,
        5 => InputCodes.BtnExtra,
        _ => 0,
    };

    private static HostKeyCode[] Build()
    {
        var map = new HostKeyCode[(int)SDL.Scancode.Count];
        foreach (var (scancode, code) in Table)
        {
            if (map[(int)scancode] == HostKeyCode.None)
            {
                map[(int)scancode] = code;
            }
        }

        return map;
    }
}
