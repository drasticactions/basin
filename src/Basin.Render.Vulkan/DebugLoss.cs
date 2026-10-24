namespace Basin.Render.Vulkan;

internal static class DebugLoss
{
#if DEBUG
    private static int _remaining =
        int.TryParse(Environment.GetEnvironmentVariable("BASIN_DEBUG_RENDERER_LOSS"), out var passes) && passes > 0 ? passes : 0;
#endif

    public static bool Due()
    {
#if DEBUG
        if (_remaining <= 0)
        {
            return false;
        }

        _remaining--;
        return _remaining == 0;
#else
        return false;
#endif
    }
}
