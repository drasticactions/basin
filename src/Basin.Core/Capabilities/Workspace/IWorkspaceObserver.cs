using Basin.Diagnostics;
using Pixman;

namespace Basin.Capabilities;

public interface IWorkspaceObserver
{
    void OnWorkspacesChanged();

    void OnWorkspaceMembersChanged();
}
