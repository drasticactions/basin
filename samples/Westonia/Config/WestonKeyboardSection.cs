using System.Globalization;

namespace Westonia;

public sealed class WestonKeyboardSection
{
    public string? Rules { get; set; }

    public string? Model { get; set; }

    public string? Layout { get; set; }

    public string? Variant { get; set; }

    public string? Options { get; set; }

    public int RepeatRate { get; set; } = 40;

    public int RepeatDelay { get; set; } = 400;

    public bool NumlockOn { get; set; }

    public bool VtSwitching { get; set; } = true;
}
