using Basin.Host;
using Basin.Scene;

namespace Basin.Tests;

internal static class HandoffSource
{
    public static BufferBase Stripes(CompositorTestHost host, int width, int height)
    {
        BufferBase? buffer = null;
        if (host.DeviceAllocator is not null)
        {
            buffer = host.DeviceAllocator?.Allocate(width, height, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear], BufferUse.Render) as BufferBase;
        }

        buffer ??= new MemoryBuffer(width, height, DrmFormat.Xrgb8888);
        if (!Paint(buffer))
        {
            buffer.Destroy();
            buffer = new MemoryBuffer(width, height, DrmFormat.Xrgb8888);
            Paint(buffer);
        }

        return buffer;
    }

    private static unsafe bool Paint(BufferBase buffer)
    {
        if (!buffer.BeginDataAccess(BufferDataAccess.Write, out var view))
        {
            return false;
        }

        try
        {
            for (var y = 0; y < buffer.Height; y++)
            {
                var row = (uint*)((byte*)view.Data + ((long)view.Stride * y));
                for (var x = 0; x < buffer.Width; x++)
                {
                    row[x] = (x / 20 % 2 == 0) ^ (y >= buffer.Height - 20) ? 0xFFF0F0F0u : 0xFF2040A0u;
                }
            }
        }
        finally
        {
            buffer.EndDataAccess();
        }

        return true;
    }

    public static HandoffRun Run(CompositorTestHost host, BufferBase frame, int width, int height, IHandoffTransition transition, int sourceWidth = 0, int sourceHeight = 0)
    {
        sourceWidth = sourceWidth > 0 ? sourceWidth : width;
        sourceHeight = sourceHeight > 0 ? sourceHeight : height;
        var layer = new SceneTree(host.Scene.Root) { ExcludeFromScanout = true };
        return new HandoffRun(
            new HandoffFrame("golden", width, height, sourceWidth, sourceHeight),
            transition,
            layer,
            layer,
            new Box(0, 0, width, height),
            frame,
            sourceWidth,
            sourceHeight,
            frame,
            null,
            0);
    }
}
