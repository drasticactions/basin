using Basin.Capabilities;

namespace Basin.Portal;

public sealed class AutoAnswerPrompts : IPortalPrompts
{
    public enum SourceAnswer
    {
        FirstOutput,

        AllOutputs,

        FirstToplevel,

        Toplevel,

        Deny,

        Cancel,
    }

    private TaskCompletionSource? _hold;

    public SourceAnswer Sources { get; set; } = SourceAnswer.FirstOutput;

    public ulong ToplevelId { get; set; }

    public uint PersistMode { get; set; }

    public bool DenyDevices { get; set; }

    public InputDeviceCapability? Devices { get; set; }

    public bool? Clipboard { get; set; }

    public Box? Area { get; set; }

    public bool DenyArea { get; set; }

    public bool DenyShortcuts { get; set; }

    public string? ShortcutTrigger { get; set; }

    public bool ConfirmAnswer { get; set; } = true;

    public bool Hold { get; set; }

    public List<object> Asked { get; } = [];

    public int HeldCount => _hold is null ? 0 : 1;

    public void Release()
    {
        var hold = _hold;
        _hold = null;
        hold?.TrySetResult();
    }

    public async ValueTask<PromptOutcome<SourceSelection>> SelectSources(SourcePrompt prompt, CancellationToken cancellation)
    {
        Asked.Add(prompt);
        if (!await WaitAsync(cancellation).ConfigureAwait(true))
        {
            return PromptOutcome<SourceSelection>.Cancelled;
        }

        switch (Sources)
        {
            case SourceAnswer.Deny:
                return PromptOutcome<SourceSelection>.Denied;
            case SourceAnswer.Cancel:
                return PromptOutcome<SourceSelection>.Cancelled;
        }

        var sources = new List<SelectedSource>();
        if (Sources is SourceAnswer.FirstToplevel or SourceAnswer.Toplevel)
        {
            foreach (var toplevel in prompt.Toplevels)
            {
                if (Sources == SourceAnswer.FirstToplevel || toplevel.Id == ToplevelId)
                {
                    sources.Add(new SelectedSource(PromptSourceKinds.Window, null, toplevel.Id));
                    break;
                }
            }
        }
        else if ((prompt.Kinds & PromptSourceKinds.Monitor) != 0)
        {
            foreach (var output in prompt.Outputs)
            {
                sources.Add(new SelectedSource(PromptSourceKinds.Monitor, output.Output, 0));
                if (Sources == SourceAnswer.FirstOutput || !prompt.Multiple)
                {
                    break;
                }
            }
        }

        if (sources.Count == 0)
        {
            return PromptOutcome<SourceSelection>.Denied;
        }

        return PromptOutcome<SourceSelection>.Accepted(new SourceSelection(sources, prompt.OfferPersist ? PersistMode : 0));
    }

    public async ValueTask<PromptOutcome<DeviceSelection>> SelectDevices(DevicePrompt prompt, CancellationToken cancellation)
    {
        Asked.Add(prompt);
        if (!await WaitAsync(cancellation).ConfigureAwait(true))
        {
            return PromptOutcome<DeviceSelection>.Cancelled;
        }

        if (DenyDevices)
        {
            return PromptOutcome<DeviceSelection>.Denied;
        }

        var devices = (Devices ?? prompt.Requested) & prompt.Requested;
        var clipboard = prompt.ClipboardRequested && (Clipboard ?? true);
        return PromptOutcome<DeviceSelection>.Accepted(new DeviceSelection(devices, clipboard, prompt.OfferPersist ? PersistMode : 0));
    }

    public async ValueTask<PromptOutcome<Box>> SelectArea(AreaPrompt prompt, CancellationToken cancellation)
    {
        Asked.Add(prompt);
        if (!await WaitAsync(cancellation).ConfigureAwait(true))
        {
            return PromptOutcome<Box>.Cancelled;
        }

        if (DenyArea)
        {
            return PromptOutcome<Box>.Denied;
        }

        if (Area is { } area)
        {
            return PromptOutcome<Box>.Accepted(prompt.PickPoint ? new Box(area.X, area.Y, 1, 1) : area);
        }

        return PromptOutcome<Box>.Accepted(prompt.PickPoint ? new Box(0, 0, 1, 1) : default);
    }

    public async ValueTask<PromptOutcome<ShortcutBinding[]>> BindShortcuts(ShortcutPrompt prompt, CancellationToken cancellation)
    {
        Asked.Add(prompt);
        if (!await WaitAsync(cancellation).ConfigureAwait(true))
        {
            return PromptOutcome<ShortcutBinding[]>.Cancelled;
        }

        if (DenyShortcuts)
        {
            return PromptOutcome<ShortcutBinding[]>.Denied;
        }

        var bindings = new List<ShortcutBinding>();
        foreach (var row in prompt.Shortcuts)
        {
            var trigger = ShortcutTrigger ?? (row.PreferredTaken ? "" : row.PreferredTrigger);
            if (!string.IsNullOrEmpty(trigger))
            {
                bindings.Add(new ShortcutBinding(row.Id, trigger));
            }
        }

        return PromptOutcome<ShortcutBinding[]>.Accepted(bindings.ToArray());
    }

    public async ValueTask<PromptOutcome<bool>> Confirm(ConfirmPrompt prompt, CancellationToken cancellation)
    {
        Asked.Add(prompt);
        if (!await WaitAsync(cancellation).ConfigureAwait(true))
        {
            return PromptOutcome<bool>.Cancelled;
        }

        return ConfirmAnswer ? PromptOutcome<bool>.Accepted(true) : PromptOutcome<bool>.Denied;
    }

    ValueTask<PromptOutcome<SourceSelection>> IPortalPrompts.SelectSources(in SourcePrompt prompt, CancellationToken cancellation) =>
        SelectSources(prompt, cancellation);

    ValueTask<PromptOutcome<DeviceSelection>> IPortalPrompts.SelectDevices(in DevicePrompt prompt, CancellationToken cancellation) =>
        SelectDevices(prompt, cancellation);

    ValueTask<PromptOutcome<Box>> IPortalPrompts.SelectArea(in AreaPrompt prompt, CancellationToken cancellation) =>
        SelectArea(prompt, cancellation);

    ValueTask<PromptOutcome<ShortcutBinding[]>> IPortalPrompts.BindShortcuts(in ShortcutPrompt prompt, CancellationToken cancellation) =>
        BindShortcuts(prompt, cancellation);

    ValueTask<PromptOutcome<bool>> IPortalPrompts.Confirm(in ConfirmPrompt prompt, CancellationToken cancellation) =>
        Confirm(prompt, cancellation);

    private async Task<bool> WaitAsync(CancellationToken cancellation)
    {
        if (cancellation.IsCancellationRequested)
        {
            return false;
        }

        if (!Hold)
        {
            return true;
        }

        var hold = _hold ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellation.Register(static state => ((TaskCompletionSource)state!).TrySetCanceled(), hold);
        try
        {
            await hold.Task.ConfigureAwait(true);
            return true;
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_hold, hold))
            {
                _hold = null;
            }

            return false;
        }
    }
}
