namespace Basin.Frames.Metacity;

internal enum MetacityExpressionError
{
    None,
    BadCharacter,
    BadParens,
    UnknownVariable,
    DivideByZero,
    ModOnFloat,
    Overflow,
    Structure,
    Empty,
}
