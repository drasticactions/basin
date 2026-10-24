namespace Basin.Ipc;

/// <summary>
/// Waits for an event that an action causes, with no race between the action and the subscription.
/// </summary>
public static class IpcEventWait
{
    /// <summary>
    /// The wait time when the caller gives none: 10 seconds.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The number of matches after which a collect window ends early, when the caller gives no limit.
    /// </summary>
    public const int DefaultMaxCollected = 64;

    /// <summary>
    /// Subscribes on a connection of its own, waits for the subscription to be in place, runs the action and then waits for a matching event.
    /// </summary>
    /// <param name="socketPath">The control socket path.</param>
    /// <param name="events">The event names to subscribe to.</param>
    /// <param name="match">A predicate that accepts an event, or <see langword="null"/> to accept every event.</param>
    /// <param name="then">The action to run after the subscription. It returns <see langword="false"/> to stop the wait.</param>
    /// <param name="timeout">The longest wait for the first match. The default is <see cref="DefaultTimeout"/>.</param>
    /// <param name="collect">How long to keep matches after the first one. Zero ends the wait at the first match.</param>
    /// <param name="maxCollected">The number of matches that ends a collect window early. It must be at least 1.</param>
    /// <param name="cancellationToken">A token that cancels the wait and the action.</param>
    /// <returns>The matches, which are empty after a timeout.</returns>
    public static async Task<IpcEventWaitResult> RunAsync(
        string socketPath,
        IReadOnlyList<string> events,
        Func<IpcEvent, bool>? match = null,
        Func<CancellationToken, Task<bool>>? then = null,
        TimeSpan? timeout = null,
        TimeSpan collect = default,
        int maxCollected = DefaultMaxCollected,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(socketPath);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCollected, 1);
        await using var waiter = await BasinIpcClient.ConnectAsync(socketPath, cancellationToken).ConfigureAwait(false);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? DefaultTimeout);
        var subscribed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stream = waiter.SubscribeAsync(events, subscribed.SetResult, deadline.Token).GetAsyncEnumerator(deadline.Token);
        var matches = new List<IpcEvent>();
        Task<bool>? pending = null;
        try
        {
            var first = stream.MoveNextAsync().AsTask();
            pending = first;
            var ready = await Task.WhenAny(subscribed.Task, first).ConfigureAwait(false);
            if (ready == first && !subscribed.Task.IsCompleted)
            {
                _ = await first.ConfigureAwait(false);
            }

            if (then is not null && !await then(cancellationToken).ConfigureAwait(false))
            {
                return new IpcEventWaitResult(matches, Stopped: true);
            }

            var next = first;
            var collecting = false;
            while (true)
            {
                bool more;
                try
                {
                    more = await next.ConfigureAwait(false);
                    pending = null;
                }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    pending = null;
                    break;
                }

                if (!more)
                {
                    break;
                }

                var item = stream.Current;
                if (match is null || match(item))
                {
                    matches.Add(item);
                    if (collect <= TimeSpan.Zero || matches.Count >= maxCollected)
                    {
                        break;
                    }

                    if (!collecting)
                    {
                        collecting = true;
                        deadline.CancelAfter(collect);
                    }
                }

                next = stream.MoveNextAsync().AsTask();
                pending = next;
            }
        }
        finally
        {
            await deadline.CancelAsync().ConfigureAwait(false);
            if (pending is not null)
            {
                try
                {
                    _ = await pending.ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is OperationCanceledException or IpcCallException or IOException or ObjectDisposedException or System.Net.Sockets.SocketException)
                {
                }
            }

            await stream.DisposeAsync().ConfigureAwait(false);
        }

        return new IpcEventWaitResult(matches, Stopped: false);
    }
}
