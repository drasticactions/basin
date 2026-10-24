using System.Collections.Concurrent;
using System.Diagnostics;
using static Basin.Hosted.HostedLog;

namespace Basin.Hosted;

/// <summary>
/// Runs a <see cref="BasinCompositorHost"/> on a thread of its own with no toolkit. It frames only while a view needs a repaint or a client waits for a frame.
/// A frame draws no pixels unless a recording or a screenshot asks for them.
/// </summary>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="BasinHeadlessDriver"/> class. Nothing runs until <see cref="Start"/>.
    /// </summary>
    /// <param name="create">The factory that builds the host. It runs on the driver thread, so the host captures that thread.</param>
    /// <param name="interval">The shortest time between two frames. The default is 16 milliseconds.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="interval"/> is zero or negative.</exception>
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

    /// <summary>
    /// Gets the host that the factory built. Only the driver thread can use it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The driver has not started.</exception>
    public BasinCompositorHost Host => _host ?? throw new InvalidOperationException("the driver has not started");

    /// <summary>
    /// Gets the count of frames that the driver completed. Any thread can read it.
    /// </summary>
    public long Frames => Interlocked.Read(ref _frames);

    /// <summary>
    /// Gets a value indicating whether the calling thread is the driver thread.
    /// </summary>
    public bool IsDriverThread => Thread.CurrentThread == _thread;

    /// <summary>
    /// Occurs on the driver thread after each frame that the host opened.
    /// </summary>
    public event Action? FrameCompleted;

    /// <summary>
    /// Starts the driver thread and blocks until the factory returns. An exception from the factory comes back from this call.
    /// </summary>
    /// <exception cref="InvalidOperationException">The driver was started before.</exception>
    /// <exception cref="ObjectDisposedException">The driver is disposed.</exception>
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

    /// <summary>
    /// Queues work for the driver thread and wakes it. Any thread can call it. After <see cref="Stop"/> begins, the work is dropped.
    /// </summary>
    /// <param name="action">The work. An exception from it is logged and does not stop the driver.</param>
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

    /// <summary>
    /// Runs work on the driver thread and returns its result. Any thread can call it.
    /// </summary>
    /// <typeparam name="T">The type of the result.</typeparam>
    /// <param name="work">The work to run.</param>
    /// <returns>A task that completes with the result or the exception of <paramref name="work"/>. It is canceled when the driver is stopping.</returns>
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

    /// <summary>
    /// Asks for one frame at the next interval. Any thread can call it.
    /// </summary>
    public void RequestFrame() => Post(() => _frameWanted = true);

    /// <summary>
    /// Stops the loop, runs the queued work and <paramref name="beforeDispose"/> on the driver thread, then disposes the host there.
    /// It waits for the thread unless the driver thread calls it. A call before <see cref="Start"/> or a second call does nothing.
    /// </summary>
    /// <param name="beforeDispose">The teardown that must run on the driver thread before the host is disposed, or <see langword="null"/>.</param>
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

    /// <summary>
    /// Calls <see cref="Stop"/> and marks the driver disposed.
    /// </summary>
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
        if (_frameWanted || _host!.ScreenshotPending)
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
                var target = view.Target;
                host.NotifyPresented(
                    view.SceneOutput, host.CapturePresented ? HostedViewCapture.Read(host, view, null) : null, target.Width, target.Height);
            }

            Screenshot(host);
            host.Scene.SendFrameDone((uint)Environment.TickCount);
        }
        finally
        {
            host.ExitFrame();
        }

        Interlocked.Increment(ref _frames);
        FrameCompleted?.Invoke();
    }

    private static void Screenshot(BasinCompositorHost host)
    {
        if (!host.TakeScreenshotRequest(out var path, out var done))
        {
            return;
        }

        var view = host.Views.Count > 0 ? host.Views[0] : null;
        done(view is not null && HostedViewCapture.Read(host, view, null) is { } pixels &&
            HostedViewCapture.WritePng(pixels, view.Target.Width, view.Target.Height, path));
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
