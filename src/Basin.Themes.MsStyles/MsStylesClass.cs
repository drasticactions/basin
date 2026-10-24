namespace Basin.Themes.MsStyles;

/// <summary>
/// Holds the properties of one theme class, grouped in part and state blocks. A lookup tries the exact part and state, then the part with state 0, then the class with part 0. At each step it walks this class, then <see cref="Next"/> and its successors.
/// </summary>
public sealed class MsStylesClass
{
    private readonly Dictionary<(int Part, int State), MsStylesPartState> _blocks = [];
    private readonly List<MsStylesPartState> _order = [];

    internal MsStylesClass(MsStylesTheme theme, string app, string name)
    {
        Theme = theme;
        App = app;
        Name = name;
    }

    /// <summary>
    /// Gets the theme that holds the class.
    /// </summary>
    public MsStylesTheme Theme { get; }

    /// <summary>
    /// Gets the application prefix of the class. It is empty for a class without an <c>app::</c> prefix.
    /// </summary>
    public string App { get; }

    /// <summary>
    /// Gets the class name without the application prefix, for example <c>WINDOW</c>.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the class name with its application prefix, as <c>app::class</c>. It is <see cref="Name"/> when <see cref="App"/> is empty.
    /// </summary>
    public string FullName => App.Length == 0 ? Name : App + "::" + Name;

