using Wayland.Server;

namespace Basin.Capabilities.Defaults;

public sealed class DefaultActivationTokens : IActivationTokens
{
    private readonly Dictionary<string, Token> _tokens = [];
    private int _counter;

    private readonly record struct Token(WlClient Client, Surface? Surface, uint Serial, string? AppId);

    public int Outstanding => _tokens.Count;

    public string? LastRedeemedAppId { get; private set; }

    public string Mint(WlClient client, Surface? requestingSurface, uint serial, string? appId)
    {
        ArgumentNullException.ThrowIfNull(client);
        var token = $"basin-{++_counter}";
        _tokens[token] = new Token(client, requestingSurface, serial, appId);
        return token;
    }

    public bool Redeem(string token, Surface target)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(target);
        if (!_tokens.Remove(token, out var minted))
        {
            return false;
        }

        LastRedeemedAppId = minted.AppId;
        return true;
    }

    public void ExpireAll() => _tokens.Clear();
}
