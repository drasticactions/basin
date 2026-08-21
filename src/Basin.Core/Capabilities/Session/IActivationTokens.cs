using Wayland.Server;

namespace Basin.Capabilities;

public interface IActivationTokens
{
    string Mint(WlClient client, Surface? requestingSurface, uint serial, string? appId);

    bool Redeem(string token, Surface target);

    void ExpireAll();
}
