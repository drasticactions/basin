using System.CommandLine;
using Basin;
using Basin.Backend.Drm;
using Basin.Backend.Libinput;
using Basin.Backend.Wayland;
using Basin.Cli;
using Basin.Diagnostics;
using Basin.Scene;
using Basin.Seat;
using Basin.Shell.Xdg;
using Microsoft.Extensions.Logging;
using Wayland;
using Wayland.Server;
using Xkb;

namespace TinyWl;

internal sealed class Toplevel(XdgToplevelWindow window, SceneSurface scene)
{
    public XdgToplevelWindow Window { get; } = window;

    public SceneSurface Scene { get; } = scene;

    public SceneTree Tree => Scene.Tree;

    public Surface Surface => Window.Surface;

    public Box Geometry => Window.Xdg.EffectiveGeometry;
}
