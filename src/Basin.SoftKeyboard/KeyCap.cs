using System.Globalization;

namespace Basin.SoftKeyboard;

public sealed class KeyCap
{
    public static readonly KeyCap Backspace = new(KeyFunction.Backspace, "backspace");
    public static readonly KeyCap Return = new(KeyFunction.Return, "return");
    public static readonly KeyCap Shift = new(KeyFunction.Shift, "shift");
    public static readonly KeyCap CapsLock = new(KeyFunction.Shift, "caps", "Caps");
    public static readonly KeyCap Symbol = new(KeyFunction.Symbol, "symbol");
    public static readonly KeyCap Hide = new(KeyFunction.Hide, "hide");
    public static readonly KeyCap Tab = new(KeyFunction.Tab, "tab");
    public static readonly KeyCap Left = new(KeyFunction.Left, "left");
    public static readonly KeyCap Right = new(KeyFunction.Right, "right");
    public static readonly KeyCap Language = new(KeyFunction.Language, "language");
    public static readonly KeyCap Control = new(KeyFunction.Control, "control");
    public static readonly KeyCap Alt = new(KeyFunction.Alt, "alt");
    public static readonly KeyCap Super = new(KeyFunction.Super, "super");
    public static readonly KeyCap Escape = new(KeyFunction.Escape, "escape");
    public static readonly KeyCap Up = new(KeyFunction.Up, "up");
    public static readonly KeyCap Down = new(KeyFunction.Down, "down");
    public static readonly KeyCap Home = new(KeyFunction.Home, "home");
    public static readonly KeyCap End = new(KeyFunction.End, "end");
    public static readonly KeyCap PageUp = new(KeyFunction.PageUp, "page-up");
    public static readonly KeyCap PageDown = new(KeyFunction.PageDown, "page-down");
    public static readonly KeyCap Delete = new(KeyFunction.Delete, "delete");
    public static readonly KeyCap Emoji = new(KeyFunction.Emoji, "emoji");
    public static readonly KeyCap LayoutSwitch = new(KeyFunction.LayoutSwitch, "layout-switch");
    public static readonly KeyCap Space = Character(" ");
    public static readonly KeyCap ExtraSmall = SizeCap(KeyboardSize.ExtraSmall, "XS");
    public static readonly KeyCap Small = SizeCap(KeyboardSize.Small, "S");
    public static readonly KeyCap Medium = SizeCap(KeyboardSize.Medium, "M");
    public static readonly KeyCap Large = SizeCap(KeyboardSize.Large, "L");

    private KeyCap(KeyFunction function, string name, string? label = null, int argument = 0)
    {
        Function = function;
        Name = name;
        Text = string.Empty;
        UpperText = string.Empty;
        Label = label;
        Argument = argument;
    }

    private KeyCap(string text, string? label, bool? letter = null)
    {
        Function = KeyFunction.Text;
        Name = text;
        Text = text;
        UpperText = text.Length == 1 ? text.ToUpper(CultureInfo.InvariantCulture) : text;
        Label = label;
        IsLetter = letter ?? (text.Length == 1 && IsPagedLetter(text[0]));
    }

    public KeyFunction Function { get; }

    public string Name { get; }

    public string Text { get; }

    public string UpperText { get; }

    public string? Label { get; }

    public int Argument { get; }

    public bool IsLetter { get; }

    public bool IsText => Function == KeyFunction.Text;

    public bool IsShortcut => Function == KeyFunction.Text && Text.Length > 1;

    public bool IsSpace => Function == KeyFunction.Text && Text == " ";

    public bool IsDigit => Function == KeyFunction.Text && Text.Length == 1 && Text[0] is >= '0' and <= '9';

    public bool Repeats => Function is KeyFunction.Backspace or KeyFunction.Left or KeyFunction.Right or KeyFunction.Up or KeyFunction.Down
        or KeyFunction.Delete or KeyFunction.PageUp or KeyFunction.PageDown || IsSpace;

    public bool IsCapsLock => Function == KeyFunction.Shift && Name == "caps";

    public bool IsModifier => Function is KeyFunction.Shift or KeyFunction.Control or KeyFunction.Alt or KeyFunction.Super;

