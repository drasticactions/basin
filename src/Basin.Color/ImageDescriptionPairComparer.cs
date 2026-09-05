using System.Runtime.CompilerServices;
using Basin.Capabilities;

namespace Basin.Color;

public sealed class ImageDescriptionPairComparer : IEqualityComparer<(ImageDescription Source, ImageDescription Output)>
{
    public static ImageDescriptionPairComparer Instance { get; } = new();

    public bool Equals((ImageDescription Source, ImageDescription Output) x, (ImageDescription Source, ImageDescription Output) y) =>
        ReferenceEquals(x.Source, y.Source) && ReferenceEquals(x.Output, y.Output);

    public int GetHashCode((ImageDescription Source, ImageDescription Output) key) =>
        HashCode.Combine(RuntimeHelpers.GetHashCode(key.Source), RuntimeHelpers.GetHashCode(key.Output));
}
