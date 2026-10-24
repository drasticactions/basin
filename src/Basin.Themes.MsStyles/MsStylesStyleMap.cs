namespace Basin.Themes.MsStyles;

/// <summary>
/// Maps theme names to IDs: property names, enumeration value names, and the part and state names of each class. The tables are a port of wine's uxtheme style map. Names match without case.
/// </summary>
public static partial class MsStylesStyleMap
{
    private static readonly Dictionary<string, MsStylesPropertyName> ByPropertyName;
    private static readonly Dictionary<int, MsStylesPropertyName> ByPropertyId;
    private static readonly Dictionary<string, MsStylesClassMap> ByClassName;

    static MsStylesStyleMap()
    {
        ByPropertyName = BuildPropertyIndex();
        ByPropertyId = BuildIdIndex();
        ByClassName = new Dictionary<string, MsStylesClassMap>(StringComparer.OrdinalIgnoreCase);
        foreach (var map in Classes)
        {
            ByClassName[map.Name] = map;
        }
    }

    /// <summary>
    /// Gets every property name in the table.
    /// </summary>
    public static IReadOnlyList<MsStylesPropertyName> Properties => PropertyNames;

    /// <summary>
    /// Gets every enumeration value name in the table.
    /// </summary>
    public static IReadOnlyList<MsStylesEnumName> Enums => EnumNames;

    /// <summary>
    /// Gets the part and state names of every class in the table.
    /// </summary>
    public static IReadOnlyList<MsStylesClassMap> ClassMaps => Classes;

