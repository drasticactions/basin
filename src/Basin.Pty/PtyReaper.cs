using static Basin.Pty.PtyLog;

namespace Basin.Pty;

internal static class PtyReaper
{
    private const int KillAfterMs = 3000;
    private const int PollMs = 20;

    private static readonly List<Orphan> Orphans = [];

    internal static int Pending => Orphans.Count;

    public static void Collect(TimeSpan grace)
    {
        var deadline = Environment.TickCount64 + (long)grace.TotalMilliseconds;
        while (Orphans.Count > 0)
        {
            foreach (var orphan in Orphans.ToList())
            {
                if (Environment.TickCount64 >= deadline && !orphan.Killed)
                {
                    orphan.Killed = true;
                    _ = PtyNative.Kill(-orphan.Pid, PtyNative.SignalKill);
                    _ = PtyNative.Kill(orphan.Pid, PtyNative.SignalKill);
                }

                _ = Poll(orphan);
            }

            if (Orphans.Count > 0)
            {
                Thread.Sleep(5);
            }
        }
    }

    public static void Adopt(ICompositorEventLoop loop, int pid, int pidfd)
    {
        var orphan = new Orphan(pid, pidfd);
        Orphans.Add(orphan);
        if (pidfd >= 0)
        {
            orphan.Watch = loop.AddFd(pidfd, FdReadiness.Readable, (_, _) => Poll(orphan));
        }

        orphan.Timer = loop.AddTimer(() => OnTimer(orphan));
        orphan.Timer.UpdateTimer(pidfd >= 0 ? KillAfterMs : PollMs);
    }

    private static void OnTimer(Orphan orphan)
    {
        if (Poll(orphan))
        {
            return;
        }

        if (Environment.TickCount64 - orphan.Since >= KillAfterMs && !orphan.Killed)
        {
            orphan.Killed = true;
            Log.Debug($"reaper: pid {orphan.Pid} ignored the hangup, its group is killed");
            _ = PtyNative.Kill(-orphan.Pid, PtyNative.SignalKill);
            _ = PtyNative.Kill(orphan.Pid, PtyNative.SignalKill);
        }

        orphan.Timer?.UpdateTimer(PollMs);
    }

    private static bool Poll(Orphan orphan)
    {
        try
        {
            if (!PtyNative.TryReap(orphan.Pid, out _))
            {
                return false;
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, $"reaper: reaping pid {orphan.Pid} failed");
        }

        orphan.Watch?.Remove();
        orphan.Timer?.Remove();
        if (orphan.Pidfd >= 0)
        {
            _ = PtyNative.Close(orphan.Pidfd);
        }

        Orphans.Remove(orphan);
        return true;
    }

    private sealed class Orphan(int pid, int pidfd)
    {
        public int Pid { get; } = pid;

        public int Pidfd { get; } = pidfd;

        public long Since { get; } = Environment.TickCount64;

        public bool Killed { get; set; }

        public IEventSource? Watch { get; set; }

        public IEventSource? Timer { get; set; }
    }
}
