using System.Runtime.CompilerServices;

namespace Basin.Kinematics;

/// <summary>
/// Holds up to <see cref="Capacity"/> snap targets inline, with no allocation.
/// </summary>
public struct SnapTargets
{
    /// <summary>
    /// The largest number of targets that the set holds.
    /// </summary>
    public const int Capacity = 16;

    private Slots _slots;
    private int _count;

    /// <summary>
    /// Gets the number of targets in the set.
    /// </summary>
    public readonly int Count => _count;

    /// <summary>
    /// Adds a target to the set.
    /// </summary>
    /// <param name="target">The target to add.</param>
    /// <returns><see langword="false"/> when the set already holds <see cref="Capacity"/> targets.</returns>
    public bool Add(in SnapTarget target)
    {
        if (_count >= Capacity)
        {
            return false;
        }

        _slots[_count++] = target;
        return true;
    }

    /// <summary>
    /// Removes every target.
    /// </summary>
    public void Clear() => _count = 0;

    /// <summary>
    /// Finds the point nearest a position over all targets. On a tie, the target added first wins.
    /// </summary>
    /// <param name="x">The horizontal position.</param>
    /// <param name="y">The vertical position.</param>
    /// <param name="snappedX">The horizontal position of the nearest point, or <paramref name="x"/> when the set is empty.</param>
    /// <param name="snappedY">The vertical position of the nearest point, or <paramref name="y"/> when the set is empty.</param>
    /// <returns><see langword="false"/> when the set is empty.</returns>
    public readonly bool Nearest(double x, double y, out double snappedX, out double snappedY)
    {
        snappedX = x;
        snappedY = y;
        var best = double.MaxValue;
        for (var i = 0; i < _count; i++)
        {
            var (sx, sy) = _slots[i].Nearest(x, y);
            var dx = sx - x;
            var dy = sy - y;
            var distance = (dx * dx) + (dy * dy);
            if (distance < best)
            {
                best = distance;
                snappedX = sx;
                snappedY = sy;
            }
        }

        return _count > 0;
    }

    /// <summary>
    /// Starts a spring from the position and velocity of a fling toward the target nearest the fling's <see cref="Fling.ProjectedRest"/>.
    /// </summary>
    /// <param name="fling">The fling to hand off. It is not changed.</param>
    /// <param name="spring">The spring to start.</param>
    /// <returns><see langword="false"/> when the set is empty and the spring is not started.</returns>
    public readonly bool Handoff(in Fling fling, ref SpringHandoff spring)
    {
        var (restX, restY) = fling.ProjectedRest;
        if (!Nearest(restX, restY, out var targetX, out var targetY))
        {
            return false;
        }

        spring.Start(fling.X, fling.Y, fling.VelocityX, fling.VelocityY, targetX, targetY);
        return true;
    }

    [InlineArray(Capacity)]
    private struct Slots
    {
        private SnapTarget _element;
    }
}
