using System.Runtime.InteropServices;
using System.Text;
using Basin.Backend.Drm;
using Basin.Desktop;
using Basin.Desktop.Protocol;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class SyncobjSequenceTests : IDisposable
{
    private const int Steps = 150;
    private const int PoolSize = 6;
    private const int FinalWidth = 40;

    [DllImport("libc", SetLastError = true)]
    private static extern int open(string path, int flags);

    [DllImport("libc")]
    private static extern int close(int fd);

    private readonly int _drmFd;

    public SyncobjSequenceTests()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "explicit sync is DRM syncobjs, which are a Linux object");
        _drmFd = open(CompositorTestHost.RenderNodePath, 2);
    }

    public void Dispose()
    {
        if (_drmFd >= 0)
        {
            close(_drmFd);
        }
    }

    public static TheoryData<int> Seeds()
    {
        var data = new TheoryData<int>();
        for (var seed = 1; seed <= 40; seed++)
        {
            data.Add(seed);
        }

        return data;
    }

    private sealed record CommitRecord(int Index, int Buffer, int Width, DrmSyncobjTimeline Release, int Step)
    {
        public bool Signaled { get; set; }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Random_commit_sequences_release_every_point_and_never_one_on_screen(int seed)
    {
        Assert.SkipWhen(_drmFd < 0, "no render node available");
        var probe = DrmSyncobjTimeline.TryCreate(_drmFd);
        Assert.SkipWhen(probe is null, $"{CompositorTestHost.RenderNodePath} has no syncobj support");
        probe!.Release();

        var random = new Random(seed);
        var mayDestroySyncObject = seed % 2 == 0;
        var log = new StringBuilder();
        var timelines = new List<DrmSyncobjTimeline>();
        var timelineProxies = new List<WpLinuxDrmSyncobjTimelineV1>();

        using var host = new CompositorTestHost();
        using var manager = new LinuxDrmSyncobjManager(host.Display, host.Compositor, new DrmSyncDevice(_drmFd));
        WpLinuxDrmSyncobjManagerV1? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "wp_linux_drm_syncobj_manager_v1")
            {
                proxy = registry.Bind<WpLinuxDrmSyncobjManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);

        (DrmSyncobjTimeline Timeline, WpLinuxDrmSyncobjTimelineV1 Proxy) NewTimeline()
        {
            var timeline = DrmSyncobjTimeline.Create(_drmFd);
            timelines.Add(timeline);
            var fd = timeline.ExportFd();
            var imported = proxy!.ImportTimeline(fd);
            close(fd);
            timelineProxies.Add(imported);
            return (timeline, imported);
        }

        var parent = host.Client.Compositor.CreateSurface();
        var parentBuffer = host.Client.CreateBuffer(80, 80, Fill.Solid(80, 80, 0xFF202020));
        parent.Attach(parentBuffer.Proxy, 0, 0);
        parent.Commit();
        var child = host.Client.Compositor.CreateSurface();
        var subsurface = host.Client.Subcompositor.GetSubsurface(child, parent);
        var synchronized = true;
        WpLinuxDrmSyncobjSurfaceV1? syncSurface = proxy!.GetSurface(child);
        var acquire = NewTimeline();
        host.PumpToServer();
        var server = host.Compositor.Surfaces.Single(s => s.SubsurfaceRole is not null);

        var pool = new ClientShmBuffer[PoolSize];
        var releasedSinceAttach = new bool[PoolSize];
        var attached = new bool[PoolSize];
        for (var i = 0; i < PoolSize; i++)
        {
            var index = i;
            pool[i] = host.Client.CreateBuffer(10 + i, 10, Fill.Solid(10 + i, 10, 0xFF000000u | (uint)(i * 0x203040)));
            pool[i].Proxy.Release += (_, _) => releasedSinceAttach[index] = true;
        }

        var commits = new List<CommitRecord>();
        ulong nextAcquire = 1;

        void CommitBuffer(int buffer, int width, WlBuffer wire, int step)
        {
            if (syncSurface is not null)
            {
                if (random.Next(4) != 0)
                {
                    acquire.Timeline.Signal(nextAcquire);
                }

                var release = NewTimeline();
                syncSurface.SetAcquirePoint(acquire.Proxy, 0, (uint)nextAcquire);
                syncSurface.SetReleasePoint(release.Proxy, 0, 1);
                commits.Add(new CommitRecord(commits.Count, buffer, width, release.Timeline, step));
                log.Append(CultureInfo(step)).Append(" commit buffer ").Append(buffer).Append(" acquire ").Append(nextAcquire)
                    .Append(acquire.Timeline.IsSignaled(nextAcquire) ? " signaled" : " unsignaled").Append(" as commit ").Append(commits.Count - 1).Append('\n');
                nextAcquire++;
            }
            else
            {
                log.Append(CultureInfo(step)).Append(" commit buffer ").Append(buffer).Append(" without points\n");
            }

            if (buffer >= 0)
            {
                releasedSinceAttach[buffer] = false;
                attached[buffer] = true;
            }

            child.Attach(wire, 0, 0);
            child.Damage(0, 0, width, 10);
            child.Commit();
        }

        void Check(int step)
        {
            var current = server.Current.Buffer?.Width ?? -1;
            foreach (var commit in commits)
            {
                if (commit.Signaled || !commit.Release.IsSignaled(1))
                {
                    continue;
                }

                commit.Signaled = true;
                var recommitted = commits.Any(later => later.Index > commit.Index && later.Buffer == commit.Buffer);
                Assert.False(
                    current == commit.Width && !recommitted,
                    $"seed {seed} step {step}: commit {commit.Index} of buffer {commit.Buffer} signaled while that buffer is on screen\n{log}");
            }
        }

        for (var step = 0; step < Steps; step++)
        {
            var roll = random.Next(100);
            if (roll < 35)
            {
                var buffer = random.Next(PoolSize);
                CommitBuffer(buffer, 10 + buffer, pool[buffer].Proxy, step);
            }
            else if (roll < 42)
            {
                log.Append(CultureInfo(step)).Append(" commit null\n");
                child.Attach(null, 0, 0);
                child.Commit();
            }
            else if (roll < 47)
            {
                log.Append(CultureInfo(step)).Append(" commit damage only\n");
                child.Damage(0, 0, 1, 1);
                child.Commit();
            }
            else if (roll < 67)
            {
                log.Append(CultureInfo(step)).Append(" parent commit\n");
                parent.Commit();
            }
            else if (roll < 75)
            {
                synchronized = !synchronized;
                log.Append(CultureInfo(step)).Append(synchronized ? " set_sync\n" : " set_desync\n");
                if (synchronized)
                {
                    subsurface.SetSync();
                }
                else
                {
                    subsurface.SetDesync();
                }
            }
            else if (roll < 85)
            {
                log.Append(CultureInfo(step)).Append(" signal acquires to ").Append(nextAcquire - 1).Append('\n');
                if (nextAcquire > 1)
                {
                    acquire.Timeline.Signal(nextAcquire - 1);
                }
            }
            else if (roll < 92 && mayDestroySyncObject)
            {
                if (syncSurface is not null)
                {
                    log.Append(CultureInfo(step)).Append(" destroy sync object\n");
                    syncSurface.Dispose();
                    syncSurface = null;
                }
                else
                {
                    log.Append(CultureInfo(step)).Append(" create sync object\n");
                    syncSurface = proxy.GetSurface(child);
                }
            }
            else
            {
                log.Append(CultureInfo(step)).Append(" dispatch\n");
                host.Loop.Dispatch(1);
            }

            host.PumpToServer();
            host.Loop.Dispatch(0);
            Check(step);
        }

        log.Append("end: signal acquires, set_desync, final buffer\n");
        if (nextAcquire > 1)
        {
            acquire.Timeline.Signal(nextAcquire - 1);
        }

        subsurface.SetDesync();
        parent.Commit();
        host.PumpToServer();
        Pump(host, () => !server.HasParkedCommits);
        Check(Steps);
        var final = host.Client.CreateBuffer(FinalWidth, 10, Fill.Solid(FinalWidth, 10, 0xFFFFFFFF));
        var commitsBeforeFinal = commits.Count;
        CommitBuffer(-1, FinalWidth, final.Proxy, Steps);
        acquire.Timeline.Signal(nextAcquire);
        parent.Commit();
        host.PumpToServer();
        Pump(host, () => !server.HasParkedCommits);
        host.PumpToClient();
        Check(Steps + 1);

        for (var i = 0; i < commitsBeforeFinal; i++)
        {
            Assert.True(commits[i].Release.IsSignaled(1), $"seed {seed}: commit {i} of buffer {commits[i].Buffer} never signaled\n{log}");
        }

        for (var i = 0; i < PoolSize; i++)
        {
            Assert.True(!attached[i] || releasedSinceAttach[i], $"seed {seed}: buffer {i} never got wl_buffer.release after its last attach\n{log}");
        }

        syncSurface?.Dispose();
        foreach (var timelineProxy in timelineProxies)
        {
            timelineProxy.Dispose();
        }

        subsurface.Dispose();
        child.Destroy();
        parent.Destroy();
        host.PumpToServer();
        foreach (var timeline in timelines)
        {
            timeline.Release();
        }
    }

    private static string CultureInfo(int step) => step.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static void Pump(CompositorTestHost host, Func<bool> condition)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            host.Loop.Dispatch(10);
        }

        Assert.True(condition(), "condition not reached within 5 s");
    }
}
