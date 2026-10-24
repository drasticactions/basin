using System.Globalization;
using Basin.Capabilities;
using Basin.Config;
using Basin.Diagnostics;
using Basin.Host;
using Basin.Scene;

namespace Basin.Accessibility;

/// <summary>
/// The accessibility of one desktop in one object: the gate, the screen reader's manager and the compositor's accessible window,
/// the screen reader process, zoom with focus tracking, the screen curtain and the keyboard aids.
/// A compositor makes it once, calls <see cref="Apply"/> with its settings, <see cref="StartAsync"/> once the loop runs,
/// and <see cref="Run"/> for each accessibility action that a binding names. Use it on the compositor thread.
/// </summary>
public sealed class DesktopAccessibility : IAnnouncer, IDisposable
{
    private readonly DesktopAccessibilityOptions _options;
    private readonly Basin.Desktop.TextInputCaret? _caret;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="DesktopAccessibility"/> class. Zoom, the curtain and the reader exist at once. The bus interfaces start in <see cref="StartAsync"/>.
    /// </summary>
    /// <param name="options">The parts of the compositor.</param>
    public DesktopAccessibility(DesktopAccessibilityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        Curtain = options.Scene.Curtain ?? new SceneCurtain(options.Scene);
        Zoom = new OutputZoom(options.Scene, options.Pointer);
        if (options.TextInput is { } textInput)
        {
            _caret = new Basin.Desktop.TextInputCaret(textInput, options.Scene);
            Zoom.Track(_caret);
        }

        Zoom.Changed += () => BasinReport.Line(string.Create(CultureInfo.InvariantCulture, $"ZOOM factor={Zoom.Factor:0.###}"));
        if (options.Reader is { } given)
        {
            Reader = given;
        }
        else
        {
            Reader = new ScreenReader(options.Processes, ["orca", "--replace"], "toggle", options.Log);
            Reader.Started += process => BasinReport.Line($"READER on pid={process.Pid}");
            Reader.Stopped += () => BasinReport.Line("READER off");
        }
        if (options.Aids is { } aids)
        {
            aids.Announcer = this;
            aids.OptionsChanged += changed => BasinReport.Line($"AIDS sticky={(changed.Sticky ? "on" : "off")}");
        }
    }

    /// <summary>
    /// Gets the action names that <see cref="Run"/> knows.
    /// </summary>
    public static IReadOnlyList<string> Actions { get; } = ["screen-reader", "magnifier", "magnifier-in", "magnifier-out", "screen-curtain"];

    /// <summary>
    /// Gets the default chords for the actions, as GNOME binds them where it has one.
    /// </summary>
    public static IReadOnlyList<(string Chord, string Action)> DefaultBindings { get; } =
    [
        ("Super+Alt+S", "screen-reader"),
        ("Super+Alt+8", "magnifier"),
        ("Super+Alt+equal", "magnifier-in"),
        ("Super+Alt+minus", "magnifier-out"),
        ("Super+Alt+C", "screen-curtain"),
    ];

    /// <summary>
    /// Finds the action of a default chord by the key's place on a US keyboard, for a compositor that binds evdev codes with Super and Alt held.
    /// </summary>
    /// <param name="key">The evdev key code.</param>
    /// <returns>The action name, or <see langword="null"/> when the key has no default action.</returns>
    public static string? DefaultActionForKey(uint key) => key switch
    {
        31 => "screen-reader",
        9 => "magnifier",
        13 => "magnifier-in",
        12 => "magnifier-out",
        46 => "screen-curtain",
        _ => null,
    };

    /// <summary>
    /// Finds the action of a default chord by the keysym of the key without Shift, for a compositor that binds keysyms with Super and Alt held.
    /// </summary>
    /// <param name="keysym">The keysym.</param>
    /// <returns>The action name, or <see langword="null"/> when the keysym has no default action.</returns>
    public static string? DefaultActionForKeysym(uint keysym) => keysym switch
    {
        's' or 'S' => "screen-reader",
        '8' => "magnifier",
        '=' => "magnifier-in",
        '-' => "magnifier-out",
        'c' or 'C' => "screen-curtain",
        _ => null,
    };

    /// <summary>
    /// Gets the screen curtain of the scene. Give it to the cursor controller and to the IPC server.
    /// </summary>
    public SceneCurtain Curtain { get; }

    /// <summary>
    /// Gets the zoom of the scene's outputs.
    /// </summary>
    public OutputZoom Zoom { get; }

    /// <summary>
    /// Gets the screen reader process.
    /// </summary>
    public ScreenReader Reader { get; }

