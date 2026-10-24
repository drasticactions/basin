using System.Globalization;

namespace Basin.Capabilities;

/// <summary>
/// Converts device settings, device types and their values to and from the words that configuration files and IPC use.
/// Parsing ignores case and accepts <c>-</c> for <c>_</c>.
/// </summary>
public static class InputSettingNames
{
    private static readonly InputSetting[] AllSettings = Enum.GetValues<InputSetting>();

    private static readonly InputDeviceType[] AllTypes = Enum.GetValues<InputDeviceType>();

    private static readonly string[] FlagWords = ["true", "false"];

    private static readonly string[] ButtonMapWords = ["lrm", "lmr"];

    private static readonly string[] DragLockWords = ["disabled", "enabled", "enabled_sticky"];

    private static readonly string[] ThreeFingerDragWords = ["disabled", "enabled_3fg", "enabled_4fg"];

    private static readonly string[] ClickMethodWords = ["none", "button_areas", "clickfinger"];

    private static readonly string[] ScrollMethodWords = ["none", "two_finger", "edge", "on_button_down"];

    private static readonly string[] AccelProfileWords = ["none", "flat", "adaptive", "custom"];

    private static readonly string[] SendEventsWords = ["enabled", "disabled", "disabled_on_external_mouse"];

    /// <summary>Gets every <see cref="InputSetting"/> value in declaration order.</summary>
    public static ReadOnlySpan<InputSetting> Settings => AllSettings;

    /// <summary>Gets every <see cref="InputDeviceType"/> value in declaration order.</summary>
    public static ReadOnlySpan<InputDeviceType> Types => AllTypes;

    /// <summary>Gets the configuration key of a setting, for example <c>natural_scroll</c>.</summary>
    /// <param name="setting">The setting.</param>
    /// <returns>The key in lower case with underscores.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="setting"/> is not a defined value.</exception>
    public static string Key(InputSetting setting) => setting switch
    {
        InputSetting.SendEvents => "send_events",
        InputSetting.Tap => "tap",
        InputSetting.TapButtonMap => "tap_button_map",
        InputSetting.Drag => "drag",
        InputSetting.DragLock => "drag_lock",
        InputSetting.ThreeFingerDrag => "three_finger_drag",
        InputSetting.CalibrationMatrix => "calibration_matrix",
        InputSetting.AccelProfile => "accel_profile",
        InputSetting.AccelSpeed => "accel_speed",
        InputSetting.NaturalScroll => "natural_scroll",
        InputSetting.LeftHanded => "left_handed",
        InputSetting.ClickMethod => "click_method",
        InputSetting.ClickfingerButtonMap => "clickfinger_button_map",
        InputSetting.MiddleEmulation => "middle_emulation",
        InputSetting.ScrollMethod => "scroll_method",
        InputSetting.ScrollButton => "scroll_button",
        InputSetting.ScrollButtonLock => "scroll_button_lock",
        InputSetting.DisableWhileTyping => "dwt",
        InputSetting.DisableWhileTrackpointing => "dwtp",
        InputSetting.Rotation => "rotation",
        InputSetting.ScrollFactor => "scroll_factor",
        InputSetting.MapToOutput => "map_to_output",
        InputSetting.KeymapRules => "rules",
        InputSetting.KeymapModel => "model",
        InputSetting.KeymapLayout => "layout",
        InputSetting.KeymapVariant => "variant",
        InputSetting.KeymapOptions => "options",
        InputSetting.RepeatRate => "repeat_rate",
        InputSetting.RepeatDelay => "repeat_delay",
        _ => throw new ArgumentOutOfRangeException(nameof(setting), setting, null),
    };

    /// <summary>Finds the setting whose configuration key is the given text.</summary>
    /// <param name="text">The key to parse.</param>
    /// <param name="setting">The setting, or the default value when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the text names a setting.</returns>
    public static bool TryParseKey(ReadOnlySpan<char> text, out InputSetting setting)
    {
        foreach (var candidate in AllSettings)
        {
            if (WordEquals(text, Key(candidate)))
            {
                setting = candidate;
                return true;
            }
        }

        setting = default;
        return false;
    }

