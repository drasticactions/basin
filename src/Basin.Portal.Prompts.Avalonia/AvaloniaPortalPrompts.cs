using Avalonia.Controls;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.UI.Avalonia;

namespace Basin.Portal.Prompts.Avalonia;

/// <summary>
/// Implements <see cref="IPortalPrompts"/> with Avalonia dialogs on surfaces of an <see cref="IUIHost"/>, which an <see cref="IPortalPromptHost"/> shows.
/// Use it on the thread that runs the event loop.
/// </summary>
public sealed class AvaloniaPortalPrompts : IPortalPrompts, IDisposable
{
    /// <summary>
    /// The width of a dialog in logical pixels.
    /// </summary>
    public const int DialogWidth = 520;
    /// <summary>
    /// The height of the confirm dialog in logical pixels.
    /// </summary>
    public const int ConfirmHeight = 200;
    /// <summary>
    /// The height of the device dialog in logical pixels.
    /// </summary>
    public const int DeviceHeight = 320;
    /// <summary>
    /// The height of one row in the source dialog, in logical pixels.
    /// </summary>
    public const int SourceRowHeight = 52;
    /// <summary>
    /// The height of the source dialog without its rows, in logical pixels.
    /// </summary>
    public const int SourceChrome = 190;
    /// <summary>
    /// The height of the source dialog with 5 rows, the most that it shows without a scroll.
    /// </summary>
    public const int SourceHeight = SourceChrome + (5 * SourceRowHeight);
    /// <summary>
    /// The height of the shortcut dialog in logical pixels.
    /// </summary>
    public const int ShortcutHeight = 360;

    private static readonly BasinLogger Log = BasinLog.For("portal-prompts");

    private readonly IPortalPromptHost _promptHost;
    private readonly OutputLayout _layout;
    private readonly List<Shown> _shown = [];
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AvaloniaPortalPrompts"/> class.
    /// </summary>
    /// <param name="promptHost">The host that places the surfaces on the screen.</param>
    /// <param name="layout">The layout that supplies the outputs and their positions.</param>
    /// <param name="host">The toolkit host that makes the surfaces, or <see langword="null"/> to set it later.</param>
    /// <exception cref="ArgumentNullException"><paramref name="promptHost"/> or <paramref name="layout"/> is <see langword="null"/>.</exception>
    public AvaloniaPortalPrompts(IPortalPromptHost promptHost, OutputLayout layout, IUIHost? host = null)
    {
        ArgumentNullException.ThrowIfNull(promptHost);
        ArgumentNullException.ThrowIfNull(layout);
        _promptHost = promptHost;
        _layout = layout;
        Host = host;
    }

    /// <summary>
    /// Gets or sets the toolkit host that makes the surfaces. A prompt that arrives while it is <see langword="null"/> is cancelled.
    /// The host must make <see cref="AvaloniaUISurface"/> surfaces.
    /// </summary>
    public IUIHost? Host { get; set; }

    /// <summary>
    /// Gets the number of prompts that are on the screen now.
    /// </summary>
    public int Open => _shown.Count;

    /// <summary>
    /// Gets the height of the source dialog for a number of rows. The rows count as 1 to 5.
    /// </summary>
    /// <param name="rows">The number of source rows.</param>
    /// <returns>The height in logical pixels.</returns>
    public static int SourceHeightFor(int rows) => SourceChrome + (Math.Clamp(rows, 1, 5) * SourceRowHeight);

    /// <summary>
    /// Asks the user which outputs or windows to share.
    /// </summary>
    /// <param name="prompt">The request.</param>
    /// <param name="cancellation">A token that closes the dialog with a cancelled response.</param>
    /// <returns>The chosen sources when the user accepts. A persist choice gives persist mode 2.</returns>
    public async ValueTask<PromptOutcome<SourceSelection>> SelectSources(SourcePrompt prompt, CancellationToken cancellation)
    {
        var model = new SourcePromptModel(in prompt);
        var response = await ShowAsync(model, new SourcePromptView(), DialogWidth, SourceHeightFor(model.Rows.Count), prompt.ParentWindow, cancellation).ConfigureAwait(true);
        return response == PromptResponse.Accepted
            ? PromptOutcome<SourceSelection>.Accepted(new SourceSelection(model.Selection, model.Persist ? 2u : 0u))
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
        var model = new DevicePromptModel(in prompt);
        var response = await ShowAsync(model, new DevicePromptView(), DialogWidth, DeviceHeight, prompt.ParentWindow, cancellation).ConfigureAwait(true);
        return response == PromptResponse.Accepted
            ? PromptOutcome<DeviceSelection>.Accepted(new DeviceSelection(model.Devices, model.Clipboard, model.Persist ? 2u : 0u))
            : new PromptOutcome<DeviceSelection>(response, default);
    }

