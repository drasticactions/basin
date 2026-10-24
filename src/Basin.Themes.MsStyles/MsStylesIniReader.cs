using System.Text;

namespace Basin.Themes.MsStyles;

internal static class MsStylesIniReader
{
    public const string SysMetricsClass = "sysmetrics";
    public const string DocumentationClass = "documentation";

    public static void Read(MsStylesTheme theme, string? color, string? size)
    {
        var resources = theme.Resources;
        var colors = Names(resources, "COLORNAMES");
        var sizes = Names(resources, "SIZENAMES");
        var files = Names(resources, "FILERESNAMES");
        if (colors.Count == 0 || sizes.Count == 0)
        {
            throw new MsStylesFormatException("A PACKTHEM 3 file needs COLORNAMES and SIZENAMES.");
        }

        var colorIndex = Select(colors, color, "color scheme");
        var sizeIndex = Select(sizes, size, "size");
        theme.SetVariants(colors, sizes, colors[colorIndex], sizes[sizeIndex]);

        if (resources.TryFind("TEXTFILE", "THEMES_INI", out var themesIni))
        {
            ReadThemesIni(theme, Text(themesIni));
        }

        var index = sizes.Count * colorIndex + sizeIndex;
        if (index >= files.Count || !resources.TryFind("TEXTFILE", files[index], out var variant))
        {
            throw new MsStylesFormatException($"The INI for {colors[colorIndex]}/{sizes[sizeIndex]} is missing.");
        }

        ReadVariant(theme, Text(variant));
    }

    public static IReadOnlyList<string> Names(PeResources resources, string type)
    {
        if (!resources.TryFind(type, 1, out var bytes))
        {
            return [];
        }

        var names = new List<string>();
        foreach (var name in Text(bytes).Split('\0'))
        {
            if (name.Length == 0)
            {
                break;
            }

            names.Add(name);
        }

        return names;
    }

    public static int Select(IReadOnlyList<string> names, string? wanted, string what)
    {
        if (string.IsNullOrEmpty(wanted))
        {
            return 0;
        }

        for (var i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], wanted, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        throw new ArgumentException($"The theme has no {what} '{wanted}'. It has {string.Join(", ", names)}.", nameof(wanted));
    }

    public static bool TryParseSectionName(string section, out string app, out string className, out string? part, out string? state)
    {
        app = string.Empty;
        part = null;
        state = null;
        var rest = section.AsSpan();
        var appSeparator = rest.IndexOf("::", StringComparison.Ordinal);
        if (appSeparator >= 0)
        {
            app = rest[..appSeparator].Trim().ToString();
            rest = rest[(appSeparator + 2)..];
        }

        var dot = rest.IndexOf('.');
        var open = rest.IndexOf('(');
        ReadOnlySpan<char> classSpan;
        ReadOnlySpan<char> partState = default;
        if (dot >= 0 && (open < 0 || dot < open))
        {
            classSpan = rest[..dot];
            partState = rest[(dot + 1)..];
            var partOpen = partState.IndexOf('(');
            if (partOpen >= 0)
            {
                var close = partState.IndexOf(')');
                if (close < partOpen)
                {
                    className = string.Empty;
                    return false;
                }

                state = partState[(partOpen + 1)..close].Trim().ToString();
                part = partState[..partOpen].Trim().ToString();
            }
            else
            {
                part = partState.Trim().ToString();
            }
        }
        else if (open >= 0)
        {
            classSpan = rest[..open];
            var close = rest.IndexOf(')');
            if (close < open)
            {
                className = string.Empty;
                return false;
            }

            state = rest[(open + 1)..close].Trim().ToString();
        }
        else
        {
            classSpan = rest;
        }

        className = classSpan.Trim().ToString();
        return className.Length > 0;
    }

    private static void ReadThemesIni(MsStylesTheme theme, string text)
    {
        var ini = MsStylesIni.Parse(text);
        foreach (var section in ini.Sections)
        {
            if (string.Equals(section.Name, DocumentationClass, StringComparison.OrdinalIgnoreCase))
            {
                var block = theme.AddClass(string.Empty, DocumentationClass).Block(0, 0);
                foreach (var (name, value) in section.Values)
                {
                    if (MsStylesStyleMap.TryLookupProperty(name, out var property) && property.Primitive == ThemePrimitive.String)
                    {
                        block.TryAdd(new MsStylesProperty((int)property.Id, ThemePrimitive.String, PropertyOrigin.Class, 3) { Text = value });
                    }
                    else if (string.Equals(name, "ThemeName", StringComparison.OrdinalIgnoreCase))
                    {
                        block.TryAdd(new MsStylesProperty((int)ThemeProperty.Name, ThemePrimitive.String, PropertyOrigin.Class, 3) { Text = value });
                    }
                }

                continue;
            }

            var displayName = section.Values.FirstOrDefault(v => string.Equals(v.Name, "DisplayName", StringComparison.OrdinalIgnoreCase)).Value;
            if (displayName is null)
            {
                continue;
            }

            if (section.Name.StartsWith("ColorScheme.", StringComparison.OrdinalIgnoreCase))
            {
                theme.SetColorDisplayName(section.Name["ColorScheme.".Length..], displayName);
            }
            else if (section.Name.StartsWith("Size.", StringComparison.OrdinalIgnoreCase))
            {
                theme.SetSizeDisplayName(section.Name["Size.".Length..], displayName);
            }
        }
    }

