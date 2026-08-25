using System.Globalization;

namespace Westonia;

public sealed class WestonScreensaverSection
{
    public string? Path { get; set; }

    public int DurationSeconds { get; set; } = 60;
}