    /// <summary>
    /// Covers one output and asks the user for an area or a pixel.
    /// The output is the one in the request, else the output of the parent window, else the first output.
    /// </summary>
    /// <param name="prompt">The request.</param>
    /// <param name="cancellation">A token that closes the dialog with a cancelled response.</param>
    /// <returns>The area in layout coordinates when the user accepts, or a cancelled outcome when there is no output.</returns>
    public async ValueTask<PromptOutcome<Box>> SelectArea(AreaPrompt prompt, CancellationToken cancellation)
    {
        var output = prompt.Output ?? _promptHost.OutputFor(prompt.ParentWindow) ?? FirstOutput();
        if (output is null)
        {
            return PromptOutcome<Box>.Cancelled;
        }

        var box = _layout.BoxOf(output);
        var model = new AreaPromptModel(in prompt, box.Width, box.Height);
        var response = await ShowAsync(model, new AreaPromptView(), box.Width, box.Height, output, cancellation).ConfigureAwait(true);
        if (response != PromptResponse.Accepted)
        {
            return new PromptOutcome<Box>(response, default);
        }

        var selection = model.Selection;
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
        var model = new ShortcutPromptModel(in prompt);
        var response = await ShowAsync(model, new ShortcutPromptView(), DialogWidth, ShortcutHeight, prompt.ParentWindow, cancellation).ConfigureAwait(true);
        return response == PromptResponse.Accepted
            ? PromptOutcome<ShortcutBinding[]>.Accepted(model.Bindings.ToArray())
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
        var model = new ConfirmPromptModel(in prompt);
        var response = await ShowAsync(model, new ConfirmPromptView(), DialogWidth, ConfirmHeight, prompt.ParentWindow, cancellation).ConfigureAwait(true);
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

    /// <summary>
    /// Hides every open prompt and completes each pending prompt with a cancelled response.
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
            Finish(shown, PromptResponse.Cancelled);
        }
    }

    private IOutput? FirstOutput()
    {
        foreach (var (output, _) in _layout.Outputs)
        {
            return output;
        }

        return null;
    }

    private Task<PromptResponse> ShowAsync(PromptModel model, Control view, int width, int height, string parentWindow, CancellationToken cancellation)
    {
        var output = _promptHost.OutputFor(parentWindow) ?? FirstOutput();
        if (output is null)
        {
            Log.Warn($"prompt: no output to show it on, the prompt is cancelled");
            model.Dispose();
            return Task.FromResult(PromptResponse.Cancelled);
        }

        return ShowAsync(model, view, width, height, output, cancellation);
    }

    private Task<PromptResponse> ShowAsync(PromptModel model, Control view, int width, int height, IOutput output, CancellationToken cancellation)
    {
        if (_disposed || cancellation.IsCancellationRequested)
        {
            model.Dispose();
            return Task.FromResult(PromptResponse.Cancelled);
        }

        if (Host is not { } host)
        {
            Log.Warn($"prompt: asked before the toolkit host came up, the prompt is cancelled");
            model.Dispose();
            return Task.FromResult(PromptResponse.Cancelled);
        }

        var created = host.CreateSurface(new UISurfaceOptions
        {
            Target = host.Produces,
            Width = width,
            Height = height,
            Scale = output.Scale,
        });
        if (created is not AvaloniaUISurface surface)
        {
            created?.Dispose();
            Log.Warn($"prompt: the UI host made no Avalonia surface, the prompt is cancelled");
            model.Dispose();
            return Task.FromResult(PromptResponse.Cancelled);
        }

        view.DataContext = model;
        surface.Content = view;
        var shown = new Shown(surface, model, new TaskCompletionSource<PromptResponse>());
        _shown.Add(shown);
        model.Completed += response => Finish(shown, response);
        shown.Registration = cancellation.Register(() => Finish(shown, PromptResponse.Cancelled));
        try
        {
            _promptHost.Show(surface, output);
        }
        catch (Exception e)
        {
            Log.Warn(e, $"prompt: the prompt host refused it, the prompt is cancelled");
            Finish(shown, PromptResponse.Cancelled);
        }

        return shown.Completion.Task;
    }

    private void Finish(Shown shown, PromptResponse response)
    {
        if (!_shown.Remove(shown))
        {
            return;
        }

        shown.Registration.Dispose();
        try
        {
            _promptHost.Hide(shown.Surface);
        }
        catch (Exception e)
        {
            Log.Warn(e, $"prompt: the prompt host did not hide it");
        }

        shown.Surface.Content = null;
        shown.Surface.Dispose();
        shown.Model.Dispose();
        shown.Completion.TrySetResult(response);
    }

    private sealed class Shown(AvaloniaUISurface surface, PromptModel model, TaskCompletionSource<PromptResponse> completion)
    {
        public AvaloniaUISurface Surface { get; } = surface;

        public PromptModel Model { get; } = model;

        public TaskCompletionSource<PromptResponse> Completion { get; } = completion;

        public CancellationTokenRegistration Registration { get; set; }
    }
}
