namespace Basin.Capabilities;

public readonly record struct PreeditString(string Text, int CursorBegin, int CursorEnd);
