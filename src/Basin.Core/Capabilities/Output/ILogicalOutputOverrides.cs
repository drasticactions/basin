using Wayland.Server;

namespace Basin.Capabilities;

public interface ILogicalOutputOverrides
{
    void SetClientOverride(WlClient client, OutputGlobal output, Box logical);

    void ClearClientOverride(WlClient client, OutputGlobal output);
}
