using AccessKit;
using AccessKit.Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using AkAction = AccessKit.Action;

namespace Basin.UI.Avalonia;

internal static class AccessKitChrome
{
    private static readonly AccessKitAvaloniaOptions Options = new()
    {
        ValidateUpdates = true,
        TrackWindowActivation = false,
        ApplyRootTransform = false,
        MapRole = static peer => PressableOwner(peer) is not null ? Role.Button : null,
        ConfigureNode = static (peer, node) =>
        {
            if (PressableOwner(peer) is not null)
            {
                node.AddAction(AkAction.Click);
            }
        },
        HandleAction = static (peer, request) =>
        {
            if (request.Action != AkAction.Click || PressableOwner(peer) is not { } owner)
            {
                return false;
            }

            ChromeAutomation.Invoke(owner);
            return true;
        },
    };

    public static AccessKitTopLevel Attach(TopLevel root)
    {
        AccessKitLogSink.Install();
        var connection = AccessKitAvalonia.Attach(root, Options);
        if (connection.Adapter is { } adapter)
        {
            adapter.UnhandledException += static (_, error) => AccessKitLogSink.Output.Warn(error, $"accessibility: an adapter handler failed");
        }

        return connection;
    }

    private static Control? PressableOwner(AutomationPeer peer) =>
        peer is ControlAutomationPeer { Owner: { } owner } && owner is not TextBox && ChromeAutomation.GetIsButton(owner) ? owner : null;
}
