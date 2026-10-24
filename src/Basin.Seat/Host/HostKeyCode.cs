namespace Basin.Seat;

/// <summary>
/// A physical key position, named as the W3C <c>KeyboardEvent.code</c> value names it.
/// The name is the key position, not the character that the active layout types there.
/// </summary>
public enum HostKeyCode
{
    /// <summary>
    /// No key. It has no evdev code.
    /// </summary>
    None = 0,

    /// <summary>
    /// The Escape key.
    /// </summary>
    Escape,

    /// <summary>
    /// The writing key that types 1 on a US layout. It is not the key on the numeric keypad.
    /// </summary>
    Digit1,

    /// <summary>
    /// The writing key that types 2 on a US layout. It is not the key on the numeric keypad.
    /// </summary>
    Digit2,

    /// <summary>
    /// The writing key that types 3 on a US layout. It is not the key on the numeric keypad.
    /// </summary>
    Digit3,

    /// <summary>
    /// The writing key that types 4 on a US layout. It is not the key on the numeric keypad.
    /// </summary>
    Digit4,

    /// <summary>
    /// The writing key that types 5 on a US layout. It is not the key on the numeric keypad.
    /// </summary>
    Digit5,

    /// <summary>
    /// The writing key that types 6 on a US layout. It is not the key on the numeric keypad.
    /// </summary>
    Digit6,

    /// <summary>
    /// The writing key that types 7 on a US layout. It is not the key on the numeric keypad.
    /// </summary>
    Digit7,

    /// <summary>
    /// The writing key that types 8 on a US layout. It is not the key on the numeric keypad.
    /// </summary>
    Digit8,

    /// <summary>
    /// The writing key that types 9 on a US layout. It is not the key on the numeric keypad.
    /// </summary>
    Digit9,

    /// <summary>
    /// The writing key that types 0 on a US layout. It is not the key on the numeric keypad.
    /// </summary>
    Digit0,

    /// <summary>
    /// The writing key right of 0 that types a minus sign on a US layout.
    /// </summary>
    Minus,

    /// <summary>
    /// The writing key left of Backspace that types an equals sign on a US layout.
    /// </summary>
    Equal,

    /// <summary>
    /// The Backspace key.
    /// </summary>
    Backspace,

    /// <summary>
    /// The Tab key.
    /// </summary>
    Tab,

    /// <summary>
    /// The writing key that types Q on a US layout.
    /// </summary>
    KeyQ,

    /// <summary>
    /// The writing key that types W on a US layout.
    /// </summary>
    KeyW,

    /// <summary>
    /// The writing key that types E on a US layout.
    /// </summary>
    KeyE,

    /// <summary>
    /// The writing key that types R on a US layout.
    /// </summary>
    KeyR,

    /// <summary>
    /// The writing key that types T on a US layout.
    /// </summary>
    KeyT,

    /// <summary>
    /// The writing key that types Y on a US layout.
    /// </summary>
    KeyY,

    /// <summary>
    /// The writing key that types U on a US layout.
    /// </summary>
    KeyU,

    /// <summary>
    /// The writing key that types I on a US layout.
    /// </summary>
    KeyI,

    /// <summary>
    /// The writing key that types O on a US layout.
    /// </summary>
    KeyO,

    /// <summary>
    /// The writing key that types P on a US layout.
    /// </summary>
    KeyP,

    /// <summary>
    /// The writing key right of P that types a left bracket on a US layout.
    /// </summary>
    BracketLeft,

    /// <summary>
    /// The second writing key right of P that types a right bracket on a US layout.
    /// </summary>
    BracketRight,

    /// <summary>
    /// The Enter key of the main block.
    /// </summary>
    Enter,

    /// <summary>
    /// The left Control key.
    /// </summary>
    ControlLeft,

    /// <summary>
    /// The writing key that types A on a US layout.
    /// </summary>
    KeyA,

    /// <summary>
    /// The writing key that types S on a US layout.
    /// </summary>
    KeyS,

    /// <summary>
    /// The writing key that types D on a US layout.
    /// </summary>
    KeyD,

