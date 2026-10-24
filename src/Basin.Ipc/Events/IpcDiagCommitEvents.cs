using System.Text.Json;
using Basin.Capabilities;

namespace Basin.Ipc;

internal sealed class IpcDiagCommitEvents(IpcServer server, IpcDescribe describe, ISurfaceDiagnostics diagnostics) : IpcEventSource
{
    private IDisposable? _watch;

    public static string? TargetOf(JsonElement? options) =>
        options is { ValueKind: JsonValueKind.Object } given && given.TryGetProperty("target", out var target) && target.ValueKind == JsonValueKind.String
            ? target.GetString()
            : null;

    protected internal override string? Validate(string name, JsonElement? options)
    {
        if (options is not { ValueKind: JsonValueKind.Object } given || TargetOf(options) is not { Length: > 0 })
        {
            return $"{name} needs the options {{\"target\": SELECTOR}}. Give a window id, an app_id, part of a title, or \"*\" for every surface.";
        }

        foreach (var property in given.EnumerateObject())
        {
            if (property.Name != "target")
            {
                return $"{name} takes one option, \"target\"";
            }
        }

        return null;
    }

    protected override void Attach() => _watch = diagnostics.WatchCommits(Wanted, OnCommit);

    protected override void Detach()
    {
        _watch?.Dispose();
        _watch = null;
    }

    private bool Wanted(Surface surface)
    {
        var info = Describe(surface);
        foreach (var subscriber in server.Events.Subscribers(IpcEventNames.DiagCommit))
        {
            if (Matches(subscriber, surface, info))
            {
                return true;
            }
        }

        return false;
    }

    private ToplevelInfo? Describe(Surface surface) =>
        describe.Toplevels is { } model && model.TryFind(surface, out var info) ? info : null;

    private bool Matches(IIpcEventSubscriber subscriber, Surface surface, ToplevelInfo? info) =>
        TargetOf(server.Events.OptionsOf(IpcEventNames.DiagCommit, subscriber)) is { } target
        && (target == "*" || (info is { } found && IpcWindowSelector.Matches(target, found)));

    private void OnCommit(CommitTrace trace)
    {
        var info = Describe(trace.Surface);
        var damage = new IpcBox[trace.Damage.Count];
        for (var i = 0; i < damage.Length; i++)
        {
            var box = trace.Damage[i];
            damage[i] = new IpcBox(box.X, box.Y, box.Width, box.Height);
        }

        var wire = new IpcDiagCommit(
            trace.Kind, info?.Id ?? 0, info?.AppId, trace.Width, trace.Height, trace.Format.ToString(), damage, trace.FrameCallbacks,
            trace.Feedback, trace.TimestampMs);
        var bus = server.Events;
        var subscribers = bus.Subscribers(IpcEventNames.DiagCommit);
        for (var i = subscribers.Count - 1; i >= 0; i--)
        {
            if (i < subscribers.Count && Matches(subscribers[i], trace.Surface, info))
            {
                bus.EmitTo(IpcEventNames.DiagCommit, subscribers[i], wire, IpcJsonContext.Default.IpcDiagCommit);
            }
        }
    }
}
