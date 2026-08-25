namespace Basin.Capabilities;

public readonly record struct ContrastParameters(
    double Contrast,
    double Intensity,
    double Saturation,
    bool Frost = false,
    uint FrostColor = 0);
