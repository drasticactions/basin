using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Westonia;

public sealed class WestonAutolaunchSection
{
    public string? Path { get; set; }

    public bool Watch { get; set; }
}
