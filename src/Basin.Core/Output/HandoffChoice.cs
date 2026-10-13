namespace Basin;

public readonly record struct HandoffChoice(HandoffMode? Flag, HandoffMode? Environment)
{
    public const string Variable = "BASIN_HANDOFF";

    public static HandoffChoice Take(HandoffMode? flag)
    {
        var value = System.Environment.GetEnvironmentVariable(Variable);
        System.Environment.SetEnvironmentVariable(Variable, null);
        return new HandoffChoice(flag, Parse(value));
    }

    public static HandoffMode? Parse(string? value) => value switch
    {
        "auto" => HandoffMode.Auto,
        "retain" => HandoffMode.Retain,
        "off" => HandoffMode.Off,
        _ => null,
    };

    public static string Name(HandoffMode mode) => mode switch
    {
        HandoffMode.Retain => "retain",
        HandoffMode.Off => "off",
        _ => "auto",
    };

    public HandoffMode Resolve(HandoffMode configured) =>
        Flag ?? (configured != HandoffMode.Auto ? configured : Environment ?? HandoffMode.Auto);
}
