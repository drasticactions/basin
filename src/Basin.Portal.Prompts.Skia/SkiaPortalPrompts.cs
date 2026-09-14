using Basin.Capabilities;
using Basin.Diagnostics;
using SkiaSharp;

namespace Basin.Portal.Prompts.Skia;

public sealed class SkiaPortalPrompts : IPortalPrompts, IDisposable
{
    private static readonly BasinLogger Log = BasinLog.For("portal-prompts");

    private readonly ISkiaPromptHost _host;
    private readonly OutputLayout _layout;
    private readonly List<Shown> _shown = [];
    private SkiaPromptTheme? _theme;
    private SKTypeface? _typeface;
    private bool _disposed;

    public SkiaPortalPrompts(ISkiaPromptHost host, OutputLayout layout)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(layout);
        _host = host;
        _layout = layout;
    }

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

    public IKeymapLookup? Keymap { get; set; }

    public int Open => _shown.Count;

    public SkiaPromptTheme Theme => _theme ??= new SkiaPromptTheme(_typeface);

    internal static string AppLine(string appId, string displayName) =>
        string.IsNullOrEmpty(displayName)
            ? string.IsNullOrEmpty(appId) ? "An unknown application is asking" : $"{appId} is asking"
            : $"{displayName} ({appId}) is asking";

    public async ValueTask<PromptOutcome<SourceSelection>> SelectSources(SourcePrompt prompt, CancellationToken cancellation)
    {
        var view = new SourcePromptView(Theme, in prompt);
        var response = await ShowAsync(view, prompt.ParentWindow, cancellation).ConfigureAwait(true);
        return response == PromptResponse.Accepted
            ? PromptOutcome<SourceSelection>.Accepted(new SourceSelection(view.Selection, view.Persist ? 2u : 0u))
            : new PromptOutcome<SourceSelection>(response, default);
    }

    public async ValueTask<PromptOutcome<DeviceSelection>> SelectDevices(DevicePrompt prompt, CancellationToken cancellation)
    {
        var view = new DevicePromptView(Theme, in prompt);
        var response = await ShowAsync(view, prompt.ParentWindow, cancellation).ConfigureAwait(true);
        return response == PromptResponse.Accepted
            ? PromptOutcome<DeviceSelection>.Accepted(new DeviceSelection(view.Devices, view.Clipboard, view.Persist ? 2u : 0u))
            : new PromptOutcome<DeviceSelection>(response, default);
    }

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

    public async ValueTask<PromptOutcome<ShortcutBinding[]>> BindShortcuts(ShortcutPrompt prompt, CancellationToken cancellation)
    {
        var view = new ShortcutPromptView(Theme, in prompt, Keymap);
        var response = await ShowAsync(view, prompt.ParentWindow, cancellation).ConfigureAwait(true);
        return response == PromptResponse.Accepted
            ? PromptOutcome<ShortcutBinding[]>.Accepted(view.Bindings.ToArray())
            : new PromptOutcome<ShortcutBinding[]>(response, default);
    }

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
            Log.Warn($"a portal prompt was asked with no output to show it on");
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
        var shown = new Shown(view);
        _shown.Add(shown);
        view.Completed += response => Finish(shown, completion, response);
        ISkiaPromptSurface? surface;
        try
        {
            surface = _host.Show(view, output, cover);
        }
        catch (Exception e)
        {
            Log.Warn($"the prompt host refused a portal prompt: {e.Message}");
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
            Log.Warn($"the prompt host failed to hide a portal prompt: {e.Message}");
        }

        shown.View.Dispose();
        completion.TrySetResult(response);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var shown in _shown.ToArray())
        {
            shown.View.PointerLeave();
            _shown.Remove(shown);
            shown.Registration.Dispose();
            try
            {
                shown.Surface?.Dispose();
            }
            catch (Exception e)
            {
                Log.Warn($"the prompt host failed to hide a portal prompt: {e.Message}");
            }

            shown.View.Dispose();
        }

        _theme?.Dispose();
        _theme = null;
    }

    private sealed class Shown(SkiaPromptView view)
    {
        public SkiaPromptView View { get; } = view;

        public ISkiaPromptSurface? Surface { get; set; }

        public CancellationTokenRegistration Registration { get; set; }
    }
}
