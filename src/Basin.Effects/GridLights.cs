using System.Runtime.CompilerServices;

namespace Basin.Effects;

/// <summary>
/// Holds up to <see cref="Capacity"/> box highlights and one pointer highlight for a canvas grid. It holds no references, so a copy is independent.
/// </summary>
public struct GridLights
{
    /// <summary>
    /// The number of box slots.
    /// </summary>
    public const int Capacity = 10;

    /// <summary>
    /// The default value of <see cref="GlowBoost"/>.
    /// </summary>
    public const float DefaultGlowBoost = 2f;

    [InlineArray(Capacity)]
    private struct Slots
    {
        private GridLight _element0;
    }

    private Slots _slots;
    private int _count;

    /// <summary>
    /// Initializes a new instance of the <see cref="GridLights"/> struct with no sources, a radius of 160 pixels and a pale blue highlight color.
    /// </summary>
    public GridLights()
    {
        Radius = 160f;
        Color = new RenderColor(0xe8 / 255f, 0xec / 255f, 1f, 1f);
        GlowBoost = DefaultGlowBoost;
    }

    /// <summary>
    /// Gets the number of box slots in use, which is one more than the highest index that <see cref="Set(int, float, float, float, float, float)"/> wrote.
    /// </summary>
    public readonly int Count => _count;

    /// <summary>
    /// Gets or sets the falloff distance of every source, in screen pixels. Zero or less turns every highlight off.
    /// </summary>
    public float Radius { get; set; }

    /// <summary>
    /// Gets or sets the color that a lit line moves toward.
    /// </summary>
    public RenderColor Color { get; set; }

    /// <summary>
    /// Gets or sets the extra glow factor at full highlight. The glow scales by one plus the weight times this value.
    /// </summary>
    public float GlowBoost { get; set; }

    /// <summary>
    /// Gets the horizontal position of the pointer highlight.
    /// </summary>
    public float PointerX { get; private set; }

    /// <summary>
    /// Gets the vertical position of the pointer highlight.
    /// </summary>
    public float PointerY { get; private set; }

    /// <summary>
    /// Gets the strength of the pointer highlight, from 0 to 1. Zero means no pointer highlight.
    /// </summary>
    public float PointerStrength { get; private set; }

    /// <summary>
    /// Gets the box source in a slot.
    /// </summary>
    /// <param name="index">The slot index.</param>
    /// <returns>The source, or the default value when <paramref name="index"/> is not below <see cref="Count"/>.</returns>
    public readonly GridLight this[int index] => index >= 0 && index < _count ? _slots[index] : default;

    /// <summary>
    /// Gets a value indicating whether any source has a positive strength and <see cref="Radius"/> is positive.
    /// </summary>
    public readonly bool IsLit
    {
        get
        {
            if (Radius <= 0f)
            {
                return false;
            }

            if (PointerStrength > 0f)
            {
                return true;
            }

            for (var i = 0; i < _count; i++)
            {
                if (_slots[i].Strength > 0f)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Sets a box source from a <see cref="Box"/>.
    /// </summary>
    /// <param name="index">The slot index, from 0 to <see cref="Capacity"/> minus 1.</param>
    /// <param name="box">The box that the source lights.</param>
    /// <param name="strength">The strength, clamped between 0 and 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the slots.</exception>
    public void Set(int index, in Box box, float strength) =>
        Set(index, box.X, box.Y, box.Width, box.Height, strength);

    /// <summary>
    /// Sets a box source. A negative width or height becomes zero.
    /// </summary>
    /// <param name="index">The slot index, from 0 to <see cref="Capacity"/> minus 1.</param>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="strength">The strength, clamped between 0 and 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the slots.</exception>
    public void Set(int index, float x, float y, float width, float height, float strength)
    {
        if (index < 0 || index >= Capacity)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        _slots[index] = new GridLight(x, y, Math.Max(0f, width), Math.Max(0f, height), Math.Clamp(strength, 0f, 1f));
        _count = Math.Max(_count, index + 1);
    }

    /// <summary>
    /// Sets the pointer highlight, which is a point source.
    /// </summary>
    /// <param name="x">The horizontal position.</param>
    /// <param name="y">The vertical position.</param>
    /// <param name="strength">The strength, clamped between 0 and 1.</param>
    public void SetPointer(double x, double y, float strength)
    {
        PointerX = (float)x;
        PointerY = (float)y;
        PointerStrength = Math.Clamp(strength, 0f, 1f);
    }

    /// <summary>
    /// Removes every box source and the pointer highlight. <see cref="Radius"/>, <see cref="Color"/> and <see cref="GlowBoost"/> do not change.
    /// </summary>
    public void Clear()
    {
        _slots = default;
        _count = 0;
        PointerX = 0f;
        PointerY = 0f;
        PointerStrength = 0f;
    }

    /// <summary>
    /// Calculates the combined highlight weight at a point. Each source weighs its strength times <see cref="Falloff(double)"/> of its distance over <see cref="Radius"/>.
    /// </summary>
    /// <param name="x">The horizontal position.</param>
    /// <param name="y">The vertical position.</param>
    /// <returns>One minus the product of one minus each weight, from 0 to 1. The distance to a box is zero inside it.</returns>
    public readonly float At(double x, double y)
    {
        if (Radius <= 0f)
        {
            return 0f;
        }

        var dark = 1.0;
        for (var i = 0; i < _count; i++)
        {
            var light = _slots[i];
            if (light.Strength <= 0f)
            {
                continue;
            }

            var dx = Math.Max(Math.Max(light.X - x, x - light.Right), 0.0);
            var dy = Math.Max(Math.Max(light.Y - y, y - light.Bottom), 0.0);
            dark *= 1.0 - (light.Strength * Falloff(Math.Sqrt((dx * dx) + (dy * dy)) / Radius));
        }

        if (PointerStrength > 0f)
        {
            var dx = x - PointerX;
            var dy = y - PointerY;
            dark *= 1.0 - (PointerStrength * Falloff(Math.Sqrt((dx * dx) + (dy * dy)) / Radius));
        }

        return (float)Math.Clamp(1.0 - dark, 0.0, 1.0);
    }

    /// <summary>
    /// Calculates the smooth falloff (1 − t)² (1 + 2t) of a source.
    /// </summary>
    /// <param name="t">The distance over the radius. A negative value counts as zero.</param>
    /// <returns>1 at zero, and 0 at 1 or more.</returns>
    public static double Falloff(double t)
    {
        if (t >= 1.0)
        {
            return 0.0;
        }

        t = Math.Max(0.0, t);
        return (1.0 - t) * (1.0 - t) * (1.0 + (2.0 * t));
    }
}
