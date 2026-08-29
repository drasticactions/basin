using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class FullscreenShellGlobal : IDisposable
{
    public const int Version = 1;

    public const string RoleName = "zwp_fullscreen_shell_v1";

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly OutputLayout _layout;
    private readonly HashSet<WlClient> _bound = [];
    private readonly Dictionary<IOutput, Presentation> _presented = [];

    public FullscreenShellGlobal(WlServerDisplay display, CompositorGlobal compositor, OutputLayout layout)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(compositor);
        ArgumentNullException.ThrowIfNull(layout);
        _compositor = compositor;
        _layout = layout;
        _global = display.CreateGlobal(ZwpFullscreenShellV1.Interface, Version, OnBind);
    }

    public IReadOnlyCollection<WlClient> BoundClients => _bound;

    public Surface? PresentedSurface { get; private set; }

    public IOutput? PresentedOutput { get; private set; }

    public event Action<Surface?>? PresentedSurfaceChanged;

    public Surface? PresentedOn(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return _presented.TryGetValue(output, out var presentation) ? presentation.Surface : null;
    }

    public void Dispose()
    {
        _presented.Clear();
        _bound.Clear();
        PresentedSurface = null;
        PresentedOutput = null;
        _global.Dispose();
    }

    private sealed class Presentation(Surface surface, ZwpFullscreenShellModeFeedbackV1Resource? feedback)
    {
        public Surface Surface { get; } = surface;

        public ZwpFullscreenShellModeFeedbackV1Resource? Feedback { get; set; } = feedback;
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var shell = new ZwpFullscreenShellV1Resource(client, version, id);
        if (_bound.Add(client))
        {
            client.Destroyed += () => _bound.Remove(client);
        }

        shell.SendCapability(ZwpFullscreenShellV1.Capability.ArbitraryModes);
        shell.PresentSurface += (_, e) => OnPresentSurface(shell, e);
        shell.PresentSurfaceForMode += (_, e) => OnPresentSurfaceForMode(shell, e);
    }

    private void OnPresentSurface(ZwpFullscreenShellV1Resource shell, ZwpFullscreenShellV1Resource.PresentSurfaceEventArgs e)
    {
        if (e.Method > ZwpFullscreenShellV1.PresentMethod.Stretch)
        {
            shell.PostError((uint)ZwpFullscreenShellV1.Error.InvalidMethod, "unknown present method");
            return;
        }

        var output = OutputGlobal.FromResource(e.Output)?.Output ?? FirstOutput;
        if (e.Surface is null)
        {
            Withdraw(output);
            return;
        }

        if (_compositor.ResolveSurface(e.Surface) is not { } surface)
        {
            return;
        }

        if (!surface.TrySetRole(RoleName, this) && surface.RoleObject != this)
        {
            shell.PostError((uint)ZwpFullscreenShellV1.Error.Role, "surface already has a role");
            return;
        }

        if (output is null)
        {
            return;
        }

        Present(output, surface, feedback: null);
    }

    private void OnPresentSurfaceForMode(
        ZwpFullscreenShellV1Resource shell, ZwpFullscreenShellV1Resource.PresentSurfaceForModeEventArgs e)
    {
        var feedback = new ZwpFullscreenShellModeFeedbackV1Resource(shell.Client, shell.Version, e.Feedback);
        var output = OutputGlobal.FromResource(e.Output)?.Output ?? FirstOutput;
        if (_compositor.ResolveSurface(e.Surface) is not { } surface)
        {
            Cancel(feedback);
            return;
        }

        if (!surface.TrySetRole(RoleName, this) && surface.RoleObject != this)
        {
            shell.PostError((uint)ZwpFullscreenShellV1.Error.Role, "surface already has a role");
            return;
        }

        if (output is null)
        {
            Cancel(feedback);
            return;
        }

        Present(output, surface, feedback);
        var current = output.CurrentMode;
        var wanted = surface.Current;
        if (wanted.Width == current.Width && wanted.Height == current.Height)
        {
            Settle(output, feedback, static resource => resource.SendModeSuccessful());
        }
        else
        {
            Settle(output, feedback, static resource => resource.SendModeFailed());
        }
    }

    private IOutput? FirstOutput
    {
        get
        {
            var outputs = _layout.Outputs;
            return outputs.Length > 0 ? outputs[0].Output : null;
        }
    }

    private void Present(IOutput output, Surface surface, ZwpFullscreenShellModeFeedbackV1Resource? feedback)
    {
        if (_presented.TryGetValue(output, out var existing))
        {
            Cancel(existing.Feedback);
            existing.Feedback = null;
            if (ReferenceEquals(existing.Surface, surface))
            {
                existing.Feedback = feedback;
                return;
            }
        }

        _presented[output] = new Presentation(surface, feedback);
        surface.Destroyed += () => Withdraw(output, surface);
        PresentedSurface = surface;
        PresentedOutput = output;
        PresentedSurfaceChanged?.Invoke(surface);
    }

    private void Withdraw(IOutput? output) => Withdraw(output, null);

    private void Withdraw(IOutput? output, Surface? only)
    {
        if (output is null || !_presented.TryGetValue(output, out var presentation))
        {
            return;
        }

        if (only is not null && !ReferenceEquals(only, presentation.Surface))
        {
            return;
        }

        Cancel(presentation.Feedback);
        _presented.Remove(output);
        if (!ReferenceEquals(PresentedSurface, presentation.Surface))
        {
            return;
        }

        PresentedSurface = null;
        PresentedOutput = null;
        foreach (var (candidateOutput, candidate) in _presented)
        {
            PresentedSurface = candidate.Surface;
            PresentedOutput = candidateOutput;
            break;
        }

        PresentedSurfaceChanged?.Invoke(PresentedSurface);
    }

    private void Settle(
        IOutput output,
        ZwpFullscreenShellModeFeedbackV1Resource feedback,
        Action<ZwpFullscreenShellModeFeedbackV1Resource> report)
    {
        if (_presented.TryGetValue(output, out var presentation) && presentation.Feedback == feedback)
        {
            presentation.Feedback = null;
        }

        if (!feedback.IsDestroyed)
        {
            report(feedback);
        }
    }

    private static void Cancel(ZwpFullscreenShellModeFeedbackV1Resource? feedback)
    {
        if (feedback is not null && !feedback.IsDestroyed)
        {
            feedback.SendPresentCancelled();
        }
    }
}
