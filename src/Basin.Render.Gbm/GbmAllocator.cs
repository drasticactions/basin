using Basin.Diagnostics;
using Mesa.Gbm;

namespace Basin.Render.Gbm;

public sealed class GbmAllocator : IAllocator
{
    private readonly GbmDevice _gbm;
    private readonly FdLedger? _ledger;

    public GbmAllocator(GbmDevice gbm, DrmFormatSet renderableFormats, FdLedger? ledger = null)
    {
        _gbm = gbm;
        _ledger = ledger;
        Formats = renderableFormats;
    }

    public DrmFormatSet Formats { get; }

    public IBuffer? Allocate(int width, int height, DrmFormat format, ReadOnlySpan<ulong> modifiers, BufferUse use)
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
