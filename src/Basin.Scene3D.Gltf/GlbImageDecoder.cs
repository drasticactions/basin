using Basin.Scene3D;

namespace Basin.Scene3D.Gltf;

/// <summary>
/// Decodes an image that a glTF file embeds, for example a PNG. The caller supplies it, so the reader has no image codec.
/// </summary>
/// <param name="encoded">The encoded bytes of the image.</param>
/// <returns>The decoded RGBA image.</returns>
public delegate WorldImage GlbImageDecoder(ReadOnlySpan<byte> encoded);
