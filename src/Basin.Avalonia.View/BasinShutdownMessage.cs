using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Basin.Diagnostics;

namespace Basin.Avalonia;

public sealed class BasinShutdownMessage
{
    private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Completed => _done.Task;

    internal void Complete() => _done.TrySetResult();
}
