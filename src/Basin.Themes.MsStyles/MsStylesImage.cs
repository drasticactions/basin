namespace Basin.Themes.MsStyles;

/// <summary>
/// Holds the encoded bytes of one image resource in a theme. The theme reads each image once and gives the same value to every property that names it.
/// </summary>
/// <param name="Id">The number of the image in the theme. Numbers start at 1 and follow the order in which the reader loaded the images.</param>
/// <param name="Name">The resource name of the image.</param>
/// <param name="Bytes">The encoded image. A bitmap always starts with a <c>BM</c> file header.</param>
/// <param name="Format">The encoding of <paramref name="Bytes"/>.</param>
/// <param name="Premultiplied">A value indicating whether the color components are premultiplied by alpha. It is <see langword="true"/> for a PNG from an <c>IMAGE</c> resource.</param>
public readonly record struct MsStylesImage(int Id, string Name, ReadOnlyMemory<byte> Bytes, MsStylesImageFormat Format, bool Premultiplied = false);
