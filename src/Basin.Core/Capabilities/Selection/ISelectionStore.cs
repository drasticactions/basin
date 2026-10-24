namespace Basin.Capabilities;

/// <summary>
/// Holds the clipboard and the primary selection. Every selection protocol and the X11 bridge read and write it.
/// </summary>
public interface ISelectionStore
{
    /// <summary>
    /// Copies the MIME types that the current owner of a selection offers.
    /// </summary>
    /// <param name="kind">The selection to read.</param>
    /// <param name="types">The span that receives the MIME types.</param>
    /// <returns>The number of types written, 0 when the selection is empty, or -1 when the span is too small.</returns>
    int GetOffer(SelectionKind kind, Span<string> types);

    /// <summary>
    /// Gets the data source that owns a selection.
    /// </summary>
    /// <param name="kind">The selection to read.</param>
    /// <returns>The owner, or <see langword="null"/> when the selection is empty or its source is destroyed.</returns>
    DataSource? Current(SelectionKind kind);

    /// <summary>
    /// Replaces the owner of a selection and raises <see cref="SelectionChanged"/>.
    /// </summary>
    /// <param name="kind">The selection to change.</param>
    /// <param name="source">The new owner, or <see langword="null"/> to clear the selection.</param>
    /// <param name="serial">The input serial that authorizes the change, or <see cref="SelectionSerial.Unchecked"/> to skip the check.</param>
    /// <returns><see langword="true"/> when the selection changed, or <see langword="false"/> when the serial is not valid.</returns>
    bool SetSelection(SelectionKind kind, DataSource? source, uint serial);

    /// <summary>
    /// Asks the owner of a selection to write its data in one MIME type into a descriptor. The store takes ownership of <paramref name="fd"/>.
    /// </summary>
    /// <param name="kind">The selection to read.</param>
    /// <param name="mimeType">The MIME type to receive.</param>
    /// <param name="fd">The write end of the pipe that receives the data.</param>
    /// <returns><see langword="true"/> when an owner received the request, or <see langword="false"/> when the selection is empty and the descriptor was closed.</returns>
    bool Receive(SelectionKind kind, string mimeType, ClientFd fd);

    /// <summary>
    /// Occurs when the owner of a selection changes, with the selection as the argument.
    /// </summary>
    event Action<SelectionKind>? SelectionChanged;
}
