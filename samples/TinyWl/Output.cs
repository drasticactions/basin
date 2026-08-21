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

internal sealed class Output(IOutput output, OutputGlobal global)
{
    public IOutput Handle { get; } = output;

    public OutputGlobal Global { get; } = global;

    public SceneOutput? Scene { get; set; }

    public OutputScheduler? Scheduler { get; set; }

    public Swapchain? Swapchain { get; set; }
}
