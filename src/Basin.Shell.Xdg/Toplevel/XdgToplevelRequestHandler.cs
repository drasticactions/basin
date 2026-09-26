using Basin.Capabilities;

namespace Basin.Shell.Xdg;

public delegate bool? XdgToplevelRequestHandler(XdgToplevelWindow window, in ToplevelRequest request);
