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

internal enum CursorMode
{
    Passthrough,
    Move,
    Resize,
}