    /// <summary>
    /// The writing key that types F on a US layout.
    /// </summary>
    KeyF,

    /// <summary>
    /// The writing key that types G on a US layout.
    /// </summary>
    KeyG,

    /// <summary>
    /// The writing key that types H on a US layout.
    /// </summary>
    KeyH,

    /// <summary>
    /// The writing key that types J on a US layout.
    /// </summary>
    KeyJ,

    /// <summary>
    /// The writing key that types K on a US layout.
    /// </summary>
    KeyK,

    /// <summary>
    /// The writing key that types L on a US layout.
    /// </summary>
    KeyL,

    /// <summary>
    /// The writing key right of L that types a semicolon on a US layout.
    /// </summary>
    Semicolon,

    /// <summary>
    /// The second writing key right of L that types an apostrophe on a US layout.
    /// </summary>
    Quote,

    /// <summary>
    /// The writing key left of 1 that types a grave accent on a US layout.
    /// </summary>
    Backquote,

    /// <summary>
    /// The left Shift key.
    /// </summary>
    ShiftLeft,

    /// <summary>
    /// The writing key that types a backslash on a US layout. An ISO keyboard has it left of Enter.
    /// </summary>
    Backslash,

    /// <summary>
    /// The writing key that types Z on a US layout.
    /// </summary>
    KeyZ,

    /// <summary>
    /// The writing key that types X on a US layout.
    /// </summary>
    KeyX,

    /// <summary>
    /// The writing key that types C on a US layout.
    /// </summary>
    KeyC,

    /// <summary>
    /// The writing key that types V on a US layout.
    /// </summary>
    KeyV,

    /// <summary>
    /// The writing key that types B on a US layout.
    /// </summary>
    KeyB,

    /// <summary>
    /// The writing key that types N on a US layout.
    /// </summary>
    KeyN,

    /// <summary>
    /// The writing key that types M on a US layout.
    /// </summary>
    KeyM,

    /// <summary>
    /// The writing key right of M that types a comma on a US layout.
    /// </summary>
    Comma,

    /// <summary>
    /// The second writing key right of M that types a period on a US layout.
    /// </summary>
    Period,

    /// <summary>
    /// The third writing key right of M that types a slash on a US layout.
    /// </summary>
    Slash,

    /// <summary>
    /// The right Shift key.
    /// </summary>
    ShiftRight,

    /// <summary>
    /// The multiply key of the numeric keypad.
    /// </summary>
    NumpadMultiply,

    /// <summary>
    /// The left Alt key, or the left Option key on a Mac keyboard.
    /// </summary>
    AltLeft,

    /// <summary>
    /// The space bar.
    /// </summary>
    Space,

    /// <summary>
    /// The Caps Lock key.
    /// </summary>
    CapsLock,

    /// <summary>
    /// The F1 function key.
    /// </summary>
    F1,

    /// <summary>
    /// The F2 function key.
    /// </summary>
    F2,

    /// <summary>
    /// The F3 function key.
    /// </summary>
    F3,

    /// <summary>
    /// The F4 function key.
    /// </summary>
    F4,

    /// <summary>
    /// The F5 function key.
    /// </summary>
    F5,

    /// <summary>
    /// The F6 function key.
    /// </summary>
    F6,

    /// <summary>
    /// The F7 function key.
    /// </summary>
    F7,

    /// <summary>
    /// The F8 function key.
    /// </summary>
    F8,

    /// <summary>
    /// The F9 function key.
    /// </summary>
    F9,

    /// <summary>
    /// The F10 function key.
    /// </summary>
    F10,

    /// <summary>
    /// The F11 function key.
    /// </summary>
    F11,

    /// <summary>
    /// The F12 function key.
    /// </summary>
    F12,

    /// <summary>
    /// The F13 function key.
    /// </summary>
    F13,

    /// <summary>
    /// The F14 function key.
    /// </summary>
    F14,

    /// <summary>
    /// The F15 function key.
    /// </summary>
    F15,

    /// <summary>
    /// The F16 function key.
    /// </summary>
    F16,

    /// <summary>
    /// The F17 function key.
    /// </summary>
    F17,

