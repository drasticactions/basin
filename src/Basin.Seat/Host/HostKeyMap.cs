namespace Basin.Seat;

public static class HostKeyMap
{
    private static readonly (HostKeyCode Code, uint Evdev)[] Table =
    [
        (HostKeyCode.Escape, 1),
        (HostKeyCode.Digit1, 2),
        (HostKeyCode.Digit2, 3),
        (HostKeyCode.Digit3, 4),
        (HostKeyCode.Digit4, 5),
        (HostKeyCode.Digit5, 6),
        (HostKeyCode.Digit6, 7),
        (HostKeyCode.Digit7, 8),
        (HostKeyCode.Digit8, 9),
        (HostKeyCode.Digit9, 10),
        (HostKeyCode.Digit0, 11),
        (HostKeyCode.Minus, 12),
        (HostKeyCode.Equal, 13),
        (HostKeyCode.Backspace, 14),
        (HostKeyCode.Tab, 15),
        (HostKeyCode.KeyQ, 16),
        (HostKeyCode.KeyW, 17),
        (HostKeyCode.KeyE, 18),
        (HostKeyCode.KeyR, 19),
        (HostKeyCode.KeyT, 20),
        (HostKeyCode.KeyY, 21),
        (HostKeyCode.KeyU, 22),
        (HostKeyCode.KeyI, 23),
        (HostKeyCode.KeyO, 24),
        (HostKeyCode.KeyP, 25),
        (HostKeyCode.BracketLeft, 26),
        (HostKeyCode.BracketRight, 27),
        (HostKeyCode.Enter, 28),
        (HostKeyCode.ControlLeft, 29),
        (HostKeyCode.KeyA, 30),
        (HostKeyCode.KeyS, 31),
        (HostKeyCode.KeyD, 32),
        (HostKeyCode.KeyF, 33),
        (HostKeyCode.KeyG, 34),
        (HostKeyCode.KeyH, 35),
        (HostKeyCode.KeyJ, 36),
        (HostKeyCode.KeyK, 37),
        (HostKeyCode.KeyL, 38),
        (HostKeyCode.Semicolon, 39),
        (HostKeyCode.Quote, 40),
        (HostKeyCode.Backquote, 41),
        (HostKeyCode.ShiftLeft, 42),
        (HostKeyCode.Backslash, 43),
        (HostKeyCode.KeyZ, 44),
        (HostKeyCode.KeyX, 45),
        (HostKeyCode.KeyC, 46),
        (HostKeyCode.KeyV, 47),
        (HostKeyCode.KeyB, 48),
        (HostKeyCode.KeyN, 49),
        (HostKeyCode.KeyM, 50),
        (HostKeyCode.Comma, 51),
        (HostKeyCode.Period, 52),
        (HostKeyCode.Slash, 53),
        (HostKeyCode.ShiftRight, 54),
        (HostKeyCode.NumpadMultiply, 55),
        (HostKeyCode.AltLeft, 56),
        (HostKeyCode.Space, 57),
        (HostKeyCode.CapsLock, 58),
        (HostKeyCode.F1, 59),
        (HostKeyCode.F2, 60),
        (HostKeyCode.F3, 61),
        (HostKeyCode.F4, 62),
        (HostKeyCode.F5, 63),
        (HostKeyCode.F6, 64),
        (HostKeyCode.F7, 65),
        (HostKeyCode.F8, 66),
        (HostKeyCode.F9, 67),
        (HostKeyCode.F10, 68),
        (HostKeyCode.NumLock, 69),
        (HostKeyCode.ScrollLock, 70),
        (HostKeyCode.Numpad7, 71),
        (HostKeyCode.Numpad8, 72),
        (HostKeyCode.Numpad9, 73),
        (HostKeyCode.NumpadSubtract, 74),
        (HostKeyCode.Numpad4, 75),
        (HostKeyCode.Numpad5, 76),
        (HostKeyCode.Numpad6, 77),
        (HostKeyCode.NumpadAdd, 78),
        (HostKeyCode.Numpad1, 79),
        (HostKeyCode.Numpad2, 80),
        (HostKeyCode.Numpad3, 81),
        (HostKeyCode.Numpad0, 82),
        (HostKeyCode.NumpadDecimal, 83),
        (HostKeyCode.Lang5, 85),
        (HostKeyCode.IntlBackslash, 86),
        (HostKeyCode.F11, 87),
        (HostKeyCode.F12, 88),
        (HostKeyCode.IntlRo, 89),
        (HostKeyCode.Lang3, 90),
        (HostKeyCode.Lang4, 91),
        (HostKeyCode.Convert, 92),
        (HostKeyCode.KanaMode, 93),
        (HostKeyCode.NonConvert, 94),
        (HostKeyCode.NumpadEnter, 96),
        (HostKeyCode.ControlRight, 97),
        (HostKeyCode.NumpadDivide, 98),
        (HostKeyCode.PrintScreen, 99),
        (HostKeyCode.AltRight, 100),
        (HostKeyCode.Home, 102),
        (HostKeyCode.ArrowUp, 103),
        (HostKeyCode.PageUp, 104),
        (HostKeyCode.ArrowLeft, 105),
        (HostKeyCode.ArrowRight, 106),
        (HostKeyCode.End, 107),
        (HostKeyCode.ArrowDown, 108),
        (HostKeyCode.PageDown, 109),
        (HostKeyCode.Insert, 110),
        (HostKeyCode.Delete, 111),
        (HostKeyCode.AudioVolumeMute, 113),
        (HostKeyCode.AudioVolumeDown, 114),
        (HostKeyCode.AudioVolumeUp, 115),
        (HostKeyCode.Power, 116),
        (HostKeyCode.NumpadEqual, 117),
        (HostKeyCode.Pause, 119),
        (HostKeyCode.NumpadComma, 121),
        (HostKeyCode.Lang1, 122),
        (HostKeyCode.Lang2, 123),
        (HostKeyCode.IntlYen, 124),
        (HostKeyCode.MetaLeft, 125),
        (HostKeyCode.MetaRight, 126),
        (HostKeyCode.ContextMenu, 127),
        (HostKeyCode.BrowserStop, 128),
        (HostKeyCode.Again, 129),
        (HostKeyCode.Undo, 131),
        (HostKeyCode.Copy, 133),
        (HostKeyCode.Paste, 135),
        (HostKeyCode.Find, 136),
        (HostKeyCode.Cut, 137),
        (HostKeyCode.Help, 138),
        (HostKeyCode.Sleep, 142),
        (HostKeyCode.WakeUp, 143),
        (HostKeyCode.LaunchMail, 155),
        (HostKeyCode.BrowserFavorites, 156),
        (HostKeyCode.BrowserBack, 158),
        (HostKeyCode.BrowserForward, 159),
        (HostKeyCode.Eject, 161),
        (HostKeyCode.MediaTrackNext, 163),
        (HostKeyCode.MediaPlayPause, 164),
        (HostKeyCode.MediaTrackPrevious, 165),
        (HostKeyCode.MediaStop, 166),
        (HostKeyCode.BrowserHome, 172),
        (HostKeyCode.BrowserRefresh, 173),
        (HostKeyCode.NumpadParenLeft, 179),
        (HostKeyCode.NumpadParenRight, 180),
        (HostKeyCode.F13, 183),
        (HostKeyCode.F14, 184),
        (HostKeyCode.F15, 185),
        (HostKeyCode.F16, 186),
        (HostKeyCode.F17, 187),
        (HostKeyCode.F18, 188),
        (HostKeyCode.F19, 189),
        (HostKeyCode.F20, 190),
        (HostKeyCode.F21, 191),
        (HostKeyCode.F22, 192),
        (HostKeyCode.F23, 193),
        (HostKeyCode.F24, 194),
        (HostKeyCode.BrowserSearch, 217),
    ];

    private static readonly uint[] ByCode = BuildForward();

    public static IReadOnlyList<(HostKeyCode Code, uint Evdev)> Entries => Table;

    public static bool TryToEvdev(HostKeyCode code, out uint evdev)
    {
        var index = (int)code;
        if (index > 0 && index < ByCode.Length && ByCode[index] != 0)
        {
            evdev = ByCode[index];
            return true;
        }

        evdev = 0;
        return false;
    }

    public static bool TryFromEvdev(uint evdev, out HostKeyCode code)
    {
        foreach (var (candidate, value) in Table)
        {
            if (value == evdev)
            {
                code = candidate;
                return true;
            }
        }

        code = HostKeyCode.None;
        return false;
    }

    private static uint[] BuildForward()
    {
        var highest = 0;
        foreach (var (code, _) in Table)
        {
            highest = Math.Max(highest, (int)code);
        }

        var map = new uint[highest + 1];
        foreach (var (code, evdev) in Table)
        {
            map[(int)code] = evdev;
        }

        return map;
    }
}
