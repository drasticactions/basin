namespace Basin.Capabilities;

/// <summary>
/// The value of a device setting. It holds an integer, up to <see cref="MaxNumbers"/> numbers, or a string, as <see cref="InputSettingKind"/> describes.
/// </summary>
public readonly struct InputSettingValue : IEquatable<InputSettingValue>
{
    /// <summary>The largest count of numbers that a value can hold. A calibration matrix uses all of them.</summary>
    public const int MaxNumbers = 6;

    private readonly InputSettingNumbers _numbers;

    /// <summary>Initializes a new instance of the <see cref="InputSettingValue"/> struct with an integer value.</summary>
    /// <param name="value">The integer, flag or choice value.</param>
    public InputSettingValue(uint value)
    {
        Value = value;
    }

    private InputSettingValue(uint value, ReadOnlySpan<double> numbers, string? text)
    {
        Value = value;
        Count = Math.Min(numbers.Length, MaxNumbers);
        for (var i = 0; i < Count; i++)
        {
            _numbers[i] = numbers[i];
        }

        TextValue = text;
    }

    /// <summary>Gets the integer value of a flag, choice or integer setting. It is 0 for a number, matrix or text value.</summary>
    public uint Value { get; }

    /// <summary>Gets the count of numbers that the value holds.</summary>
    public int Count { get; }

    /// <summary>Gets the string of a text value. It is <see langword="null"/> for every other kind.</summary>
    public string? TextValue { get; }

    /// <summary>Gets one of the numbers that the value holds.</summary>
    /// <param name="index">The index, from 0 to <see cref="Count"/> minus 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative or not less than <see cref="Count"/>.</exception>
    public double this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            return _numbers[index];
        }
    }

    /// <summary>Gets a value indicating whether a flag is on, that is <see cref="Value"/> is not 0.</summary>
    public bool IsOn => Value != 0;

    /// <summary>Creates a flag value.</summary>
    /// <param name="on"><see langword="true"/> for on.</param>
    /// <returns>A value whose <see cref="Value"/> is 1 or 0.</returns>
    public static InputSettingValue Flag(bool on) => new(on ? 1u : 0u);

    /// <summary>Creates an integer value.</summary>
    /// <param name="value">The integer.</param>
    /// <returns>A value that holds <paramref name="value"/>.</returns>
    public static InputSettingValue Of(uint value) => new(value);

    /// <summary>Creates a click method choice.</summary>
    /// <param name="method">The click method.</param>
    /// <returns>A value that holds the method as an integer.</returns>
    public static InputSettingValue Of(InputClickMethod method) => new((uint)method);

    /// <summary>Creates a scroll method choice.</summary>
    /// <param name="method">The scroll method.</param>
    /// <returns>A value that holds the method as an integer.</returns>
    public static InputSettingValue Of(InputScrollMethod method) => new((uint)method);

    /// <summary>Creates an acceleration profile choice.</summary>
    /// <param name="profile">The acceleration profile.</param>
    /// <returns>A value that holds the profile as an integer.</returns>
    public static InputSettingValue Of(InputAccelProfile profile) => new((uint)profile);

    /// <summary>Creates a tap button map choice.</summary>
    /// <param name="map">The tap button map.</param>
    /// <returns>A value that holds the map as an integer.</returns>
    public static InputSettingValue Of(InputTapButtonMap map) => new((uint)map);

    /// <summary>Creates a clickfinger button map choice.</summary>
    /// <param name="map">The clickfinger button map.</param>
    /// <returns>A value that holds the map as an integer.</returns>
    public static InputSettingValue Of(InputClickfingerButtonMap map) => new((uint)map);

    /// <summary>Creates a drag lock choice.</summary>
    /// <param name="state">The drag lock state.</param>
    /// <returns>A value that holds the state as an integer.</returns>
    public static InputSettingValue Of(InputDragLock state) => new((uint)state);

    /// <summary>Creates a finger drag choice.</summary>
    /// <param name="state">The finger drag state.</param>
    /// <returns>A value that holds the state as an integer.</returns>
    public static InputSettingValue Of(InputThreeFingerDrag state) => new((uint)state);

    /// <summary>Creates a send events choice.</summary>
    /// <param name="mode">The send events mode.</param>
    /// <returns>A value that holds the mode as an integer.</returns>
    public static InputSettingValue Of(InputSendEventsMode mode) => new((uint)mode);

    /// <summary>Creates a value that holds one number.</summary>
    /// <param name="value">The number.</param>
    /// <returns>A value with a <see cref="Count"/> of 1.</returns>
    public static InputSettingValue Number(double value) => new(0, [value], null);

    /// <summary>Creates a calibration matrix value.</summary>
    /// <param name="six">The six numbers of the matrix, in row order.</param>
    /// <returns>A value with a <see cref="Count"/> of 6.</returns>
    /// <exception cref="ArgumentException"><paramref name="six"/> does not hold exactly six numbers.</exception>
    public static InputSettingValue Matrix(ReadOnlySpan<double> six)
    {
        if (six.Length != MaxNumbers)
        {
            throw new ArgumentException("a calibration matrix has six numbers", nameof(six));
        }

        return new InputSettingValue(0, six, null);
    }

    /// <summary>Creates a text value.</summary>
    /// <param name="text">The string.</param>
    /// <returns>A value whose <see cref="TextValue"/> is <paramref name="text"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static InputSettingValue Text(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new InputSettingValue(0, default, text);
    }

    /// <summary>Copies the numbers of the value into a span. It copies no more than the span can hold.</summary>
    /// <param name="into">The destination.</param>
    public void CopyNumbers(Span<double> into)
    {
        for (var i = 0; i < Count && i < into.Length; i++)
        {
            into[i] = _numbers[i];
        }
    }

    /// <summary>Tells whether two values are the same. The numbers must be exactly equal.</summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns><see langword="true"/> when the integer, the numbers and the text are all equal.</returns>
    public bool Equals(InputSettingValue other)
    {
        if (Value != other.Value || Count != other.Count || !string.Equals(TextValue, other.TextValue, StringComparison.Ordinal))
        {
            return false;
        }

        for (var i = 0; i < Count; i++)
        {
            if (_numbers[i] != other._numbers[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Tells whether two values are the same, with a tolerance for the numbers.</summary>
    /// <param name="other">The value to compare with.</param>
    /// <param name="tolerance">The largest difference that two numbers can have and still match.</param>
    /// <returns><see langword="true"/> when the integer and the text are equal and each number is within the tolerance.</returns>
    public bool Matches(in InputSettingValue other, double tolerance = 1e-6)
    {
        if (Value != other.Value || Count != other.Count || !string.Equals(TextValue, other.TextValue, StringComparison.Ordinal))
        {
            return false;
        }

        for (var i = 0; i < Count; i++)
        {
            if (Math.Abs(_numbers[i] - other._numbers[i]) > tolerance)
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is InputSettingValue other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Value);
        hash.Add(Count);
        hash.Add(TextValue, StringComparer.Ordinal);
        for (var i = 0; i < Count; i++)
        {
            hash.Add(_numbers[i]);
        }

        return hash.ToHashCode();
    }

    /// <summary>Gives the text, the first number or the integer, in that order of preference.</summary>
    /// <returns>A short form of the value in the invariant culture.</returns>
    public override string ToString() => TextValue ?? (Count > 0 ? _numbers[0].ToString(System.Globalization.CultureInfo.InvariantCulture) : Value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Tells whether two values are the same.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when <see cref="Equals(InputSettingValue)"/> is true.</returns>
    public static bool operator ==(InputSettingValue left, InputSettingValue right) => left.Equals(right);

    /// <summary>Tells whether two values are different.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when <see cref="Equals(InputSettingValue)"/> is false.</returns>
    public static bool operator !=(InputSettingValue left, InputSettingValue right) => !left.Equals(right);
}
