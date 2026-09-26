using Basin.Ipc;

namespace BasinMcp;

internal sealed class McpMethodFilter
{
    public McpMethodFilter(bool readOnly, IReadOnlyList<McpGlob> allow, IReadOnlyList<McpGlob> deny)
    {
        ReadOnly = readOnly;
        Allow = allow;
        Deny = deny;
    }

    public static McpMethodFilter None { get; } = new(false, [], []);

    public bool ReadOnly { get; }

    public IReadOnlyList<McpGlob> Allow { get; }

    public IReadOnlyList<McpGlob> Deny { get; }

    public bool Allows(IpcMethodDetail method)
    {
        if (ReadOnly && !method.ReadOnly)
        {
            return false;
        }

        if (Allow.Count > 0 && !Allow.Any(glob => glob.Matches(method.Name)))
        {
            return false;
        }

        return !Deny.Any(glob => glob.Matches(method.Name));
    }
}