    /// <summary>
    /// Gets the class that the lookup tries next. For <c>app::class</c> it is <c>class</c>, or <c>globals</c> when <c>class</c> does not exist. For another class it is <c>globals</c>. It is <see langword="null"/> for <c>globals</c> and when the theme has no <c>globals</c>.
    /// </summary>
    public MsStylesClass? Next { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether this class is the <c>globals</c> class, which ends every lookup chain.
    /// </summary>
    public bool IsGlobals => App.Length == 0 && string.Equals(Name, "globals", StringComparison.OrdinalIgnoreCase);

    internal IReadOnlyList<MsStylesPartState> Blocks => _order;

    /// <summary>
    /// Gets the part and state of each block in this class, in file order. It does not include the blocks of <see cref="Next"/>.
    /// </summary>
    public IEnumerable<(int Part, int State)> PartStates => _order.Select(b => (b.Part, b.State));

    /// <summary>
    /// Lists the properties that one block of this class sets. It does not follow the lookup chain.
    /// </summary>
    /// <param name="part">The part ID.</param>
    /// <param name="state">The state ID.</param>
    /// <returns>The property IDs in file order. The sequence is empty when this class has no such block.</returns>
    public IEnumerable<ThemeProperty> PropertiesOf(int part, int state) =>
        _blocks.TryGetValue((part, state), out var block) ? block.Order.Select(p => (ThemeProperty)p.Id) : [];

    /// <summary>
    /// Checks whether this class has a block for a part, in any state. It does not follow the lookup chain.
    /// </summary>
    /// <param name="part">The part ID.</param>
    /// <returns><see langword="true"/> when this class has at least one block for <paramref name="part"/>.</returns>
    public bool HasPart(int part)
    {
        foreach (var block in _order)
        {
            if (block.Part == part)
            {
                return true;
            }
        }

        return false;
    }

    internal MsStylesPartState Block(int part, int state)
    {
        if (!_blocks.TryGetValue((part, state), out var block))
        {
            block = new MsStylesPartState(part, state);
            _blocks[(part, state)] = block;
            _order.Add(block);
        }

        return block;
    }

    internal MsStylesProperty? Find(int part, int state, int id)
    {
        while (true)
        {
            for (var cls = this; cls is not null; cls = cls.Next)
            {
                if (cls._blocks.TryGetValue((part, state), out var block) && block.Properties.TryGetValue(id, out var property))
                {
                    return property;
                }
            }

            if (state != 0)
            {
                state = 0;
            }
            else if (part != 0)
            {
                part = 0;
            }
            else
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Finds the block that supplies a property through the lookup chain.
    /// </summary>
    /// <param name="part">The part ID, or 0 for the class.</param>
    /// <param name="state">The state ID, or 0 for the part.</param>
    /// <param name="id">The property to read.</param>
    /// <returns>The level of the block that sets the property, or <see cref="PropertyOrigin.NotFound"/> when no block sets it.</returns>
    public PropertyOrigin OriginOf(int part, int state, ThemeProperty id) =>
        Find(part, state, (int)id)?.Origin ?? PropertyOrigin.NotFound;

    /// <summary>
    /// Finds the value type of a property through the lookup chain.
    /// </summary>
    /// <param name="part">The part ID, or 0 for the class.</param>
    /// <param name="state">The state ID, or 0 for the part.</param>
    /// <param name="id">The property to read.</param>
    /// <returns>The value type that the theme stores, or <see cref="ThemePrimitive.None"/> when no block sets the property.</returns>
    public ThemePrimitive PrimitiveOf(int part, int state, ThemeProperty id) =>
        Find(part, state, (int)id)?.Primitive ?? ThemePrimitive.None;

    /// <summary>
    /// Reads an integer through the lookup chain. It accepts an integer, size, enumeration, Boolean or color value.
    /// </summary>
    /// <param name="part">The part ID, or 0 for the class.</param>
    /// <param name="state">The state ID, or 0 for the part.</param>
    /// <param name="id">The property to read.</param>
    /// <param name="value">The first stored integer, or 0 when the lookup fails. For a color it is the red component.</param>
    /// <returns><see langword="true"/> when a block in the chain sets the property with a matching type.</returns>
    public bool TryGetInt(int part, int state, ThemeProperty id, out int value)
    {
        if (Find(part, state, (int)id) is { Values.Length: > 0 } property &&
            property.Primitive is ThemePrimitive.Int or ThemePrimitive.Size or ThemePrimitive.Enum or ThemePrimitive.Bool or ThemePrimitive.Color)
        {
            value = property.Values[0];
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>
    /// Reads a size through the lookup chain. It accepts a size or integer value.
    /// </summary>
    /// <param name="part">The part ID, or 0 for the class.</param>
    /// <param name="state">The state ID, or 0 for the part.</param>
    /// <param name="id">The property to read.</param>
    /// <param name="value">The size in pixels, or 0 when the lookup fails.</param>
    /// <returns><see langword="true"/> when a block in the chain sets the property with a matching type.</returns>
    public bool TryGetSize(int part, int state, ThemeProperty id, out int value)
    {
        if (Find(part, state, (int)id) is { Values.Length: > 0 } property &&
            property.Primitive is ThemePrimitive.Size or ThemePrimitive.Int)
        {
            value = property.Values[0];
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>
    /// Reads a Boolean through the lookup chain. It accepts a Boolean or integer value.
    /// </summary>
    /// <param name="part">The part ID, or 0 for the class.</param>
    /// <param name="state">The state ID, or 0 for the part.</param>
    /// <param name="id">The property to read.</param>
    /// <param name="value"><see langword="true"/> when the stored integer is not 0. It is <see langword="false"/> when the lookup fails.</param>
    /// <returns><see langword="true"/> when a block in the chain sets the property with a matching type.</returns>
    public bool TryGetBool(int part, int state, ThemeProperty id, out bool value)
    {
        if (Find(part, state, (int)id) is { Values.Length: > 0 } property &&
            property.Primitive is ThemePrimitive.Bool or ThemePrimitive.Int)
        {
            value = property.Values[0] != 0;
            return true;
        }

        value = false;
        return false;
    }

    /// <summary>
    /// Reads an enumeration value through the lookup chain. It accepts an enumeration or integer value, and fails when <typeparamref name="TEnum"/> is not 4 bytes.
    /// </summary>
    /// <typeparam name="TEnum">The enumeration type to cast to. Its underlying type must be 4 bytes.</typeparam>
    /// <param name="part">The part ID, or 0 for the class.</param>
    /// <param name="state">The state ID, or 0 for the part.</param>
    /// <param name="id">The property to read.</param>
    /// <param name="value">The stored value cast to <typeparamref name="TEnum"/>, or the default value when the lookup fails.</param>
    /// <returns><see langword="true"/> when a block in the chain sets the property with a matching type.</returns>
    public bool TryGetEnum<TEnum>(int part, int state, ThemeProperty id, out TEnum value)
        where TEnum : struct, Enum
    {
        if (System.Runtime.CompilerServices.Unsafe.SizeOf<TEnum>() == sizeof(int) &&
            Find(part, state, (int)id) is { Values.Length: > 0 } property &&
            property.Primitive is ThemePrimitive.Enum or ThemePrimitive.Int)
        {
            var raw = property.Values[0];
            value = System.Runtime.CompilerServices.Unsafe.As<int, TEnum>(ref raw);
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Reads a color through the lookup chain. It accepts only a color value.
    /// </summary>
    /// <param name="part">The part ID, or 0 for the class.</param>
    /// <param name="state">The state ID, or 0 for the part.</param>
    /// <param name="id">The property to read.</param>
    /// <param name="value">The color, or the default value when the lookup fails.</param>
    /// <returns><see langword="true"/> when a block in the chain sets the property with a matching type.</returns>
    public bool TryGetColor(int part, int state, ThemeProperty id, out MsStylesColor value)
    {
        if (Find(part, state, (int)id) is { Primitive: ThemePrimitive.Color, Values.Length: >= 3 } property)
        {
            value = new MsStylesColor((byte)property.Values[0], (byte)property.Values[1], (byte)property.Values[2]);
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Reads margins through the lookup chain. It accepts only a margins value.
    /// </summary>
    /// <param name="part">The part ID, or 0 for the class.</param>
    /// <param name="state">The state ID, or 0 for the part.</param>
    /// <param name="id">The property to read.</param>
    /// <param name="value">The margins, or the default value when the lookup fails.</param>
    /// <returns><see langword="true"/> when a block in the chain sets the property with a matching type.</returns>
    public bool TryGetMargins(int part, int state, ThemeProperty id, out MsStylesMargins value)
    {
        if (Find(part, state, (int)id) is { Primitive: ThemePrimitive.Margins, Values.Length: >= 4 } property)
        {
            value = new MsStylesMargins(property.Values[0], property.Values[1], property.Values[2], property.Values[3]);
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Reads a rectangle through the lookup chain. It accepts only a rectangle value.
    /// </summary>
    /// <param name="part">The part ID, or 0 for the class.</param>
    /// <param name="state">The state ID, or 0 for the part.</param>
    /// <param name="id">The property to read.</param>
    /// <param name="value">The rectangle, or the default value when the lookup fails.</param>
    /// <returns><see langword="true"/> when a block in the chain sets the property with a matching type.</returns>
    public bool TryGetRect(int part, int state, ThemeProperty id, out MsStylesRect value)
    {
        if (Find(part, state, (int)id) is { Primitive: ThemePrimitive.Rect, Values.Length: >= 4 } property)
        {
            value = new MsStylesRect(property.Values[0], property.Values[1], property.Values[2], property.Values[3]);
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Reads a position through the lookup chain. It accepts only a position value.
    /// </summary>
    /// <param name="part">The part ID, or 0 for the class.</param>
    /// <param name="state">The state ID, or 0 for the part.</param>
    /// <param name="id">The property to read.</param>
    /// <param name="value">The position, or the default value when the lookup fails.</param>
    /// <returns><see langword="true"/> when a block in the chain sets the property with a matching type.</returns>
    public bool TryGetPosition(int part, int state, ThemeProperty id, out MsStylesPosition value)
    {
        if (Find(part, state, (int)id) is { Primitive: ThemePrimitive.Position, Values.Length: >= 2 } property)
        {
            value = new MsStylesPosition(property.Values[0], property.Values[1]);
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Reads a list of integers through the lookup chain. It accepts only an integer list value.
    /// </summary>
    /// <param name="part">The part ID, or 0 for the class.</param>
    /// <param name="state">The state ID, or 0 for the part.</param>
    /// <param name="id">The property to read.</param>
    /// <param name="value">The stored integers, or an empty span when the lookup fails. The span refers to the theme's own storage.</param>
    /// <returns><see langword="true"/> when a block in the chain sets the property with a matching type.</returns>
    public bool TryGetIntList(int part, int state, ThemeProperty id, out ReadOnlySpan<int> value)
    {
        if (Find(part, state, (int)id) is { Primitive: ThemePrimitive.IntList } property)
        {
            value = property.Values;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Reads text through the lookup chain. It accepts a string or file name value.
    /// </summary>
    /// <param name="part">The part ID, or 0 for the class.</param>
    /// <param name="state">The state ID, or 0 for the part.</param>
    /// <param name="id">The property to read.</param>
    /// <param name="value">The text, or empty when the lookup fails.</param>
    /// <returns><see langword="true"/> when a block in the chain sets the property with a matching type.</returns>
    public bool TryGetString(int part, int state, ThemeProperty id, out string value)
    {
        if (Find(part, state, (int)id) is { Text: { } text } property &&
            property.Primitive is ThemePrimitive.String or ThemePrimitive.Filename)
        {
            value = text;
            return true;
        }

        value = string.Empty;
        return false;
    }

    /// <summary>
    /// Reads a font through the lookup chain. It accepts any property that holds a font.
    /// </summary>
    /// <param name="part">The part ID, or 0 for the class.</param>
    /// <param name="state">The state ID, or 0 for the part.</param>
    /// <param name="id">The property to read.</param>
    /// <param name="value">The font, or the default value when the lookup fails.</param>
    /// <returns><see langword="true"/> when a block in the chain sets the property with a matching type.</returns>
    public bool TryGetFont(int part, int state, ThemeProperty id, out MsStylesFont value)
    {
        if (Find(part, state, (int)id) is { HasFont: true } property)
        {
            value = property.Font;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Reads an image through the lookup chain. When <paramref name="id"/> is <see cref="ThemeProperty.AtlasImage"/> and no image is found, it also reads <see cref="ThemeProperty.DiskStream"/>.
    /// </summary>
    /// <param name="part">The part ID, or 0 for the class.</param>
    /// <param name="state">The state ID, or 0 for the part.</param>
    /// <param name="id">The property to read.</param>
    /// <param name="value">The image, or the default value when the lookup fails.</param>
    /// <returns><see langword="true"/> when a block in the chain sets the property with a matching type.</returns>
    public bool TryGetImage(int part, int state, ThemeProperty id, out MsStylesImage value)
    {
        if (Find(part, state, (int)id) is { HasImage: true } property)
        {
            value = property.Image;
            return true;
        }

        if (id == ThemeProperty.AtlasImage && Find(part, state, (int)ThemeProperty.DiskStream) is { HasImage: true } stream)
        {
            value = stream.Image;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Gets the class name with its application prefix.
    /// </summary>
    /// <returns>The <see cref="FullName"/> of the class.</returns>
    public override string ToString() => FullName;
}
