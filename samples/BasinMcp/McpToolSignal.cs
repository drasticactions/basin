using ModelContextProtocol.Server;

namespace BasinMcp;

internal sealed class McpToolSignal : McpServerPrimitiveCollection<McpServerTool>
{
    public int Raised { get; private set; }

    public void Signal()
    {
        Raised++;
        RaiseChanged();
    }
}