    /// <summary>
    /// Gets the screen reader's manager, or <see langword="null"/> before the gate opens or when the session bus is missing.
    /// </summary>
    public A11yManager? Manager { get; private set; }

    /// <summary>
    /// Gets the compositor's accessible window, or <see langword="null"/> before the gate opens or when AccessKit does not load.
    /// </summary>
    public CompositorAccessibility? Tree { get; private set; }

    /// <summary>
    /// Gets the focus tracker that zoom follows, or <see langword="null"/> before the gate opens or without windows to map through.
    /// </summary>
    public AccessibleFocusTracker? Focus { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the gate opened and the toolkit started.
    /// </summary>
    public bool IsOn { get; private set; }

    /// <summary>
    /// Occurs once the compositor's accessible window exists, before the toolkit starts, so the compositor can add its chrome to it.
    /// </summary>
    public event Action<CompositorAccessibility>? TreeStarted;

    /// <summary>
    /// Applies the <c>[accessibility]</c> settings: the reader command and mode, reduced motion and the keyboard aids. Call it again after each reload.
    /// </summary>
    /// <param name="settings">The settings.</param>
    public void Apply(AccessibilitySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ReducedMotion.IsOn = settings.ReducedMotion;
        if (_options.Reader is null)
        {
            Reader.Argv = settings.ScreenReaderCommand;
            Reader.Mode = settings.ScreenReader;
        }

        if (_options.Aids is { } aids)
        {
            aids.Options = settings.Keyboard;
        }
    }

    /// <summary>
    /// Opens the accessibility gate. When it opens, it starts the manager, the accessible window and the focus tracker, then the toolkit, then the reader in <c>on</c> mode.
    /// </summary>
    /// <param name="mode">The <c>enabled</c> mode.</param>
    /// <returns><see langword="true"/> when accessibility is on.</returns>
    public async Task<bool> StartAsync(AccessibilityMode mode)
    {
        IsOn = await AccessibilityGate.StartAsync(mode, _options.Host, _options.StartToolkit ?? (static () => Task.FromResult<string?>(null)), _options.Log, StartInterfaces);
        if (!_disposed)
        {
            Reader.AccessibilityStarted(IsOn);
        }

        return IsOn;
    }

    /// <summary>
    /// Runs one of <see cref="Actions"/>.
    /// </summary>
    /// <param name="action">The action name.</param>
    /// <returns><see langword="false"/> when the name is not an accessibility action.</returns>
    public bool Run(string action)
    {
        switch (action)
        {
            case "screen-reader":
                ToggleReader();
                return true;
            case "magnifier":
                Zoom.Toggle();
                return true;
            case "magnifier-in":
                Zoom.ZoomIn();
                return true;
            case "magnifier-out":
                Zoom.ZoomOut();
                return true;
            case "screen-curtain":
                Curtain.Set(!Curtain.IsDrawn);
                BasinReport.Line($"CURTAIN {(Curtain.IsDrawn ? "on" : "off")}");
                Announce(Curtain.IsDrawn ? "Screen curtain on" : "Screen curtain off", AnnouncePriority.Assertive);
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc/>
    public void Announce(string text, AnnouncePriority priority = AnnouncePriority.Polite) => Tree?.Announce(text, priority);

    /// <summary>
    /// Stops the reader, the bus interfaces, zoom tracking and the keyboard aids. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_options.Reader is null)
        {
            Reader.Dispose();
        }

        _caret?.Dispose();

        Zoom.Dispose();
        Focus?.Dispose();
        Focus = null;
        Tree?.Dispose();
        Tree = null;
        Manager?.Dispose();
        Manager = null;
        _options.Aids?.Dispose();
    }

    private void ToggleReader()
    {
        if (!Reader.IsAvailable)
        {
            Announce($"No screen reader: {Reader.Unavailable}");
            BasinReport.Line("READERSTATE off available=no");
            return;
        }

        Reader.Toggle();
    }

    private void StartInterfaces()
    {
        if (_disposed)
        {
            return;
        }

        var loop = _options.Host.Loop;
        Manager = A11yManager.TryStart(loop, _options.Seat, _options.Pointer, null, out _);
        BasinReport.Line($"A11Y manager {(Manager is null ? "off" : "on")}");
        Tree = CompositorAccessibility.TryStart(loop, _options.Name, out _);
        if (_options.Toplevels is { } toplevels)
        {
            Focus = AccessibleFocusTracker.TryStart(loop, toplevels, null, out _);
            Zoom.Track(Focus);
        }

        if (Tree is { } tree)
        {
            TreeStarted?.Invoke(tree);
        }
    }
}
