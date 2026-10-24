using System.Globalization;
using Avalonia.Media;

namespace Basin.SoftKeyboard.Avalonia;

/// <summary>
/// Keeps <see cref="FormattedText"/> objects for key labels, so that a frame with known labels makes no new text. The cache empties itself when it is full.
/// </summary>
public sealed class SoftKeyTextCache
{
    private readonly Dictionary<(string Text, double Size, Color Color, FontWeight Weight), FormattedText> _texts = [];
    private readonly Dictionary<FontWeight, Typeface> _faces = [];
    private readonly FontFamily _family;

    /// <summary>
    /// Initializes a new instance of the <see cref="SoftKeyTextCache"/> class.
    /// </summary>
    /// <param name="family">The font family of every text.</param>
    /// <param name="capacity">The number of texts that the cache holds before it empties itself. It must be 1 or more.</param>
    /// <exception cref="ArgumentNullException"><paramref name="family"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is less than 1.</exception>
    public SoftKeyTextCache(FontFamily family, int capacity = 2048)
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _family = family;
        Capacity = capacity;
    }

    /// <summary>
    /// Gets the number of texts that the cache holds before it empties itself.
    /// </summary>
    public int Capacity { get; }

    /// <summary>
    /// Gets the number of texts in the cache now.
    /// </summary>
    public int Count => _texts.Count;

    /// <summary>
    /// Gets the text for this label, size, color and weight. A missing text is made and kept. When the cache is full, it is emptied first.
    /// </summary>
    /// <param name="text">The label text.</param>
    /// <param name="size">The font size.</param>
    /// <param name="color">The text color.</param>
    /// <param name="weight">The font weight.</param>
    /// <returns>The cached text. The cache owns it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public FormattedText Get(string text, double size, Color color, FontWeight weight = FontWeight.Normal)
    {
        ArgumentNullException.ThrowIfNull(text);
        var key = (text, size, color, weight);
        if (!_texts.TryGetValue(key, out var formatted))
        {
            if (_texts.Count >= Capacity)
            {
                _texts.Clear();
            }

            if (!_faces.TryGetValue(weight, out var face))
            {
                face = new Typeface(_family, FontStyle.Normal, weight);
                _faces[weight] = face;
            }

            formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, new SolidColorBrush(color));
            _texts[key] = formatted;
        }

        return formatted;
    }

    /// <summary>
    /// Removes every text from the cache.
    /// </summary>
    public void Clear() => _texts.Clear();
}
