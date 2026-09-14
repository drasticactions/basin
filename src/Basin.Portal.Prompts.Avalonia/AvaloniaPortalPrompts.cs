using Avalonia.Controls;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.UI.Avalonia;

namespace Basin.Portal.Prompts.Avalonia;

public sealed class AvaloniaPortalPrompts : IPortalPrompts, IDisposable
{
    public const int DialogWidth = 520;
    public const int ConfirmHeight = 200;
    public const int DeviceHeight = 320;
    public const int SourceRowHeight = 52;
    public const int SourceChrome = 190;
    public const int SourceHeight = SourceChrome + (5 * SourceRowHeight);
    public const int ShortcutHeight = 360;

    private static readonly BasinLogger Log = BasinLog.For("portal-prompts");

    private readonly IPortalPromptHost _promptHost;
    private readonly OutputLayout _layout;
    private readonly List<Shown> _shown = [];
    private bool _disposed;

    public AvaloniaPortalPrompts(IPortalPromptHost promptHost, OutputLayout layout, IUIHost? host = null)
    {
        ArgumentNullException.ThrowIfNull(promptHost);
        ArgumentNullException.ThrowIfNull(layout);
        _promptHost = promptHost;
        _layout = layout;
        Host = host;
    }

    public IUIHost? Host { get; set; }

    public int Open => _shown.Count;

    public static int SourceHeightFor(int rows) => SourceChrome + (Math.Clamp(rows, 1, 5) * SourceRowHeight);

    public async ValueTask<PromptOutcome<SourceSelection>> SelectSources(SourcePrompt prompt, CancellationToken cancellation)
    {
        var model = new SourcePromptModel(in prompt);
        var response = await ShowAsync(model, new SourcePromptView(), DialogWidth, SourceHeightFor(model.Rows.Count), prompt.ParentWindow, cancellation).ConfigureAwait(true);
        return response == PromptResponse.Accepted
            ? PromptOutcome<SourceSelection>.Accepted(new SourceSelection(model.Selection, model.Persist ? 2u : 0u))
            : new PromptOutcome<SourceSelection>(response, default);
    }

    public async ValueTask<PromptOutcome<DeviceSelection>> SelectDevices(DevicePrompt prompt, CancellationToken cancellation)
    {
        var model = new DevicePromptModel(in prompt);
        var response = await ShowAsync(model, new DevicePromptView(), DialogWidth, DeviceHeight, prompt.ParentWindow, cancellation).ConfigureAwait(true);
        return response == PromptResponse.Accepted
            ? PromptOutcome<DeviceSelection>.Accepted(new DeviceSelection(model.Devices, model.Clipboard, model.Persist ? 2u : 0u))
            : new PromptOutcome<DeviceSelection>(response, default);
    }

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

    public async ValueTask<PromptOutcome<ShortcutBinding[]>> BindShortcuts(ShortcutPrompt prompt, CancellationToken cancellation)
    {
        var model = new ShortcutPromptModel(in prompt);
        var response = await ShowAsync(model, new ShortcutPromptView(), DialogWidth, ShortcutHeight, prompt.ParentWindow, cancellation).ConfigureAwait(true);
        return response == PromptResponse.Accepted
            ? PromptOutcome<ShortcutBinding[]>.Accepted(model.Bindings.ToArray())
            : new PromptOutcome<ShortcutBinding[]>(response, default);
    }

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
            Log.Warn($"a portal prompt was asked with no output to show it on");
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
            Log.Warn($"a portal prompt was asked before the toolkit host came up");
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
            Log.Warn($"the UI host produced no Avalonia surface for a portal prompt");
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
            Log.Warn($"the prompt host refused a portal prompt: {e.Message}");
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
            Log.Warn($"the prompt host failed to hide a portal prompt: {e.Message}");
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
