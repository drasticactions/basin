using Basin.Diagnostics;
using Basin.Hosted;
using Basin.Sdl;
using Xunit;

namespace SdlComp.Tests;

internal sealed class FakeLeaseRig : IDisposable
{
    public FakeLeaseRig()
    {
        Environment.SetEnvironmentVariable("BASIN_MUTE", "1");
        BasinCounters.Reset();
        Loop = new SdlLoop(new SdlLoopOptions { AppName = "sdl-comp-tests", VideoDriver = "offscreen" });
        Host = new BasinCompositorHost(global::SdlComp.SdlComp.HostOptions(null, colorManagement: true));
        Loop.Attach(Host);
        Frames = new SdlFrameLoop(Loop, Host);
        Window = new SdlOutputWindow(Loop, Host, Device, new SdlOutputWindowOptions { Width = 160, Height = 120 });
        Frames.Add(Window);
        Frames.FrameInterval = TimeSpan.Zero;
        Window.Window.Show();
    }

    public FakeSdlDevice Device { get; } = new();

    public SdlLoop Loop { get; }

    public BasinCompositorHost Host { get; }

    public SdlFrameLoop Frames { get; }

    public SdlOutputWindow Window { get; }

    public FakeSdlLease Lease => Device.Lease!;

    public void Tick()
    {
        Loop.MarkActivity();
        Frames.RunOnce(0);
    }

    public void Dispose()
    {
        Frames.Dispose();
        Loop.Detach();
        Host.Dispose();
        Window.Dispose();
        Device.Dispose();
        Loop.Dispose();
        if (BasinCounters.Enabled)
        {
            Assert.Equal(0, BasinCounters.LiveObjects);
            Assert.Equal(0, BasinCounters.PendingFrees);
        }
    }
}
