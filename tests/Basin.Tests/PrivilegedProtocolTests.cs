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
        using var plasmaDesktops = new PlasmaVirtualDesktopManager(host.Display, model: null);
        using var plasmaWindows = new PlasmaWindowManager(host.Display, toplevels: null, workspaces: null);
        using var virtualKeyboard = new VirtualKeyboardManager(host.Display, sink: null);
        using var virtualPointer = new VirtualPointerManager(host.Display, sink: null);
        using var transientSeat = new TransientSeatManager(host.Display);
        using var inputMethod = new InputMethodRelay(host.Display, host.Seat);
        using var textInput = new TextInputManager(host.Display, host.Seat, inputMethod);
        using var sessionLock = new SessionLockManager(host.Display, host.Compositor);
        using var securityContext = new SecurityContextManager(host.Display, host.Loop);
        using var layerShell = new Basin.Shell.Xdg.LayerShell(host.Display, host.Compositor);
        using var xwaylandGrab = new Basin.XWayland.XWaylandKeyboardGrabManager(host.Display, host.Compositor, host.Seat);

        var advertised = new HashSet<string>();
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) => advertised.Add(e.Interface);
        host.PumpToClient();

        var missing = PrivilegedProtocols.All.Where(name => !advertised.Contains(name)).ToList();
        Assert.True(
            missing.Count == 0,
            $"privileged names that no global advertises (a typo denies nothing): {string.Join(", ", missing)}");
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
