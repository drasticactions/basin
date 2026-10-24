using Basin.Diagnostics;
using Pixman;

namespace Basin.Capabilities;

/// <summary>
/// Receives the per-event tablet input from an <see cref="ITabletSource"/>. The calls come on the compositor thread.
/// </summary>
public interface ITabletObserver
{
    /// <summary>
    /// Called when a tool enters or leaves the sensing range of a tablet. An axis event with the entry position comes before an entry.
    /// </summary>
    /// <param name="toolId">The id of the tool, from <see cref="TabletToolInfo.Id"/>.</param>
    /// <param name="tabletId">The id of the tablet, from <see cref="TabletInfo.Id"/>.</param>
    /// <param name="inProximity"><see langword="true"/> when the tool enters, <see langword="false"/> when it leaves.</param>
    void OnToolProximity(ulong toolId, ulong tabletId, bool inProximity);

    /// <summary>
    /// Called with the current values of all axes of a tool.
    /// </summary>
    /// <param name="toolId">The id of the tool.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="axes">The axis values.</param>
    void OnToolAxis(ulong toolId, uint timeMs, TabletToolAxes axes);

    /// <summary>
    /// Called when a tool button changes state. Tip contact is reported as the button <c>BTN_TOUCH</c> (0x14a).
    /// </summary>
    /// <param name="toolId">The id of the tool.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="button">The Linux input event code of the button.</param>
    /// <param name="pressed"><see langword="true"/> when the button is pressed, <see langword="false"/> when it is released.</param>
    void OnToolButton(ulong toolId, uint timeMs, uint button, bool pressed);

    /// <summary>
    /// Called when a button, ring, strip or dial on a pad changes.
    /// </summary>
    /// <param name="padId">The id of the pad, from <see cref="TabletPadInfo.Id"/>.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="padEvent">The control that changed and its value.</param>
    void OnPadEvent(ulong padId, uint timeMs, TabletPadEvent padEvent);
}
