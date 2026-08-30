using Basin;
using Basin.Capabilities;
using Wayland.Server;

namespace TinyComp;

internal sealed class OutputPolicy
{
    public bool AutoLayout { get; set; } = true;

    public bool AutoVrrActive { get; set; }

    public ImageDescription ColorDescription { get; set; } = ImageDescription.Srgb;

    public bool KmsColorRouted { get; set; }

    public Box UsableArea { get; set; }

    public ulong GroupId { get; set; }

    public WorkspaceSet<TinyComp.Workspace> Workspaces { get; } = new();
}
