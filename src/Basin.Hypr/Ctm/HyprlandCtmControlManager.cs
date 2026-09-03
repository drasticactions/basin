using Basin.Capabilities;
using Basin.Hypr.Protocol;
using Wayland.Server;

namespace Basin.Hypr;

public sealed class HyprlandCtmControlManager : IDisposable
{
    public const int Version = 2;

    private readonly WlGlobal _global;
    private readonly OutputLayout _layout;
    private readonly ICtmControl _ctm;
    private readonly Dictionary<IOutput, double[]> _matrices = [];
    private HyprlandCtmControlManagerV1Resource? _owner;

    public HyprlandCtmControlManager(WlServerDisplay display, OutputLayout layout, ICtmControl ctm)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(ctm);
        _layout = layout;
        _ctm = ctm;
        _global = display.CreateGlobal(HyprlandCtmControlManagerV1.Interface, Version, OnBind);
    }

    public bool HasOwner => _owner is { IsDestroyed: false };

    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new HyprlandCtmControlManagerV1Resource(client, version, id);
        if (HasOwner)
        {
            if (manager.Version >= 2)
            {
                manager.SendBlocked();
            }

            return;
        }

        _owner = manager;
        _matrices.Clear();
        manager.SetCtmForOutput += (_, e) =>
        {
            if (OutputGlobal.FromResource(e.Output)?.Output is not { } output)
            {
                return;
            }

            var matrix = new[]
            {
                e.Mat0.ToDouble(), e.Mat1.ToDouble(), e.Mat2.ToDouble(),
                e.Mat3.ToDouble(), e.Mat4.ToDouble(), e.Mat5.ToDouble(),
                e.Mat6.ToDouble(), e.Mat7.ToDouble(), e.Mat8.ToDouble(),
            };
            foreach (var component in matrix)
            {
                if (!double.IsFinite(component) || component < 0.0)
                {
                    manager.PostError((uint)HyprlandCtmControlManagerV1.Error.InvalidMatrix, "a matrix component was invalid");
                    return;
                }
            }

            _matrices[output] = matrix;
        };
        manager.Commit += (_, _) =>
        {
            foreach (var (output, _) in _layout.Outputs)
            {
                if (_matrices.TryGetValue(output, out var matrix))
                {
                    _ctm.SetCtm(output, matrix);
                }
                else
                {
                    _ctm.ResetCtm(output);
                }
            }
        };
        manager.Destroyed += (_, _) =>
        {
            if (!ReferenceEquals(_owner, manager))
            {
                return;
            }

            _owner = null;
            _matrices.Clear();
            foreach (var (output, _) in _layout.Outputs)
            {
                _ctm.ResetCtm(output);
            }
        };
    }
}
