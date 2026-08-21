using Avalonia.Input;
using Avalonia.Input.Raw;

namespace Basin.UI.Avalonia;

internal static class EvdevInput
{
    public const double AxisStep = 10.0;

    private const uint BtnLeft = 0x110;
    private const uint BtnRight = 0x111;
    private const uint BtnMiddle = 0x112;
    private const uint BtnSide = 0x113;
    private const uint BtnExtra = 0x114;

    private static readonly PhysicalKey[] PhysicalKeys = BuildPhysicalKeys();

    public static PhysicalKey PhysicalKeyOf(uint evdevCode) =>
        evdevCode < (uint)PhysicalKeys.Length ? PhysicalKeys[evdevCode] : PhysicalKey.None;

    public static RawPointerEventType? PointerEventType(uint button, bool pressed) => button switch
    {
        BtnLeft => pressed ? RawPointerEventType.LeftButtonDown : RawPointerEventType.LeftButtonUp,
        BtnRight => pressed ? RawPointerEventType.RightButtonDown : RawPointerEventType.RightButtonUp,
        BtnMiddle => pressed ? RawPointerEventType.MiddleButtonDown : RawPointerEventType.MiddleButtonUp,
        BtnSide => pressed ? RawPointerEventType.XButton1Down : RawPointerEventType.XButton1Up,
        BtnExtra => pressed ? RawPointerEventType.XButton2Down : RawPointerEventType.XButton2Up,
        _ => null,
    };

    public static RawInputModifiers WithButton(RawInputModifiers modifiers, uint button, bool pressed)
    {
        var flag = button switch
        {
            BtnLeft => RawInputModifiers.LeftMouseButton,
            BtnRight => RawInputModifiers.RightMouseButton,
            BtnMiddle => RawInputModifiers.MiddleMouseButton,
            BtnSide => RawInputModifiers.XButton1MouseButton,
            BtnExtra => RawInputModifiers.XButton2MouseButton,
            _ => RawInputModifiers.None,
        };

        return pressed ? modifiers | flag : modifiers & ~flag;
    }

    public static RawInputModifiers WithKey(RawInputModifiers modifiers, uint key, bool pressed)
    {
        var flag = key switch
        {
            42 or 54 => RawInputModifiers.Shift,
            29 or 97 => RawInputModifiers.Control,
            56 or 100 => RawInputModifiers.Alt,
            125 or 126 => RawInputModifiers.Meta,
            _ => RawInputModifiers.None,
        };

        return pressed ? modifiers | flag : modifiers & ~flag;
    }

    public static RawInputModifiers PointerModifiers(RawInputModifiers modifiers) => modifiers & (
        RawInputModifiers.LeftMouseButton |
        RawInputModifiers.RightMouseButton |
        RawInputModifiers.MiddleMouseButton |
        RawInputModifiers.XButton1MouseButton |
        RawInputModifiers.XButton2MouseButton);