    private static void ReadVariant(MsStylesTheme theme, string text)
    {
        var diagnostics = theme.Diagnostics;
        var ini = MsStylesIni.Parse(text, diagnostics);
        foreach (var section in ini.Sections)
        {
            MsStylesPartState block;
            bool isGlobal;
            if (string.Equals(section.Name, "SysMetrics", StringComparison.OrdinalIgnoreCase))
            {
                block = theme.AddClass(string.Empty, SysMetricsClass).Block(0, 0);
                isGlobal = true;
            }
            else
            {
                if (!TryParseSectionName(section.Name, out var app, out var className, out var partName, out var stateName))
                {
                    diagnostics.Add("section", section.Name);
                    continue;
                }

                if (!MsStylesStyleMap.TryLookupPartState(className, partName, stateName, out var part, out var state))
                {
                    var kind = !MsStylesStyleMap.HasClass(className) ? "class"
                        : !MsStylesStyleMap.TryLookupPartState(className, partName, null, out _, out _) ? "part"
                        : "state";
                    diagnostics.Add(kind, section.Name);
                    continue;
                }

                block = theme.AddClass(app, className).Block(part, state);
                isGlobal = app.Length == 0 && string.Equals(className, "globals", StringComparison.OrdinalIgnoreCase);
            }

            var origin = block.State != 0 ? PropertyOrigin.State
                : block.Part != 0 ? PropertyOrigin.Part
                : isGlobal ? PropertyOrigin.Global
                : PropertyOrigin.Class;
            foreach (var (name, value) in section.Values)
            {
                if (!MsStylesStyleMap.TryLookupProperty(name, out var property))
                {
                    diagnostics.Add("property", name);
                    continue;
                }

                if (block.Properties.ContainsKey((int)property.Id))
                {
                    continue;
                }

                if (Parse(theme, property, value, origin) is { } parsed)
                {
                    block.TryAdd(parsed);
                }
            }
        }
    }

    private static MsStylesProperty? Parse(MsStylesTheme theme, MsStylesPropertyName name, string value, PropertyOrigin origin)
    {
        var property = new MsStylesProperty((int)name.Id, name.Primitive, origin, 3);
        var diagnostics = theme.Diagnostics;
        switch (name.Primitive)
        {
            case ThemePrimitive.Enum:
                if (MsStylesStyleMap.TryLookupEnum(name.Id, value, out var enumValue))
                {
                    property.Values = [enumValue];
                }
                else if (MsStylesValueParser.Integers(value, 1) is [var number])
                {
                    property.Values = [number];
                }
                else
                {
                    diagnostics.Add("enum", $"{name.Name}={value}");
                    return null;
                }

                break;
            case ThemePrimitive.String:
                property.Text = value;
                break;
            case ThemePrimitive.Bool:
                property.Values = [MsStylesValueParser.ParseBool(value) ? 1 : 0];
                break;
            case ThemePrimitive.Int:
            case ThemePrimitive.Size:
                return WithIntegers(property, value, 1, name, diagnostics);
            case ThemePrimitive.Color:
                return WithIntegers(property, value, 3, name, diagnostics);
            case ThemePrimitive.Margins:
            case ThemePrimitive.Rect:
                return WithIntegers(property, value, 4, name, diagnostics);
            case ThemePrimitive.Position:
                return WithIntegers(property, value, 2, name, diagnostics);
            case ThemePrimitive.IntList:
                property.Values = MsStylesValueParser.Integers(value, MsStylesValueParser.MaxIntList);
                break;
            case ThemePrimitive.Font:
                if (!MsStylesValueParser.TryParseFont(value, out var font))
                {
                    diagnostics.Add("value", $"{name.Name}={value}");
                    return null;
                }

                property.Font = font;
                property.HasFont = true;
                property.Text = value;
                break;
            case ThemePrimitive.Filename:
                property.Text = value;
                var resourceName = MsStylesValueParser.ResourceNameOf(value);
                if (theme.TryImage(PeResources.BitmapType, resourceName, out var image))
                {
                    property.Image = image;
                    property.HasImage = true;
                }
                else
                {
                    diagnostics.Add("image", resourceName);
                }

                break;
            default:
                diagnostics.Add("primitive", $"{name.Name}:{name.Primitive}");
                return null;
        }

        return property;
    }

    private static MsStylesProperty? WithIntegers(MsStylesProperty property, string value, int count, MsStylesPropertyName name, MsStylesDiagnostics diagnostics)
    {
        var values = MsStylesValueParser.Integers(value, count);
        if (values.Length < count)
        {
            diagnostics.Add("value", $"{name.Name}={value}");
            return null;
        }

        property.Values = values;
        return property;
    }

    private static string Text(ReadOnlyMemory<byte> bytes) => Encoding.Unicode.GetString(bytes.Span);
}
