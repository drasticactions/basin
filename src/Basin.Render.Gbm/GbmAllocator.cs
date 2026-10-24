using Basin.Diagnostics;
using Mesa.Gbm;

namespace Basin.Render.Gbm;

/// <summary>
/// Allocates dmabufs through a GBM device. Every buffer supports rendering, and linear NV12 is made as one R8 buffer with two planes.
/// </summary>
public sealed class GbmAllocator : IAllocator
{
    private readonly GbmDevice _gbm;
    private readonly FdLedger? _ledger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GbmAllocator"/> class. The allocator does not own the GBM device.
    /// </summary>
    /// <param name="gbm">The GBM device to allocate on. It must outlive the allocator.</param>
    /// <param name="renderableFormats">The formats and modifiers to advertise. The allocator does not check requests against them.</param>
    /// <param name="ledger">The ledger that records the fds of each buffer, or <see langword="null"/>.</param>
    public GbmAllocator(GbmDevice gbm, DrmFormatSet renderableFormats, FdLedger? ledger = null)
    {
        _gbm = gbm;
        _ledger = ledger;
        Formats = renderableFormats;
    }

    /// <inheritdoc/>
    public DrmFormatSet Formats { get; }

    /// <inheritdoc/>
    public BufferBase? Allocate(int width, int height, DrmFormat format, ReadOnlySpan<ulong> modifiers, BufferUse use)
    {
        var usage = GbmBufferFlags.Rendering;
        if ((use & BufferUse.Scanout) != 0)
        {
            usage |= GbmBufferFlags.Scanout;
        }

        if ((use & BufferUse.Cursor) != 0)
        {
            usage |= GbmBufferFlags.Cursor | GbmBufferFlags.Linear;
        }

        if (format == DrmFormat.Nv12 && modifiers.Length == 1 && modifiers[0] == DrmFormatSet.ModifierLinear)
        {
            return AllocateLinearNv12(width, height, usage);
        }

        GbmBuffer bo;
        try
        {
            var explicitModifiers = FilterExplicit(modifiers);
            bo = explicitModifiers.Length > 0
                ? _gbm.CreateBuffer((uint)width, (uint)height, (uint)format, explicitModifiers, usage)
                : _gbm.CreateBuffer((uint)width, (uint)height, (uint)format, usage);
        }
        catch (GbmException)
        {
            return null;
        }

        using (bo)
        {
            var attributes = new DmabufAttributes
            {
                Width = width,
                Height = height,
                Format = format,
                Modifier = bo.Modifier,
                PlaneCount = bo.PlaneCount,
            };
            for (var plane = 0; plane < attributes.PlaneCount; plane++)
            {
                attributes.Fds[plane] = bo.ExportDmaBufForPlane(plane);
                attributes.Offsets[plane] = bo.GetOffsetForPlane(plane);
                attributes.Strides[plane] = bo.GetStrideForPlane(plane);
            }

            return new DmabufBuffer(attributes, _ledger);
        }
    }

    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern int dup(int fd);

    private DmabufBuffer? AllocateLinearNv12(int width, int height, GbmBufferFlags usage)
    {
        var lumaHeight = (height + 1) & ~1;
        GbmBuffer bo;
        try
        {
            bo = _gbm.CreateBuffer(
                (uint)((width + 1) & ~1), (uint)(lumaHeight + (lumaHeight / 2)), (uint)DrmFormat.R8,
                [DrmFormatSet.ModifierLinear], usage & ~GbmBufferFlags.Scanout);
        }
        catch (GbmException)
        {
            return null;
        }

        using (bo)
        {
            var stride = bo.GetStrideForPlane(0);
            var attributes = new DmabufAttributes
            {
                Width = width,
                Height = height,
                Format = DrmFormat.Nv12,
                Modifier = DrmFormatSet.ModifierLinear,
                PlaneCount = 2,
            };
            attributes.Fds[0] = bo.ExportDmaBufForPlane(0);
            attributes.Fds[1] = dup(attributes.Fds[0]);
            attributes.Offsets[0] = bo.GetOffsetForPlane(0);
            attributes.Offsets[1] = attributes.Offsets[0] + (stride * (uint)lumaHeight);
            attributes.Strides[0] = stride;
            attributes.Strides[1] = stride;
            return new DmabufBuffer(attributes, _ledger);
        }
    }

    /// <summary>
    /// Does nothing. The GBM device belongs to the caller, and allocated buffers stay valid.
    /// </summary>
    public void Dispose()
    {
    }

    private static ulong[] FilterExplicit(ReadOnlySpan<ulong> modifiers)
    {
        var count = 0;
        foreach (var modifier in modifiers)
        {
            if (modifier != DrmFormatSet.ModifierInvalid)
            {
                count++;
            }
        }

        var result = new ulong[count];
        var i = 0;
        foreach (var modifier in modifiers)
        {
            if (modifier != DrmFormatSet.ModifierInvalid)
            {
                result[i++] = modifier;
            }
        }

        return result;
    }
}
