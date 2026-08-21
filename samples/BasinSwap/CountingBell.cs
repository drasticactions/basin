using System.Diagnostics;
using Basin;
using Basin.Backend.Headless;
using Basin.Capabilities;
using Basin.Cli;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Render.Pixman;
using Basin.Scene;
using Basin.Shell.Xdg;
using Wayland.Server;

namespace Basin.Samples.Swap;

internal sealed class CountingBell : IBell
{
    public int Rings { get; private set; }

    public void Ring(Surface? surface) => Rings++;
}
