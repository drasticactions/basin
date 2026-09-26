using System.Collections.Concurrent;
using System.Diagnostics;
using static Basin.Hosted.HostedLog;

namespace Basin.Hosted;

[System.Runtime.Versioning.UnsupportedOSPlatform("browser")]
public sealed class BasinHeadlessDriver : IDisposable
{
    private readonly Func<BasinCompositorHost> _create;
    private readonly TimeSpan _interval;
    private readonly ConcurrentQueue<Action> _posted = new();
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? _thread;
    private BasinCompositorHost? _host;
    private IEventSource? _wakeSource;
    private int _wakeFd = -1;
    private volatile bool _stopping;
    private Action? _beforeDispose;
    private bool _frameWanted;
    private long _nextFrame;
    private long _frames;
    private bool _disposed;

    public BasinHeadlessDriver(Func<BasinCompositorHost> create, TimeSpan? interval = null)
    {
        ArgumentNullException.ThrowIfNull(create);
        _create = create;
        _interval = interval ?? TimeSpan.FromMilliseconds(16);
        if (_interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "the frame interval must be positive");
        }
    }

    public BasinCompositorHost Host => _host ?? throw new InvalidOperationException("the driver has not started");

    public long Frames => Interlocked.Read(ref _frames);

    public bool IsDriverThread => Thread.CurrentThread == _thread;

    public event Action? FrameCompleted;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_thread is not null)
        {
            throw new InvalidOperationException("the driver starts once");
        }

        _thread = new Thread(Run) { IsBackground = true, Name = "basin-headless" };
        _thread.Start();
        _started.Task.GetAwaiter().GetResult();
    }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_stopping)
        {
            return;
        }

        _posted.Enqueue(action);
        Wake();
    }

    public Task<T> InvokeAsync<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try
            {
                done.TrySetResult(work());
            }
            catch (Exception exception)
            {
                done.TrySetException(exception);
            }
        });
        if (_stopping)
        {
            done.TrySetCanceled();
        }

        return done.Task;
    }

    public void RequestFrame() => Post(() => _frameWanted = true);

    public void Stop(Action? beforeDispose = null)
    {
        if (_thread is null || _stopping)
        {
            return;
        }

        _beforeDispose = beforeDispose;
        _stopping = true;
        Wake();
        if (Thread.CurrentThread != _thread)
        {
            _thread.Join();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
    }

    private void Run()
    {
        try
        {
            _host = _create();
            _wakeFd = HeadlessNative.EventFd(0, HeadlessNative.EfdCloexec | HeadlessNative.EfdNonblock);
            if (_wakeFd < 0)
            {
                throw new InvalidOperationException("eventfd failed; the headless driver cannot be woken");
            }

            _wakeSource = _host.Loop.AddFd(_wakeFd, FdReadiness.Readable, (_, _) => Drain());
            _host.Scene.FrameRequested += OnFrameRequested;
            _host.Session.WakeupRequested += OnFrameRequested;
        }
        catch (Exception exception)
        {
            _started.TrySetException(exception);
            return;
        }

        _started.TrySetResult();
        var host = _host;
        while (!_stopping)
        {
            try
            {
                host.Loop.Dispatch(Timeout());
                RunPosted();
                host.Display.FlushClients();
                if (Due())
                {
                    Frame(host);
                }
            }
            catch (Exception exception)
            {
                Log.Error($"the headless driver caught {exception}");
            }
        }

        RunPosted();
        try
        {
            _beforeDispose?.Invoke();
        }
        catch (Exception exception)
        {
            Log.Error($"the headless driver's teardown failed: {exception}");
        }

        host.Scene.FrameRequested -= OnFrameRequested;
        host.Session.WakeupRequested -= OnFrameRequested;
        _wakeSource?.Remove();
        _wakeSource = null;
        host.Dispose();
        _ = HeadlessNative.Close(_wakeFd);
        _wakeFd = -1;
    }

    private void OnFrameRequested() => _frameWanted = true;

    private bool Wanted()
    {
        if (_frameWanted)
        {
            return true;
        }

        foreach (var view in _host!.Views)
        {
            if (view.SceneOutput.NeedsRepaint)
            {
                return true;
            }
        }

        return false;
    }

    private int Timeout()
    {
        if (!_posted.IsEmpty)
        {
            return 0;
        }

        if (!Wanted())
        {
            return -1;
        }

        var remaining = _nextFrame - Stopwatch.GetTimestamp();
        return remaining <= 0 ? 0 : (int)Math.Ceiling(remaining * 1000.0 / Stopwatch.Frequency);
    }

    private bool Due() => Wanted() && Stopwatch.GetTimestamp() >= _nextFrame;

    private void Frame(BasinCompositorHost host)
    {
        var now = Stopwatch.GetTimestamp();
        _nextFrame = now + (long)(_interval.TotalSeconds * Stopwatch.Frequency);
        _frameWanted = false;
        if (!host.EnterFrame(Stopwatch.GetElapsedTime(0, now)))
        {
            return;
        }

        try
        {
            foreach (var view in host.Views)
            {
                host.Session.MarkPresented(view.SceneOutput);
            }

            host.Scene.SendFrameDone((uint)Environment.TickCount);
        }
        finally
        {
            host.ExitFrame();
        }

        Interlocked.Increment(ref _frames);
        FrameCompleted?.Invoke();
    }

    private void RunPosted()
    {
        while (_posted.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                Log.Error($"a posted action failed on the headless driver: {exception}");
            }
        }
    }

    private unsafe void Drain()
    {
        ulong count;
        _ = HeadlessNative.Read(_wakeFd, &count, sizeof(ulong));
        RunPosted();
    }

    private unsafe void Wake()
    {
        if (_wakeFd < 0)
        {
            return;
        }

        ulong one = 1;
        _ = HeadlessNative.Write(_wakeFd, &one, sizeof(ulong));
    }
}
