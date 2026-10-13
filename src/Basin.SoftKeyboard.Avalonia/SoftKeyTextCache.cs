using System.Globalization;
using Avalonia.Media;

namespace Basin.SoftKeyboard.Avalonia;

public sealed class SoftKeyTextCache
{
    private readonly Dictionary<(string Text, double Size, Color Color, FontWeight Weight), FormattedText> _texts = [];
    private readonly Dictionary<FontWeight, Typeface> _faces = [];
    private readonly FontFamily _family;

    public SoftKeyTextCache(FontFamily family, int capacity = 2048)
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _family = family;
        Capacity = capacity;
    }

    public int Capacity { get; }

    public int Count => _texts.Count;

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

    public void Clear() => _texts.Clear();
}
