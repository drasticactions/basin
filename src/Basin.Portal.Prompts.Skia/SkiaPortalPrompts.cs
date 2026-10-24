using Basin.Capabilities;
using Basin.Diagnostics;
using SkiaSharp;

namespace Basin.Portal.Prompts.Skia;

/// <summary>
/// Implements <see cref="IPortalPrompts"/> with dialogs that Skia draws and that an <see cref="ISkiaPromptHost"/> shows.
/// Use it on the thread that runs the event loop.
/// </summary>
public sealed class SkiaPortalPrompts : IPortalPrompts, IDisposable
{
    private static readonly BasinLogger Log = BasinLog.For("portal-prompts");

    private readonly ISkiaPromptHost _host;
    private readonly OutputLayout _layout;
    private readonly List<Shown> _shown = [];
    private SkiaPromptTheme? _theme;
    private SKTypeface? _typeface;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SkiaPortalPrompts"/> class.
    /// </summary>
    /// <param name="host">The host that shows the views.</param>
    /// <param name="layout">The layout that supplies the outputs and their positions.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public SkiaPortalPrompts(ISkiaPromptHost host, OutputLayout layout)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(layout);
        _host = host;
        _layout = layout;
    }

    /// <summary>
    /// Gets or sets the typeface for the dialogs. A change drops the theme, and the next prompt builds it again. The caller keeps ownership.
    /// </summary>
    public SKTypeface? Typeface
    {
        get => _typeface;
        set
        {
            _typeface = value;
            _theme?.Dispose();
            _theme = null;
        }
    }

    /// <summary>
    /// Gets or sets the keymap that the shortcut prompt uses to turn keycodes into keysyms.
    /// </summary>
    public IKeymapLookup? Keymap { get; set; }

    /// <summary>
    /// Gets the number of prompts that are on the screen now.
    /// </summary>
    public int Open => _shown.Count;

    /// <summary>
    /// Gets the theme for the dialogs. It is made on first use, and this instance owns it.
    /// </summary>
    public SkiaPromptTheme Theme => _theme ??= new SkiaPromptTheme(_typeface);

    internal static string AppLine(string appId, string displayName) =>
        string.IsNullOrEmpty(displayName)
            ? string.IsNullOrEmpty(appId) ? "An unknown application is asking" : $"{appId} is asking"
            : $"{displayName} ({appId}) is asking";

    /// <summary>
    /// Asks the user which outputs or windows to share.
    /// </summary>
    /// <param name="prompt">The request.</param>
    /// <param name="cancellation">A token that closes the dialog with a cancelled response.</param>
    /// <returns>The chosen sources when the user accepts. A persist choice gives persist mode 2.</returns>
    public async ValueTask<PromptOutcome<SourceSelection>> SelectSources(SourcePrompt prompt, CancellationToken cancellation)
    {
        var view = new SourcePromptView(Theme, in prompt);
        var response = await ShowAsync(view, prompt.ParentWindow, cancellation).ConfigureAwait(true);
        return response == PromptResponse.Accepted
            ? PromptOutcome<SourceSelection>.Accepted(new SourceSelection(view.Selection, view.Persist ? 2u : 0u))
            : new PromptOutcome<SourceSelection>(response, default);
    }

    /// <summary>
    /// Asks the user which input devices a remote desktop session can control.
    /// </summary>
    /// <param name="prompt">The request.</param>
    /// <param name="cancellation">A token that closes the dialog with a cancelled response.</param>
    /// <returns>The chosen devices and the clipboard choice when the user accepts.</returns>
    public async ValueTask<PromptOutcome<DeviceSelection>> SelectDevices(DevicePrompt prompt, CancellationToken cancellation)
    {
        var view = new DevicePromptView(Theme, in prompt);
        var response = await ShowAsync(view, prompt.ParentWindow, cancellation).ConfigureAwait(true);
        return response == PromptResponse.Accepted
            ? PromptOutcome<DeviceSelection>.Accepted(new DeviceSelection(view.Devices, view.Clipboard, view.Persist ? 2u : 0u))
            : new PromptOutcome<DeviceSelection>(response, default);
    }

    /// <summary>
    /// Covers one output and asks the user for an area or a pixel.
    /// The output is the one in the request, else the output of the parent window, else the first output.
    /// </summary>
    /// <param name="prompt">The request.</param>
    /// <param name="cancellation">A token that closes the view with a cancelled response.</param>
    /// <returns>The area in layout coordinates when the user accepts, or a cancelled outcome when there is no output.</returns>
    public async ValueTask<PromptOutcome<Box>> SelectArea(AreaPrompt prompt, CancellationToken cancellation)
    {
        var output = prompt.Output ?? _host.OutputFor(prompt.ParentWindow) ?? FirstOutput();
        if (output is null)
        {
            return PromptOutcome<Box>.Cancelled;
        }

        var box = _layout.BoxOf(output);
        var view = new AreaPromptView(Theme, in prompt, box.Width, box.Height);
        var response = await ShowAsync(view, output, cover: true, cancellation).ConfigureAwait(true);
        if (response != PromptResponse.Accepted)
        {
            return new PromptOutcome<Box>(response, default);
        }

        var selection = view.Selection;
        return PromptOutcome<Box>.Accepted(selection.IsEmpty ? default : selection.Translated(box.X, box.Y));
    }

    /// <summary>
    /// Asks the user to bind a trigger to each requested global shortcut.
    /// </summary>
    /// <param name="prompt">The request.</param>
    /// <param name="cancellation">A token that closes the dialog with a cancelled response.</param>
    /// <returns>The bound shortcuts when the user accepts. Rows without a trigger are left out.</returns>
    public async ValueTask<PromptOutcome<ShortcutBinding[]>> BindShortcuts(ShortcutPrompt prompt, CancellationToken cancellation)
    {
        var view = new ShortcutPromptView(Theme, in prompt, Keymap);
        var response = await ShowAsync(view, prompt.ParentWindow, cancellation).ConfigureAwait(true);
        return response == PromptResponse.Accepted
            ? PromptOutcome<ShortcutBinding[]>.Accepted(view.Bindings.ToArray())
            : new PromptOutcome<ShortcutBinding[]>(response, default);
    }

    /// <summary>
    /// Asks a yes or no question.
    /// </summary>
    /// <param name="prompt">The request.</param>
    /// <param name="cancellation">A token that closes the dialog with a cancelled response.</param>
    /// <returns>An accepted outcome with <see langword="true"/> when the user allows, or the response otherwise.</returns>
    public async ValueTask<PromptOutcome<bool>> Confirm(ConfirmPrompt prompt, CancellationToken cancellation)
    {
        var view = new ConfirmPromptView(Theme, in prompt);
        var response = await ShowAsync(view, prompt.ParentWindow, cancellation).ConfigureAwait(true);
        return response == PromptResponse.Accepted
            ? PromptOutcome<bool>.Accepted(true)
            : new PromptOutcome<bool>(response, default);
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

    private IOutput? FirstOutput()
    {
        foreach (var (output, _) in _layout.Outputs)
        {
            return output;
        }

        return null;
    }

    private Task<PromptResponse> ShowAsync(SkiaPromptView view, string parentWindow, CancellationToken cancellation)
    {
        var output = _host.OutputFor(parentWindow) ?? FirstOutput();
        if (output is null)
        {
            Log.Warn($"prompt: no output to show it on, the prompt is cancelled");
            return Task.FromResult(PromptResponse.Cancelled);
        }

        return ShowAsync(view, output, cover: false, cancellation);
    }

    private Task<PromptResponse> ShowAsync(SkiaPromptView view, IOutput output, bool cover, CancellationToken cancellation)
    {
        if (_disposed || cancellation.IsCancellationRequested)
        {
            view.Dispose();
            return Task.FromResult(PromptResponse.Cancelled);
        }

        var completion = new TaskCompletionSource<PromptResponse>();
        var shown = new Shown(view, completion);
        _shown.Add(shown);
        view.Completed += response => Finish(shown, completion, response);
        ISkiaPromptSurface? surface;
        try
        {
            surface = _host.Show(view, output, cover);
        }
        catch (Exception e)
        {
            Log.Warn(e, $"prompt: the prompt host refused it, the prompt is cancelled");
            surface = null;
        }

        if (surface is null)
        {
            Finish(shown, completion, PromptResponse.Cancelled);
            return completion.Task;
        }

        shown.Surface = surface;
        shown.Registration = cancellation.Register(() => Finish(shown, completion, PromptResponse.Cancelled));
        return completion.Task;
    }

    private void Finish(Shown shown, TaskCompletionSource<PromptResponse> completion, PromptResponse response)
    {
        if (!_shown.Remove(shown))
        {
            return;
        }

        shown.Registration.Dispose();
        try
        {
            shown.Surface?.Dispose();
        }
        catch (Exception e)
        {
            Log.Warn(e, $"prompt: the prompt host did not hide it");
        }

        shown.View.Dispose();
        completion.TrySetResult(response);
    }

    /// <summary>
    /// Hides every open prompt, completes each pending prompt with a cancelled response, and disposes the theme.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var shown in _shown.ToArray())
        {
            Finish(shown, shown.Completion, PromptResponse.Cancelled);
        }

        _theme?.Dispose();
        _theme = null;
    }

    private sealed class Shown(SkiaPromptView view, TaskCompletionSource<PromptResponse> completion)
    {
        public SkiaPromptView View { get; } = view;

        public TaskCompletionSource<PromptResponse> Completion { get; } = completion;

        public ISkiaPromptSurface? Surface { get; set; }

        public CancellationTokenRegistration Registration { get; set; }
    }
}
