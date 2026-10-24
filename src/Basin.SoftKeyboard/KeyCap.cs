using System.Globalization;

namespace Basin.SoftKeyboard;

/// <summary>
/// What a key shows and does: a text or a function. Instances are immutable, and the function caps are shared.
/// </summary>
public sealed class KeyCap
{
    /// <summary>
    /// The Backspace cap, <c>{backspace}</c>.
    /// </summary>
    public static readonly KeyCap Backspace = new(KeyFunction.Backspace, "backspace");
    /// <summary>
    /// The Return cap, <c>{return}</c>.
    /// </summary>
    public static readonly KeyCap Return = new(KeyFunction.Return, "return");
    /// <summary>
    /// The Shift cap, <c>{shift}</c>.
    /// </summary>
    public static readonly KeyCap Shift = new(KeyFunction.Shift, "shift");
    /// <summary>
    /// The Caps cap, <c>{caps}</c>. It is a Shift key that toggles the Shift lock.
    /// </summary>
    public static readonly KeyCap CapsLock = new(KeyFunction.Shift, "caps", "Caps");
    /// <summary>
    /// The Symbol cap, <c>{symbol}</c>.
    /// </summary>
    public static readonly KeyCap Symbol = new(KeyFunction.Symbol, "symbol");
    /// <summary>
    /// The Hide cap, <c>{hide}</c>.
    /// </summary>
    public static readonly KeyCap Hide = new(KeyFunction.Hide, "hide");
    /// <summary>
    /// The Tab cap, <c>{tab}</c>.
    /// </summary>
    public static readonly KeyCap Tab = new(KeyFunction.Tab, "tab");
    /// <summary>
    /// The Left arrow cap, <c>{left}</c>.
    /// </summary>
    public static readonly KeyCap Left = new(KeyFunction.Left, "left");
    /// <summary>
    /// The Right arrow cap, <c>{right}</c>.
    /// </summary>
    public static readonly KeyCap Right = new(KeyFunction.Right, "right");
    /// <summary>
    /// The language cap with no label, <c>{language}</c>.
    /// </summary>
    public static readonly KeyCap Language = new(KeyFunction.Language, "language");
    /// <summary>
    /// The Control cap, <c>{control}</c>.
    /// </summary>
    public static readonly KeyCap Control = new(KeyFunction.Control, "control");
    /// <summary>
    /// The Alt cap, <c>{alt}</c>.
    /// </summary>
    public static readonly KeyCap Alt = new(KeyFunction.Alt, "alt");
    /// <summary>
    /// The Super cap, <c>{super}</c>.
    /// </summary>
    public static readonly KeyCap Super = new(KeyFunction.Super, "super");
    /// <summary>
    /// The Escape cap, <c>{escape}</c>.
    /// </summary>
    public static readonly KeyCap Escape = new(KeyFunction.Escape, "escape");
    /// <summary>
    /// The Up arrow cap, <c>{up}</c>.
    /// </summary>
    public static readonly KeyCap Up = new(KeyFunction.Up, "up");
    /// <summary>
    /// The Down arrow cap, <c>{down}</c>.
    /// </summary>
    public static readonly KeyCap Down = new(KeyFunction.Down, "down");
    /// <summary>
    /// The Home cap, <c>{home}</c>.
    /// </summary>
    public static readonly KeyCap Home = new(KeyFunction.Home, "home");
    /// <summary>
    /// The End cap, <c>{end}</c>.
    /// </summary>
    public static readonly KeyCap End = new(KeyFunction.End, "end");
    /// <summary>
    /// The Page Up cap, <c>{page-up}</c>.
    /// </summary>
    public static readonly KeyCap PageUp = new(KeyFunction.PageUp, "page-up");
    /// <summary>
    /// The Page Down cap, <c>{page-down}</c>.
    /// </summary>
    public static readonly KeyCap PageDown = new(KeyFunction.PageDown, "page-down");
    /// <summary>
    /// The Delete cap, <c>{delete}</c>.
    /// </summary>
    public static readonly KeyCap Delete = new(KeyFunction.Delete, "delete");
    /// <summary>
    /// The emoji page cap, <c>{emoji}</c>.
    /// </summary>
    public static readonly KeyCap Emoji = new(KeyFunction.Emoji, "emoji");
    /// <summary>
    /// The cap that asks for the next keyboard shape, <c>{layout-switch}</c>.
    /// </summary>
    public static readonly KeyCap LayoutSwitch = new(KeyFunction.LayoutSwitch, "layout-switch");
    /// <summary>
    /// The Space cap, a text cap with one space character.
    /// </summary>
    public static readonly KeyCap Space = Character(" ");
    /// <summary>
    /// The cap that chooses <see cref="KeyboardSize.ExtraSmall"/>, <c>{size-xs}</c>.
    /// </summary>
    public static readonly KeyCap ExtraSmall = SizeCap(KeyboardSize.ExtraSmall, "XS");
    /// <summary>
    /// The cap that chooses <see cref="KeyboardSize.Small"/>, <c>{size-s}</c>.
    /// </summary>
    public static readonly KeyCap Small = SizeCap(KeyboardSize.Small, "S");
    /// <summary>
    /// The cap that chooses <see cref="KeyboardSize.Medium"/>, <c>{size-m}</c>.
    /// </summary>
    public static readonly KeyCap Medium = SizeCap(KeyboardSize.Medium, "M");
    /// <summary>
    /// The cap that chooses <see cref="KeyboardSize.Large"/>, <c>{size-l}</c>.
    /// </summary>
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