    /// <summary>
    /// The F18 function key.
    /// </summary>
    F18,

    /// <summary>
    /// The F19 function key.
    /// </summary>
    F19,

    /// <summary>
    /// The F20 function key.
    /// </summary>
    F20,

    /// <summary>
    /// The F21 function key.
    /// </summary>
    F21,

    /// <summary>
    /// The F22 function key.
    /// </summary>
    F22,

    /// <summary>
    /// The F23 function key.
    /// </summary>
    F23,

    /// <summary>
    /// The F24 function key.
    /// </summary>
    F24,

    /// <summary>
    /// The Num Lock key, or the Clear key on a Mac keyboard.
    /// </summary>
    NumLock,

    /// <summary>
    /// The Scroll Lock key.
    /// </summary>
    ScrollLock,

    /// <summary>
    /// The 0 key of the numeric keypad.
    /// </summary>
    Numpad0,

    /// <summary>
    /// The 1 key of the numeric keypad.
    /// </summary>
    Numpad1,

    /// <summary>
    /// The 2 key of the numeric keypad.
    /// </summary>
    Numpad2,

    /// <summary>
    /// The 3 key of the numeric keypad.
    /// </summary>
    Numpad3,

    /// <summary>
    /// The 4 key of the numeric keypad.
    /// </summary>
    Numpad4,

    /// <summary>
    /// The 5 key of the numeric keypad.
    /// </summary>
    Numpad5,

    /// <summary>
    /// The 6 key of the numeric keypad.
    /// </summary>
    Numpad6,

    /// <summary>
    /// The 7 key of the numeric keypad.
    /// </summary>
    Numpad7,

    /// <summary>
    /// The 8 key of the numeric keypad.
    /// </summary>
    Numpad8,

    /// <summary>
    /// The 9 key of the numeric keypad.
    /// </summary>
    Numpad9,

    /// <summary>
    /// The subtract key of the numeric keypad.
    /// </summary>
    NumpadSubtract,

    /// <summary>
    /// The add key of the numeric keypad.
    /// </summary>
    NumpadAdd,

    /// <summary>
    /// The decimal point key of the numeric keypad.
    /// </summary>
    NumpadDecimal,

    /// <summary>
    /// The divide key of the numeric keypad.
    /// </summary>
    NumpadDivide,

    /// <summary>
    /// The Enter key of the numeric keypad.
    /// </summary>
    NumpadEnter,

    /// <summary>
    /// The equals key of the numeric keypad.
    /// </summary>
    NumpadEqual,

    /// <summary>
    /// The comma key of the numeric keypad that some keyboards have.
    /// </summary>
    NumpadComma,

    /// <summary>
    /// The left parenthesis key of the numeric keypad that some keyboards have.
    /// </summary>
    NumpadParenLeft,

    /// <summary>
    /// The right parenthesis key of the numeric keypad that some keyboards have.
    /// </summary>
    NumpadParenRight,

    /// <summary>
    /// The extra writing key right of left Shift on an ISO keyboard.
    /// </summary>
    IntlBackslash,

    /// <summary>
    /// The extra writing key left of right Shift on a Japanese keyboard.
    /// </summary>
    IntlRo,

    /// <summary>
    /// The extra writing key left of Backspace on a Japanese keyboard.
    /// </summary>
    IntlYen,

    /// <summary>
    /// The right Control key.
    /// </summary>
    ControlRight,

    /// <summary>
    /// The right Alt key. Many layouts use it as AltGr.
    /// </summary>
    AltRight,

    /// <summary>
    /// The left Meta key, for example the Windows key or the Command key.
    /// </summary>
    MetaLeft,

    /// <summary>
    /// The right Meta key, for example the Windows key or the Command key.
    /// </summary>
    MetaRight,

    /// <summary>
    /// The context menu key.
    /// </summary>
    ContextMenu,

    /// <summary>
    /// The Print Screen key.
    /// </summary>
    PrintScreen,

    /// <summary>
    /// The Pause key.
    /// </summary>
    Pause,

    /// <summary>
    /// The Insert key.
    /// </summary>
    Insert,

