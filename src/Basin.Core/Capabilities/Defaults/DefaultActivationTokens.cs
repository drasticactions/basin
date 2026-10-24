using Wayland.Server;

namespace Basin.Capabilities.Defaults;

/// <summary>
/// Issues numbered single-use activation tokens. It does no focus-stealing check: each minted token activates any surface once.
/// </summary>
public sealed class DefaultActivationTokens : IActivationTokens
{
    private readonly Dictionary<string, Token> _tokens = [];
    private int _counter;

    private readonly record struct Token(WlClient Client, Surface? Surface, uint Serial, string? AppId);

    /// <summary>
    /// Gets the number of tokens that are minted and not redeemed or expired.
    /// </summary>
    public int Outstanding => _tokens.Count;

    /// <summary>
    /// Gets the app id that was stored with the last redeemed token, or <see langword="null"/> when none was stored or no token was redeemed.
    /// </summary>
    public string? LastRedeemedAppId { get; private set; }

    /// <inheritdoc/>
    public string Mint(WlClient client, Surface? requestingSurface, uint serial, string? appId)
    {
        ArgumentNullException.ThrowIfNull(client);
        var token = $"basin-{++_counter}";
        _tokens[token] = new Token(client, requestingSurface, serial, appId);
        return token;
    }

    /// <summary>
    /// Consumes a token. It does not check the client, the serial or the surface that minted the token.
    /// </summary>
    /// <param name="token">The token that the client presents.</param>
    /// <param name="target">The surface to activate.</param>
    /// <returns><see langword="true"/> when the token was minted and not consumed yet, otherwise <see langword="false"/>.</returns>
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

    /// <inheritdoc/>
    public void ExpireAll() => _tokens.Clear();
}