    /// <summary>
    /// Gets what the cap does. A text cap has <see cref="KeyFunction.Text"/>.
    /// </summary>
    public KeyFunction Function { get; }

    /// <summary>
    /// Gets the cap's name: the text of a text cap, or the name in the layout file without braces, for example <c>page:symbols</c>.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the text that the cap types. It is empty for a function cap.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the upper-case form of a one-character text, computed once. A longer text is unchanged, and a function cap gives an empty string.
    /// </summary>
    public string UpperText { get; }

    /// <summary>
    /// Gets the label that a view draws in place of the default, or <see langword="null"/> for the default.
    /// </summary>
    public string? Label { get; }

    /// <summary>
    /// Gets the cap's number: the size of a size cap, the language index of a language choice, or the number of a function key. It is 0 otherwise.
    /// </summary>
    public int Argument { get; }

    /// <summary>
    /// Gets a value indicating whether a text cap is a letter for the symbol page. A letter shows its alternate on the symbol page and not with Shift.
    /// </summary>
    public bool IsLetter { get; }

    /// <summary>
    /// Gets a value indicating whether the cap types text.
    /// </summary>
    public bool IsText => Function == KeyFunction.Text;

    /// <summary>
    /// Gets a value indicating whether the cap types a string of more than one character, such as <c>.com</c>, as one commit.
    /// </summary>
    public bool IsShortcut => Function == KeyFunction.Text && Text.Length > 1;

    /// <summary>
    /// Gets a value indicating whether the cap types one space.
    /// </summary>
    public bool IsSpace => Function == KeyFunction.Text && Text == " ";

    /// <summary>
    /// Gets a value indicating whether the cap types one ASCII digit.
    /// </summary>
    public bool IsDigit => Function == KeyFunction.Text && Text.Length == 1 && Text[0] is >= '0' and <= '9';

    /// <summary>
    /// Gets a value indicating whether a held key repeats: Space, Backspace, Delete, the arrows, Page Up and Page Down.
    /// </summary>
    public bool Repeats => Function is KeyFunction.Backspace or KeyFunction.Left or KeyFunction.Right or KeyFunction.Up or KeyFunction.Down
        or KeyFunction.Delete or KeyFunction.PageUp or KeyFunction.PageDown || IsSpace;

    /// <summary>
    /// Gets a value indicating whether the cap is <see cref="CapsLock"/>.
    /// </summary>
    public bool IsCapsLock => Function == KeyFunction.Shift && Name == "caps";

    /// <summary>
    /// Gets a value indicating whether the cap is Shift, Control, Alt or Super.
    /// </summary>
    public bool IsModifier => Function is KeyFunction.Shift or KeyFunction.Control or KeyFunction.Alt or KeyFunction.Super;

    /// <summary>
    /// Gets a value indicating whether the cap goes out as a key with a keycode, such as Return, an arrow or a function key.
    /// </summary>
    public bool IsKeystroke => Function is KeyFunction.Backspace or KeyFunction.Return or KeyFunction.Tab or KeyFunction.Left
        or KeyFunction.Right or KeyFunction.Escape or KeyFunction.Up or KeyFunction.Down or KeyFunction.Home or KeyFunction.End
        or KeyFunction.PageUp or KeyFunction.PageDown or KeyFunction.Delete or KeyFunction.FunctionKey;

    /// <summary>
    /// Gets the page name of a page cap or the shape name of a shape cap. It is empty for any other cap.
    /// </summary>
    public string Target => Function is KeyFunction.Page or KeyFunction.ShapeChoice ? Name[(Name.IndexOf(':') + 1)..] : string.Empty;

    /// <summary>
    /// Gets the size that a size cap chooses. The value has no meaning for any other cap.
    /// </summary>
    public KeyboardSize Size => (KeyboardSize)Argument;

    /// <summary>
    /// Makes a text cap. A one-character text is a letter when it is <c>a</c> to <c>z</c>, <c>ä</c>, <c>ö</c> or <c>å</c>, in either case.
    /// </summary>
    /// <param name="text">The text that the cap types.</param>
    /// <param name="label">The label to draw, or <see langword="null"/> for the text.</param>
    /// <returns>A new cap.</returns>
    /// <exception cref="ArgumentException"><paramref name="text"/> is <see langword="null"/> or empty.</exception>
    public static KeyCap Character(string text, string? label = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        return new KeyCap(text, label);
    }

