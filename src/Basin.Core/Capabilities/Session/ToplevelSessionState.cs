using Wayland.Server;

namespace Basin.Capabilities;

public readonly record struct ToplevelSessionState
{
    public Box Geometry { get; init; }

    public string? OutputLayoutId { get; init; }

    public ToplevelSessionStates States { get; init; }

    public string? WorkspaceName { get; init; }

    public ReadOnlyMemory<byte> ConsumerData { get; init; }

    public bool CanRestorePosition(string currentLayoutId) =>
        OutputLayoutId is { } saved && saved == currentLayoutId;
}
