using System.Globalization;
using Basin.Diagnostics;

namespace Basin.Frames.Metacity;

internal sealed class MetacityExpression
{
    private const int MaxOperands = 32;

    private static readonly BasinLogger Log = BasinLog.For("metacity");

    private readonly MetacityToken[] _tokens;
    private bool _reported;

    private MetacityExpression(string source, MetacityToken[] tokens, bool constant, int value)
    {
        Source = source;
        _tokens = tokens;
        Constant = constant;
        Value = value;
    }

    public string Source { get; }

    public bool Constant { get; }

    public int Value { get; }

    public bool UsesObjectSize
    {
        get
        {
            foreach (var token in _tokens)
            {
                if (token.Kind == MetacityTokenKind.Variable && token.Variable is MetacityVariable.ObjectWidth or MetacityVariable.ObjectHeight)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public static MetacityExpression Compile(string source, MetacityTheme theme, out string? error)
    {
        var tokens = Tokenize(source, out error);
        if (error is not null)
        {
            return new MetacityExpression(source, [], true, 0);
        }

        var constant = true;
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token.Kind != MetacityTokenKind.Variable)
            {
                continue;
            }

            if (theme.TryGetIntConstant(token.Name!, out var ival))
            {
                tokens[i] = new MetacityToken(MetacityTokenKind.Int, ival, 0, MetacityOperator.None, MetacityVariable.Unknown, null);
            }
            else if (theme.TryGetFloatConstant(token.Name!, out var dval))
            {
                tokens[i] = new MetacityToken(MetacityTokenKind.Double, 0, dval, MetacityOperator.None, MetacityVariable.Unknown, null);
            }
            else
            {
                tokens[i] = token with { Variable = VariableFromName(token.Name!) };
                constant = false;
            }
        }

        var validation = new MetacityExpressionEnvironment { Width = 1, Height = 1, ObjectWidth = 1, ObjectHeight = 1 };
        var structural = EvaluateTokens(tokens, in validation, true, true, out _);
        if (structural is not MetacityExpressionError.None and not MetacityExpressionError.DivideByZero and not MetacityExpressionError.ModOnFloat)
        {
            error = Describe(structural, tokens);
            return new MetacityExpression(source, [], true, 0);
        }

        if (constant)
        {
            var empty = default(MetacityExpressionEnvironment);
            var result = EvaluateTokens(tokens, in empty, false, false, out var value);
            if (result != MetacityExpressionError.None)
            {
                error = Describe(result, tokens);
                return new MetacityExpression(source, [], true, 0);
            }

            return new MetacityExpression(source, tokens, true, value);
        }

        return new MetacityExpression(source, tokens, false, 0);
    }

    public int Evaluate(in MetacityExpressionEnvironment env, bool hasObject)
    {
        if (Constant)
        {
            return Value;
        }

        var result = EvaluateTokens(_tokens, in env, hasObject, false, out var value);
        if (result != MetacityExpressionError.None)
        {
            if (!_reported)
            {
                _reported = true;
                Log.Warn($"Theme contained an expression that resulted in an error: {Describe(result, _tokens)} in \"{Source}\"");
            }

            return 0;
        }

        return value;
    }

    public int Position(in MetacityExpressionEnvironment env, bool horizontal, bool hasObject = false) =>
        (horizontal ? env.X : env.Y) + Evaluate(in env, hasObject);

    public int Size(in MetacityExpressionEnvironment env, bool hasObject = false) =>
        Math.Max(Evaluate(in env, hasObject), 1);

    private static string Describe(MetacityExpressionError error, MetacityToken[] tokens) => error switch
    {
        MetacityExpressionError.BadParens => "Coordinate expression had unbalanced parentheses",
        MetacityExpressionError.UnknownVariable => $"Coordinate expression had unknown variable or constant \"{FirstUnknown(tokens)}\"",
        MetacityExpressionError.DivideByZero => "Coordinate expression results in division by zero",
        MetacityExpressionError.ModOnFloat => "Coordinate expression tries to use mod operator on a floating-point number",
        MetacityExpressionError.Overflow => "Coordinate expression parser overflowed its buffer.",
        MetacityExpressionError.Structure => "Coordinate expression has an operator where an operand was expected",
        MetacityExpressionError.Empty => "Coordinate expression doesn't seem to have any operators or operands",
        _ => "Coordinate expression was empty or not understood",
    };

    private static string FirstUnknown(MetacityToken[] tokens)
    {
        foreach (var token in tokens)
        {
            if (token.Kind == MetacityTokenKind.Variable && token.Variable == MetacityVariable.Unknown)
            {
                return token.Name ?? "?";
            }
        }

        return "object_width";
    }

    private static MetacityVariable VariableFromName(string name) => name switch
    {
        "width" => MetacityVariable.Width,
        "height" => MetacityVariable.Height,
        "object_width" => MetacityVariable.ObjectWidth,
        "object_height" => MetacityVariable.ObjectHeight,
        "left_width" => MetacityVariable.LeftWidth,
        "right_width" => MetacityVariable.RightWidth,
        "top_height" => MetacityVariable.TopHeight,
        "bottom_height" => MetacityVariable.BottomHeight,
        "mini_icon_width" => MetacityVariable.MiniIconWidth,
        "mini_icon_height" => MetacityVariable.MiniIconHeight,
        "icon_width" => MetacityVariable.IconWidth,
        "icon_height" => MetacityVariable.IconHeight,
        "title_width" => MetacityVariable.TitleWidth,
        "title_height" => MetacityVariable.TitleHeight,
        "frame_x_center" => MetacityVariable.FrameXCenter,
        "frame_y_center" => MetacityVariable.FrameYCenter,
        _ => MetacityVariable.Unknown,
    };

    private static MetacityToken[] Tokenize(string expr, out string? error)
    {
        error = null;
        var tokens = new List<MetacityToken>();
        var p = 0;
        while (p < expr.Length)
        {
            var ch = expr[p];
            switch (ch)
            {
                case '*':
                case '/':
                case '+':
                case '-':
                case '%':
                case '`':
                {
                    var op = OperatorAt(expr, p, out var length);
                    if (op == MetacityOperator.None)
                    {
                        error = $"Coordinate expression contained unknown operator at the start of this text: \"{expr[p..]}\"";
                        return [];
                    }

                    tokens.Add(new MetacityToken(MetacityTokenKind.Operator, 0, 0, op, MetacityVariable.Unknown, null));
                    p += length;
                    break;
                }

                case '(':
                    tokens.Add(new MetacityToken(MetacityTokenKind.OpenParen, 0, 0, MetacityOperator.None, MetacityVariable.Unknown, null));
                    p++;
                    break;
                case ')':
                    tokens.Add(new MetacityToken(MetacityTokenKind.CloseParen, 0, 0, MetacityOperator.None, MetacityVariable.Unknown, null));
                    p++;
                    break;
                case ' ':
                case '\t':
                case '\n':
                    p++;
                    break;
                default:
                    if (IsVariableChar(ch))
                    {
                        var start = p;
                        while (p < expr.Length && IsVariableChar(expr[p]))
                        {
                            p++;
                        }

                        tokens.Add(new MetacityToken(MetacityTokenKind.Variable, 0, 0, MetacityOperator.None, MetacityVariable.Unknown, expr[start..p]));
                    }
                    else
                    {
                        var start = p;
                        while (p < expr.Length && (expr[p] == '.' || char.IsAsciiDigit(expr[p])))
                        {
                            p++;
                        }

                        if (p == start)
                        {
                            error = $"Coordinate expression contains character '{ch}' which is not allowed";
                            return [];
                        }

                        var text = expr[start..p];
                        if (text.Contains('.'))
                        {
                            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                            {
                                error = $"Coordinate expression contains floating point number '{text}' which could not be parsed";
                                return [];
                            }

                            tokens.Add(new MetacityToken(MetacityTokenKind.Double, 0, d, MetacityOperator.None, MetacityVariable.Unknown, null));
                        }
                        else
                        {
                            if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var l))
                            {
                                error = $"Coordinate expression contains integer '{text}' which could not be parsed";
                                return [];
                            }

                            tokens.Add(new MetacityToken(MetacityTokenKind.Int, (int)l, 0, MetacityOperator.None, MetacityVariable.Unknown, null));
                        }
                    }

                    break;
            }
        }

