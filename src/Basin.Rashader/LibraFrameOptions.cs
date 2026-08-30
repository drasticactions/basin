using System.Runtime.InteropServices;

namespace Basin.Rashader;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct LibraFrameOptions
{
    public nuint Version;
    public byte ClearHistory;
    public int FrameDirection;
    public uint Rotation;
    public uint TotalSubframes;
    public uint CurrentSubframe;
    public float AspectRatio;
    public float FramesPerSecond;
    public uint FrametimeDelta;
    public uint ColorSpace;
    public float BrightnessNits;
    public uint ExpandGamut;
    public fixed float Gyroscope[3];
    public fixed float Accelerometer[3];
    public fixed float AccelerometerRest[3];
}
