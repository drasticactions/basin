namespace Basin.Capabilities;

public readonly record struct UITextField(uint Purpose, uint Hint)
{
    public const uint PurposeNormal = 0;
    public const uint PurposeAlpha = 1;
    public const uint PurposeDigits = 2;
    public const uint PurposeNumber = 3;
    public const uint PurposeUrl = 5;
    public const uint PurposeEmail = 6;
    public const uint PurposeName = 7;
    public const uint PurposePassword = 8;
    public const uint PurposePin = 9;

    public const uint HintAutoCapitalization = 0x4;
    public const uint HintLowercase = 0x8;
    public const uint HintUppercase = 0x10;
    public const uint HintHiddenText = 0x40;
    public const uint HintSensitiveData = 0x80;
    public const uint HintMultiline = 0x200;
}