    private static PhysicalKey[] BuildPhysicalKeys()
    {
        var keys = new PhysicalKey[256];
        void Set(int code, PhysicalKey key) => keys[code] = key;

        Set(1, PhysicalKey.Escape);
        Set(2, PhysicalKey.Digit1);
        Set(3, PhysicalKey.Digit2);
        Set(4, PhysicalKey.Digit3);
        Set(5, PhysicalKey.Digit4);
        Set(6, PhysicalKey.Digit5);
        Set(7, PhysicalKey.Digit6);
        Set(8, PhysicalKey.Digit7);
        Set(9, PhysicalKey.Digit8);
        Set(10, PhysicalKey.Digit9);
        Set(11, PhysicalKey.Digit0);
        Set(12, PhysicalKey.Minus);
        Set(13, PhysicalKey.Equal);
        Set(14, PhysicalKey.Backspace);
        Set(15, PhysicalKey.Tab);
        Set(16, PhysicalKey.Q);
        Set(17, PhysicalKey.W);
        Set(18, PhysicalKey.E);
        Set(19, PhysicalKey.R);
        Set(20, PhysicalKey.T);
        Set(21, PhysicalKey.Y);
        Set(22, PhysicalKey.U);
        Set(23, PhysicalKey.I);
        Set(24, PhysicalKey.O);
        Set(25, PhysicalKey.P);
        Set(26, PhysicalKey.BracketLeft);
        Set(27, PhysicalKey.BracketRight);
        Set(28, PhysicalKey.Enter);
        Set(29, PhysicalKey.ControlLeft);
        Set(30, PhysicalKey.A);
        Set(31, PhysicalKey.S);
        Set(32, PhysicalKey.D);
        Set(33, PhysicalKey.F);
        Set(34, PhysicalKey.G);
        Set(35, PhysicalKey.H);
        Set(36, PhysicalKey.J);
        Set(37, PhysicalKey.K);
        Set(38, PhysicalKey.L);
        Set(39, PhysicalKey.Semicolon);
        Set(40, PhysicalKey.Quote);
        Set(41, PhysicalKey.Backquote);
        Set(42, PhysicalKey.ShiftLeft);
        Set(43, PhysicalKey.Backslash);
        Set(44, PhysicalKey.Z);
        Set(45, PhysicalKey.X);
        Set(46, PhysicalKey.C);
        Set(47, PhysicalKey.V);
        Set(48, PhysicalKey.B);
        Set(49, PhysicalKey.N);
        Set(50, PhysicalKey.M);
        Set(51, PhysicalKey.Comma);
        Set(52, PhysicalKey.Period);
        Set(53, PhysicalKey.Slash);
        Set(54, PhysicalKey.ShiftRight);
        Set(55, PhysicalKey.NumPadMultiply);
        Set(56, PhysicalKey.AltLeft);
        Set(57, PhysicalKey.Space);
        Set(58, PhysicalKey.CapsLock);
        Set(59, PhysicalKey.F1);
        Set(60, PhysicalKey.F2);
        Set(61, PhysicalKey.F3);
        Set(62, PhysicalKey.F4);
        Set(63, PhysicalKey.F5);
        Set(64, PhysicalKey.F6);
        Set(65, PhysicalKey.F7);
        Set(66, PhysicalKey.F8);
        Set(67, PhysicalKey.F9);
        Set(68, PhysicalKey.F10);
        Set(69, PhysicalKey.NumLock);
        Set(70, PhysicalKey.ScrollLock);
        Set(71, PhysicalKey.NumPad7);
        Set(72, PhysicalKey.NumPad8);
        Set(73, PhysicalKey.NumPad9);
        Set(74, PhysicalKey.NumPadSubtract);
        Set(75, PhysicalKey.NumPad4);
        Set(76, PhysicalKey.NumPad5);
        Set(77, PhysicalKey.NumPad6);
        Set(78, PhysicalKey.NumPadAdd);
        Set(79, PhysicalKey.NumPad1);
        Set(80, PhysicalKey.NumPad2);
        Set(81, PhysicalKey.NumPad3);
        Set(82, PhysicalKey.NumPad0);
        Set(83, PhysicalKey.NumPadDecimal);
        Set(85, PhysicalKey.Lang5);
        Set(86, PhysicalKey.IntlBackslash);
        Set(87, PhysicalKey.F11);
        Set(88, PhysicalKey.F12);
        Set(89, PhysicalKey.IntlRo);
        Set(90, PhysicalKey.Lang3);
        Set(91, PhysicalKey.Lang4);
        Set(92, PhysicalKey.Convert);
        Set(93, PhysicalKey.KanaMode);
        Set(94, PhysicalKey.NonConvert);
        Set(96, PhysicalKey.NumPadEnter);
        Set(97, PhysicalKey.ControlRight);
        Set(98, PhysicalKey.NumPadDivide);
        Set(99, PhysicalKey.PrintScreen);
        Set(100, PhysicalKey.AltRight);
        Set(102, PhysicalKey.Home);
        Set(103, PhysicalKey.ArrowUp);
        Set(104, PhysicalKey.PageUp);
        Set(105, PhysicalKey.ArrowLeft);
        Set(106, PhysicalKey.ArrowRight);
        Set(107, PhysicalKey.End);
        Set(108, PhysicalKey.ArrowDown);
        Set(109, PhysicalKey.PageDown);
        Set(110, PhysicalKey.Insert);
        Set(111, PhysicalKey.Delete);
        Set(113, PhysicalKey.AudioVolumeMute);
        Set(114, PhysicalKey.AudioVolumeDown);
        Set(115, PhysicalKey.AudioVolumeUp);
        Set(116, PhysicalKey.Power);
        Set(117, PhysicalKey.NumPadEqual);
        Set(119, PhysicalKey.Pause);
        Set(121, PhysicalKey.NumPadComma);
        Set(122, PhysicalKey.Lang1);
        Set(123, PhysicalKey.Lang2);
        Set(124, PhysicalKey.IntlYen);
        Set(125, PhysicalKey.MetaLeft);
        Set(126, PhysicalKey.MetaRight);
        Set(127, PhysicalKey.ContextMenu);
        Set(128, PhysicalKey.BrowserStop);
        Set(129, PhysicalKey.Again);
        Set(131, PhysicalKey.Undo);
        Set(132, PhysicalKey.Select);
        Set(133, PhysicalKey.Copy);
        Set(134, PhysicalKey.Open);
        Set(135, PhysicalKey.Paste);
        Set(136, PhysicalKey.Find);
        Set(137, PhysicalKey.Cut);
        Set(138, PhysicalKey.Help);
        Set(140, PhysicalKey.LaunchApp2);
        Set(142, PhysicalKey.Sleep);
        Set(143, PhysicalKey.WakeUp);
        Set(144, PhysicalKey.LaunchApp1);
        Set(155, PhysicalKey.LaunchMail);
        Set(156, PhysicalKey.BrowserFavorites);
        Set(158, PhysicalKey.BrowserBack);
        Set(159, PhysicalKey.BrowserForward);
        Set(161, PhysicalKey.Eject);
        Set(163, PhysicalKey.MediaTrackNext);
        Set(164, PhysicalKey.MediaPlayPause);
        Set(165, PhysicalKey.MediaTrackPrevious);
        Set(166, PhysicalKey.MediaStop);
        Set(171, PhysicalKey.MediaSelect);
        Set(172, PhysicalKey.BrowserHome);
        Set(173, PhysicalKey.BrowserRefresh);
        Set(179, PhysicalKey.NumPadParenLeft);
        Set(180, PhysicalKey.NumPadParenRight);
        Set(183, PhysicalKey.F13);
        Set(184, PhysicalKey.F14);
        Set(185, PhysicalKey.F15);
        Set(186, PhysicalKey.F16);
        Set(187, PhysicalKey.F17);
        Set(188, PhysicalKey.F18);
        Set(189, PhysicalKey.F19);
        Set(190, PhysicalKey.F20);
        Set(191, PhysicalKey.F21);
        Set(192, PhysicalKey.F22);
        Set(193, PhysicalKey.F23);
        Set(194, PhysicalKey.F24);
        Set(217, PhysicalKey.BrowserSearch);
        return keys;
    }
}
