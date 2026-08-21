namespace Basin.Capabilities;

public readonly record struct KeymapNames(
    string? Rules = null,
    string? Model = null,
    string? Layout = null,
    string? Variant = null,
    string? Options = null);
