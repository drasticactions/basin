using Basin.Hosted;
using Basin.Sdl;

namespace SdlComp.Tests;

internal sealed class SdlSession : IDisposable
{
    public SdlSession(bool host = true)
    {
        Environment.SetEnvironmentVariable("BASIN_MUTE", "1");
        Loop = new SdlLoop(new SdlLoopOptions { AppName = "sdl-comp-tests", VideoDriver = "offscreen" });
        if (!host)
        {
            return;
        }

        try
        {
            Host = new BasinCompositorHost(SdlComp.HostOptions(null));
        }
        catch
        {
            Loop.Dispose();
            throw;
        }

        Loop.Attach(Host);
    }

    public SdlLoop Loop { get; }

    public BasinCompositorHost? Host { get; }

    public bool PumpUntil(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            var remaining = deadline - Environment.TickCount64;
            if (remaining <= 0)
            {
                return false;
            }

            Loop.Pump((int)Math.Min(remaining, 50));
        }

        return true;
    }

    public void Dispose()
    {
        Loop.Detach();
        Host?.Dispose();
        Loop.Dispose();
    }
}
