using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Ipc;
using Basin.Scene;

namespace Basin.Tests;

internal static class IpcFullRig
{
    public static IpcTestRig Create(
        string? path = null, bool listen = false, Action<IpcServer>? register = null, TestSelectionStore? selection = null,
        Action<BasinServices>? services = null)
    {
        selection ??= new TestSelectionStore();
        var extra = services;
        var rig = new IpcTestRig(
            (services, host) =>
            {
                extra?.Invoke(services);
                services.UseDefault<IX11DensityControl>(new NextStartDensityControl());
                services.Use(host.Layout);
                services.Use<IToplevelModel>(new TestToplevelModel());
                services.Use<IToplevelStack>(new TestToplevelStack());
                services.Use<IWorkspaceModel>(new TestWorkspaceModel());
                services.Use<IOutputConfiguration>(new LayoutOutputConfiguration(host.Layout));
                services.Use<IOutputPower>(new TestOutputPower());
                services.Use<IIdleSource>(new IpcReadEventTests.TestIdle());
                services.Use<ILockState>(new Lock());
                services.Use<IActiveKeymap>(new Keymap());
                services.Use<IKeymapLookup>(new IpcInputTests.TableLookup());
                services.Use<IInputSink>(new RecordingInputSink());
                services.Use<IScreenCapture>(new SceneScreenCapture(host.Scene, host.Layout) { Renderer = host.Renderer });
                services.Use<ISelectionStore>(selection);
                var devices = new FakeInputDeviceConfiguration();
                devices.AddTouchpad();
                services.Use<IInputDeviceConfiguration>(devices);
            },
            new IpcSessionInfo { Compositor = "full", Quit = () => { }, Settle = new ManualSettle() },
            path,
            listen);
        _ = rig.Own(selection);
        rig.Server.SyntheticInput = new IpcInputTests.RecordingSynthetic();
        rig.Server.Approvals = new IpcApprovalBroker();
        register?.Invoke(rig.Server);
        return rig;
    }

    private sealed class NextStartDensityControl : IX11DensityControl
    {
        public bool TrySet(ulong toplevelId, X11Density density, Action<X11DensityResult> completed, out string? error)
        {
            error = null;
            completed(new X11DensityResult(X11DensityOutcome.AppliesNextStart, "exec=test x11=native"));
            return true;
        }
    }

    private sealed class Lock : ILockState
    {
        public bool IsLocked => false;

        public void AddObserver(ILockStateObserver observer)
        {
        }

        public void RemoveObserver(ILockStateObserver observer)
        {
        }
    }

    private sealed class Keymap : IActiveKeymap
    {
        public (int Fd, uint Size)? KeymapBuffer => null;

        public event Action? KeymapChanged
        {
            add { }
            remove { }
        }
    }
}
