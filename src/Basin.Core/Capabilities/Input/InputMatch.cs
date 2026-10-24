namespace Basin.Capabilities;

/// <summary>
/// The conditions that select the devices for a configuration rule. A device must meet each condition that is set.
/// </summary>
/// <param name="Type">The device type, or <see langword="null"/> for any type.</param>
/// <param name="NameGlob">A glob for the device name with <c>*</c> and <c>?</c>, or <see langword="null"/> for any name.</param>
/// <param name="Sysname">The exact kernel device name, or <see langword="null"/> for any.</param>
/// <param name="Vendor">The vendor identifier, or <see langword="null"/> for any.</param>
/// <param name="Product">The product identifier, or <see langword="null"/> for any.</param>
/// <param name="Output">The exact name of the mapped output, or <see langword="null"/> for any.</param>
/// <param name="AnyKeyboard"><see langword="true"/> to require the keyboard capability, whatever the device type.</param>
public readonly record struct InputMatch(
    InputDeviceType? Type = null,
    string? NameGlob = null,
    string? Sysname = null,
    uint? Vendor = null,
    uint? Product = null,
    string? Output = null,
    bool AnyKeyboard = false)
{
    /// <summary>Gets a value indicating whether no condition is set, so that the match selects every device.</summary>
    public bool IsCatchAll => Type is null && NameGlob is null && Sysname is null && Vendor is null && Product is null &&
        Output is null && !AnyKeyboard;

    /// <summary>Gets a value indicating whether a condition identifies particular devices. A type or <see cref="AnyKeyboard"/> alone does not.</summary>
    public bool NamesDevice => NameGlob is not null || Sysname is not null || Vendor is not null || Product is not null || Output is not null;

    /// <summary>
    /// Gets a score that orders rules from general to specific. Vendor and product weigh most, then sysname, name, type and output.
    /// </summary>
    public int Specificity =>
        (Vendor is not null ? 32 : 0) +
        (Product is not null ? 32 : 0) +
        (Sysname is not null ? 16 : 0) +
        (NameGlob is not null ? 8 : 0) +
        (Type is not null || AnyKeyboard ? 4 : 0) +
        (Output is not null ? 2 : 0);

    /// <summary>Tells whether a device meets every condition that is set. Name and output comparisons are case-sensitive.</summary>
    /// <param name="device">The device to test.</param>
    /// <returns><see langword="true"/> when the device matches.</returns>
    public bool Matches(in InputDeviceInfo device)
    {
        if (Type is { } type && device.Type != type)
        {
            return false;
        }

        if (AnyKeyboard && (device.Capabilities & InputDeviceCapability.Keyboard) == 0)
        {
            return false;
        }

        if (Vendor is { } vendor && device.Vendor != vendor)
        {
            return false;
        }

        if (Product is { } product && device.Product != product)
        {
            return false;
        }

        if (Sysname is { } sysname && !string.Equals(sysname, device.Sysname, StringComparison.Ordinal))
        {
            return false;
        }

        if (Output is { } output && !string.Equals(output, device.OutputName, StringComparison.Ordinal))
        {
            return false;
        }

        return NameGlob is not { } glob || Glob(glob, device.Name);
    }

    /// <summary>Tells whether the match applies to keyboards in general, for example to set the seat keymap.</summary>
    /// <returns><see langword="true"/> when the match names no device and does not exclude keyboards.</returns>
    public bool MatchesKeyboardFallback() => !NamesDevice && (Type is null or InputDeviceType.Keyboard || AnyKeyboard);

    /// <summary>Matches text against a glob, where <c>*</c> matches any run of characters and <c>?</c> matches one character.</summary>
    /// <param name="pattern">The glob. The comparison is case-sensitive.</param>
    /// <param name="text">The text to test.</param>
    /// <returns><see langword="true"/> when the pattern matches all of the text.</returns>
    public static bool Glob(ReadOnlySpan<char> pattern, ReadOnlySpan<char> text)
    {
        var p = 0;
        var t = 0;
        var star = -1;
        var mark = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || pattern[p] == text[t]))
            {
                p++;
                t++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                mark = t;
            }
            else if (star >= 0)
            {
                p = star + 1;
                t = ++mark;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }
}
