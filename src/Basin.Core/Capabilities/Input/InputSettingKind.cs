namespace Basin.Capabilities;

/// <summary>The shape of the value that an <see cref="InputSetting"/> takes.</summary>
public enum InputSettingKind
{
    /// <summary>On or off, in <see cref="InputSettingValue.Value"/> as 1 or 0.</summary>
    Flag,

    /// <summary>One value of an enum, in <see cref="InputSettingValue.Value"/>.</summary>
    Choice,

    /// <summary>An unsigned integer, in <see cref="InputSettingValue.Value"/>.</summary>
    Integer,

    /// <summary>One floating-point number, at index 0 of the value.</summary>
    Number,

    /// <summary>Six floating-point numbers.</summary>
    Matrix,

    /// <summary>A string, in <see cref="InputSettingValue.TextValue"/>.</summary>
    Text,
}
