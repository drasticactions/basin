namespace Basin.Capabilities;

public readonly record struct InputSettingValue(uint Value, IReadOnlyList<double> Numbers)
{
    public InputSettingValue(uint value)
        : this(value, [])
    {
    }
}
