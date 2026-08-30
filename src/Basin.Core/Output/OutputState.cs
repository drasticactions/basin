using Basin.Capabilities;
using Pixman;

namespace Basin;

public sealed class OutputState : IDisposable
{
    public OutputStateFields Fields { get; private set; }

    public bool Enabled { get; private set; }

    public OutputMode Mode { get; private set; }

    public double Scale { get; private set; } = 1;

    public double AspectRatio { get; private set; }

    public OutputTransform Transform { get; private set; }

    public IBuffer? Buffer { get; private set; }

    public PixmanRegion32 Damage { get; } = new();

    public bool AdaptiveSync { get; private set; }

    public int InFenceFd { get; private set; } = -1;

    public bool OutFenceRequested { get; private set; }

    public bool Tearing { get; private set; }

    public HdrStaticMetadata? Hdr { get; private set; }

    public IReadOnlyList<OutputLayer>? Layers { get; private set; }

    public OutputGammaRamps? GammaLut { get; private set; }

    public double[]? Ctm { get; private set; }

    public OutputGammaRamps? DegammaLut { get; private set; }

    public OutputRgbRange RgbRange { get; private set; }

    public uint MaxBitsPerColor { get; private set; }

    public uint Overscan { get; private set; }

    public IReadOnlyList<OutputMode>? CustomModes { get; private set; }

    public uint Sharpness { get; private set; }

    public uint AbmLevel { get; private set; }

    public OutputState SetEnabled(bool enabled)
    {
        Enabled = enabled;
        Fields |= OutputStateFields.Enabled;
        return this;
    }

    public OutputState SetMode(OutputMode mode)
    {
        Mode = mode;
        Fields |= OutputStateFields.Mode;
        return this;
    }

    public OutputState SetScale(double scale)
    {
        Scale = OutputScaling.Snap(scale);
        Fields |= OutputStateFields.Scale;
        return this;
    }

    public OutputState SetAspectRatio(double aspectRatio)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(aspectRatio);
        AspectRatio = aspectRatio;
        Fields |= OutputStateFields.AspectRatio;
        return this;
    }

    public OutputState SetTransform(OutputTransform transform)
    {
        Transform = transform;
        Fields |= OutputStateFields.Transform;
        return this;
    }

    public OutputState SetBuffer(IBuffer buffer)
    {
        Buffer = buffer;
        Fields |= OutputStateFields.Buffer;
        return this;
    }

    public OutputState SetDamage(PixmanRegion32 damage)
    {
        Damage.Copy(damage);
        Fields |= OutputStateFields.Damage;
        return this;
    }

    public OutputState SetAdaptiveSync(bool enabled)
    {
        AdaptiveSync = enabled;
        Fields |= OutputStateFields.AdaptiveSync;
        return this;
    }

    public OutputState SetHdr(HdrStaticMetadata? metadata)
    {
        Hdr = metadata;
        Fields |= OutputStateFields.Hdr;
        return this;
    }

    public OutputState SetLayers(IReadOnlyList<OutputLayer> layers)
    {
        Layers = layers;
        Fields |= OutputStateFields.Layers;
        return this;
    }

    public OutputState SetGammaLut(OutputGammaRamps? ramps)
    {
        GammaLut = ramps;
        Fields |= OutputStateFields.GammaLut;
        return this;
    }

    public OutputState SetCtm(double[]? matrix)
    {
        if (matrix is { Length: not 9 })
        {
            throw new ArgumentException("a CTM is 9 row-major coefficients", nameof(matrix));
        }

        Ctm = matrix;
        Fields |= OutputStateFields.Ctm;
        return this;
    }

    public OutputState SetDegammaLut(OutputGammaRamps? ramps)
    {
        DegammaLut = ramps;
        Fields |= OutputStateFields.DegammaLut;
        return this;
    }

    public OutputState SetRgbRange(OutputRgbRange range)
    {
        RgbRange = range;
        Fields |= OutputStateFields.RgbRange;
        return this;
    }

    public OutputState SetMaxBitsPerColor(uint maxBpc)
    {
        MaxBitsPerColor = maxBpc;
        Fields |= OutputStateFields.MaxBitsPerColor;
        return this;
    }

    public OutputState SetOverscan(uint percent)
    {
        if (percent > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(percent), "overscan is a percentage from 0 to 100");
        }

        Overscan = percent;
        Fields |= OutputStateFields.Overscan;
        return this;
    }

    public OutputState SetCustomModes(IReadOnlyList<OutputMode> modes)
    {
        ArgumentNullException.ThrowIfNull(modes);
        CustomModes = modes;
        Fields |= OutputStateFields.CustomModes;
        return this;
    }

    public OutputState SetSharpness(uint sharpness)
    {
        if (sharpness > 10000)
        {
            throw new ArgumentOutOfRangeException(nameof(sharpness), "sharpness runs from 0 to 10000");
        }

        Sharpness = sharpness;
        Fields |= OutputStateFields.Sharpness;
        return this;
    }

    public OutputState SetAbmLevel(uint level)
    {
        if (level > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(level), "the abm level runs from 0 to 4");
        }

        AbmLevel = level;
        Fields |= OutputStateFields.AbmLevel;
        return this;
    }

    public OutputState SetTearing(bool tearing)
    {
        Tearing = tearing;
        Fields |= OutputStateFields.Tearing;
        return this;
    }

    public OutputState SetInFence(int syncFileFd)
    {
        InFenceFd = syncFileFd;
        Fields |= OutputStateFields.InFence;
        return this;
    }

    public OutputState RequestOutFence()
    {
        OutFenceRequested = true;
        Fields |= OutputStateFields.OutFence;
        return this;
    }

    public void Clear()
    {
        Fields = OutputStateFields.None;
        Buffer = null;
        Damage.Clear();
        InFenceFd = -1;
        OutFenceRequested = false;
        Tearing = false;
        Hdr = null;
        Layers = null;
        GammaLut = null;
        Ctm = null;
        DegammaLut = null;
        RgbRange = OutputRgbRange.Automatic;
        MaxBitsPerColor = 0;
        Overscan = 0;
        CustomModes = null;
        Sharpness = 0;
        AbmLevel = 0;
    }

    public void Dispose() => Damage.Dispose();
}
