namespace Basin.Capabilities;

public readonly record struct CaptureFormat(int Width, int Height, DrmFormat Format)
{
    public int Stride => Width * Format.BytesPerPixel();
}
