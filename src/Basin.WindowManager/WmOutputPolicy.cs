namespace Basin.WindowManager;

/// <summary>
/// Output measurements that window managers share.
/// </summary>
public static class WmOutputPolicy
{
    /// <summary>
    /// Gets the area of an output that windows can use.
    /// </summary>
    /// <param name="output">The output to measure.</param>
    /// <returns>The non-exclusive area, or the whole output area when the non-exclusive area is empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    public static Rect UsableArea(WmOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var area = output.NonExclusiveArea;
        return area.IsEmpty ? output.Area : area;
    }

    /// <summary>
    /// Finds the output that a rectangle overlaps most. On a tie, the first output in the list wins.
    /// </summary>
    /// <param name="rect">The rectangle in the compositor's logical space.</param>
    /// <param name="outputs">The outputs to compare.</param>
    /// <returns>The output with the largest overlap, or <see langword="null"/> when the rectangle overlaps none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="outputs"/> is <see langword="null"/>.</exception>
    public static WmOutput? LargestOverlap(Rect rect, IReadOnlyList<WmOutput> outputs)
    {
        ArgumentNullException.ThrowIfNull(outputs);
        WmOutput? best = null;
        long bestArea = 0;
        foreach (var output in outputs)
        {
            var overlap = rect.Intersect(output.Area);
            var area = (long)overlap.Width * overlap.Height;
            if (!overlap.IsEmpty && area > bestArea)
            {
                bestArea = area;
                best = output;
            }
        }

        return best;
    }
}
