using Basin;
using Basin.Capabilities;
using Basin.Host;
using Wayland.Server;

namespace TinyComp;

internal static class OutputViewPolicy
{
    extension(OutputView view)
    {
        public OutputPolicy Policy => (OutputPolicy)view.Tag!;

        public WorkspaceSet<TinyComp.Workspace> Workspaces => view.Policy.Workspaces;

        public TinyComp.Workspace? Active
        {
            get => view.Policy.Workspaces.Active;
            set => view.Policy.Workspaces.Active = value;
        }

        public Box UsableArea
        {
            get => view.Policy.UsableArea;
            set => view.Policy.UsableArea = value;
        }

        public ulong GroupId
        {
            get => view.Policy.GroupId;
            set => view.Policy.GroupId = value;
        }

        public bool AutoLayout
        {
            get => view.Policy.AutoLayout;
            set => view.Policy.AutoLayout = value;
        }

        public bool AutoVrrActive
        {
            get => view.Policy.AutoVrrActive;
            set => view.Policy.AutoVrrActive = value;
        }

        public bool KmsColorRouted
        {
            get => view.Policy.KmsColorRouted;
            set => view.Policy.KmsColorRouted = value;
        }

        public ImageDescription ColorDescription
        {
            get => view.Policy.ColorDescription;
            set => view.Policy.ColorDescription = value;
        }
    }
}
