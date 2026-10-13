namespace Basin;

public readonly record struct HandoffInheritance(
    string Output,
    OutputMode Mode,
    bool Modeset,
    HandoffReason Reason,
    int Errno);
