namespace Basin.SoftKeyboard.Tests;

internal static class Keys
{
    public static KeyboardLayout Layout(string id = "us-qwerty") =>
        KeyboardLayouts.Load(id) ?? throw new InvalidOperationException(id);

    public static SoftKeyboardEngine Engine(string id = "us-qwerty", float width = 1024, KeyTiming? timing = null)
    {
        var engine = new SoftKeyboardEngine(Layout(id).Grid(), timing);
        engine.Layout(width, 335, 5);
        return engine;
    }

    public static int Find(SoftKeyboardEngine engine, string name)
    {
        var grid = engine.Grid;
        for (var i = 0; i < grid.CellCount; i++)
        {
            if (grid[i] is { IsVisible: true, Plain: { } plain } && (plain.IsText ? plain.Text : plain.Name) == name)
            {
                return i;
            }
        }

        throw new InvalidOperationException($"no key {name}");
    }

    public static (float X, float Y) Center(SoftKeyboardEngine engine, string name)
    {
        var rect = engine.Rects[Find(engine, name)];
        return (rect.CenterX, rect.CenterY);
    }

    public static List<TypedKey> Record(SoftKeyboardEngine engine)
    {
        var typed = new List<TypedKey>();
        engine.Typed += typed.Add;
        return typed;
    }

    public static void Tap(SoftKeyboardEngine engine, string name, long now, int id = 1) =>
        engine.TapCell(id, Find(engine, name), now);

    public static void Down(SoftKeyboardEngine engine, string name, long now, int id = 1)
    {
        var (x, y) = Center(engine, name);
        engine.TouchDown(id, x, y, now);
        engine.Frame(now);
    }

    public static string Text(IEnumerable<TypedKey> typed) =>
        string.Concat(typed.Select(static t => t.Cap.IsText ? t.Text : "<" + t.Cap.Name + ">"));
}
