using Basin.Capabilities;

namespace Basin.Portal;

/// <summary>
/// An <see cref="IPortalPrompts"/> that answers each prompt from its properties with no user. Tests and headless runs use it. It records every prompt in <see cref="Asked"/>.
/// </summary>
public sealed class AutoAnswerPrompts : IPortalPrompts
{
    /// <summary>
    /// The answer to a source prompt.
    /// </summary>
    public enum SourceAnswer
    {
        /// <summary>
        /// Select the first output.
        /// </summary>
        FirstOutput,

        /// <summary>
        /// Select every output, or the first one when the prompt does not allow more than one.
        /// </summary>
        AllOutputs,

        /// <summary>
        /// Select the first window.
        /// </summary>
        FirstToplevel,

        /// <summary>
        /// Select the window with the id in <see cref="ToplevelId"/>.
        /// </summary>
        Toplevel,

        /// <summary>
        /// Refuse the prompt.
        /// </summary>
        Deny,

        /// <summary>
        /// Cancel the prompt.
        /// </summary>
        Cancel,
    }

    private TaskCompletionSource? _hold;

    /// <summary>
    /// Gets or sets the answer to source prompts. The default is <see cref="SourceAnswer.FirstOutput"/>.
    /// </summary>
    public SourceAnswer Sources { get; set; } = SourceAnswer.FirstOutput;

    /// <summary>
    /// Gets or sets the window id that <see cref="SourceAnswer.Toplevel"/> selects.
    /// </summary>
    public ulong ToplevelId { get; set; }

    /// <summary>
    /// Gets or sets the persist mode in the answer when the prompt offers persistence.
    /// </summary>
    public uint PersistMode { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether device prompts are refused.
    /// </summary>
    public bool DenyDevices { get; set; }

    /// <summary>
    /// Gets or sets the devices to grant, limited to the requested ones. <see langword="null"/> grants every requested device.
    /// </summary>
    public InputDeviceCapability? Devices { get; set; }

    /// <summary>
    /// Gets or sets the clipboard answer when the prompt requests it. <see langword="null"/> grants it.
    /// </summary>
    public bool? Clipboard { get; set; }

    /// <summary>
    /// Gets or sets the area that area prompts select. A point prompt takes its origin. <see langword="null"/> selects an empty area or the point 0, 0.
    /// </summary>
    public Box? Area { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether area prompts are refused.
    /// </summary>
    public bool DenyArea { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether shortcut prompts are refused.
    /// </summary>
    public bool DenyShortcuts { get; set; }

    /// <summary>
    /// Gets or sets the trigger for each shortcut. <see langword="null"/> uses the preferred trigger when it is free.
    /// </summary>
    public string? ShortcutTrigger { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether confirm prompts are accepted. <see langword="false"/> refuses them.
    /// </summary>
    public bool ConfirmAnswer { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether prompts wait for <see cref="Release"/> before they answer.
    /// </summary>
    public bool Hold { get; set; }

    /// <summary>
    /// Gets every prompt that was asked, in order.
    /// </summary>
    public List<object> Asked { get; } = [];

    /// <summary>
    /// Gets 1 while a held prompt waits, otherwise 0.
    /// </summary>
    public int HeldCount => _hold is null ? 0 : 1;

    /// <summary>
    /// Lets the held prompts answer.
    /// </summary>
    public void Release()
    {
        var hold = _hold;
        _hold = null;
        hold?.TrySetResult();
    }

    /// <summary>
    /// Answers a source prompt from <see cref="Sources"/>.
    /// </summary>
    /// <param name="prompt">The prompt.</param>
    /// <param name="cancellation">The token that cancels the prompt.</param>
    /// <returns>The selected sources, or a refused or canceled outcome.</returns>
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

    /// <summary>
    /// Answers a device prompt from <see cref="Devices"/>, <see cref="Clipboard"/> and <see cref="DenyDevices"/>.
    /// </summary>
    /// <param name="prompt">The prompt.</param>
    /// <param name="cancellation">The token that cancels the prompt.</param>
    /// <returns>The granted devices, or a refused or canceled outcome.</returns>
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

    /// <summary>
    /// Answers an area or point prompt from <see cref="Area"/> and <see cref="DenyArea"/>.
    /// </summary>
    /// <param name="prompt">The prompt.</param>
    /// <param name="cancellation">The token that cancels the prompt.</param>
    /// <returns>The selected area, or a refused or canceled outcome.</returns>
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

    /// <summary>
    /// Answers a shortcut prompt from <see cref="ShortcutTrigger"/> and <see cref="DenyShortcuts"/>.
    /// </summary>
    /// <param name="prompt">The prompt.</param>
    /// <param name="cancellation">The token that cancels the prompt.</param>
    /// <returns>The bindings, or a refused or canceled outcome.</returns>
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

    /// <summary>
    /// Answers a confirm prompt from <see cref="ConfirmAnswer"/>.
    /// </summary>
    /// <param name="prompt">The prompt.</param>
    /// <param name="cancellation">The token that cancels the prompt.</param>
    /// <returns>An accepted outcome, or a refused or canceled outcome.</returns>
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
