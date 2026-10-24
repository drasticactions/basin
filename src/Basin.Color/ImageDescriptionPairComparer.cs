using System.Runtime.CompilerServices;
using Basin.Capabilities;

namespace Basin.Color;

/// <summary>
/// Compares pairs of image descriptions by reference, for caches keyed on a source and output
/// description.
/// </summary>
public sealed class ImageDescriptionPairComparer : IEqualityComparer<(ImageDescription Source, ImageDescription Output)>
{
    /// <summary>
    /// Gets the shared instance.
    /// </summary>
    public static ImageDescriptionPairComparer Instance { get; } = new();

    /// <summary>
    /// Tells whether two pairs hold the same description objects.
    /// </summary>
    /// <param name="x">The first pair.</param>
    /// <param name="y">The second pair.</param>
    /// <returns><see langword="true"/> when both members are the same references.</returns>
    public bool Equals((ImageDescription Source, ImageDescription Output) x, (ImageDescription Source, ImageDescription Output) y) =>
        ReferenceEquals(x.Source, y.Source) && ReferenceEquals(x.Output, y.Output);

    /// <summary>
    /// Hashes a pair by the identity of its two descriptions.
    /// </summary>
    /// <param name="key">The pair to hash.</param>
    /// <returns>A hash that does not depend on the descriptions' values.</returns>
    public int GetHashCode((ImageDescription Source, ImageDescription Output) key) =>
        HashCode.Combine(RuntimeHelpers.GetHashCode(key.Source), RuntimeHelpers.GetHashCode(key.Output));
}
