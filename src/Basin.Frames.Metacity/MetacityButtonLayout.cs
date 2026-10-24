namespace Basin.Frames.Metacity;

/// <summary>
/// A parsed button layout in marco's <c>left:right</c> grammar, for example <c>menu:minimize,maximize,close</c>. It is immutable.
/// </summary>
public sealed class MetacityButtonLayout
{
    internal const int MaxButtonsPerCorner = 11;

    private MetacityButtonLayout(string text)
    {
        Text = text;
        Left = new MetacityButtonFunction[MaxButtonsPerCorner + 1];
        LeftSpacer = new bool[MaxButtonsPerCorner + 1];
        Right = new MetacityButtonFunction[MaxButtonsPerCorner + 1];
        RightSpacer = new bool[MaxButtonsPerCorner + 1];
        Array.Fill(Left, MetacityButtonFunction.None);
        Array.Fill(Right, MetacityButtonFunction.None);
    }

    /// <summary>
    /// Gets the layout <c>menu:minimize,maximize,close</c>.
    /// </summary>
    public static MetacityButtonLayout Default { get; } = Parse("menu:minimize,maximize,close");

    /// <summary>
    /// Gets the text that the layout was parsed from. It is empty for a <see langword="null"/> input.
    /// </summary>
    public string Text { get; }

    internal MetacityButtonFunction[] Left { get; }

    internal bool[] LeftSpacer { get; }

    internal MetacityButtonFunction[] Right { get; }

    internal bool[] RightSpacer { get; }

    internal int LeftCount { get; private set; }

    internal int RightCount { get; private set; }

    /// <summary>
    /// Parses a button layout as marco does. Unknown names are skipped, a repeated button is ignored, and <c>spacer</c> adds a gap after the button before it.
    /// </summary>
    /// <param name="text">The layout text, or <see langword="null"/> for no buttons.</param>
    /// <returns>The layout. Parsing never fails.</returns>
    public static MetacityButtonLayout Parse(string? text)
    {
        var layout = new MetacityButtonLayout(text ?? string.Empty);
        if (string.IsNullOrEmpty(text))
        {
            return layout;
        }

        var sides = text.Split(':', 2);
        layout.LeftCount = ParseSide(sides[0], layout.Left, layout.LeftSpacer);
        if (sides.Length > 1)
        {
            layout.RightCount = ParseSide(sides[1], layout.Right, layout.RightSpacer);
        }

        return layout;
    }

    /// <summary>
    /// Gives the text that the layout was parsed from.
    /// </summary>
    /// <returns>The value of <see cref="Text"/>.</returns>
    public override string ToString() => Text;

    private static int ParseSide(string side, MetacityButtonFunction[] functions, bool[] spacers)
    {
        Span<bool> used = stackalloc bool[MaxButtonsPerCorner];
        var i = 0;
        foreach (var name in side.Split(','))
        {
            var function = FunctionFromName(name);
            if (i > 0 && name == "spacer")
            {
                spacers[i - 1] = true;
                var opposite = Opposite(function);
                if (opposite != MetacityButtonFunction.None && i >= 2)
                {
                    spacers[i - 2] = true;
                }
            }
            else if (function != MetacityButtonFunction.None && !used[(int)function] && i < MaxButtonsPerCorner)
            {
                functions[i] = function;
                used[(int)function] = true;
                i++;
                var opposite = Opposite(function);
                if (opposite != MetacityButtonFunction.None && i < MaxButtonsPerCorner)
                {
                    functions[i++] = opposite;
                }
            }
        }

        functions[i] = MetacityButtonFunction.None;
        spacers[i] = false;
        return i;
    }

    private static MetacityButtonFunction FunctionFromName(string name) => name switch
    {
        "menu" => MetacityButtonFunction.Menu,
        "appmenu" => MetacityButtonFunction.AppMenu,
        "minimize" => MetacityButtonFunction.Minimize,
        "maximize" => MetacityButtonFunction.Maximize,
        "close" => MetacityButtonFunction.Close,
        "shade" => MetacityButtonFunction.Shade,
        "above" => MetacityButtonFunction.Above,
        "stick" => MetacityButtonFunction.Stick,
        _ => MetacityButtonFunction.None,
    };

    internal static MetacityButtonFunction Opposite(MetacityButtonFunction function) => function switch
    {
        MetacityButtonFunction.Shade => MetacityButtonFunction.Unshade,
        MetacityButtonFunction.Unshade => MetacityButtonFunction.Shade,
        MetacityButtonFunction.Above => MetacityButtonFunction.Unabove,
        MetacityButtonFunction.Unabove => MetacityButtonFunction.Above,
        MetacityButtonFunction.Stick => MetacityButtonFunction.Unstick,
        MetacityButtonFunction.Unstick => MetacityButtonFunction.Stick,
        _ => MetacityButtonFunction.None,
    };
}
