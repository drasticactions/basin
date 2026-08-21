using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Westonia;

public sealed class WestonCoreSection
{
    public bool XWayland { get; set; }

    public string? Shell { get; set; }

    public string? GbmFormat { get; set; }

    public bool RequireInput { get; set; } = true;

    public int IdleTimeSeconds { get; set; } = 300;

    public int RepaintWindowMillis { get; set; } = 7;

    public string? Renderer { get; set; }
}
