using Basin.Cli;

namespace PlasmaHost;

internal sealed record PlasmaHostOptions
{
    public required BackendKind Backend { get; init; }

    public required string Renderer { get; init; }

    public int Outputs { get; init; } = 1;

    public double[] Scales { get; init; } = [];

    public long Frames { get; init; }

    public string? Screenshot { get; init; }

    public string? Shell { get; init; } = "plasmashell";

    public string? Config { get; init; }
}
