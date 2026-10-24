using Basin.Capabilities;
using Pixman;

namespace Basin;

/// <summary>
/// Collects the changes for one atomic output commit. Each setter marks its field in <see cref="Fields"/>, and the output keeps the current value of each field that is not marked.
/// </summary>
public sealed class OutputState : IDisposable
{
    /// <summary>
    /// Gets the fields that the state sets.
    /// </summary>
    public OutputStateFields Fields { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the output must be on. It applies only when <see cref="Fields"/> has <see cref="OutputStateFields.Enabled"/>.
    /// </summary>
    public bool Enabled { get; private set; }

    /// <summary>
    /// Gets the mode to set. It applies only when <see cref="Fields"/> has <see cref="OutputStateFields.Mode"/>.
    /// </summary>
    public OutputMode Mode { get; private set; }

    /// <summary>
    /// Gets the scale to set, snapped to 1/120.
    /// </summary>
    public double Scale { get; private set; } = 1;

    /// <summary>
    /// Gets the transform to set.
    /// </summary>
    public OutputTransform Transform { get; private set; }

    /// <summary>
    /// Gets the buffer for the primary plane, or <see langword="null"/> when the state carries none. The state does not lock it.
    /// </summary>
    public IBuffer? Buffer { get; private set; }

    /// <summary>
    /// Gets the damage of the new buffer in framebuffer pixels. The state owns the region.
    /// </summary>
    public PixmanRegion32 Damage { get; } = new();

    /// <summary>
    /// Gets a value indicating whether variable refresh must be on.
    /// </summary>
    public bool AdaptiveSync { get; private set; }

    /// <summary>
    /// Gets the sync_file that the primary plane must wait for, or -1 for none. The fd is borrowed for the length of the commit.
    /// </summary>
    public int InFenceFd { get; private set; } = -1;

    /// <summary>
    /// Gets a value indicating whether the commit must give a fence that signals when the frame is on the screen.
    /// </summary>
    public bool OutFenceRequested { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the flip can tear, which means that it does not wait for the vblank.
    /// </summary>
    public bool Tearing { get; private set; }

    /// <summary>
    /// Gets the content type to send to the display.
    /// </summary>
    public OutputContentType ContentType { get; private set; }

    /// <summary>
    /// Gets the color description of the buffer content, or <see langword="null"/> for the output's own color space.
    /// </summary>
    public Capabilities.ImageDescription? ContentDescription { get; private set; }

    /// <summary>
    /// Gets the HDR metadata to send, or <see langword="null"/> to send none.
    /// </summary>
    public HdrStaticMetadata? Hdr { get; private set; }

    /// <summary>
    /// Gets the candidates for hardware planes. The list is borrowed until <see cref="Clear"/>, not copied.
    /// </summary>
    public IReadOnlyList<OutputLayer>? Layers { get; private set; }

    /// <summary>
    /// Gets the gamma lookup table to set, or <see langword="null"/> to remove it.
    /// </summary>
    public OutputGammaRamps? GammaLut { get; private set; }

    /// <summary>
    /// Gets the color transform matrix as nine coefficients in row-major order, or <see langword="null"/> to remove it.
    /// </summary>
    public double[]? Ctm { get; private set; }

    /// <summary>
    /// Gets the degamma lookup table to set, or <see langword="null"/> to remove it.
    /// </summary>
    public OutputGammaRamps? DegammaLut { get; private set; }

    /// <summary>
    /// Gets the RGB quantization range to set.
    /// </summary>
    public OutputRgbRange RgbRange { get; private set; }

    /// <summary>
    /// Gets the largest bit depth per color on the link, or zero for the driver default.
    /// </summary>
    public uint MaxBitsPerColor { get; private set; }

    /// <summary>
    /// Gets the overscan in percent, from 0 to 100.
    /// </summary>
    public uint Overscan { get; private set; }

    /// <summary>
    /// Gets the custom modes to offer in addition to the modes of the display.
    /// </summary>
    public IReadOnlyList<OutputMode>? CustomModes { get; private set; }

    /// <summary>
    /// Gets the panel sharpness, from 0 to 10000. Zero turns it off.
    /// </summary>
    public uint Sharpness { get; private set; }

    /// <summary>
    /// Gets the adaptive backlight level, from 0 to 4. Zero turns it off.
    /// </summary>
    public uint AbmLevel { get; private set; }

    /// <summary>
    /// Sets whether the output is on.
    /// </summary>
    /// <param name="enabled"><see langword="true"/> to turn the output on.</param>
    /// <returns>This state, for chained calls.</returns>
    public OutputState SetEnabled(bool enabled)
    {
        Enabled = enabled;
        Fields |= OutputStateFields.Enabled;
        return this;
    }

    /// <summary>
    /// Sets the mode.
    /// </summary>
    /// <param name="mode">The mode in physical pixels.</param>
    /// <returns>This state, for chained calls.</returns>
    public OutputState SetMode(OutputMode mode)
    {
        Mode = mode;
        Fields |= OutputStateFields.Mode;
        return this;
    }

    /// <summary>
    /// Sets the scale. The value is snapped to a multiple of 1/120.
    /// </summary>
    /// <param name="scale">The logical-to-physical scale.</param>
    /// <returns>This state, for chained calls.</returns>
    public OutputState SetScale(double scale)
    {
        Scale = OutputScaling.Snap(scale);
        Fields |= OutputStateFields.Scale;
        return this;
    }

    /// <summary>
    /// Sets the transform.
    /// </summary>
    /// <param name="transform">What the panel does to the image.</param>
    /// <returns>This state, for chained calls.</returns>
    public OutputState SetTransform(OutputTransform transform)
    {
        Transform = transform;
        Fields |= OutputStateFields.Transform;
        return this;
    }

    /// <summary>
    /// Sets the buffer for the primary plane.
    /// </summary>
    /// <param name="buffer">The buffer to scan out. The caller keeps it locked until the output releases it.</param>
    /// <returns>This state, for chained calls.</returns>
    public OutputState SetBuffer(IBuffer buffer)
    {
        Buffer = buffer;
        Fields |= OutputStateFields.Buffer;
        return this;
    }

    /// <summary>
    /// Sets the damage of the buffer. The state copies the region.
    /// </summary>
    /// <param name="damage">The damage in framebuffer pixels.</param>
    /// <returns>This state, for chained calls.</returns>
    public OutputState SetDamage(PixmanRegion32 damage)
    {
        Damage.Copy(damage);
        Fields |= OutputStateFields.Damage;
        return this;
    }

    /// <summary>
    /// Sets whether variable refresh is on. A test fails when it turns it on and the output cannot.
    /// </summary>
    /// <param name="enabled"><see langword="true"/> to turn variable refresh on.</param>
    /// <returns>This state, for chained calls.</returns>
    public OutputState SetAdaptiveSync(bool enabled)
    {
        AdaptiveSync = enabled;
        Fields |= OutputStateFields.AdaptiveSync;
        return this;
    }

    /// <summary>
    /// Sets the HDR metadata for the connector.
    /// </summary>
    /// <param name="metadata">The metadata, or <see langword="null"/> to send none.</param>
    /// <returns>This state, for chained calls.</returns>
    public OutputState SetHdr(HdrStaticMetadata? metadata)
    {
        Hdr = metadata;
        Fields |= OutputStateFields.Hdr;
        return this;
    }

    /// <summary>
    /// Sets the candidates for hardware planes. The test commit writes <see cref="OutputLayer.Accepted"/> on each one.
    /// </summary>
    /// <param name="layers">The layers from bottom to top. The list is borrowed until <see cref="Clear"/>.</param>
    /// <returns>This state, for chained calls.</returns>
    public OutputState SetLayers(IReadOnlyList<OutputLayer> layers)
    {
        Layers = layers;
        Fields |= OutputStateFields.Layers;
        return this;
    }

    /// <summary>
    /// Sets the gamma lookup table.
    /// </summary>
    /// <param name="ramps">The table, or <see langword="null"/> to remove it.</param>
    /// <returns>This state, for chained calls.</returns>
    public OutputState SetGammaLut(OutputGammaRamps? ramps)
    {
        GammaLut = ramps;
        Fields |= OutputStateFields.GammaLut;
        return this;
    }

    /// <summary>
    /// Sets the color transform matrix. It throws <see cref="ArgumentException"/> when the matrix does not have nine values.
    /// </summary>
    /// <param name="matrix">Nine coefficients in row-major order, or <see langword="null"/> to remove the matrix.</param>
    /// <returns>This state, for chained calls.</returns>
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

    /// <summary>
    /// Sets the degamma lookup table.
    /// </summary>
    /// <param name="ramps">The table, or <see langword="null"/> to remove it.</param>
    /// <returns>This state, for chained calls.</returns>
    public OutputState SetDegammaLut(OutputGammaRamps? ramps)
    {
        DegammaLut = ramps;
        Fields |= OutputStateFields.DegammaLut;
        return this;
    }

    /// <summary>
    /// Sets the RGB quantization range. A test fails when the range is not automatic and the output cannot set it.
    /// </summary>
    /// <param name="range">The range to send.</param>
    /// <returns>This state, for chained calls.</returns>
    public OutputState SetRgbRange(OutputRgbRange range)
    {
        RgbRange = range;
        Fields |= OutputStateFields.RgbRange;
        return this;
    }

    /// <summary>
    /// Sets the largest bit depth per color on the link. A test fails when the value is not zero and the output cannot set it.
    /// </summary>
    /// <param name="maxBpc">The bit depth, or zero for the driver default.</param>
    /// <returns>This state, for chained calls.</returns>
    public OutputState SetMaxBitsPerColor(uint maxBpc)
    {
        MaxBitsPerColor = maxBpc;
        Fields |= OutputStateFields.MaxBitsPerColor;
        return this;
    }

    /// <summary>
    /// Sets the overscan. It throws <see cref="ArgumentOutOfRangeException"/> for a value above 100.
    /// </summary>
    /// <param name="percent">The overscan in percent.</param>
    /// <returns>This state, for chained calls.</returns>
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

    /// <summary>
    /// Sets the custom modes. A test fails when the list is not empty and the output cannot drive custom modes.
    /// </summary>
    /// <param name="modes">The modes. The state keeps a reference, not a copy.</param>
    /// <returns>This state, for chained calls.</returns>
    public OutputState SetCustomModes(IReadOnlyList<OutputMode> modes)
    {
        ArgumentNullException.ThrowIfNull(modes);
        CustomModes = modes;
        Fields |= OutputStateFields.CustomModes;
        return this;
    }

    /// <summary>
    /// Sets the panel sharpness. It throws <see cref="ArgumentOutOfRangeException"/> for a value above 10000.
    /// </summary>
    /// <param name="sharpness">The sharpness from 0 to 10000.</param>
    /// <returns>This state, for chained calls.</returns>
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

    /// <summary>
    /// Sets the adaptive backlight level. It throws <see cref="ArgumentOutOfRangeException"/> for a value above 4.
    /// </summary>
    /// <param name="level">The level from 0 to 4.</param>
    /// <returns>This state, for chained calls.</returns>
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

    /// <summary>
    /// Sets the color description of the buffer content.
    /// </summary>
    /// <param name="description">The description, or <see langword="null"/> for the output's own color space.</param>
    /// <returns>This state, for chained calls.</returns>
    public OutputState SetContentDescription(Capabilities.ImageDescription? description)
    {
        ContentDescription = description;
        Fields |= OutputStateFields.ContentDescription;
        return this;
    }

    /// <summary>
    /// Sets the content type to send to the display.
    /// </summary>
    /// <param name="type">The content type.</param>
    /// <returns>This state, for chained calls.</returns>
    public OutputState SetContentType(OutputContentType type)
    {
        ContentType = type;
        Fields |= OutputStateFields.ContentType;
        return this;
    }

    /// <summary>
    /// Sets whether the flip can tear.
    /// </summary>
    /// <param name="tearing"><see langword="true"/> to let the flip skip the vblank wait.</param>
    /// <returns>This state, for chained calls.</returns>
    public OutputState SetTearing(bool tearing)
    {
        Tearing = tearing;
        Fields |= OutputStateFields.Tearing;
        return this;
    }

    /// <summary>
    /// Sets a fence that the primary plane must wait for. A commit fails when the output cannot wait for a fence.
    /// </summary>
    /// <param name="syncFileFd">A sync_file fd, or -1 for none. It is borrowed for the length of the commit.</param>
    /// <returns>This state, for chained calls.</returns>
    public OutputState SetInFence(int syncFileFd)
    {
        InFenceFd = syncFileFd;
        Fields |= OutputStateFields.InFence;
        return this;
    }

    /// <summary>
    /// Asks the commit for a fence that signals when the frame is on the screen. An output that cannot give one ignores the request.
    /// </summary>
    /// <returns>This state, for chained calls.</returns>
    public OutputState RequestOutFence()
    {
        OutFenceRequested = true;
        Fields |= OutputStateFields.OutFence;
        return this;
    }

    /// <summary>
    /// Removes every field from the state and drops its references to buffers, layers and fences, so the object can be used again.
    /// </summary>
    public void Clear()
    {
        Fields = OutputStateFields.None;
        Buffer = null;
        Damage.Clear();
        InFenceFd = -1;
        OutFenceRequested = false;
        Tearing = false;
        ContentType = OutputContentType.None;
        ContentDescription = null;
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

    /// <summary>
    /// Releases the damage region.
    /// </summary>
    public void Dispose() => Damage.Dispose();
}