    public bool IsKeystroke => Function is KeyFunction.Backspace or KeyFunction.Return or KeyFunction.Tab or KeyFunction.Left
        or KeyFunction.Right or KeyFunction.Escape or KeyFunction.Up or KeyFunction.Down or KeyFunction.Home or KeyFunction.End
        or KeyFunction.PageUp or KeyFunction.PageDown or KeyFunction.Delete or KeyFunction.FunctionKey;

    public string Target => Function is KeyFunction.Page or KeyFunction.ShapeChoice ? Name[(Name.IndexOf(':') + 1)..] : string.Empty;

    public KeyboardSize Size => (KeyboardSize)Argument;

    public static KeyCap Character(string text, string? label = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        return new KeyCap(text, label);
    }

    public static KeyCap Character(string text, string? label, bool letter)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        return new KeyCap(text, label, letter);
    }

    public static KeyCap LanguageKey(string label) => new(KeyFunction.Language, "language", label);

    public static KeyCap LanguageChoice(int index, string label) => new(KeyFunction.LanguageChoice, "language-" + index.ToString(CultureInfo.InvariantCulture), label, index);

    public static KeyCap FunctionKeyCap(int number) =>
        number is >= 1 and <= 12
            ? FunctionKeys[number - 1]
            : throw new ArgumentOutOfRangeException(nameof(number), number, "function keys are F1 to F12");

    public static KeyCap PageKey(string page, string? label = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(page);
        return new KeyCap(KeyFunction.Page, "page:" + page, label);
    }

    public static KeyCap ShapeKey(string shape, string? label = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(shape);
        return new KeyCap(KeyFunction.ShapeChoice, "shape:" + shape, label);
    }

    public KeyCap WithLabel(string? label) => IsText ? new KeyCap(Text, label, IsLetter) : new KeyCap(Function, Name, label, Argument);

    public static KeyCap SizeCap(KeyboardSize size) => size switch
    {
        KeyboardSize.ExtraSmall => ExtraSmall,
        KeyboardSize.Small => Small,
        KeyboardSize.Large => Large,
        _ => Medium,
    };

    public static bool TryParse(string token, out KeyCap? cap)
    {
        ArgumentNullException.ThrowIfNull(token);
        cap = null;
        if (token.Length > 2 && token[0] == '{' && token[^1] == '}')
        {
            var inner = token[1..^1];
            if (inner.StartsWith("page:", StringComparison.Ordinal) && inner.Length > 5)
            {
                cap = PageKey(inner[5..]);
                return true;
            }

            if (inner.StartsWith("shape:", StringComparison.Ordinal) && inner.Length > 6)
            {
                cap = ShapeKey(inner[6..]);
                return true;
            }

            if (inner.Length is 2 or 3 && inner[0] == 'f' && int.TryParse(inner.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number is >= 1 and <= 12)
            {
                cap = FunctionKeys[number - 1];
                return true;
            }

            cap = inner switch
            {
                "backspace" => Backspace,
                "return" => Return,
                "shift" => Shift,
                "symbol" => Symbol,
                "hide" => Hide,
                "tab" => Tab,
                "left" => Left,
                "right" => Right,
                "language" => Language,
                "caps" => CapsLock,
                "control" => Control,
                "alt" => Alt,
                "super" => Super,
                "escape" => Escape,
                "up" => Up,
                "down" => Down,
                "home" => Home,
                "end" => End,
                "page-up" => PageUp,
                "page-down" => PageDown,
                "delete" => Delete,
                "emoji" => Emoji,
                "layout-switch" => LayoutSwitch,
                "size-xs" => ExtraSmall,
                "size-s" => Small,
                "size-m" => Medium,
                "size-l" => Large,
                _ => null,
            };
            return cap is not null;
        }

        if (token.Length == 0)
        {
            return false;
        }

        cap = token == " " ? Space : Character(token);
        return true;
    }

    public override string ToString() => Function == KeyFunction.Text ? Text : Name;

    private static readonly KeyCap[] FunctionKeys = [.. Enumerable.Range(1, 12).Select(static n =>
        new KeyCap(KeyFunction.FunctionKey, "f" + n.ToString(CultureInfo.InvariantCulture), "F" + n.ToString(CultureInfo.InvariantCulture), n))];

    private static KeyCap SizeCap(KeyboardSize size, string label) =>
        new(KeyFunction.Size, "size-" + label.ToLowerInvariant(), label, (int)size);

    private static bool IsPagedLetter(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or 'ä' or 'ö' or 'å' or 'Ä' or 'Ö' or 'Å';
}