    /// <summary>Gets the configuration word for a device type, for example <c>tablet_tool</c>.</summary>
    /// <param name="type">The device type.</param>
    /// <returns>The word in lower case with underscores.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is not a defined value.</exception>
    public static string TypeName(InputDeviceType type) => type switch
    {
        InputDeviceType.Keyboard => "keyboard",
        InputDeviceType.Pointer => "pointer",
        InputDeviceType.Touchpad => "touchpad",
        InputDeviceType.Touch => "touch",
        InputDeviceType.TabletTool => "tablet_tool",
        InputDeviceType.TabletPad => "tablet_pad",
        InputDeviceType.Switch => "switch",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    /// <summary>Finds the device type whose configuration word is the given text.</summary>
    /// <param name="text">The word to parse.</param>
    /// <param name="type">The type, or the default value when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the text names a device type.</returns>
    public static bool TryParseType(ReadOnlySpan<char> text, out InputDeviceType type)
    {
        foreach (var candidate in AllTypes)
        {
            if (WordEquals(text, TypeName(candidate)))
            {
                type = candidate;
                return true;
            }
        }

        type = default;
        return false;
    }

    /// <summary>Gets the word for a setting layer. An undefined value gives <c>default</c>.</summary>
    /// <param name="layer">The layer.</param>
    /// <returns>One of <c>default</c>, <c>config</c> or <c>runtime</c>.</returns>
    public static string LayerName(InputSettingLayer layer) => layer switch
    {
        InputSettingLayer.Config => "config",
        InputSettingLayer.Runtime => "runtime",
        _ => "default",
    };

    /// <summary>Gets the shape of the value that a setting takes.</summary>
    /// <param name="setting">The setting.</param>
    /// <returns>The kind. A setting that has no other kind gives <see cref="InputSettingKind.Text"/>.</returns>
    public static InputSettingKind KindOf(InputSetting setting) => setting switch
    {
        InputSetting.Tap or InputSetting.Drag or InputSetting.NaturalScroll or InputSetting.LeftHanded
            or InputSetting.MiddleEmulation or InputSetting.ScrollButtonLock or InputSetting.DisableWhileTyping
            or InputSetting.DisableWhileTrackpointing => InputSettingKind.Flag,
        InputSetting.TapButtonMap or InputSetting.ClickfingerButtonMap or InputSetting.DragLock
            or InputSetting.ThreeFingerDrag or InputSetting.ClickMethod or InputSetting.ScrollMethod
            or InputSetting.AccelProfile or InputSetting.SendEvents => InputSettingKind.Choice,
        InputSetting.ScrollButton or InputSetting.Rotation or InputSetting.RepeatRate
            or InputSetting.RepeatDelay => InputSettingKind.Integer,
        InputSetting.AccelSpeed or InputSetting.ScrollFactor => InputSettingKind.Number,
        InputSetting.CalibrationMatrix => InputSettingKind.Matrix,
        _ => InputSettingKind.Text,
    };

    /// <summary>Tells whether a setting configures the keymap or key repeat.</summary>
    /// <param name="setting">The setting.</param>
    /// <returns><see langword="true"/> for the keymap names, the repeat rate and the repeat delay.</returns>
    public static bool IsKeyboard(InputSetting setting) => setting is InputSetting.KeymapRules or InputSetting.KeymapModel
        or InputSetting.KeymapLayout or InputSetting.KeymapVariant or InputSetting.KeymapOptions
        or InputSetting.RepeatRate or InputSetting.RepeatDelay;

    /// <summary>Tells whether the compositor applies a setting itself, not libinput or the keymap.</summary>
    /// <param name="setting">The setting.</param>
    /// <returns><see langword="true"/> for <see cref="InputSetting.ScrollFactor"/> and <see cref="InputSetting.MapToOutput"/>.</returns>
    public static bool IsCompositorSide(InputSetting setting) =>
        setting is InputSetting.ScrollFactor or InputSetting.MapToOutput;

    /// <summary>Tells whether libinput applies a setting.</summary>
    /// <param name="setting">The setting.</param>
    /// <returns><see langword="true"/> when the setting is neither a keyboard setting nor a compositor-side setting.</returns>
    public static bool IsLibinput(InputSetting setting) => !IsKeyboard(setting) && !IsCompositorSide(setting);

    /// <summary>Gets the canonical words that a choice or flag setting accepts, for example for completion.</summary>
    /// <param name="setting">The setting.</param>
    /// <returns>The words. A setting that takes a number or text gives an empty span.</returns>
    public static ReadOnlySpan<string> Words(InputSetting setting) => setting switch
    {
        InputSetting.TapButtonMap or InputSetting.ClickfingerButtonMap => ButtonMapWords,
        InputSetting.DragLock => DragLockWords,
        InputSetting.ThreeFingerDrag => ThreeFingerDragWords,
        InputSetting.ClickMethod => ClickMethodWords,
        InputSetting.ScrollMethod => ScrollMethodWords,
        InputSetting.AccelProfile => AccelProfileWords,
        InputSetting.SendEvents => SendEventsWords,
        _ when KindOf(setting) == InputSettingKind.Flag => FlagWords,
        _ => [],
    };

    /// <summary>Tells whether a word parses for a setting and the device supports the value that it names.</summary>
    /// <param name="setting">The setting.</param>
    /// <param name="word">The word to test.</param>
    /// <param name="support">What the device supports for the setting.</param>
    /// <returns><see langword="true"/> when the device can take the value.</returns>
    public static bool IsSupportedWord(InputSetting setting, string word, in InputSettingSupport support)
    {
        if (!support.Supported || !TryParse(setting, word, out var value))
        {
            return false;
        }

        return setting switch
        {
            InputSetting.ClickMethod or InputSetting.ScrollMethod or InputSetting.AccelProfile or InputSetting.SendEvents =>
                value.Value == 0 || (support.Mask & value.Value) != 0,
            InputSetting.ThreeFingerDrag => value.Value == 0 || (value.Value == 1 ? support.Count >= 3 : support.Count >= 4),
            _ => true,
        };
    }

    /// <summary>
    /// Parses the text form of a value for a setting. It rejects a value out of range, for example an acceleration speed outside -1 to 1.
    /// </summary>
    /// <param name="setting">The setting that gives the kind of value.</param>
    /// <param name="text">The text to parse. Leading and trailing white space is ignored.</param>
    /// <param name="value">The parsed value, or the default value when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the text is a valid value for the setting.</returns>
    public static bool TryParse(InputSetting setting, ReadOnlySpan<char> text, out InputSettingValue value)
    {
        value = default;
        text = text.Trim();
        switch (KindOf(setting))
        {
            case InputSettingKind.Flag:
                if (WordEquals(text, "true") || WordEquals(text, "enabled"))
                {
                    value = InputSettingValue.Flag(true);
                    return true;
                }

                if (WordEquals(text, "false") || WordEquals(text, "disabled"))
                {
                    value = InputSettingValue.Flag(false);
                    return true;
                }

                return false;

            case InputSettingKind.Choice:
                return TryParseChoice(setting, text, out value);

            case InputSettingKind.Integer:
                if (!uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var integer))
                {
                    return false;
                }

                if (setting == InputSetting.Rotation && integer >= 360)
                {
                    return false;
                }

                value = InputSettingValue.Of(integer);
                return true;

            case InputSettingKind.Number:
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
                {
                    return false;
                }

                if ((setting == InputSetting.AccelSpeed && number is < -1 or > 1) ||
                    (setting == InputSetting.ScrollFactor && number <= 0))
                {
                    return false;
                }

                value = InputSettingValue.Number(number);
                return true;

            case InputSettingKind.Matrix:
                Span<double> numbers = stackalloc double[InputSettingValue.MaxNumbers];
                var count = 0;
                foreach (var range in text.SplitAny(" ,\t"))
                {
                    var part = text[range];
                    if (part.IsEmpty)
                    {
                        continue;
                    }

                    if (count == numbers.Length ||
                        !double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[count]) ||
                        !double.IsFinite(numbers[count]))
                    {
                        return false;
                    }

                    count++;
                }

                if (count != numbers.Length)
                {
                    return false;
                }

                value = InputSettingValue.Matrix(numbers);
                return true;

            default:
                value = InputSettingValue.Text(text.ToString());
                return true;
        }
    }

    /// <summary>Formats a value as the text that <see cref="TryParse"/> accepts for the setting.</summary>
    /// <param name="setting">The setting that gives the kind of value.</param>
    /// <param name="value">The value to format.</param>
    /// <returns>The text in the invariant culture. A choice with no word gives its number.</returns>
    public static string Format(InputSetting setting, in InputSettingValue value)
    {
        switch (KindOf(setting))
        {
            case InputSettingKind.Flag:
                return value.IsOn ? "true" : "false";
            case InputSettingKind.Choice:
                return ChoiceWord(setting, value.Value) ?? value.Value.ToString(CultureInfo.InvariantCulture);
            case InputSettingKind.Integer:
                return value.Value.ToString(CultureInfo.InvariantCulture);
            case InputSettingKind.Number:
                return value.Count > 0 ? value[0].ToString("R", CultureInfo.InvariantCulture) : "0";
            case InputSettingKind.Matrix:
                var builder = new System.Text.StringBuilder();
                for (var i = 0; i < value.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(' ');
                    }

                    builder.Append(value[i].ToString("R", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            default:
                return value.TextValue ?? string.Empty;
        }
    }

    /// <summary>Compares text with a canonical word. The text can use upper case and <c>-</c> for <c>_</c>.</summary>
    /// <param name="text">The text to compare.</param>
    /// <param name="canonical">The canonical word in lower case with underscores.</param>
    /// <returns><see langword="true"/> when the text names the word.</returns>
    public static bool WordEquals(ReadOnlySpan<char> text, string canonical)
    {
        if (text.Length != canonical.Length)
        {
            return false;
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i] == '-' ? '_' : char.ToLowerInvariant(text[i]);
            if (c != canonical[i])
            {
                return false;
            }
        }

        return true;
    }

    private static string? ChoiceWord(InputSetting setting, uint raw)
    {
        var words = Words(setting);
        for (var i = 0; i < words.Length; i++)
        {
            if (TryParseChoice(setting, words[i], out var candidate) && candidate.Value == raw)
            {
                return words[i];
            }
        }

        return null;
    }

    private static bool TryParseChoice(InputSetting setting, ReadOnlySpan<char> text, out InputSettingValue value)
    {
        uint? raw = setting switch
        {
            InputSetting.TapButtonMap or InputSetting.ClickfingerButtonMap =>
                WordEquals(text, "lrm") ? 0u : WordEquals(text, "lmr") ? 1u : null,
            InputSetting.DragLock =>
                WordEquals(text, "disabled") || WordEquals(text, "false") ? 0u
                : WordEquals(text, "enabled") || WordEquals(text, "true") ? 1u
                : WordEquals(text, "enabled_sticky") ? 2u : null,
            InputSetting.ThreeFingerDrag =>
                WordEquals(text, "disabled") || WordEquals(text, "false") ? 0u
                : WordEquals(text, "enabled_3fg") ? 1u
                : WordEquals(text, "enabled_4fg") ? 2u : null,
            InputSetting.ClickMethod =>
                WordEquals(text, "none") ? 0u
                : WordEquals(text, "button_areas") ? 1u
                : WordEquals(text, "clickfinger") ? 2u : null,
            InputSetting.ScrollMethod =>
                WordEquals(text, "none") ? 0u
                : WordEquals(text, "two_finger") ? 1u
                : WordEquals(text, "edge") ? 2u
                : WordEquals(text, "on_button_down") ? 4u : null,
            InputSetting.AccelProfile =>
                WordEquals(text, "none") ? 0u
                : WordEquals(text, "flat") ? 1u
                : WordEquals(text, "adaptive") ? 2u
                : WordEquals(text, "custom") ? 4u : null,
            InputSetting.SendEvents =>
                WordEquals(text, "enabled") || WordEquals(text, "true") ? 0u
                : WordEquals(text, "disabled") || WordEquals(text, "false") ? 1u
                : WordEquals(text, "disabled_on_external_mouse") ? 2u : null,
            _ => null,
        };
        value = raw is { } v ? InputSettingValue.Of(v) : default;
        return raw is not null;
    }
}
