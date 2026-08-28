using Basin;
using Basin.Cli;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Host;
using Basin.Renderers;
using Basin.Scene;
using Basin.Shell.Xdg;
using Wayland.Server;

namespace Dam;

internal sealed record DamOptions
{
    public required BackendKind Backend { get; init; }

    public required string Renderer { get; init; }

    public bool ServerDecorations { get; init; }

    public bool LastOutputOnly { get; init; }

    public bool AllowVtSwitch { get; init; }

    public bool EnableXWayland { get; init; }

    public long Frames { get; init; }

    public string[] Application { get; init; } = [];
}
