using System.Globalization;
using System.Text;
using System.Xml;

namespace Basin.Frames.Metacity;

internal sealed class MetacityThemeReader
{
    private const int MaxReasonable = 4096;
    private const int ThemeVersion = 1000 * MetacityThemes.MajorVersion + MetacityThemes.MinorVersion;

    private readonly XmlReader _reader;
    private readonly IXmlLineInfo _lines;
    private readonly MetacityTheme _theme;
    private readonly Stack<int> _required = new();
    private readonly List<(string Name, string Value)> _attributes = [];
    private readonly HashSet<int> _consumed = [];

    private MetacityThemeReader(XmlReader reader, MetacityTheme theme)
    {
        _reader = reader;
        _lines = (IXmlLineInfo)reader;
        _theme = theme;
    }

    public static MetacityTheme Parse(string text, string name, string directory, string? filePath, int majorVersion)
    {
        var settings = new XmlReaderSettings
        {
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };
        using var xml = XmlReader.Create(new StringReader(Repair(text)), settings);
        var theme = new MetacityTheme(name, directory, filePath, 1000 * majorVersion);
        var parser = new MetacityThemeReader(xml, theme);
        try
        {
            parser.ReadDocument();
        }
        catch (XmlException e)
        {
            throw new MetacityThemeException($"Line {e.LineNumber} character {e.LinePosition}: {e.Message}", e);
        }

        return theme;
    }

    internal static string Repair(string text)
    {
        var builder = new StringBuilder(text.Length + 16);
        var inTag = false;
        var quote = '\0';
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (!inTag)
            {
                if (ch == '<')
                {
                    if (string.CompareOrdinal(text, i, "<!--", 0, 4) == 0)
                    {
                        var end = text.IndexOf("-->", i, StringComparison.Ordinal);
                        end = end < 0 ? text.Length : end + 3;
                        builder.Append(text, i, end - i);
                        i = end - 1;
                        continue;
                    }

                    if (string.CompareOrdinal(text, i, "<![CDATA[", 0, 9) == 0)
                    {
                        var end = text.IndexOf("]]>", i, StringComparison.Ordinal);
                        end = end < 0 ? text.Length : end + 3;
                        builder.Append(text, i, end - i);
                        i = end - 1;
                        continue;
                    }

                    inTag = true;
                }

                builder.Append(ch);
                continue;
            }

            if (quote != '\0' && ch == '<')
            {
                builder.Append("&lt;");
                continue;
            }

            builder.Append(ch);
            if (quote != '\0')
            {
                if (ch == quote)
                {
                    quote = '\0';
                    if (i + 1 < text.Length && (char.IsLetter(text[i + 1]) || text[i + 1] is '_' or ':'))
                    {
                        builder.Append(' ');
                    }
                }
            }
            else if (ch is '"' or '\'')
            {
                quote = ch;
            }
            else if (ch == '>')
            {
                inTag = false;
            }
        }

