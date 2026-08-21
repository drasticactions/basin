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
    string? Video,
    Basin.Capabilities.IVideoDecoder? VideoDecoder,
    bool XWayland,
    bool Tray,
    bool Clipboard,
    bool Drag,
    bool FollowCursor,
    IReadOnlyList<Hotkey> Hotkeys);