    /// <summary>
    /// Makes a text cap and sets whether it is a letter for the symbol page.
    /// </summary>
    /// <param name="text">The text that the cap types.</param>
    /// <param name="label">The label to draw, or <see langword="null"/> for the text.</param>
    /// <param name="letter"><see langword="true"/> to treat the cap as a letter.</param>
    /// <returns>A new cap.</returns>
    /// <exception cref="ArgumentException"><paramref name="text"/> is <see langword="null"/> or empty.</exception>
    public static KeyCap Character(string text, string? label, bool letter)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        return new KeyCap(text, label, letter);
    }

    /// <summary>
    /// Makes a language cap with a label.
    /// </summary>
    /// <param name="label">The label, for example <c>En</c>.</param>
    /// <returns>A new cap.</returns>
    public static KeyCap LanguageKey(string label) => new(KeyFunction.Language, "language", label);

    /// <summary>
    /// Makes an entry of the language key's long-press list.
    /// </summary>
    /// <param name="index">The language index, which becomes <see cref="Argument"/>.</param>
    /// <param name="label">The label to draw.</param>
    /// <returns>A new cap.</returns>
    public static KeyCap LanguageChoice(int index, string label) => new(KeyFunction.LanguageChoice, "language-" + index.ToString(CultureInfo.InvariantCulture), label, index);

    /// <summary>
    /// Gets the shared cap of a function key.
    /// </summary>
    /// <param name="number">The function key number, from 1 to 12.</param>
    /// <returns>The cap for F1 to F12.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="number"/> is not from 1 to 12.</exception>
    public static KeyCap FunctionKeyCap(int number) =>
        number is >= 1 and <= 12
            ? FunctionKeys[number - 1]
            : throw new ArgumentOutOfRangeException(nameof(number), number, "The function key number must be from 1 to 12.");

    /// <summary>
    /// Makes a cap that shows a named page, <c>{page:NAME}</c>.
    /// </summary>
    /// <param name="page">The page name.</param>
    /// <param name="label">The label to draw, or <see langword="null"/> for the default.</param>
    /// <returns>A new cap.</returns>
    /// <exception cref="ArgumentException"><paramref name="page"/> is <see langword="null"/> or empty.</exception>
    public static KeyCap PageKey(string page, string? label = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(page);
        return new KeyCap(KeyFunction.Page, "page:" + page, label);
    }

    /// <summary>
    /// Makes a cap that asks for a named keyboard shape, <c>{shape:NAME}</c>.
    /// </summary>
    /// <param name="shape">The shape name.</param>
    /// <param name="label">The label to draw, or <see langword="null"/> for the default.</param>
    /// <returns>A new cap.</returns>
    /// <exception cref="ArgumentException"><paramref name="shape"/> is <see langword="null"/> or empty.</exception>
    public static KeyCap ShapeKey(string shape, string? label = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(shape);
        return new KeyCap(KeyFunction.ShapeChoice, "shape:" + shape, label);
    }

    /// <summary>
    /// Makes a copy of the cap with a different label.
    /// </summary>
    /// <param name="label">The new label, or <see langword="null"/> for the default.</param>
    /// <returns>A new cap with the same function, text and argument.</returns>
    public KeyCap WithLabel(string? label) => IsText ? new KeyCap(Text, label, IsLetter) : new KeyCap(Function, Name, label, Argument);

    /// <summary>
    /// Gets the shared cap that chooses a size. An undefined value gives <see cref="Medium"/>.
    /// </summary>
    /// <param name="size">The size.</param>
    /// <returns>The size cap.</returns>
    public static KeyCap SizeCap(KeyboardSize size) => size switch
    {
        KeyboardSize.ExtraSmall => ExtraSmall,
        KeyboardSize.Small => Small,
        KeyboardSize.Large => Large,
        _ => Medium,
    };

    /// <summary>
    /// Reads a cap from its layout-file form: a function name in braces, for example <c>{backspace}</c>, or a text.
    /// </summary>
    /// <param name="token">The token from the layout file.</param>
    /// <param name="cap">The cap when the method returns <see langword="true"/>, otherwise <see langword="null"/>.</param>
    /// <returns><see langword="false"/> for an empty token or an unknown function name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="token"/> is <see langword="null"/>.</exception>
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

    /// <inheritdoc/>
    public override string ToString() => Function == KeyFunction.Text ? Text : Name;

    private static readonly KeyCap[] FunctionKeys = [.. Enumerable.Range(1, 12).Select(static n =>
        new KeyCap(KeyFunction.FunctionKey, "f" + n.ToString(CultureInfo.InvariantCulture), "F" + n.ToString(CultureInfo.InvariantCulture), n))];

    private static KeyCap SizeCap(KeyboardSize size, string label) =>
        new(KeyFunction.Size, "size-" + label.ToLowerInvariant(), label, (int)size);

    private static bool IsPagedLetter(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or 'ä' or 'ö' or 'å' or 'Ä' or 'Ö' or 'Å';
}
