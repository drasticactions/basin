namespace Basin.Notifications;

/// <summary>
/// Raw pixels from an <c>image-data</c>, <c>image_data</c> or <c>icon_data</c> hint, in the <c>(iiibiiay)</c> layout of the specification.
/// The server checks that the stride, size and channel count fit the bytes.
/// </summary>
/// <param name="Width">The width in pixels.</param>
/// <param name="Height">The height in pixels.</param>
/// <param name="RowStride">The number of bytes from the start of one row to the next.</param>
/// <param name="HasAlpha">A value indicating whether the pixels have an alpha channel.</param>
/// <param name="BitsPerSample">The number of bits per channel sample, normally 8.</param>
/// <param name="Channels">The number of channels per pixel, 3 for RGB or 4 for RGBA.</param>
/// <param name="Pixels">The pixel bytes, rows top to bottom. It is the only copy of the data from the message.</param>
public sealed record NotificationImage(
    int Width,
    int Height,
    int RowStride,
    bool HasAlpha,
    int BitsPerSample,
    int Channels,
    ReadOnlyMemory<byte> Pixels);
