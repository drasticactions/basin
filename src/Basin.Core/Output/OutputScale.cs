namespace Basin;

public static class OutputScale
{
    public static double Choose(OutputMode mode, (int Width, int Height) physicalMm, OutputClass outputClass)
    {
        if (physicalMm.Width < 3 || physicalMm.Height < 3)
        {
            return 1.0;
        }

        double targetDpi;
        double minSize;
        switch (outputClass)
        {
            case OutputClass.Laptop:
                targetDpi = 125;
                minSize = 800;
                break;
            case OutputClass.Handheld:
                targetDpi = 150;
                minSize = 360;
                break;
            default:
                targetDpi = physicalMm.Height > 500 ? 30.5 : 96;
                minSize = 800;
                break;
        }

        var dpiX = mode.Width / (physicalMm.Width / 25.4);
        var maxScaleX = Math.Clamp(mode.Width / minSize, 1.0, 3.0);
        var scaleX = Math.Clamp(dpiX / targetDpi, 1.0, maxScaleX);

        var dpiY = mode.Height / (physicalMm.Height / 25.4);
        var maxScaleY = Math.Clamp(mode.Height / minSize, 1.0, 3.0);
        var scaleY = Math.Clamp(dpiY / targetDpi, 1.0, maxScaleY);

        var scale = Math.Min(scaleX, scaleY);
        scale = Math.Round(100.0 * scale / 5.0) * 5.0 / 100.0;

        if (scale < 1.20)
        {
            return 1.0;
        }

        var integerScale = Math.Round(scale);
        return Math.Abs(integerScale - scale) < 0.06 ? integerScale : scale;
    }
}
