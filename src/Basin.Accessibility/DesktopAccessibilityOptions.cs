using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Host;
using Basin.Seat;

namespace Basin.Accessibility;

/// <summary>
/// The parts of a compositor that <see cref="DesktopAccessibility"/> drives.
/// </summary>
public sealed record DesktopAccessibilityOptions
{
    /// <summary>
    /// Gets the name of the compositor's accessible window, usually the program name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the host, for the accessibility gate.
    /// </summary>
    public required BasinHost Host { get; init; }

    /// <summary>
    /// Gets the scene whose outputs zoom and whose curtain is drawn.
    /// </summary>
    public required Scene.Scene Scene { get; init; }

    /// <summary>
    /// Gets the seat whose keys the screen reader monitors.
    /// </summary>
    public required Seat.Seat Seat { get; init; }

    /// <summary>
    /// Gets the layout pointer that zoom follows and that the pointer locator reports.
    /// </summary>
    public required LayoutPointer Pointer { get; init; }

    /// <summary>
    /// Gets the process tracker that runs the screen reader.
    /// </summary>
    public required ProcessTracker Processes { get; init; }

    /// <summary>
    /// Gets the windows, so zoom can follow the focus of an application, or <see langword="null"/> to follow the compositor's own chrome only.
    /// </summary>
    public IToplevelModel? Toplevels { get; init; }

    /// <summary>
    /// Gets the keyboard aids of the seat, or <see langword="null"/> when the compositor reads no physical keys.
    /// </summary>
    public KeyboardAids? Aids { get; init; }

    /// <summary>
    /// Gets the step that starts the toolkit's own accessibility, such as an Avalonia bridge. It returns the reason when the toolkit cannot start, or <see langword="null"/>.
    /// </summary>
    public Func<Task<string?>>? StartToolkit { get; init; }

    /// <summary>
    /// Gets the text-input-v3 manager, so zoom follows the caret of a client that has an input method but no accessibility tree, or <see langword="null"/>.
    /// </summary>
    public Basin.Desktop.TextInputManager? TextInput { get; init; }

    /// <summary>
    /// Gets a screen reader that the caller owns and reports, or <see langword="null"/> for one that the suite makes, reports and disposes.
    /// <see cref="DesktopAccessibility.Apply"/> leaves the command and mode of the caller's reader alone.
    /// </summary>
    public ScreenReader? Reader { get; init; }

    /// <summary>
    /// Gets the logger.
    /// </summary>
    public BasinLogger Log { get; init; }
}