    /// <summary>
    /// Resolves a property name. A name that the table does not have resolves through the <see cref="ThemeProperty"/> member of the same name and <see cref="PrimitiveOfRange"/>.
    /// </summary>
    /// <param name="name">The property name, for example <c>BGTYPE</c>.</param>
    /// <param name="property">The name, ID and value type of the property, or the default value when the name does not resolve.</param>
    /// <returns><see langword="true"/> when the name resolves.</returns>
    public static bool TryLookupProperty(string name, out MsStylesPropertyName property)
    {
        if (ByPropertyName.TryGetValue(name, out property))
        {
            return true;
        }

        if (Enum.TryParse<ThemeProperty>(name, ignoreCase: true, out var id) && !int.TryParse(name, out _) &&
            PrimitiveOfRange((int)id) is var primitive && primitive != ThemePrimitive.None)
        {
            property = new MsStylesPropertyName(name.ToUpperInvariant(), primitive, id);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Finds the value type that the ID range of a property implies, as the TMT ranges define it.
    /// </summary>
    /// <param name="id">The property ID.</param>
    /// <returns>The value type, or <see cref="ThemePrimitive.None"/> when the ID is in no known range.</returns>
    public static ThemePrimitive PrimitiveOfRange(int id) => id switch
    {
        >= 600 and <= 608 => ThemePrimitive.String,
        >= 801 and <= 899 => ThemePrimitive.Font,
        >= 1201 and <= 1299 => ThemePrimitive.Size,
        >= 1601 and <= 1699 => ThemePrimitive.Color,
        >= 2201 and <= 2299 => ThemePrimitive.Bool,
        >= 2401 and <= 2499 => ThemePrimitive.Int,
        >= 2601 and <= 2699 => ThemePrimitive.Font,
        >= 3001 and <= 3099 => ThemePrimitive.Filename,
        >= 3201 and <= 3299 => ThemePrimitive.String,
        >= 3401 and <= 3499 => ThemePrimitive.Position,
        >= 3601 and <= 3699 => ThemePrimitive.Margins,
        >= 3801 and <= 3899 => ThemePrimitive.Color,
        >= 4001 and <= 4099 => ThemePrimitive.Enum,
        5001 => ThemePrimitive.Filename,
        5002 or 5004 or 5005 => ThemePrimitive.Rect,
        5003 => ThemePrimitive.Color,
        5006 => ThemePrimitive.Int,
        6000 => ThemePrimitive.IntList,
        8002 => ThemePrimitive.Rect,
        _ => ThemePrimitive.None,
    };

    /// <summary>
    /// Finds the table entry of a property ID. It does not use the ID ranges.
    /// </summary>
    /// <param name="id">The property ID.</param>
    /// <param name="property">The table entry, or the default value when the table has no entry for the ID.</param>
    /// <returns><see langword="true"/> when the table has an entry for the ID.</returns>
    public static bool TryLookupProperty(int id, out MsStylesPropertyName property) =>
        ByPropertyId.TryGetValue(id, out property);

    /// <summary>
    /// Resolves the name of an enumeration value.
    /// </summary>
    /// <param name="property">The enumerated property.</param>
    /// <param name="name">The value name, for example <c>IMAGEFILE</c>.</param>
    /// <param name="value">The integer value, or 0 when the name does not resolve.</param>
    /// <returns><see langword="true"/> when the property has a value with the name.</returns>
    public static bool TryLookupEnum(ThemeProperty property, string name, out int value)
    {
        foreach (var entry in EnumNames)
        {
            if (entry.Property == property && string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = entry.Value;
                return true;
            }
        }

        value = 0;
        return false;
    }

    /// <summary>
    /// Finds the name of an enumeration value.
    /// </summary>
    /// <param name="property">The enumerated property.</param>
    /// <param name="value">The integer value.</param>
    /// <returns>The value name, or <see langword="null"/> when the table has no name for it.</returns>
    public static string? EnumName(ThemeProperty property, int value)
    {
        foreach (var entry in EnumNames)
        {
            if (entry.Property == property && entry.Value == value)
            {
                return entry.Name;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks whether the table has value names for a property.
    /// </summary>
    /// <param name="property">The property to check.</param>
    /// <returns><see langword="true"/> when the table has at least one value name for the property.</returns>
    public static bool IsEnumProperty(ThemeProperty property)
    {
        foreach (var entry in EnumNames)
        {
            if (entry.Property == property)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether the table has part and state names for a class. An <c>app::</c> prefix is ignored.
    /// </summary>
    /// <param name="className">The class name, for example <c>WINDOW</c>.</param>
    /// <returns><see langword="true"/> when the table has the class.</returns>
    public static bool HasClass(string className) => ByClassName.ContainsKey(BaseName(className));

    /// <summary>
    /// Resolves the part and state names of an INI section. When <paramref name="part"/> is <see langword="null"/>, the state name is searched in every part. An <c>app::</c> prefix is ignored.
    /// </summary>
    /// <param name="className">The class name, for example <c>WINDOW</c>.</param>
    /// <param name="part">The part name, or <see langword="null"/> for none.</param>
    /// <param name="state">The state name, or <see langword="null"/> for none.</param>
    /// <param name="partId">The part ID, or 0 when <paramref name="part"/> is <see langword="null"/> or does not resolve.</param>
    /// <param name="stateId">The state ID, or 0 when <paramref name="state"/> is <see langword="null"/> or does not resolve.</param>
    /// <returns><see langword="true"/> when every name that is not <see langword="null"/> resolves.</returns>
    public static bool TryLookupPartState(string className, string? part, string? state, out int partId, out int stateId)
    {
        partId = 0;
        stateId = 0;
        if (ByClassName.TryGetValue(BaseName(className), out var map))
        {
            if (part is not null)
            {
                foreach (var entry in map.Entries)
                {
                    if (entry.State == 0 && string.Equals(entry.Name, part, StringComparison.OrdinalIgnoreCase))
                    {
                        partId = entry.Part;
                        break;
                    }
                }
            }

            if (state is not null && (part is null || partId != 0))
            {
                foreach (var entry in map.Entries)
                {
                    var partMatches = part is null ? true : entry.Part == partId;
                    if (partMatches && entry.State != 0 && string.Equals(entry.Name, state, StringComparison.OrdinalIgnoreCase))
                    {
                        stateId = entry.State;
                        break;
                    }
                }
            }
        }

        return (part is null || partId != 0) && (state is null || stateId != 0);
    }

    /// <summary>
    /// Finds the name of a part. An <c>app::</c> prefix is ignored.
    /// </summary>
    /// <param name="className">The class name.</param>
    /// <param name="part">The part ID.</param>
    /// <returns>The part name, or <see langword="null"/> when <paramref name="part"/> is 0 or the table has no name for it.</returns>
    public static string? PartName(string className, int part)
    {
        if (part == 0 || !ByClassName.TryGetValue(BaseName(className), out var map))
        {
            return null;
        }

        foreach (var entry in map.Entries)
        {
            if (entry.Part == part && entry.State == 0)
            {
                return entry.Name;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the name of a state. An <c>app::</c> prefix is ignored.
    /// </summary>
    /// <param name="className">The class name.</param>
    /// <param name="part">The part ID, or 0 to search the states of every part.</param>
    /// <param name="state">The state ID.</param>
    /// <returns>The state name, or <see langword="null"/> when <paramref name="state"/> is 0 or the table has no name for it.</returns>
    public static string? StateName(string className, int part, int state)
    {
        if (state == 0 || !ByClassName.TryGetValue(BaseName(className), out var map))
        {
            return null;
        }

        foreach (var entry in map.Entries)
        {
            if (entry.State == state && (part == 0 ? true : entry.Part == part))
            {
                return entry.Name;
            }
        }

        return null;
    }

    private static string BaseName(string className)
    {
        var separator = className.IndexOf("::", StringComparison.Ordinal);
        return separator >= 0 ? className[(separator + 2)..] : className;
    }

    private static Dictionary<string, MsStylesPropertyName> BuildPropertyIndex()
    {
        var index = new Dictionary<string, MsStylesPropertyName>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in PropertyNames)
        {
            index.TryAdd(entry.Name, entry);
        }

        return index;
    }

    private static Dictionary<int, MsStylesPropertyName> BuildIdIndex()
    {
        var index = new Dictionary<int, MsStylesPropertyName>();
        foreach (var entry in PropertyNames)
        {
            index.TryAdd((int)entry.Id, entry);
        }

        return index;
    }
}