        return builder.ToString();
    }

    private MetacityThemeException Error(string message) =>
        new($"Line {_lines.LineNumber} character {_lines.LinePosition}: {message}");

    private void ReadDocument()
    {
        while (_reader.Read() && _reader.NodeType != XmlNodeType.Element)
        {
        }

        if (_reader.NodeType != XmlNodeType.Element)
        {
            throw Error("Outermost element in theme must be <metacity_theme> not <>");
        }

        var element = _reader.Name;
        ReadAttributes();
        if (Enter(element, root: true))
        {
            throw Error($"Outermost element in theme must be <metacity_theme> not <{element}>");
        }

        if (element != "metacity_theme")
        {
            throw Error($"Outermost element in theme must be <metacity_theme> not <{element}>");
        }

        _theme.FormatVersion = _required.Peek();
        CheckNoAttributes(element);
        ForEachChild(element, ReadThemeChild);
        if (_theme.Validate() is { } error)
        {
            throw Error(error);
        }

        Leave();
    }

    private void ReadAttributes()
    {
        _attributes.Clear();
        _consumed.Clear();
        if (_reader.MoveToFirstAttribute())
        {
            do
            {
                _attributes.Add((_reader.Name, _reader.Value));
            }
            while (_reader.MoveToNextAttribute());
            _reader.MoveToElement();
        }
    }

    private bool Enter(string element, bool root = false)
    {
        var required = _required.Count > 0 ? _required.Peek() : _theme.FormatVersion;
        for (var i = 0; i < _attributes.Count; i++)
        {
            if (_attributes[i].Name != "version")
            {
                continue;
            }

            _consumed.Add(i);
            if (required < 3000)
            {
                throw Error("\"version\" attribute cannot be used in metacity-theme-1.xml or metacity-theme-2.xml");
            }

            var (satisfied, elementRequired) = CheckVersion(_attributes[i].Value);
            if (root)
            {
                if (!satisfied)
                {
                    throw new MetacityThemeException(
                        $"Line {_lines.LineNumber} character {_lines.LinePosition}: Theme requires version {_attributes[i].Value} but latest supported theme version is {MetacityThemes.MajorVersion}.{MetacityThemes.MinorVersion}")
                    {
                        TooOld = true,
                    };
                }

                if (elementRequired > _theme.FormatVersion)
                {
                    _theme.FormatVersion = elementRequired;
                    required = elementRequired;
                }
            }
            else if (!satisfied)
            {
                return true;
            }

            if (elementRequired > required)
            {
                required = elementRequired;
            }
        }

        _required.Push(required);
        return false;
    }

    private void Leave() => _required.Pop();

    private (bool Satisfied, int Required) CheckVersion(string text)
    {
        var s = text.AsSpan().Trim();
        if (s.Length == 0 || s[0] is not ('<' or '>'))
        {
            throw Error($"Bad version specification '{text}'");
        }

        var comparison = s[0];
        var orEqual = s.Length > 1 && s[1] == '=';
        s = s[(orEqual ? 2 : 1)..].Trim();
        var dot = s.IndexOf('.');
        var majorText = dot < 0 ? s : s[..dot];
        var minorText = dot < 0 ? default : s[(dot + 1)..];
        if (majorText.Length == 0 || !int.TryParse(majorText, NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            (dot >= 0 && (minorText.Length == 0 || !int.TryParse(minorText, NumberStyles.None, CultureInfo.InvariantCulture, out _))))
        {
            throw Error($"Bad version specification '{text}'");
        }

        var version = 1000 * major;
        if (dot >= 0)
        {
            version += int.Parse(minorText, NumberStyles.None, CultureInfo.InvariantCulture);
        }

        if (comparison == '<')
        {
            return (orEqual ? ThemeVersion <= version : ThemeVersion < version, 0);
        }

        return orEqual ? (ThemeVersion >= version, version) : (ThemeVersion > version, version + 1);
    }

    private delegate void ChildHandler(string element);

    private void ForEachChild(string owner, ChildHandler handler, Action<string>? onText = null)
    {
        var empty = _reader.IsEmptyElement;
        _reader.Read();
        if (empty)
        {
            return;
        }

        while (true)
        {
            switch (_reader.NodeType)
            {
                case XmlNodeType.Element:
                {
                    var child = _reader.Name;
                    ReadAttributes();
                    if (Enter(child))
                    {
                        _reader.Skip();
                        continue;
                    }

                    handler(child);
                    Leave();
                    continue;
                }

                case XmlNodeType.EndElement:
                    _reader.Read();
                    return;
                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.SignificantWhitespace:
                    if (onText is null)
                    {
                        if (!string.IsNullOrWhiteSpace(_reader.Value))
                        {
                            throw Error($"No text is allowed inside element <{owner}>");
                        }
                    }
                    else
                    {
                        onText(_reader.Value);
                    }

                    _reader.Read();
                    continue;
                case XmlNodeType.None:
                    return;
                default:
                    _reader.Read();
                    continue;
            }
        }
    }

    private void Leaf(string element, string nestedMessage) =>
        ForEachChild(element, _ => throw Error(nestedMessage));

    private string? Attribute(string name)
    {
        for (var i = 0; i < _attributes.Count; i++)
        {
            if (_attributes[i].Name == name)
            {
                _consumed.Add(i);
                return _attributes[i].Value;
            }
        }

        return null;
    }

    private string Required(string name, string element) =>
        Attribute(name) ?? throw Error($"No \"{name}\" attribute on element <{element}>");

    private void CheckUnknown(string element)
    {
        for (var i = 0; i < _attributes.Count; i++)
        {
            if (!_consumed.Contains(i))
            {
                throw Error($"Attribute \"{_attributes[i].Name}\" is invalid on <{element}> element in this context");
            }
        }
    }

    private void Locate(string element, params string[] names)
    {
        foreach (var name in names)
        {
            Attribute(name.TrimStart('!'));
        }

        CheckUnknown(element);
        foreach (var name in names)
        {
            if (name[0] == '!' && Attribute(name[1..]) is null)
            {
                throw Error($"No \"{name[1..]}\" attribute on element <{element}>");
            }
        }
    }

    private void CheckNoAttributes(string element)
    {
        for (var i = 0; i < _attributes.Count; i++)
        {
            if (_attributes[i].Name != "version")
            {
                throw Error($"Attribute \"{_attributes[i].Name}\" is invalid on <{element}> element in this context");
            }
        }
    }

    private int PositiveInteger(string text)
    {
        long l;
        if (_theme.TryGetIntConstant(text, out var constant))
        {
            l = constant;
        }
        else
        {
            var span = text.AsSpan().TrimStart();
            var end = 0;
            if (end < span.Length && span[end] is '+' or '-')
            {
                end++;
            }

            while (end < span.Length && char.IsAsciiDigit(span[end]))
            {
                end++;
            }

            if (end == 0 || !long.TryParse(span[..end], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out l))
            {
                throw Error($"Could not parse \"{text}\" as an integer");
            }

            if (end != span.Length)
            {
                throw Error($"Did not understand trailing characters \"{span[end..]}\" in string \"{text}\"");
            }
        }

        if (l < 0)
        {
            throw Error($"Integer {l} must be positive");
        }

        if (l > MaxReasonable)
        {
            throw Error($"Integer {l} is too large, current max is {MaxReasonable}");
        }

        return (int)l;
    }

    private double Double(string text)
    {
        var span = text.AsSpan().Trim();
        var end = 0;
        while (end < span.Length && (char.IsAsciiDigit(span[end]) || span[end] is '.' or '+' or '-' or 'e' or 'E'))
        {
            end++;
        }

        while (end > 0 && !double.TryParse(span[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            end--;
        }

        if (end == 0)
        {
            throw Error($"Could not parse \"{text}\" as a floating point number");
        }

        if (end != span.Length)
        {
            throw Error($"Did not understand trailing characters \"{span[end..]}\" in string \"{text}\"");
        }

        return double.Parse(span[..end], NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private bool Boolean(string text) => text switch
    {
        "true" => true,
        "false" => false,
        _ => throw Error($"Boolean values must be \"true\" or \"false\" not \"{text}\""),
    };

    private int Rounding(string text) => text switch
    {
        "true" => 5,
        "false" => 0,
        _ => PositiveInteger(text),
    };

    private double Angle(string text) => Double(text);

    private MetacityAlphaSpec Alpha(string text)
    {
        var parts = text.Split(':');
        var alphas = new byte[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            var v = Double(parts[i]);
            if (v < 0.0 - 1e-6 || v > 1.0 + 1e-6)
            {
                throw Error($"Alpha must be between 0.0 (invisible) and 1.0 (fully opaque), was {v}");
            }

            alphas[i] = (byte)(v * 255);
        }

        return new MetacityAlphaSpec(alphas);
    }

    private MetacityColorSpec Color(string text)
    {
        if (_theme.TryGetColorConstant(text, out var referent))
        {
            text = referent;
        }

        var spec = MetacityColorSpec.Parse(text, out var error);
        if (error is not null)
        {
            throw Error(error);
        }

        spec.Index = _theme.ColorSpecs.Count;
        _theme.ColorSpecs.Add(spec);
        return spec;
    }

    private MetacityExpression Expression(string text)
    {
        var expression = MetacityExpression.Compile(text, _theme, out var error);
        if (error is not null)
        {
            throw Error(error);
        }

        return expression;
    }

    private double TitleScale(string text) => text switch
    {
        "xx-small" => 1.0 / (1.2 * 1.2 * 1.2),
        "x-small" => 1.0 / (1.2 * 1.2),
        "small" => 1.0 / 1.2,
        "medium" => 1.0,
        "large" => 1.2,
        "x-large" => 1.2 * 1.2,
        "xx-large" => 1.2 * 1.2 * 1.2,
        _ => throw Error($"Invalid title scale \"{text}\" (must be one of xx-small,x-small,small,medium,large,x-large,xx-large)"),
    };

    private void ReadThemeChild(string element)
    {
        switch (element)
        {
            case "info":
                CheckNoAttributes(element);
                ForEachChild(element, ReadInfoChild);
                break;
            case "constant":
                ReadConstant(element);
                break;
            case "frame_geometry":
                ReadFrameGeometry(element);
                break;
            case "draw_ops":
            {
                Locate(element, "!name");
                var name = Attribute("name")!;
                if (_theme.LookupDrawOpList(name) is not null)
                {
                    throw Error($"<{element}> name \"{name}\" used a second time");
                }

                var list = new MetacityDrawOpList();
                _theme.InsertDrawOpList(name, list);
                ReadDrawOpsChildren(element, list);
                break;
            }

            case "frame_style":
                ReadFrameStyle(element);
                break;
            case "frame_style_set":
                ReadFrameStyleSet(element);
                break;
            case "window":
                ReadWindow(element);
                break;
            case "menu_icon":
            {
                MetacityDrawOpList? list = null;
                ForEachChild(element, child =>
                {
                    if (child != "draw_ops")
                    {
                        throw Error($"Element <{child}> is not allowed below <menu_icon>");
                    }

                    if (list is not null)
                    {
                        throw Error("Can't have a two draw_ops for a <menu_icon> element (theme specified a draw_ops attribute and also a <draw_ops> element, or specified two elements)");
                    }

                    CheckNoAttributes(child);
                    list = new MetacityDrawOpList();
                    ReadDrawOpsChildren(child, list);
                });
                break;
            }

            case "fallback":
                Leaf(element, $"Element <{_reader.Name}> is not allowed inside a <fallback> element");
                break;
            default:
                throw Error($"Element <{element}> is not allowed below <metacity_theme>");
        }
    }

    private void ReadInfoChild(string element)
    {
        if (element is not ("name" or "author" or "copyright" or "description" or "date"))
        {
            throw Error($"Element <{element}> is not allowed below <info>");
        }

        CheckNoAttributes(element);
        ForEachChild(
            element,
            child => throw Error($"Element <{child}> is not allowed inside a name/author/date/description element"),
            text =>
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return;
                }

                switch (element)
                {
                    case "name":
                        _theme.ReadableName = Once(_theme.ReadableName, element, text);
                        break;
                    case "author":
                        _theme.Author = Once(_theme.Author, element, text);
                        break;
                    case "copyright":
                        _theme.Copyright = Once(_theme.Copyright, element, text);
                        break;
                    case "date":
                        _theme.Date = Once(_theme.Date, element, text);
                        break;
                    default:
                        _theme.Description = Once(_theme.Description, element, text);
                        break;
                }
            });
    }

    private string Once(string? current, string element, string text) =>
        current is null ? text : throw Error($"<{element}> specified twice for this theme");

    private void ReadConstant(string element)
    {
        Locate(element, "!name", "!value");
        var name = Attribute("name")!;
        var value = Attribute("value")!;
        string? error;
        if (value.Length > 0 && (value[0] == '.' || value[0] == '+' || value[0] == '-' || char.IsAsciiDigit(value[0])))
        {
            error = value.Contains('.')
                ? _theme.DefineFloatConstant(name, Double(value))
                : _theme.DefineIntConstant(name, PositiveInteger(value));
        }
        else
        {
            error = _theme.DefineColorConstant(name, value);
        }

        if (error is not null)
        {
            throw Error(error);
        }

        Leaf(element, $"Element <{_reader.Name}> is not allowed inside a <constant> element");
    }

    private void ReadFrameGeometry(string element)
    {
        Locate(element, "!name", "parent", "has_title", "title_scale", "rounded_top_left", "rounded_top_right", "rounded_bottom_left", "rounded_bottom_right", "hide_buttons");
        var name = Attribute("name")!;
        var parent = Attribute("parent");
        var hasTitle = Attribute("has_title");
        var titleScale = Attribute("title_scale");
        var roundedTopLeft = Attribute("rounded_top_left");
        var roundedTopRight = Attribute("rounded_top_right");
        var roundedBottomLeft = Attribute("rounded_bottom_left");
        var roundedBottomRight = Attribute("rounded_bottom_right");
        var hideButtons = Attribute("hide_buttons");

        var hasTitleValue = hasTitle is null || Boolean(hasTitle);
        var hideButtonsValue = hideButtons is not null && Boolean(hideButtons);
        var topLeft = roundedTopLeft is null ? 0 : Rounding(roundedTopLeft);
        var topRight = roundedTopRight is null ? 0 : Rounding(roundedTopRight);
        var bottomLeft = roundedBottomLeft is null ? 0 : Rounding(roundedBottomLeft);
        var bottomRight = roundedBottomRight is null ? 0 : Rounding(roundedBottomRight);
        var titleScaleValue = titleScale is null ? 1.0 : TitleScale(titleScale);

        if (_theme.LookupLayout(name) is not null)
        {
            throw Error($"<{element}> name \"{name}\" used a second time");
        }

        MetacityFrameLayout? parentLayout = null;
        if (parent is not null)
        {
            parentLayout = _theme.LookupLayout(parent) ?? throw Error($"<{element}> parent \"{parent}\" has not been defined");
        }

        var layout = parentLayout is null ? new MetacityFrameLayout() : new MetacityFrameLayout(parentLayout);
        if (hasTitle is not null)
        {
            layout.HasTitle = hasTitleValue;
        }

        if (hideButtonsValue)
        {
            layout.HideButtons = true;
        }

        if (titleScale is not null)
        {
            layout.TitleScale = titleScaleValue;
        }

        if (roundedTopLeft is not null)
        {
            layout.TopLeftRadius = topLeft;
        }

        if (roundedTopRight is not null)
        {
            layout.TopRightRadius = topRight;
        }

        if (roundedBottomLeft is not null)
        {
            layout.BottomLeftRadius = bottomLeft;
        }

        if (roundedBottomRight is not null)
        {
            layout.BottomRightRadius = bottomRight;
        }

        _theme.InsertLayout(name, layout);
        ForEachChild(element, child =>
        {
            switch (child)
            {
                case "distance":
                    ReadDistance(child, layout);
                    break;
                case "border":
                    ReadBorder(child, layout);
                    break;
                case "aspect_ratio":
                    ReadAspectRatio(child, layout);
                    break;
                default:
                    throw Error($"Element <{child}> is not allowed below <frame_geometry>");
            }

            Leaf(child, $"Element <{_reader.Name}> is not allowed inside a distance/border/aspect_ratio element");
        });

        if (layout.Validate() is { } error)
        {
            throw Error(error);
        }
    }

    private void ReadDistance(string element, MetacityFrameLayout layout)
    {
        Locate(element, "!name", "!value");
        var name = Attribute("name")!;
        var value = PositiveInteger(Attribute("value")!);
        switch (name)
        {
            case "left_width":
                layout.LeftWidth = value;
                break;
            case "right_width":
                layout.RightWidth = value;
                break;
            case "bottom_height":
                layout.BottomHeight = value;
                break;
            case "title_vertical_pad":
                layout.TitleVerticalPad = value;
                break;
            case "right_titlebar_edge":
                layout.RightTitlebarEdge = value;
                break;
            case "left_titlebar_edge":
                layout.LeftTitlebarEdge = value;
                break;
            case "button_width":
                layout.ButtonWidth = value;
                FixedSizing(layout);
                break;
            case "button_height":
                layout.ButtonHeight = value;
                FixedSizing(layout);
                break;
            default:
                throw Error($"Distance \"{name}\" is unknown");
        }
    }

    private void FixedSizing(MetacityFrameLayout layout)
    {
        if (layout.ButtonSizing is not (MetacityButtonSizing.Unset or MetacityButtonSizing.Fixed))
        {
            throw Error("Cannot specify both \"button_width\"/\"button_height\" and \"aspect_ratio\" for buttons");
        }

        layout.ButtonSizing = MetacityButtonSizing.Fixed;
    }

    private void ReadAspectRatio(string element, MetacityFrameLayout layout)
    {
        Locate(element, "!name", "!value");
        var name = Attribute("name")!;
        var value = Double(Attribute("value")!);
        if (name != "button")
        {
            throw Error($"Aspect ratio \"{name}\" is unknown");
        }

        layout.ButtonAspect = value;
        if (layout.ButtonSizing != MetacityButtonSizing.Unset)
        {
            throw Error("Cannot specify both \"button_width\"/\"button_height\" and \"aspect_ratio\" for buttons");
        }

        layout.ButtonSizing = MetacityButtonSizing.Aspect;
    }

    private void ReadBorder(string element, MetacityFrameLayout layout)
    {
        Locate(element, "!name", "!top", "!bottom", "!left", "!right");
        var name = Attribute("name")!;
        var border = new MetacityBorder(
            PositiveInteger(Attribute("top")!),
            PositiveInteger(Attribute("bottom")!),
            PositiveInteger(Attribute("left")!),
            PositiveInteger(Attribute("right")!));
        switch (name)
        {
            case "title_border":
                layout.TitleBorder = border;
                break;
            case "button_border":
                layout.ButtonBorder = border;
                break;
            default:
                throw Error($"Border \"{name}\" is unknown");
        }
    }

    private void ReadDrawOpsChildren(string element, MetacityDrawOpList list) =>
        ForEachChild(element, child => ReadDrawOp(child, list));

    private void ReadDrawOp(string element, MetacityDrawOpList list)
    {
        const string nested = "Element <{0}> is not allowed inside a draw operation element";
        switch (element)
        {
            case "line":
            {
                Locate(element, "!color", "!x1", "!y1", "!x2", "!y2", "dash_on_length", "dash_off_length", "width");
                var dashOn = Attribute("dash_on_length") is { } on ? PositiveInteger(on) : 0;
                var dashOff = Attribute("dash_off_length") is { } off ? PositiveInteger(off) : 0;
                var width = Attribute("width") is { } w ? PositiveInteger(w) : 0;
                var color = Color(Attribute("color")!);
                var x1 = Attribute("x1")!;
                var y1 = Attribute("y1")!;
                var x2 = Attribute("x2")!;
                var y2 = Attribute("y2")!;
                list.Ops.Add(new MetacityDrawOp(MetacityDrawOpKind.Line)
                {
                    Color = color,
                    X = Expression(x1),
                    Y = Expression(y1),
                    X2 = x1 == x2 ? null : Expression(x2),
                    Y2 = y1 == y2 ? null : Expression(y2),
                    LineWidth = width,
                    DashOn = dashOn,
                    DashOff = dashOff,
                });
                break;
            }

            case "rectangle":
            {
                Locate(element, "!color", "!x", "!y", "!width", "!height", "filled");
                var filled = Attribute("filled") is { } f && Boolean(f);
                var color = Color(Attribute("color")!);
                list.Ops.Add(new MetacityDrawOp(MetacityDrawOpKind.Rectangle)
                {
                    Color = color,
                    X = Expression(Attribute("x")!),
                    Y = Expression(Attribute("y")!),
                    Width = Expression(Attribute("width")!),
                    Height = Expression(Attribute("height")!),
                    Filled = filled,
                });
                break;
            }

            case "arc":
            {
                Locate(element, "!color", "!x", "!y", "!width", "!height", "filled", "start_angle", "extent_angle", "from", "to");
                var startAngle = Attribute("start_angle");
                var extentAngle = Attribute("extent_angle");
                var from = Attribute("from");
                var to = Attribute("to");
                if (startAngle is null && from is null)
                {
                    throw Error($"No \"start_angle\" or \"from\" attribute on element <{element}>");
                }

                if (extentAngle is null && to is null)
                {
                    throw Error($"No \"extent_angle\" or \"to\" attribute on element <{element}>");
                }

                var start = startAngle is null ? (180 - Angle(from!)) / 360.0 : Angle(startAngle);
                var extent = extentAngle is null ? ((180 - Angle(to!)) / 360.0) - start : Angle(extentAngle);
                var filled = Attribute("filled") is { } f && Boolean(f);
                var color = Color(Attribute("color")!);
                list.Ops.Add(new MetacityDrawOp(MetacityDrawOpKind.Arc)
                {
                    Color = color,
                    X = Expression(Attribute("x")!),
                    Y = Expression(Attribute("y")!),
                    Width = Expression(Attribute("width")!),
                    Height = Expression(Attribute("height")!),
                    Filled = filled,
                    StartAngle = start,
                    ExtentAngle = extent,
                });
                break;
            }

            case "clip":
                Locate(element, "!x", "!y", "!width", "!height");
                list.Ops.Add(new MetacityDrawOp(MetacityDrawOpKind.Clip)
                {
                    X = Expression(Attribute("x")!),
                    Y = Expression(Attribute("y")!),
                    Width = Expression(Attribute("width")!),
                    Height = Expression(Attribute("height")!),
                });
                break;
            case "tint":
            {
                Locate(element, "!color", "!x", "!y", "!width", "!height", "!alpha");
                var alpha = Alpha(Attribute("alpha")!);
                var color = Color(Attribute("color")!);
                list.Ops.Add(new MetacityDrawOp(MetacityDrawOpKind.Tint)
                {
                    Color = color,
                    Alpha = alpha,
                    X = Expression(Attribute("x")!),
                    Y = Expression(Attribute("y")!),
                    Width = Expression(Attribute("width")!),
                    Height = Expression(Attribute("height")!),
                });
                break;
            }

            case "gradient":
            {
                Locate(element, "!type", "!x", "!y", "!width", "!height", "alpha");
                var typeText = Attribute("type")!;
                var type = typeText switch
                {
                    "vertical" => MetacityGradientType.Vertical,
                    "horizontal" => MetacityGradientType.Horizontal,
                    "diagonal" => MetacityGradientType.Diagonal,
                    _ => throw Error($"Did not understand value \"{typeText}\" for type of gradient"),
                };
                var alpha = Attribute("alpha") is { } a ? Alpha(a) : null;
                var gradient = new MetacityGradientSpec(type);
                var op = new MetacityDrawOp(MetacityDrawOpKind.Gradient)
                {
                    X = Expression(Attribute("x")!),
                    Y = Expression(Attribute("y")!),
                    Width = Expression(Attribute("width")!),
                    Height = Expression(Attribute("height")!),
                    Gradient = gradient,
                    Alpha = alpha,
                };
                ForEachChild(element, child =>
                {
                    if (child != "color")
                    {
                        throw Error($"Element <{child}> is not allowed below <gradient>");
                    }

                    Locate(child, "!value");
                    gradient.Colors.Add(Color(Attribute("value")!));
                    Leaf(child, $"Element <{_reader.Name}> is not allowed inside a <color> element");
                });
                if (gradient.Colors.Count < 2)
                {
                    throw Error("Gradients should have at least two colors");
                }

                list.Ops.Add(op);
                return;
            }

            case "image":
            {
                Locate(element, "!x", "!y", "!width", "!height", "alpha", "!filename", "colorize", "fill_type");
                var fillType = FillType(Attribute("fill_type"), element);
                var filename = Attribute("filename")!;
                var image = _theme.LoadImage(filename, out var imageError) ?? throw Error(imageError!);
                var colorize = Attribute("colorize") is { } c ? Color(c) : null;
                var alpha = Attribute("alpha") is { } a ? Alpha(a) : null;
                list.Ops.Add(new MetacityDrawOp(MetacityDrawOpKind.Image)
                {
                    Image = image,
                    Colorize = colorize,
                    X = Expression(Attribute("x")!),
                    Y = Expression(Attribute("y")!),
                    Width = Expression(Attribute("width")!),
                    Height = Expression(Attribute("height")!),
                    Alpha = alpha,
                    FillType = fillType,
                });
                break;
            }

            case "gtk_arrow":
            {
                Locate(element, "!state", "!shadow", "!arrow", "!x", "!y", "!width", "!height", "filled");
                var filled = Attribute("filled") is not { } f || Boolean(f);
                var state = GtkState(Attribute("state")!, element);
                var shadow = GtkShadow(Attribute("shadow")!, element);
                var arrowText = Attribute("arrow")!;
                var arrow = arrowText switch
                {
                    "up" => MetacityArrow.Up,
                    "down" => MetacityArrow.Down,
                    "left" => MetacityArrow.Left,
                    "right" => MetacityArrow.Right,
                    "none" => MetacityArrow.None,
                    _ => throw Error($"Did not understand arrow \"{arrowText}\" for <{element}> element"),
                };
                list.Ops.Add(new MetacityDrawOp(MetacityDrawOpKind.GtkArrow)
                {
                    X = Expression(Attribute("x")!),
                    Y = Expression(Attribute("y")!),
                    Width = Expression(Attribute("width")!),
                    Height = Expression(Attribute("height")!),
                    Filled = filled,
                    State = state,
                    Shadow = shadow,
                    Arrow = arrow,
                });
                break;
            }

            case "gtk_box":
            {
                Locate(element, "!state", "!shadow", "!x", "!y", "!width", "!height");
                var state = GtkState(Attribute("state")!, element);
                var shadow = GtkShadow(Attribute("shadow")!, element);
                list.Ops.Add(new MetacityDrawOp(MetacityDrawOpKind.GtkBox)
                {
                    X = Expression(Attribute("x")!),
                    Y = Expression(Attribute("y")!),
                    Width = Expression(Attribute("width")!),
                    Height = Expression(Attribute("height")!),
                    State = state,
                    Shadow = shadow,
                });
                break;
            }

            case "gtk_vline":
            {
                Locate(element, "!state", "!x", "!y1", "!y2");
                var state = GtkState(Attribute("state")!, element);
                list.Ops.Add(new MetacityDrawOp(MetacityDrawOpKind.GtkVline)
                {
                    X = Expression(Attribute("x")!),
                    Y = Expression(Attribute("y1")!),
                    Y2 = Expression(Attribute("y2")!),
                    State = state,
                });
                break;
            }

            case "icon":
            {
                Locate(element, "!x", "!y", "!width", "!height", "alpha", "fill_type");
                var fillType = FillType(Attribute("fill_type"), element);
                var alpha = Attribute("alpha") is { } a ? Alpha(a) : null;
                list.Ops.Add(new MetacityDrawOp(MetacityDrawOpKind.Icon)
                {
                    X = Expression(Attribute("x")!),
                    Y = Expression(Attribute("y")!),
                    Width = Expression(Attribute("width")!),
                    Height = Expression(Attribute("height")!),
                    Alpha = alpha,
                    FillType = fillType,
                });
                break;
            }

            case "title":
            {
                Locate(element, "!color", "!x", "!y", "ellipsize_width");
                var ellipsize = Attribute("ellipsize_width");
                if (ellipsize is not null && _required.Peek() < 3001)
                {
                    throw Error($"No \"ellipsize_width\" attribute on element <{element}>");
                }

                var color = Color(Attribute("color")!);
                list.Ops.Add(new MetacityDrawOp(MetacityDrawOpKind.Title)
                {
                    Color = color,
                    X = Expression(Attribute("x")!),
                    Y = Expression(Attribute("y")!),
                    EllipsizeWidth = ellipsize is null ? null : Expression(ellipsize),
                });
                break;
            }

            case "include":
            {
                Locate(element, "x", "y", "width", "height", "!name");
                var name = Attribute("name")!;
                var target = _theme.LookupDrawOpList(name) ?? throw Error($"No <draw_ops> called \"{name}\" has been defined");
                if (ReferenceEquals(target, list) || target.Contains(list))
                {
                    throw Error($"Including draw_ops \"{name}\" here would create a circular reference");
                }

                list.Ops.Add(new MetacityDrawOp(MetacityDrawOpKind.OpList)
                {
                    OpList = target,
                    X = Expression(Attribute("x") ?? "0"),
                    Y = Expression(Attribute("y") ?? "0"),
                    Width = Expression(Attribute("width") ?? "width"),
                    Height = Expression(Attribute("height") ?? "height"),
                });
                break;
            }

            case "tile":
            {
                Locate(element, "x", "y", "width", "height", "!name", "tile_xoffset", "tile_yoffset", "!tile_width", "!tile_height");
                var name = Attribute("name")!;
                var target = _theme.LookupDrawOpList(name) ?? throw Error($"No <draw_ops> called \"{name}\" has been defined");
                if (ReferenceEquals(target, list) || target.Contains(list))
                {
                    throw Error($"Including draw_ops \"{name}\" here would create a circular reference");
                }

                list.Ops.Add(new MetacityDrawOp(MetacityDrawOpKind.Tile)
                {
                    OpList = target,
                    X = Expression(Attribute("x") ?? "0"),
                    Y = Expression(Attribute("y") ?? "0"),
                    Width = Expression(Attribute("width") ?? "width"),
                    Height = Expression(Attribute("height") ?? "height"),
                    TileXOffset = Expression(Attribute("tile_xoffset") ?? "0"),
                    TileYOffset = Expression(Attribute("tile_yoffset") ?? "0"),
                    TileWidth = Expression(Attribute("tile_width")!),
                    TileHeight = Expression(Attribute("tile_height")!),
                });
                break;
            }

            default:
                throw Error($"Element <{element}> is not allowed below <draw_ops>");
        }

        Leaf(element, string.Format(CultureInfo.InvariantCulture, nested, _reader.Name));
    }

    private MetacityFillType FillType(string? text, string element) => text switch
    {
        null or "scale" => MetacityFillType.Scale,
        "tile" => MetacityFillType.Tile,
        _ => throw Error($"Did not understand fill type \"{text}\" for <{element}> element"),
    };

    private MetacityStateFlag GtkState(string text, string element) =>
        MetacityColorSpec.TryParseState(text, out var state)
            ? state
            : throw Error($"Did not understand state \"{text}\" for <{element}> element");

    private MetacityShadow GtkShadow(string text, string element) => text switch
    {
        "none" => MetacityShadow.None,
        "in" => MetacityShadow.In,
        "out" => MetacityShadow.Out,
        "etched_in" => MetacityShadow.EtchedIn,
        "etched_out" => MetacityShadow.EtchedOut,
        _ => throw Error($"Did not understand shadow \"{text}\" for <{element}> element"),
    };

    private void ReadFrameStyle(string element)
    {
        Locate(element, "!name", "parent", "geometry", "background", "alpha");
        var name = Attribute("name")!;
        var parent = Attribute("parent");
        var geometry = Attribute("geometry");
        var background = Attribute("background");
        var alpha = Attribute("alpha");

        if (_theme.LookupStyle(name) is not null)
        {
            throw Error($"<{element}> name \"{name}\" used a second time");
        }

        MetacityFrameStyle? parentStyle = null;
        if (parent is not null)
        {
            parentStyle = _theme.LookupStyle(parent) ?? throw Error($"<{element}> parent \"{parent}\" has not been defined");
        }

        MetacityFrameLayout? layout = null;
        if (geometry is not null)
        {
            layout = _theme.LookupLayout(geometry) ?? throw Error($"<{element}> geometry \"{geometry}\" has not been defined");
        }
        else if (parentStyle is not null)
        {
            layout = parentStyle.Layout;
        }

        if (layout is null)
        {
            throw Error($"<{element}> must specify either a geometry or a parent that has a geometry");
        }

        var style = new MetacityFrameStyle(parentStyle, layout);
        if (background is not null)
        {
            var spec = MetacityColorSpec.Parse(background, out var colorError);
            if (colorError is not null)
            {
                throw Error(colorError);
            }

            style.WindowBackground = spec;
            if (alpha is not null)
            {
                style.WindowBackgroundAlpha = Alpha(alpha).Alphas[0];
            }
        }
        else if (alpha is not null)
        {
            throw Error("You must specify a background for an alpha value to be meaningful");
        }

        _theme.InsertStyle(name, style);
        ForEachChild(element, child =>
        {
            switch (child)
            {
                case "piece":
                    ReadPiece(child, style);
                    break;
                case "button":
                    ReadButton(child, style);
                    break;
                case "shadow":
                    Leaf(child, $"Element <{_reader.Name}> is not allowed below <shadow>");
                    break;
                case "padding":
                    Leaf(child, $"Element <{_reader.Name}> is not allowed below <padding>");
                    break;
                default:
                    throw Error($"Element <{child}> is not allowed below <frame_style>");
            }
        });

        if (style.Validate(_required.Peek()) is { } error)
        {
            throw Error(error);
        }
    }

    private void ReadPiece(string element, MetacityFrameStyle style)
    {
        Locate(element, "!position", "draw_ops");
        var position = Attribute("position")!;
        var drawOps = Attribute("draw_ops");
        var piece = PieceFromName(position) ?? throw Error($"Unknown position \"{position}\" for frame piece");
        if (style.Pieces[(int)piece] is not null)
        {
            throw Error($"Frame style already has a piece at position {position}");
        }

        var list = ReadOpListReference(element, drawOps);
        style.Pieces[(int)piece] = list ?? throw Error("No draw_ops provided for frame piece");
    }

    private void ReadButton(string element, MetacityFrameStyle style)
    {
        Locate(element, "!function", "!state", "draw_ops");
        var function = Attribute("function")!;
        var stateText = Attribute("state")!;
        var drawOps = Attribute("draw_ops");
        var type = ButtonTypeFromName(function) ?? throw Error($"Unknown function \"{function}\" for button");
        var required = _required.Peek();
        if (EarliestVersionWithButton(type) > required)
        {
            throw Error($"Button function \"{function}\" does not exist in this version ({required}, need {EarliestVersionWithButton(type)})");
        }

        var state = stateText switch
        {
            "normal" => MetacityButtonState.Normal,
            "pressed" => MetacityButtonState.Pressed,
            "prelight" => MetacityButtonState.Prelight,
            _ => throw Error($"Unknown state \"{stateText}\" for button"),
        };
        if (style.Buttons[(int)type, (int)state] is not null)
        {
            throw Error($"Frame style already has a button for function {function} state {stateText}");
        }

        var list = ReadOpListReference(element, drawOps);
        style.Buttons[(int)type, (int)state] = list ?? throw Error("No draw_ops provided for button");
    }

    private MetacityDrawOpList? ReadOpListReference(string element, string? drawOps)
    {
        MetacityDrawOpList? list = null;
        if (drawOps is not null)
        {
            list = _theme.LookupDrawOpList(drawOps) ?? throw Error($"No <draw_ops> with the name \"{drawOps}\" has been defined");
        }

        ForEachChild(element, child =>
        {
            if (child != "draw_ops")
            {
                throw Error($"Element <{child}> is not allowed below <{element}>");
            }

            if (list is not null)
            {
                throw Error($"Can't have a two draw_ops for a <{element}> element (theme specified a draw_ops attribute and also a <draw_ops> element, or specified two elements)");
            }

            CheckNoAttributes(child);
            list = new MetacityDrawOpList();
            ReadDrawOpsChildren(child, list);
        });
        return list;
    }

    private void ReadFrameStyleSet(string element)
    {
        Locate(element, "!name", "parent");
        var name = Attribute("name")!;
        var parent = Attribute("parent");
        if (_theme.LookupStyleSet(name) is not null)
        {
            throw Error($"<{element}> name \"{name}\" used a second time");
        }

        MetacityFrameStyleSet? parentSet = null;
        if (parent is not null)
        {
            parentSet = _theme.LookupStyleSet(parent) ?? throw Error($"<{element}> parent \"{parent}\" has not been defined");
        }

        var set = new MetacityFrameStyleSet(parentSet);
        _theme.InsertStyleSet(name, set);
        ForEachChild(element, child =>
        {
            if (child != "frame")
            {
                throw Error($"Element <{child}> is not allowed below <frame_style_set>");
            }

            ReadFrame(child, set);
        });

        if (set.Validate() is { } error)
        {
            throw Error(error);
        }
    }

    private void ReadFrame(string element, MetacityFrameStyleSet set)
    {
        Locate(element, "!focus", "!state", "resize", "!style");
        var focusText = Attribute("focus")!;
        var stateText = Attribute("state")!;
        var resizeText = Attribute("resize");
        var styleName = Attribute("style")!;
        var focus = focusText switch
        {
            "no" => MetacityFocus.No,
            "yes" => MetacityFocus.Yes,
            _ => throw Error($"\"{focusText}\" is not a valid value for focus attribute"),
        };
        var state = FrameStateFromName(stateText) ?? throw Error($"\"{focusText}\" is not a valid value for state attribute");
        var style = _theme.LookupStyle(styleName) ?? throw Error($"A style called \"{styleName}\" has not been defined");

        var resize = MetacityResize.Both;
        switch (state)
        {
            case MetacityFrameStateKind.Normal:
                if (resizeText is null)
                {
                    throw Error($"No \"resize\" attribute on element <{element}>");
                }

                resize = ResizeFromName(resizeText) ?? throw Error($"\"{focusText}\" is not a valid value for resize attribute");
                break;
            case MetacityFrameStateKind.Shaded:
                if (resizeText is not null)
                {
                    resize = ResizeFromName(resizeText) ?? throw Error($"\"{focusText}\" is not a valid value for resize attribute");
                }

                break;
            default:
                if (resizeText is not null)
                {
                    throw Error($"Should not have \"resize\" attribute on <{element}> element for maximized states");
                }

                break;
        }

        switch (state)
        {
            case MetacityFrameStateKind.Normal:
                if (set.Normal[(int)resize, (int)focus] is not null)
                {
                    throw Error($"Style has already been specified for state {stateText} resize {resizeText} focus {focusText}");
                }

                set.Normal[(int)resize, (int)focus] = style;
                break;
            case MetacityFrameStateKind.Shaded:
                if (set.Shaded[(int)resize, (int)focus] is not null)
                {
                    throw Error($"Style has already been specified for state {stateText} resize {resizeText} focus {focusText}");
                }

                set.Shaded[(int)resize, (int)focus] = style;
                break;
            default:
            {
                var styles = set.FocusStyles(state);
                if (styles[(int)focus] is not null)
                {
                    throw Error($"Style has already been specified for state {stateText} focus {focusText}");
                }

                styles[(int)focus] = style;
                break;
            }
        }

        Leaf(element, $"Element <{_reader.Name}> is not allowed inside a <frame> element");
    }

    private void ReadWindow(string element)
    {
        Locate(element, "!type", "!style_set");
        var typeName = Attribute("type")!;
        var styleSetName = Attribute("style_set")!;
        var type = FrameTypeFromName(typeName);
        if (type is null || (type == MetacityFrameType.Attached && _required.Peek() < 3002))
        {
            throw Error($"Unknown type \"{typeName}\" on <{element}> element");
        }

        var set = _theme.LookupStyleSet(styleSetName) ?? throw Error($"Unknown style_set \"{styleSetName}\" on <{element}> element");
        if (_theme.StyleSetsByType[(int)type.Value] is not null)
        {
            throw Error($"Window type \"{typeName}\" has already been assigned a style set");
        }

        _theme.StyleSetsByType[(int)type.Value] = set;
        Leaf(element, $"Element <{_reader.Name}> is not allowed inside a <window> element");
    }

    internal static int EarliestVersionWithButton(MetacityButtonType type) => type switch
    {
        MetacityButtonType.Shade or MetacityButtonType.Above or MetacityButtonType.Stick or
        MetacityButtonType.Unshade or MetacityButtonType.Unabove or MetacityButtonType.Unstick => 2000,
        MetacityButtonType.LeftSingleBackground or MetacityButtonType.RightSingleBackground => 3003,
        MetacityButtonType.AppMenu => 3005,
        _ => 1000,
    };

    private static MetacityButtonType? ButtonTypeFromName(string name) => name switch
    {
        "shade" => MetacityButtonType.Shade,
        "above" => MetacityButtonType.Above,
        "stick" => MetacityButtonType.Stick,
        "unshade" => MetacityButtonType.Unshade,
        "unabove" => MetacityButtonType.Unabove,
        "unstick" => MetacityButtonType.Unstick,
        "close" => MetacityButtonType.Close,
        "maximize" => MetacityButtonType.Maximize,
        "minimize" => MetacityButtonType.Minimize,
        "menu" => MetacityButtonType.Menu,
        "appmenu" => MetacityButtonType.AppMenu,
        "left_left_background" => MetacityButtonType.LeftLeftBackground,
        "left_middle_background" => MetacityButtonType.LeftMiddleBackground,
        "left_right_background" => MetacityButtonType.LeftRightBackground,
        "left_single_background" => MetacityButtonType.LeftSingleBackground,
        "right_left_background" => MetacityButtonType.RightLeftBackground,
        "right_middle_background" => MetacityButtonType.RightMiddleBackground,
        "right_right_background" => MetacityButtonType.RightRightBackground,
        "right_single_background" => MetacityButtonType.RightSingleBackground,
        _ => null,
    };

    internal static string ButtonTypeName(MetacityButtonType type) => type switch
    {
        MetacityButtonType.Close => "close",
        MetacityButtonType.Maximize => "maximize",
        MetacityButtonType.Minimize => "minimize",
        MetacityButtonType.Shade => "shade",
        MetacityButtonType.Above => "above",
        MetacityButtonType.Stick => "stick",
        MetacityButtonType.Unshade => "unshade",
        MetacityButtonType.Unabove => "unabove",
        MetacityButtonType.Unstick => "unstick",
        MetacityButtonType.Menu => "menu",
        MetacityButtonType.AppMenu => "appmenu",
        MetacityButtonType.LeftLeftBackground => "left_left_background",
        MetacityButtonType.LeftMiddleBackground => "left_middle_background",
        MetacityButtonType.LeftRightBackground => "left_right_background",
        MetacityButtonType.LeftSingleBackground => "left_single_background",
        MetacityButtonType.RightLeftBackground => "right_left_background",
        MetacityButtonType.RightMiddleBackground => "right_middle_background",
        MetacityButtonType.RightRightBackground => "right_right_background",
        MetacityButtonType.RightSingleBackground => "right_single_background",
        _ => "<unknown>",
    };

    internal static string ButtonStateName(MetacityButtonState state) => state switch
    {
        MetacityButtonState.Normal => "normal",
        MetacityButtonState.Pressed => "pressed",
        MetacityButtonState.Prelight => "prelight",
        _ => "<unknown>",
    };

    private static MetacityPiece? PieceFromName(string name) => name switch
    {
        "entire_background" => MetacityPiece.EntireBackground,
        "titlebar" => MetacityPiece.Titlebar,
        "titlebar_middle" => MetacityPiece.TitlebarMiddle,
        "left_titlebar_edge" => MetacityPiece.LeftTitlebarEdge,
        "right_titlebar_edge" => MetacityPiece.RightTitlebarEdge,
        "top_titlebar_edge" => MetacityPiece.TopTitlebarEdge,
        "bottom_titlebar_edge" => MetacityPiece.BottomTitlebarEdge,
        "title" => MetacityPiece.Title,
        "left_edge" => MetacityPiece.LeftEdge,
        "right_edge" => MetacityPiece.RightEdge,
        "bottom_edge" => MetacityPiece.BottomEdge,
        "overlay" => MetacityPiece.Overlay,
        _ => null,
    };

    private static MetacityFrameStateKind? FrameStateFromName(string name) => name switch
    {
        "normal" => MetacityFrameStateKind.Normal,
        "maximized" => MetacityFrameStateKind.Maximized,
        "tiled_left" => MetacityFrameStateKind.TiledLeft,
        "tiled_right" => MetacityFrameStateKind.TiledRight,
        "shaded" => MetacityFrameStateKind.Shaded,
        "maximized_and_shaded" => MetacityFrameStateKind.MaximizedAndShaded,
        "tiled_left_and_shaded" => MetacityFrameStateKind.TiledLeftAndShaded,
        "tiled_right_and_shaded" => MetacityFrameStateKind.TiledRightAndShaded,
        _ => null,
    };

    internal static string FrameStateName(MetacityFrameStateKind state) => state switch
    {
        MetacityFrameStateKind.Normal => "normal",
        MetacityFrameStateKind.Maximized => "maximized",
        MetacityFrameStateKind.TiledLeft => "tiled_left",
        MetacityFrameStateKind.TiledRight => "tiled_right",
        MetacityFrameStateKind.Shaded => "shaded",
        MetacityFrameStateKind.MaximizedAndShaded => "maximized_and_shaded",
        MetacityFrameStateKind.TiledLeftAndShaded => "tiled_left_and_shaded",
        MetacityFrameStateKind.TiledRightAndShaded => "tiled_right_and_shaded",
        _ => "<unknown>",
    };

    private static MetacityResize? ResizeFromName(string name) => name switch
    {
        "none" => MetacityResize.None,
        "vertical" => MetacityResize.Vertical,
        "horizontal" => MetacityResize.Horizontal,
        "both" => MetacityResize.Both,
        _ => null,
    };

    internal static string ResizeName(MetacityResize resize) => resize switch
    {
        MetacityResize.None => "none",
        MetacityResize.Vertical => "vertical",
        MetacityResize.Horizontal => "horizontal",
        MetacityResize.Both => "both",
        _ => "<unknown>",
    };

    internal static string FocusName(MetacityFocus focus) => focus == MetacityFocus.Yes ? "yes" : "no";

    private static MetacityFrameType? FrameTypeFromName(string name) => name switch
    {
        "normal" => MetacityFrameType.Normal,
        "dialog" => MetacityFrameType.Dialog,
        "modal_dialog" => MetacityFrameType.ModalDialog,
        "utility" => MetacityFrameType.Utility,
        "menu" => MetacityFrameType.Menu,
        "border" => MetacityFrameType.Border,
        "attached" => MetacityFrameType.Attached,
        _ => null,
    };

    internal static string FrameTypeName(MetacityFrameType type) => type switch
    {
        MetacityFrameType.Normal => "normal",
        MetacityFrameType.Dialog => "dialog",
        MetacityFrameType.ModalDialog => "modal_dialog",
        MetacityFrameType.Utility => "utility",
        MetacityFrameType.Menu => "menu",
        MetacityFrameType.Border => "border",
        MetacityFrameType.Attached => "attached",
        _ => "<unknown>",
    };
}
