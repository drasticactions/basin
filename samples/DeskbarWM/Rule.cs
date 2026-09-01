using Basin.Config;

namespace DeskbarWm;

internal sealed class Rule : WindowRule
{
    public int? Workspace { get; init; }

    public bool AllWorkspaces { get; init; }
}
