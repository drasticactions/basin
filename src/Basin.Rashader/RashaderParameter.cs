namespace Basin.Rashader;

public readonly record struct RashaderParameter(
    string Name,
    string Description,
    float Initial,
    float Minimum,
    float Maximum,
    float Step);
