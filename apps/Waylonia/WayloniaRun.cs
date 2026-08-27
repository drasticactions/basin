using System.CommandLine;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Threading;
using Basin;
using Basin.Avalonia;
using Basin.Cli;
using Basin.Diagnostics;
using Basin.Scene;

namespace Waylonia;

internal sealed record WayloniaRun(
    long Frames,
    string? Screenshot,
    string? SocketName,
    string? Command,
    string? WaypipeListen,
    string? SshHost,
    string? SshCommand,
    Basin.Transport.Waypipe.WaypipeCompression Compression,
    bool Gpu,
    bool Audio,
    string AudioFormat,
    string? Video,
    Basin.Capabilities.IVideoDecoder? VideoDecoder,
    bool XWayland,
    bool Tray,
    bool Clipboard,
    bool Drag,
    bool FollowCursor,
    bool GtkDpi,
    IReadOnlyList<Hotkey> Hotkeys,
    DesktopRecipe? Desktop = null,
    IReadOnlyList<string>? DesktopEnv = null,
    (int Width, int Height)? DesktopSize = null,
    string CaptureChord = "double:RightControl");
