using Basin.Capabilities;

namespace Basin.Desktop;

/// <summary>
/// Reports the caret that a client gives through text-input-v3 as an <see cref="IAccessibleFocus"/> caret, in layout coordinates.
/// It serves clients with no accessibility tree, such as terminals, because every client with an input method sends its caret rectangle.
/// </summary>
public sealed class TextInputCaret : IAccessibleFocus, IDisposable
{
    private readonly TextInputManager _textInput;
    private readonly Scene.Scene _scene;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextInputCaret"/> class.
    /// </summary>
    /// <param name="textInput">The text-input-v3 manager.</param>
    /// <param name="scene">The scene that shows the clients' surfaces.</param>
    public TextInputCaret(TextInputManager textInput, Scene.Scene scene)
    {
        ArgumentNullException.ThrowIfNull(textInput);
        ArgumentNullException.ThrowIfNull(scene);
        _textInput = textInput;
        _scene = scene;
        textInput.CaretMoved += OnCaretMoved;
    }

    /// <inheritdoc/>
    public event Action<Box, AccessibleFocusKind>? Moved;

    /// <summary>
    /// Stops reporting. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _textInput.CaretMoved -= OnCaretMoved;
    }

    private void OnCaretMoved(Surface surface, Box local)
    {
        if (_scene.TryMapFromSurface(surface, local, out var box))
        {
            Moved?.Invoke(box, AccessibleFocusKind.Caret);
        }
    }
}
