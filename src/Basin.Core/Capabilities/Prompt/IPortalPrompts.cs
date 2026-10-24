namespace Basin.Capabilities;

/// <summary>
/// Shows the dialogs that the xdg-desktop-portal backend needs. A portal module cannot install without an implementation, because an answer without a dialog is a policy.
/// </summary>
public interface IPortalPrompts
{
    /// <summary>
    /// Asks the user to choose the outputs, windows or virtual outputs that a screencast captures.
    /// </summary>
    /// <param name="prompt">The choices to offer and the requesting application.</param>
    /// <param name="cancellation">A token that the portal frontend cancels when it closes the request.</param>
    /// <returns>The chosen sources, or a denied or cancelled outcome.</returns>
    ValueTask<PromptOutcome<SourceSelection>> SelectSources(in SourcePrompt prompt, CancellationToken cancellation);

    /// <summary>
    /// Asks the user which input devices an application can control.
    /// </summary>
    /// <param name="prompt">The requested devices and the requesting application.</param>
    /// <param name="cancellation">A token that the portal frontend cancels when it closes the request.</param>
    /// <returns>The granted devices, or a denied or cancelled outcome.</returns>
    ValueTask<PromptOutcome<DeviceSelection>> SelectDevices(in DevicePrompt prompt, CancellationToken cancellation);

    /// <summary>
    /// Asks the user to select a rectangle or a point on screen.
    /// </summary>
    /// <param name="prompt">The output and the mode of the picker.</param>
    /// <param name="cancellation">A token that the portal frontend cancels when it closes the request.</param>
    /// <returns>The selected box in layout coordinates, or a denied or cancelled outcome. A point is a box of size 1 by 1.</returns>
    ValueTask<PromptOutcome<Box>> SelectArea(in AreaPrompt prompt, CancellationToken cancellation);

    /// <summary>
    /// Asks the user to accept or change the key triggers for the global shortcuts that an application registers.
    /// </summary>
    /// <param name="prompt">The shortcuts to bind and their preferred triggers.</param>
    /// <param name="cancellation">A token that the portal frontend cancels when it closes the request.</param>
    /// <returns>One binding for each accepted shortcut, or a denied or cancelled outcome.</returns>
    ValueTask<PromptOutcome<ShortcutBinding[]>> BindShortcuts(in ShortcutPrompt prompt, CancellationToken cancellation);

    /// <summary>
    /// Asks the user a yes-or-no question.
    /// </summary>
    /// <param name="prompt">The title and the text of the question.</param>
    /// <param name="cancellation">A token that the portal frontend cancels when it closes the request.</param>
    /// <returns>An accepted outcome that holds the answer, or a denied or cancelled outcome.</returns>
    ValueTask<PromptOutcome<bool>> Confirm(in ConfirmPrompt prompt, CancellationToken cancellation);
}
