using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Desktop;
using Xunit;

namespace Basin.Tests;

public sealed class PrivilegedProtocolTests
{
    [Fact]
    public void Every_privileged_name_is_advertised_by_the_manager_that_owns_it()
    {
        using var host = new CompositorTestHost();

        using var screencopy = new ScreencopyManager(host.Display, new OutputLayout(), host.Buffers, capture: null);
        using var imageCopy = new ImageCopyCaptureManager(host.Display, host.Buffers, capture: null);
        using var captureSources = new ImageCaptureSourceManager(host.Display);
        using var exportDmabuf = new ExportDmabufManager(host.Display, capture: null);
        using var dataControl = new DataControlManager(host.Display, new Basin.Seat.SeatSelectionStore(host.Seat));
        using var extDataControl = new ExtDataControlManager(host.Display, new Basin.Seat.SeatSelectionStore(host.Seat));
        var outputManagementLayout = new OutputLayout();
        using var outputManagement = new OutputManagementManager(
            host.Display, outputManagementLayout, new LayoutOutputSet(outputManagementLayout), configuration: null);
        using var outputPower = new OutputPowerManager(host.Display, power: null);
        using var gamma = new GammaControlManager(host.Display, gamma: null);
        using var lease = new DrmLeaseManager(host.Display, device: null);
        using var foreignToplevels = new ForeignToplevelManager(host.Display, new TestToplevelModel());
        using var toplevelList = new ForeignToplevelListManager(host.Display, new TestToplevelModel());
        using var workspaces = new WorkspaceManager(host.Display);
        using var virtualKeyboard = new VirtualKeyboardManager(host.Display, sink: null);
        using var virtualPointer = new VirtualPointerManager(host.Display, sink: null);
        using var transientSeat = new TransientSeatManager(host.Display);
        using var inputMethod = new InputMethodRelay(host.Display, host.Seat);
        using var textInput = new TextInputManager(host.Display, host.Seat, inputMethod);
        using var sessionLock = new SessionLockManager(host.Display, host.Compositor);
        using var securityContext = new SecurityContextManager(host.Display, host.Loop);
        using var layerShell = new Basin.Shell.Xdg.LayerShell(host.Display, host.Compositor);
        using var xwaylandGrab = new Basin.XWayland.XWaylandKeyboardGrabManager(host.Display, host.Compositor, host.Seat);
        using var ctm = new Basin.Hypr.HyprlandCtmControlManager(host.Display, host.Layout, new NoCtm());
        using var toplevelExport = new Basin.Hypr.HyprlandToplevelExportManager(
            host.Display, host.Layout, host.Buffers, capture: null, toplevels: null);
        using var inputCapture = new Basin.Hypr.InputCapture.HyprlandInputCaptureManager(
            host.Display, host.Loop, host.Layout, host.Seat);

        var advertised = new HashSet<string>();
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) => advertised.Add(e.Interface);
        host.PumpToClient();

        var missing = PrivilegedProtocols.All.Where(name => !advertised.Contains(name)).ToList();
        Assert.True(
            missing.Count == 0,
            $"privileged names that no global advertises (a typo denies nothing): {string.Join(", ", missing)}");
    }

    private sealed class NoCtm : ICtmControl
    {
        public bool SupportsCtm(IOutput output) => false;

        public bool SetCtm(IOutput output, ReadOnlySpan<double> rowMajor3x3) => false;

        public bool ResetCtm(IOutput output) => false;
    }

    [Fact]
    public void The_ordinary_protocols_are_not_privileged()
    {
        Assert.False(PrivilegedProtocols.Contains("wl_compositor"));
        Assert.False(PrivilegedProtocols.Contains("wl_shm"));
        Assert.False(PrivilegedProtocols.Contains("xdg_wm_base"));
        Assert.False(PrivilegedProtocols.Contains("wl_seat"));
        Assert.False(PrivilegedProtocols.Contains("zwp_linux_dmabuf_v1"));
    }
}
