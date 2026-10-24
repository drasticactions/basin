using System.Diagnostics;
using Basin.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin;

/// <summary>
/// The flags of a <c>wp_presentation_feedback.presented</c> event. The values match the protocol, and a combination sets each named flag.
/// </summary>
[Flags]
public enum PresentedFlags : uint
{
    /// <summary>
    /// No flag.
    /// </summary>
    None = 0,
    /// <summary>
    /// The presentation was synchronized to the vertical retrace.
    /// </summary>
    Vsync = 1,
    /// <summary>
    /// The display hardware gave the timestamp.
    /// </summary>
    HwClock = 2,
    /// <summary>
    /// The display hardware signaled the completion of the presentation.
    /// </summary>
    HwCompletion = 4,
    /// <summary>
    /// The display showed the client buffer directly, with no copy.
    /// </summary>
    ZeroCopy = 8,
}
