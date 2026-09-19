namespace Basin.Shell.Nested;

public sealed record PanelWindowInfo(
    long Id,
    string Title,
    string AppId,
    string? Suffix,
    int Workspace,
    bool Sticky,
    bool Focused,
    bool Minimized,
    bool DemandsAttention,
    Box Frame,
    string? Icon = null);
