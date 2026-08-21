using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Westonia;

public sealed class WestonOutputSection
{
    public string? Name { get; set; }

    public string? Mode { get; set; }

    public double Scale { get; set; } = 1.0;

    public string? Transform { get; set; }

    public string? IccProfile { get; set; }

    public string? VrrMode { get; set; }

    public int? MaxBpc { get; set; }

    public string? EotfMode { get; set; }

    public string? ColorimetryMode { get; set; }
}
