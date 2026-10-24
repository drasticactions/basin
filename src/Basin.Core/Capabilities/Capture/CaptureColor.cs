namespace Basin.Capabilities;

/// <summary>
/// Describes the color space of the pixels that a capture produces.
/// </summary>
/// <param name="Description">The description of the captured pixels, or <see langword="null"/> when it is not known.</param>
/// <param name="Hdr">The HDR static metadata of the source content or output, or <see langword="null"/> for SDR.</param>
public readonly record struct CaptureColor(ImageDescription? Description, HdrStaticMetadata? Hdr);
