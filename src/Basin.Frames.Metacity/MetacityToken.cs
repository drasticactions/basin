namespace Basin.Frames.Metacity;

internal readonly record struct MetacityToken(
    MetacityTokenKind Kind,
    int IntValue,
    double DoubleValue,
    MetacityOperator Operator,
    MetacityVariable Variable,
    string? Name);