        if (tokens.Count == 0)
        {
            error = "Coordinate expression was empty or not understood";
            return [];
        }

        return tokens.ToArray();
    }

    private static bool IsVariableChar(char c) => char.IsAsciiLetter(c) || c == '_';

    private static MetacityOperator OperatorAt(string expr, int p, out int length)
    {
        length = 1;
        switch (expr[p])
        {
            case '+':
                return MetacityOperator.Add;
            case '-':
                return MetacityOperator.Subtract;
            case '*':
                return MetacityOperator.Multiply;
            case '/':
                return MetacityOperator.Divide;
            case '%':
                return MetacityOperator.Mod;
            case '`':
                if (string.CompareOrdinal(expr, p, "`max`", 0, 5) == 0)
                {
                    length = 5;
                    return MetacityOperator.Max;
                }

                if (string.CompareOrdinal(expr, p, "`min`", 0, 5) == 0)
                {
                    length = 5;
                    return MetacityOperator.Min;
                }

                break;
        }

        length = 0;
        return MetacityOperator.None;
    }

    private static MetacityExpressionError EvaluateTokens(ReadOnlySpan<MetacityToken> tokens, in MetacityExpressionEnvironment env, bool hasObject, bool validating, out int value)
    {
        value = 0;
        var error = EvaluateGroup(tokens, in env, hasObject, validating, out var result);
        if (error != MetacityExpressionError.None)
        {
            return error;
        }

        value = result.Kind == MetacityOperandKind.Double ? (int)result.DoubleValue : result.IntValue;
        return MetacityExpressionError.None;
    }

    private static MetacityExpressionError EvaluateGroup(ReadOnlySpan<MetacityToken> tokens, in MetacityExpressionEnvironment env, bool hasObject, bool validating, out MetacityOperand result)
    {
        Span<MetacityOperand> exprs = stackalloc MetacityOperand[MaxOperands];
        var count = 0;
        var parenLevel = 0;
        var firstParen = 0;
        result = default;

        for (var i = 0; i < tokens.Length; i++)
        {
            var t = tokens[i];
            if (count >= MaxOperands)
            {
                return MetacityExpressionError.Overflow;
            }

            if (parenLevel == 0)
            {
                switch (t.Kind)
                {
                    case MetacityTokenKind.Int:
                        exprs[count++] = new MetacityOperand { Kind = MetacityOperandKind.Int, IntValue = t.IntValue };
                        break;
                    case MetacityTokenKind.Double:
                        exprs[count++] = new MetacityOperand { Kind = MetacityOperandKind.Double, DoubleValue = t.DoubleValue };
                        break;
                    case MetacityTokenKind.OpenParen:
                        parenLevel++;
                        firstParen = i;
                        break;
                    case MetacityTokenKind.CloseParen:
                        return MetacityExpressionError.BadParens;
                    case MetacityTokenKind.Variable:
                        if (validating)
                        {
                            exprs[count++] = new MetacityOperand { Kind = MetacityOperandKind.Int, IntValue = 1 };
                            break;
                        }

                        if (t.Variable == MetacityVariable.Unknown ||
                            (!hasObject && t.Variable is MetacityVariable.ObjectWidth or MetacityVariable.ObjectHeight))
                        {
                            return MetacityExpressionError.UnknownVariable;
                        }

                        exprs[count++] = new MetacityOperand { Kind = MetacityOperandKind.Int, IntValue = env.Get(t.Variable) };
                        break;
                    case MetacityTokenKind.Operator:
                        exprs[count++] = new MetacityOperand { Kind = MetacityOperandKind.Operator, Operator = t.Operator };
                        break;
                }
            }
            else
            {
                switch (t.Kind)
                {
                    case MetacityTokenKind.OpenParen:
                        parenLevel++;
                        break;
                    case MetacityTokenKind.CloseParen:
                        if (parenLevel == 1)
                        {
                            var inner = EvaluateGroup(tokens.Slice(firstParen + 1, i - firstParen - 1), in env, hasObject, validating, out var innerResult);
                            if (inner != MetacityExpressionError.None)
                            {
                                return inner;
                            }

                            exprs[count++] = innerResult;
                        }

                        parenLevel--;
                        break;
                }
            }
        }

        if (parenLevel > 0)
        {
            return MetacityExpressionError.BadParens;
        }

        if (count == 0)
        {
            return MetacityExpressionError.Empty;
        }

        for (var precedence = 2; precedence >= 0; precedence--)
        {
            var error = Reduce(exprs, ref count, precedence);
            if (error != MetacityExpressionError.None)
            {
                return error;
            }
        }

        result = exprs[0];
        return MetacityExpressionError.None;
    }

    private static MetacityExpressionError Reduce(Span<MetacityOperand> exprs, ref int count, int precedence)
    {
        var i = 1;
        while (i < count)
        {
            if (exprs[i - 1].Kind == MetacityOperandKind.Operator)
            {
                return MetacityExpressionError.Structure;
            }

            if (exprs[i].Kind != MetacityOperandKind.Operator)
            {
                return MetacityExpressionError.Structure;
            }

            if (i == count - 1)
            {
                return MetacityExpressionError.Structure;
            }

            if (exprs[i + 1].Kind == MetacityOperandKind.Operator)
            {
                return MetacityExpressionError.Structure;
            }

            var op = exprs[i].Operator;
            var compress = precedence switch
            {
                2 => op is MetacityOperator.Divide or MetacityOperator.Mod or MetacityOperator.Multiply,
                1 => op is MetacityOperator.Add or MetacityOperator.Subtract,
                _ => op is MetacityOperator.Max or MetacityOperator.Min,
            };

            if (compress)
            {
                var error = Apply(ref exprs[i - 1], ref exprs[i + 1], op);
                if (error != MetacityExpressionError.None)
                {
                    return error;
                }

                if (i + 2 < count)
                {
                    exprs.Slice(i + 2, count - i - 2).CopyTo(exprs.Slice(i));
                }

                count -= 2;
            }
            else
            {
                i += 2;
            }
        }

        return MetacityExpressionError.None;
    }

    private static MetacityExpressionError Apply(ref MetacityOperand a, ref MetacityOperand b, MetacityOperator op)
    {
        if (a.Kind == MetacityOperandKind.Double || b.Kind == MetacityOperandKind.Double)
        {
            if (a.Kind != MetacityOperandKind.Double)
            {
                a.Kind = MetacityOperandKind.Double;
                a.DoubleValue = a.IntValue;
            }

            if (b.Kind != MetacityOperandKind.Double)
            {
                b.Kind = MetacityOperandKind.Double;
                b.DoubleValue = b.IntValue;
            }

            switch (op)
            {
                case MetacityOperator.Multiply:
                    a.DoubleValue *= b.DoubleValue;
                    break;
                case MetacityOperator.Divide:
                    if (b.DoubleValue == 0.0)
                    {
                        return MetacityExpressionError.DivideByZero;
                    }

                    a.DoubleValue /= b.DoubleValue;
                    break;
                case MetacityOperator.Mod:
                    return MetacityExpressionError.ModOnFloat;
                case MetacityOperator.Add:
                    a.DoubleValue += b.DoubleValue;
                    break;
                case MetacityOperator.Subtract:
                    a.DoubleValue -= b.DoubleValue;
                    break;
                case MetacityOperator.Max:
                    a.DoubleValue = Math.Max(a.DoubleValue, b.DoubleValue);
                    break;
                case MetacityOperator.Min:
                    a.DoubleValue = Math.Min(a.DoubleValue, b.DoubleValue);
                    break;
            }

            return MetacityExpressionError.None;
        }

        switch (op)
        {
            case MetacityOperator.Multiply:
                a.IntValue *= b.IntValue;
                break;
            case MetacityOperator.Divide:
                if (b.IntValue == 0)
                {
                    return MetacityExpressionError.DivideByZero;
                }

                a.IntValue /= b.IntValue;
                break;
            case MetacityOperator.Mod:
                if (b.IntValue == 0)
                {
                    return MetacityExpressionError.DivideByZero;
                }

                a.IntValue %= b.IntValue;
                break;
            case MetacityOperator.Add:
                a.IntValue += b.IntValue;
                break;
            case MetacityOperator.Subtract:
                a.IntValue -= b.IntValue;
                break;
            case MetacityOperator.Max:
                a.IntValue = Math.Max(a.IntValue, b.IntValue);
                break;
            case MetacityOperator.Min:
                a.IntValue = Math.Min(a.IntValue, b.IntValue);
                break;
        }

        return MetacityExpressionError.None;
    }
}
