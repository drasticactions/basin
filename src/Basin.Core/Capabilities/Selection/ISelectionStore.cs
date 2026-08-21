namespace Basin.Capabilities;

public interface ISelectionStore
{
    int GetOffer(SelectionKind kind, Span<string> types);

    DataSource? Current(SelectionKind kind);

    bool SetSelection(SelectionKind kind, DataSource? source, uint serial);

    bool Receive(SelectionKind kind, string mimeType, ClientFd fd);

    event Action<SelectionKind>? SelectionChanged;
}
