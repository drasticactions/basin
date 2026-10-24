using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Basin.Diagnostics;

namespace Basin.Avalonia;

/// <summary>
/// A composition message that tells a Basin visual handler to dispose its view and stop work.
/// </summary>
public sealed class BasinShutdownMessage
{
    private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Gets a task that completes after the handler has processed the message. It never faults.
    /// </summary>
    public Task Completed => _done.Task;

    internal void Complete() => _done.TrySetResult();
}
