using Basin.Capabilities;

namespace Basin.Shell.Xdg;

/// <summary>
/// Answers a toplevel request before <see cref="XdgToplevelSource"/> applies its own handling.
/// </summary>
/// <param name="window">The window that the request is for.</param>
/// <param name="request">The action and its arguments.</param>
/// <returns><see langword="true"/> or <see langword="false"/> to answer the request, or <see langword="null"/> to let the source handle it.</returns>
public delegate bool? XdgToplevelRequestHandler(XdgToplevelWindow window, in ToplevelRequest request);