    /// <summary>
    /// The forward Delete key.
    /// </summary>
    Delete,

    /// <summary>
    /// The Home key.
    /// </summary>
    Home,

    /// <summary>
    /// The End key.
    /// </summary>
    End,

    /// <summary>
    /// The Page Up key.
    /// </summary>
    PageUp,

    /// <summary>
    /// The Page Down key.
    /// </summary>
    PageDown,

    /// <summary>
    /// The up arrow key.
    /// </summary>
    ArrowUp,

    /// <summary>
    /// The down arrow key.
    /// </summary>
    ArrowDown,

    /// <summary>
    /// The left arrow key.
    /// </summary>
    ArrowLeft,

    /// <summary>
    /// The right arrow key.
    /// </summary>
    ArrowRight,

    /// <summary>
    /// The Hangul and English toggle key on a Korean keyboard.
    /// </summary>
    Lang1,

    /// <summary>
    /// The Hanja conversion key on a Korean keyboard.
    /// </summary>
    Lang2,

    /// <summary>
    /// The Katakana key on a Japanese keyboard.
    /// </summary>
    Lang3,

    /// <summary>
    /// The Hiragana key on a Japanese keyboard.
    /// </summary>
    Lang4,

    /// <summary>
    /// The Zenkaku and Hankaku toggle key on a Japanese keyboard.
    /// </summary>
    Lang5,

    /// <summary>
    /// The Henkan conversion key on a Japanese keyboard.
    /// </summary>
    Convert,

    /// <summary>
    /// The Muhenkan key on a Japanese keyboard.
    /// </summary>
    NonConvert,

    /// <summary>
    /// The Kana mode toggle key on a Japanese keyboard.
    /// </summary>
    KanaMode,

    /// <summary>
    /// The mute key.
    /// </summary>
    AudioVolumeMute,

    /// <summary>
    /// The volume down key.
    /// </summary>
    AudioVolumeDown,

    /// <summary>
    /// The volume up key.
    /// </summary>
    AudioVolumeUp,

    /// <summary>
    /// The next track key.
    /// </summary>
    MediaTrackNext,

    /// <summary>
    /// The previous track key.
    /// </summary>
    MediaTrackPrevious,

    /// <summary>
    /// The play and pause key.
    /// </summary>
    MediaPlayPause,

    /// <summary>
    /// The media stop key.
    /// </summary>
    MediaStop,

    /// <summary>
    /// The eject key.
    /// </summary>
    Eject,

    /// <summary>
    /// The power key.
    /// </summary>
    Power,

    /// <summary>
    /// The sleep key.
    /// </summary>
    Sleep,

    /// <summary>
    /// The wake key.
    /// </summary>
    WakeUp,

    /// <summary>
    /// The browser home key.
    /// </summary>
    BrowserHome,

    /// <summary>
    /// The browser back key.
    /// </summary>
    BrowserBack,

    /// <summary>
    /// The browser forward key.
    /// </summary>
    BrowserForward,

    /// <summary>
    /// The browser refresh key.
    /// </summary>
    BrowserRefresh,

    /// <summary>
    /// The browser stop key.
    /// </summary>
    BrowserStop,

    /// <summary>
    /// The browser search key.
    /// </summary>
    BrowserSearch,

    /// <summary>
    /// The browser favorites key.
    /// </summary>
    BrowserFavorites,

    /// <summary>
    /// The key that starts the mail application.
    /// </summary>
    LaunchMail,

    /// <summary>
    /// The Undo key that some keyboards have.
    /// </summary>
    Undo,

    /// <summary>
    /// The Cut key that some keyboards have.
    /// </summary>
    Cut,

    /// <summary>
    /// The Copy key that some keyboards have.
    /// </summary>
    Copy,

    /// <summary>
    /// The Paste key that some keyboards have.
    /// </summary>
    Paste,

    /// <summary>
    /// The Find key that some keyboards have.
    /// </summary>
    Find,

    /// <summary>
    /// The Again key that some keyboards have. It repeats the last action.
    /// </summary>
    Again,

    /// <summary>
    /// The Help key that some keyboards have.
    /// </summary>
    Help,
}
