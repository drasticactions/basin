using Wayland.Server;

namespace Basin.Capabilities;

/// <summary>
/// Mints and redeems the single-use tokens that <c>xdg_activation_v1</c> uses to pass focus from one surface to another.
/// </summary>
public interface IActivationTokens
{
    /// <summary>
    /// Creates a new token for a client. Policy about whether the token can later activate a window lives in the implementation.
    /// </summary>
    /// <param name="client">The client that asks for the token.</param>
    /// <param name="requestingSurface">The surface that the client named, or <see langword="null"/> when it named none.</param>
    /// <param name="serial">The input serial that the client gave, or 0 when it gave none.</param>
    /// <param name="appId">The app id of the application to launch, or <see langword="null"/> when it is not known.</param>
    /// <returns>The token string. It is not empty.</returns>
    string Mint(WlClient client, Surface? requestingSurface, uint serial, string? appId);

    /// <summary>
    /// Consumes a token for a surface that asks to be activated. A token can be redeemed only once.
    /// </summary>
    /// <param name="token">The token that the client presents.</param>
    /// <param name="target">The surface to activate.</param>
    /// <returns><see langword="true"/> when the token is valid and the compositor must activate <paramref name="target"/>, otherwise <see langword="false"/>.</returns>
    bool Redeem(string token, Surface target);

    /// <summary>
    /// Invalidates every token that is not redeemed yet.
    /// </summary>
    void ExpireAll();
}
