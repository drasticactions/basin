using Basin.Desktop;
using Wayland.Server;

namespace Basin.Hypr;

/// <summary>
/// Allows every client that is not in a <c>wp_security_context_v1</c> sandbox. It is the default <see cref="IActionBindingPolicy"/>.
/// </summary>
public sealed class SandboxActionBindingPolicy : IActionBindingPolicy
{
    /// <summary>
    /// Refuses a client that connected through a security context, for example a Flatpak app.
    /// </summary>
    /// <param name="client">The client that bound the global.</param>
    /// <returns><see langword="true"/> when <see cref="SecurityContextManager.ContextOf"/> returns <see langword="null"/> for the client.</returns>
    public bool Allows(WlClient client) => SecurityContextManager.ContextOf(client) is null;
}
