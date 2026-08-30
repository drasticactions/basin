using Basin;

namespace TinyComp;

internal sealed class OutputSetting
{
    public double? Scale { get; init; }

    public double? Aspect { get; init; }

    public OutputTransform? Transform { get; init; }

    public (int Width, int Height, int? Refresh)? Mode { get; init; }
}
