using Basin.Shell.River.Protocol;

namespace Basin.Shell.River;

internal static class SequenceGuard
{
    public static bool EnsureWindowing(RiverWindowManagerV1Resource? manager, SequenceState state)
    {
        if (state == SequenceState.Manage)
        {
            return true;
        }

        manager?.PostError(
            (uint)RiverWindowManagerV1.Error.SequenceOrder,
            "Window management state can change only during a manage sequence.");
        return false;
    }

    public static bool EnsureRendering(RiverWindowManagerV1Resource? manager, SequenceState state)
    {
        if (state is SequenceState.Manage or SequenceState.Render)
        {
            return true;
        }

        manager?.PostError(
            (uint)RiverWindowManagerV1.Error.SequenceOrder,
            "Rendering state can change only during a manage or render sequence.");
        return false;
    }
}
