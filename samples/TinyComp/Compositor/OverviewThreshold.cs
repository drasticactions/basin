namespace TinyComp;

internal static class OverviewThreshold
{
    public static bool Step(bool heldIn, double depth, double thresholdIn, double thresholdOut) =>
        heldIn ? depth > thresholdOut : depth >= thresholdIn;

    public static double CornerDepth(double across, double along) => Math.Max(across, along);
}
