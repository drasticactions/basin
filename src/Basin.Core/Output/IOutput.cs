using Basin.Capabilities;
using Pixman;

namespace Basin;

/// <summary>
/// Represents one display that the compositor draws to. A consumer stages changes in an <see cref="OutputState"/>, tests them with <see cref="TestCommit"/> and applies them with <see cref="Commit"/>.
/// </summary>
public interface IOutput
{
    /// <summary>
    /// Gets the stable name of the output, for example the connector name <c>DP-1</c>. Protocols and configuration files address the output by it.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets a text for people that describes the output. It can be empty.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets the manufacturer of the display, or an empty string when it is unknown.
    /// </summary>
    string Make { get; }

    /// <summary>
    /// Gets the model name of the display, or an empty string when it is unknown.
    /// </summary>
    string Model { get; }

    /// <summary>
    /// Gets the serial number of the display, or an empty string when it is unknown.
    /// </summary>
    string Serial { get; }

    /// <summary>
    /// Gets the physical size of the visible area in millimeters. A value of zero means that the size is unknown.
    /// </summary>
    (int Width, int Height) PhysicalSize { get; }

    /// <summary>
    /// Gets the kind of display, which sets the viewing distance that <see cref="OutputScale.Choose"/> assumes. The default is <see cref="OutputClass.Desktop"/>.
    /// </summary>
    OutputClass Class => OutputClass.Desktop;

    /// <summary>
    /// Gets the mode that the last successful commit applied. It is the default mode before the first commit that sets one.
    /// </summary>
    OutputMode CurrentMode { get; }

    /// <summary>
    /// Gets a value indicating whether the last commit that set it turned the output on.
    /// </summary>
    bool Enabled { get; }

    /// <summary>
    /// Gets the fractional scale from logical to physical pixels that the last commit applied.
    /// </summary>
    double Scale { get; }

    /// <summary>
    /// Gets the transform that the last commit applied. It is what the panel does to the image, so the compositor must draw its inverse.
    /// </summary>
    OutputTransform Transform { get; }

    /// <summary>
    /// Gets a value indicating whether variable refresh is in force.
    /// </summary>
    bool AdaptiveSync { get; }

    /// <summary>
    /// Gets a value indicating whether the last flip was a tearing flip, not tied to the vblank. The default is <see langword="false"/>.
    /// </summary>
    bool LastFlipWasAsync => false;

    /// <summary>
    /// Gets a counter that changes when the hardware plane setup can change, for example after a modeset. A cached plane refusal is stale when the counter moved. The default is always zero.
    /// </summary>
    long PlaneEpoch => 0;

    /// <summary>
    /// Gets a value indicating whether the output can wait for an in-fence on the primary buffer. A commit with a fence that the output cannot wait for fails. The default is <see langword="false"/>.
    /// </summary>
    bool SupportsInFence => false;

    /// <summary>
    /// Tells whether the display engine can scan out a buffer with this format and modifier. The default is <see langword="true"/>, because a software or nested output constrains nothing.
    /// </summary>
    /// <param name="format">The DRM fourcc of the buffer.</param>
    /// <param name="modifier">The DRM format modifier of the buffer.</param>
    /// <param name="overlay"><see langword="true"/> to ask about an overlay plane, <see langword="false"/> to ask about the primary plane.</param>
    /// <returns><see langword="false"/> when the plane cannot take the buffer.</returns>
    bool CanScanout(DrmFormat format, ulong modifier, bool overlay) => true;

    /// <summary>
    /// Gets the configuration features that the output hardware supports. The default is <see cref="OutputConfigurationFeatures.None"/>.
    /// </summary>
    OutputConfigurationFeatures Features => OutputConfigurationFeatures.None;

    /// <summary>
    /// Gets the color abilities that the display reports, for example from its EDID. It is <see langword="null"/> when the output does not know them, which is the default.
    /// </summary>
    OutputColorimetry? Colorimetry => null;

    /// <summary>
    /// Gets the raw EDID blob of the display. It is empty when the output has no EDID, which is the default.
    /// </summary>
    ReadOnlyMemory<byte> EdidBytes => default;

    /// <summary>
    /// Gets how the panel is mounted in its case, as the kernel reports it. The default is <see cref="OutputTransform.Normal"/>.
    /// </summary>
    OutputTransform PanelOrientation => OutputTransform.Normal;

    /// <summary>
    /// Asks the scheduler of this output for a repaint without damage. It raises <see cref="RepaintRequested"/>.
    /// </summary>
    void RequestRepaint();

    /// <summary>
    /// Tests a state without applying it. The test writes <see cref="OutputLayer.Accepted"/> on each layer that the state carries.
    /// </summary>
    /// <param name="state">The staged changes. Fields that it does not set keep their current values.</param>
    /// <returns><see langword="true"/> when <see cref="Commit"/> with the same state can succeed.</returns>
    bool TestCommit(OutputState state);

    /// <summary>
    /// Tests and then applies a state. Each field that the state does not set keeps its value from earlier commits. The output raises <see cref="Committed"/> on success.
    /// </summary>
    /// <param name="state">The staged changes. File descriptors in it are borrowed for the length of the call.</param>
    /// <returns><see langword="false"/> when the output did not apply the state. The caller keeps its damage and tries again.</returns>
    bool Commit(OutputState state);

    /// <summary>
    /// Occurs when the output can take a new frame, normally when a flip completes. A disabled or destroyed output does not raise it.
    /// </summary>
    event Action? Frame;

    /// <summary>
    /// Occurs when <see cref="RequestRepaint"/> is called. The scheduler that paces the output handles it as damage.
    /// </summary>
    event Action? RepaintRequested;

    /// <summary>
    /// Occurs after a commit applies. The argument names the fields that the committed state carried.
    /// </summary>
    event Action<OutputStateFields>? Committed;

    /// <summary>
    /// Occurs once when the output is destroyed, for example on an unplug. The output must not be used after it.
    /// </summary>
    event Action? Destroyed;

    /// <summary>
    /// Occurs when the list of modes that the output offers changes, for example after a hotplug. The default implementation never raises it.
    /// </summary>
    event Action? ModesChanged
    {
        add
        {
        }

        remove
        {
        }
    }
}
