using System.Runtime.CompilerServices;
using Avalonia.Automation.Peers;

namespace Basin.UI.Avalonia;

internal static class AtSpiAccess
{
    private const string Server = "Avalonia.FreeDesktop.AtSpi.AtSpiServer, Avalonia.FreeDesktop.AtSpi";
    private const string Node = "Avalonia.FreeDesktop.AtSpi.AtSpiNode, Avalonia.FreeDesktop.AtSpi";

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    [return: UnsafeAccessorType(Server)]
    public static extern object Create();

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "StartAsync")]
    public static extern Task StartAsync([UnsafeAccessorType(Server)] object server);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "AddWindow")]
    public static extern void AddWindow([UnsafeAccessorType(Server)] object server, AutomationPeer windowPeer);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "RemoveWindow")]
    public static extern void RemoveWindow([UnsafeAccessorType(Server)] object server, AutomationPeer windowPeer);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "DisposeAsync")]
    public static extern ValueTask DisposeAsync([UnsafeAccessorType(Server)] object server);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "GetOrCreateNode")]
    [return: UnsafeAccessorType(Node)]
    public static extern object GetOrCreateNode([UnsafeAccessorType(Server)] object server, AutomationPeer peer);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "TryGetAttachedNode")]
    [return: UnsafeAccessorType(Node)]
    public static extern object? TryGetAttachedNode([UnsafeAccessorType(Server)] object server, AutomationPeer? peer);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "AttachNode")]
    public static extern bool AttachNode([UnsafeAccessorType(Server)] object server, [UnsafeAccessorType(Node)] object node, [UnsafeAccessorType(Node)] object? parent);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "EmitFocusChange")]
    public static extern void EmitFocusChange([UnsafeAccessorType(Server)] object server, [UnsafeAccessorType(Node)] object? focusedNode);
}
