namespace Basin.SoftKeyboard;

/// <summary>
/// One cell of a <see cref="KeyGrid"/>: a key, an invisible key, a spacer or no key. The <see cref="Weight"/> decides which.
/// </summary>
public sealed class KeyCell
{
    /// <summary>
    /// A cell with a weight of 0. It is no key and takes no width.
    /// </summary>
    public static readonly KeyCell Empty = new(0, null, null, null, KeyClass.Function);

    /// <summary>
    /// Makes a cell that takes width, is not drawn and gives no key for a touch.
    /// </summary>
    /// <param name="weight">The width in key units. It must be greater than 0.</param>
    /// <returns>A new spacer cell.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="weight"/> is 0 or less.</exception>
    public static KeyCell Spacer(float weight) =>
        weight > 0 ? new(weight, null, null, null, KeyClass.Function) : throw new ArgumentOutOfRangeException(nameof(weight), weight, "a spacer takes width");

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyCell"/> class.
    /// </summary>
    /// <param name="weight">The width in key units. 0 is no key, and a negative value is an invisible key of that absolute width.</param>
    /// <param name="plain">The cap without Shift or Symbol, or <see langword="null"/> for a spacer or no key.</param>
    /// <param name="alternate">The cap with Shift or Symbol, or <see langword="null"/> to use <paramref name="plain"/>.</param>
    /// <param name="longPress">The long-press list. <see langword="null"/> or an empty list gives no list.</param>
    /// <param name="keyClass">The visual class of the key.</param>
    public KeyCell(float weight, KeyCap? plain, KeyCap? alternate, IReadOnlyList<KeyCap>? longPress, KeyClass keyClass)
    {
        Weight = weight;
        Plain = plain;
        Alternate = alternate ?? plain;
        LongPress = longPress is { Count: > 0 } ? longPress : null;
        Class = keyClass;
    }

    /// <summary>
    /// Gets the width in key units. 0 is no key, and a negative value is an invisible key that takes the width of its absolute value.
    /// </summary>
    public float Weight { get; }

    /// <summary>
    /// Gets the cap without Shift or Symbol. It is <see langword="null"/> for a spacer and for no key.
    /// </summary>
    public KeyCap? Plain { get; }

    /// <summary>
    /// Gets the cap with Shift or Symbol. It is the same as <see cref="Plain"/> when the cell has no alternate.
    /// </summary>
    public KeyCap? Alternate { get; }

    /// <summary>
    /// Gets the long-press list, or <see langword="null"/> when the key has none. The list is never empty.
    /// </summary>
    public IReadOnlyList<KeyCap>? LongPress { get; }

    /// <summary>
    /// Gets the visual class of the key.
    /// </summary>
    public KeyClass Class { get; }

    /// <summary>
    /// Gets a value indicating whether the cell is a key that takes touches, visible or invisible.
    /// </summary>
    public bool IsKey => Weight != 0 && Plain is not null;

    /// <summary>
    /// Gets a value indicating whether the cell is an invisible key. Its touch goes to a visible neighbor with the same plain cap.
    /// </summary>
    public bool IsInvisible => Weight < 0;

    /// <summary>
    /// Gets a value indicating whether the cell is a key that a view draws.
    /// </summary>
    public bool IsVisible => Weight > 0 && Plain is not null;

    /// <summary>
    /// Gets a value indicating whether the cell is a spacer: it takes width but has no key.
    /// </summary>
    public bool IsSpacer => Weight > 0 && Plain is null;

    /// <summary>
    /// Makes a copy of the cell with a different weight.
    /// </summary>
    /// <param name="weight">The new width in key units.</param>
    /// <returns>A new cell with the same caps, list and class.</returns>
    public KeyCell WithWeight(float weight) => new(weight, Plain, Alternate, LongPress, Class);
}
