namespace Basin.SoftKeyboard;

public sealed class KeyCell
{
    public static readonly KeyCell Empty = new(0, null, null, null, KeyClass.Function);

    public static KeyCell Spacer(float weight) =>
        weight > 0 ? new(weight, null, null, null, KeyClass.Function) : throw new ArgumentOutOfRangeException(nameof(weight), weight, "a spacer takes width");

    public KeyCell(float weight, KeyCap? plain, KeyCap? alternate, IReadOnlyList<KeyCap>? longPress, KeyClass keyClass)
    {
        Weight = weight;
        Plain = plain;
        Alternate = alternate ?? plain;
        LongPress = longPress is { Count: > 0 } ? longPress : null;
        Class = keyClass;
    }

    public float Weight { get; }

    public KeyCap? Plain { get; }

    public KeyCap? Alternate { get; }

    public IReadOnlyList<KeyCap>? LongPress { get; }

    public KeyClass Class { get; }

    public bool IsKey => Weight != 0 && Plain is not null;

    public bool IsInvisible => Weight < 0;

    public bool IsVisible => Weight > 0 && Plain is not null;

    public bool IsSpacer => Weight > 0 && Plain is null;

    public KeyCell WithWeight(float weight) => new(weight, Plain, Alternate, LongPress, Class);
}
