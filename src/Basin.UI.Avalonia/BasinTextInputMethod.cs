using Avalonia;
using Avalonia.Input.TextInput;

namespace Basin.UI.Avalonia;

internal sealed class BasinTextInputMethod : ITextInputMethodImpl
{
    private readonly AvaloniaTextFields _fields;

    public BasinTextInputMethod(AvaloniaTextFields fields) => _fields = fields;

    public void SetClient(TextInputMethodClient? client) => _fields.SetClient(this, client);

    public void SetCursorRect(Rect rect)
    {
    }

    public void SetOptions(TextInputOptions options) => _fields.SetOptions(this, options);

    public void Reset()
    {
    }
}
