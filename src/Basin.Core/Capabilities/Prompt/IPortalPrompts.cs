namespace Basin.Capabilities;

public interface IPortalPrompts
{
    ValueTask<PromptOutcome<SourceSelection>> SelectSources(in SourcePrompt prompt, CancellationToken cancellation);

    ValueTask<PromptOutcome<DeviceSelection>> SelectDevices(in DevicePrompt prompt, CancellationToken cancellation);

    ValueTask<PromptOutcome<Box>> SelectArea(in AreaPrompt prompt, CancellationToken cancellation);

    ValueTask<PromptOutcome<ShortcutBinding[]>> BindShortcuts(in ShortcutPrompt prompt, CancellationToken cancellation);

    ValueTask<PromptOutcome<bool>> Confirm(in ConfirmPrompt prompt, CancellationToken cancellation);
}
