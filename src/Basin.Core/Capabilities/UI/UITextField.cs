namespace Basin.Capabilities;

/// <summary>
/// The purpose and hints of a focused text field. The values match the content_purpose and content_hint enums of text-input-v3.
/// </summary>
/// <param name="Purpose">The content purpose, one of the Purpose constants.</param>
/// <param name="Hint">The content hints, a combination of the Hint constants.</param>
public readonly record struct UITextField(uint Purpose, uint Hint)
{
    /// <summary>Default text input.</summary>
    public const uint PurposeNormal = 0;
    /// <summary>Letters only.</summary>
    public const uint PurposeAlpha = 1;
    /// <summary>Digits only.</summary>
    public const uint PurposeDigits = 2;
    /// <summary>A number, which can include a sign and a decimal point.</summary>
    public const uint PurposeNumber = 3;
    /// <summary>A URL.</summary>
    public const uint PurposeUrl = 5;
    /// <summary>An email address.</summary>
    public const uint PurposeEmail = 6;
    /// <summary>A name of a person.</summary>
    public const uint PurposeName = 7;
    /// <summary>A password.</summary>
    public const uint PurposePassword = 8;
    /// <summary>A PIN that has only digits.</summary>
    public const uint PurposePin = 9;

    /// <summary>Capitalize the first letter of each sentence.</summary>
    public const uint HintAutoCapitalization = 0x4;
    /// <summary>Prefer lowercase letters.</summary>
    public const uint HintLowercase = 0x8;
    /// <summary>Prefer uppercase letters.</summary>
    public const uint HintUppercase = 0x10;
    /// <summary>The field hides the characters.</summary>
    public const uint HintHiddenText = 0x40;
    /// <summary>The text is sensitive and the input method must not learn it.</summary>
    public const uint HintSensitiveData = 0x80;
    /// <summary>The field accepts more than one line.</summary>
    public const uint HintMultiline = 0x200;
}
