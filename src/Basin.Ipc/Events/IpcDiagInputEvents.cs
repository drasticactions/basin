using System.Text.Json;
using Basin.Capabilities;

namespace Basin.Ipc;

internal sealed class IpcDiagInputEvents(IpcServer server, IpcDescribe describe, IInputDiagnostics diagnostics) : IpcEventSource
{
    private IDisposable? _watch;
    private bool _motion;

    public static bool WantsMotion(JsonElement? options) =>
        options is { ValueKind: JsonValueKind.Object } given && given.TryGetProperty("motion", out var motion) && motion.ValueKind == JsonValueKind.True;

    protected internal override string? Validate(string name, JsonElement? options)
    {
        if (options is not { } given)
        {
            return null;
        }

        if (given.ValueKind != JsonValueKind.Object)
        {
            return $"The options of {name} must be an object.";
        }

        foreach (var property in given.EnumerateObject())
        {
            if (property.Name != "motion" || property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return $"{name} takes one option, \"motion\": a boolean";
            }
        }

        return null;
    }

    protected internal override void SubscribersChanged(string name)
    {
        if (IsAttached && AnyMotion() != _motion)
        {
            Watch();
        }
    }

    protected override void Attach() => Watch();

    protected override void Detach()
    {
        _watch?.Dispose();
        _watch = null;
    }

    private bool AnyMotion()
    {
        foreach (var subscriber in server.Events.Subscribers(IpcEventNames.DiagInput))
        {
            if (WantsMotion(server.Events.OptionsOf(IpcEventNames.DiagInput, subscriber)))
            {
                return true;
            }
        }

        return false;
    }

    private void Watch()
    {
        _watch?.Dispose();
        _motion = AnyMotion();
        _watch = diagnostics.WatchInput(_motion, OnInput);
    }

    private void OnInput(InputTraceEvent input)
    {
        ulong window = 0;
        string? appId = null;
        if (input.Surface is { } surface && describe.Toplevels is { } model && model.TryFind(surface, out var info))
        {
            window = info.Id;
            appId = info.AppId;
        }

        var wire = new IpcDiagInput(
            input.Kind, window, appId, input.X, input.Y, input.LayoutX, input.LayoutY, input.Code, input.Pressed, input.Value, input.Detail);
        var bus = server.Events;
        var motion = input.Kind == "motion";
        var subscribers = bus.Subscribers(IpcEventNames.DiagInput);
        for (var i = subscribers.Count - 1; i >= 0; i--)
        {
            if (i < subscribers.Count && (!motion || WantsMotion(bus.OptionsOf(IpcEventNames.DiagInput, subscribers[i]))))
            {
                bus.EmitTo(IpcEventNames.DiagInput, subscribers[i], wire, IpcJsonContext.Default.IpcDiagInput);
            }
        }
    }
}
