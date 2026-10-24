using System.Text.Json;
using Basin.Capabilities;

namespace Basin.Ipc;

internal sealed class IpcDiagDamageEvents(IpcServer server, ISceneDiagnostics diagnostics) : IpcEventSource
{
    private IDisposable? _watch;
    private bool _stacks;

    public static bool WantsStack(JsonElement? options) =>
        options is { ValueKind: JsonValueKind.Object } given && given.TryGetProperty("stack", out var stack) && stack.ValueKind == JsonValueKind.True;

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
            if (property.Name != "stack" || property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return $"{name} takes one option, \"stack\": a boolean";
            }
        }

        return null;
    }

    protected internal override void SubscribersChanged(string name)
    {
        if (!IsAttached)
        {
            return;
        }

        var stacks = AnyStack();
        if (stacks != _stacks)
        {
            Watch(stacks);
        }
    }

    protected override void Attach() => Watch(AnyStack());

    protected override void Detach()
    {
        _watch?.Dispose();
        _watch = null;
    }

    private bool AnyStack()
    {
        foreach (var subscriber in server.Events.Subscribers(IpcEventNames.DiagDamage))
        {
            if (WantsStack(server.Events.OptionsOf(IpcEventNames.DiagDamage, subscriber)))
            {
                return true;
            }
        }

        return false;
    }

    private void Watch(bool stacks)
    {
        _watch?.Dispose();
        _stacks = stacks;
        _watch = diagnostics.WatchDamage(stacks, OnDamage);
    }

    private void OnDamage(DamageInfo damage)
    {
        var bus = server.Events;
        var box = new IpcBox(damage.Box.X, damage.Box.Y, damage.Box.Width, damage.Box.Height);
        var subscribers = bus.Subscribers(IpcEventNames.DiagDamage);
        for (var i = subscribers.Count - 1; i >= 0; i--)
        {
            if (i >= subscribers.Count)
            {
                continue;
            }

            var subscriber = subscribers[i];
            var stack = WantsStack(bus.OptionsOf(IpcEventNames.DiagDamage, subscriber)) ? damage.Stack : null;
            bus.EmitTo(IpcEventNames.DiagDamage, subscriber, new IpcDiagDamage(damage.Path, box, stack), IpcJsonContext.Default.IpcDiagDamage);
        }
    }
}
