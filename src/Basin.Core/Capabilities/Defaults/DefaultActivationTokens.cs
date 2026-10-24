using Wayland.Server;

namespace Basin.Capabilities.Defaults;

/// <summary>
/// Issues numbered single-use activation tokens that expire after <see cref="TimeoutMs"/>. It does no focus-stealing check: each live token activates any surface once.
/// It keeps no reference to the minting client or surface, so a token outlives a launcher that exits before its child redeems it.
/// </summary>
public sealed class DefaultActivationTokens : IActivationTokens
{
    private readonly Dictionary<string, LinkedListNode<Token>> _tokens = [];
    private readonly LinkedList<Token> _order = [];
    private int _counter;

    private readonly record struct Token(string Name, string? AppId, long MintedAtMs);

    /// <summary>
    /// Gets or sets the time in milliseconds after which a token no longer redeems. The default is 30 000, as in wlroots.
    /// </summary>
    public long TimeoutMs { get; set; } = 30_000;

    /// <summary>
    /// Gets or sets the most tokens kept at one time. A mint past it drops the oldest token. The default is 256.
    /// </summary>
    public int Capacity { get; set; } = 256;

    /// <summary>
    /// Gets the number of tokens that are minted and not redeemed, dropped or found expired. An expired token counts until the next mint or redeem removes it.
    /// </summary>
    public int Outstanding => _tokens.Count;

    /// <summary>
    /// Gets the app id that was stored with the last redeemed token, or <see langword="null"/> when none was stored or no token was redeemed.
    /// </summary>
    public string? LastRedeemedAppId { get; private set; }

    /// <summary>
    /// Mints a token that stores only the app id and the time. It removes the expired tokens first, and the oldest token when the table is full.
    /// </summary>
    /// <param name="client">The client that asks. The token keeps no reference to it.</param>
    /// <param name="requestingSurface">The surface that asks. The token keeps no reference to it.</param>
    /// <param name="serial">The input serial. The default does not check it.</param>
    /// <param name="appId">The app id that the client gave, or <see langword="null"/>.</param>
    /// <returns>The token string.</returns>
    public string Mint(WlClient client, Surface? requestingSurface, uint serial, string? appId)
    {
        ArgumentNullException.ThrowIfNull(client);
        var now = Environment.TickCount64;
        Expire(now);
        while (_tokens.Count > 0 && _tokens.Count >= Math.Max(1, Capacity))
        {
            Drop(_order.First!);
        }

        var name = $"basin-{++_counter}";
        _tokens[name] = _order.AddLast(new Token(name, appId, now));
        return name;
    }

    /// <summary>
    /// Consumes a token. It does not check the client, the serial or the surface that minted the token.
    /// </summary>
    /// <param name="token">The token that the client presents.</param>
    /// <param name="target">The surface to activate.</param>
    /// <returns><see langword="true"/> when the token was minted, is not older than <see cref="TimeoutMs"/>, and was not consumed yet.</returns>
    public bool Redeem(string token, Surface target)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(target);
        Expire(Environment.TickCount64);
        if (!_tokens.TryGetValue(token, out var node))
        {
            return false;
        }

        Drop(node);
        LastRedeemedAppId = node.Value.AppId;
        return true;
    }

    /// <inheritdoc/>
    public void ExpireAll()
    {
        _tokens.Clear();
        _order.Clear();
    }

    private void Expire(long now)
    {
        while (_order.First is { } oldest && now - oldest.Value.MintedAtMs >= TimeoutMs)
        {
            Drop(oldest);
        }
    }

    private void Drop(LinkedListNode<Token> node)
    {
        _tokens.Remove(node.Value.Name);
        _order.Remove(node);
    }
}
