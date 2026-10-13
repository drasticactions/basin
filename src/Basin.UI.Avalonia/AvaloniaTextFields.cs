using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.VisualTree;
using Basin.Capabilities;

namespace Basin.UI.Avalonia;

public sealed class AvaloniaTextFields : IUITextFields
{
    private BasinTextInputMethod? _owner;
    private TextInputMethodClient? _client;
    private TextInputOptions _options = TextInputOptions.Default;

    public UITextField? Focused { get; private set; }

    public event Action? FocusChanged;

    public event Action<UITouchTarget>? Touched;

    internal void SetClient(BasinTextInputMethod owner, TextInputMethodClient? client)
    {
        if (client is null && !ReferenceEquals(owner, _owner))
        {
            return;
        }

        _owner = client is null ? null : owner;
        _client = client;
        Update();
    }

    internal void SetOptions(BasinTextInputMethod owner, TextInputOptions options)
    {
        if (ReferenceEquals(owner, _owner) || _owner is null)
        {
            _options = options;
            if (_client is not null)
            {
                Update();
            }
        }
    }

    internal void Classify(TopLevel root, global::Avalonia.Point point)
    {
        if (Touched is null)
        {
            return;
        }

        var hit = root.InputHitTest(point) as Visual;
        Touched.Invoke(TargetOf(hit));
    }

    private UITouchTarget TargetOf(Visual? hit)
    {
        for (var visual = hit; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is IInputPaneView)
            {
                return UITouchTarget.InputPane;
            }
        }

        if (hit is not null && FieldVisual() is { } field && (ReferenceEquals(field, hit) || field.IsVisualAncestorOf(hit)))
        {
            return UITouchTarget.Field;
        }

        return UITouchTarget.Elsewhere;
    }

    private Visual? FieldVisual()
    {
        if (_client?.TextViewVisual is not { } view)
        {
            return null;
        }

        return view.FindAncestorOfType<TextBox>(includeSelf: true) ?? view;
    }

    private void Update()
    {
        UITextField? next = _client is null ? null : Describe();
        if (next == Focused)
        {
            return;
        }

        Focused = next;
        FocusChanged?.Invoke();
    }

    private UITextField Describe()
    {
        var box = FieldVisual() as TextBox;
        var purpose = box is { PasswordChar: not '\0' } ? UITextField.PurposePassword : PurposeOf(_options.ContentType);
        var hint = 0u;
        if (_options.AutoCapitalization)
        {
            hint |= UITextField.HintAutoCapitalization;
        }

        if (_options.Lowercase)
        {
            hint |= UITextField.HintLowercase;
        }

        if (_options.Uppercase)
        {
            hint |= UITextField.HintUppercase;
        }

        if (_options.IsSensitive || purpose == UITextField.PurposePassword)
        {
            hint |= UITextField.HintSensitiveData;
        }

        if (purpose == UITextField.PurposePassword)
        {
            hint |= UITextField.HintHiddenText;
        }

        if (_options.Multiline)
        {
            hint |= UITextField.HintMultiline;
        }

        return new UITextField(purpose, hint);
    }

    private static uint PurposeOf(TextInputContentType type) => type switch
    {
        TextInputContentType.Alpha => UITextField.PurposeAlpha,
        TextInputContentType.Digits => UITextField.PurposeDigits,
        TextInputContentType.Pin => UITextField.PurposePin,
        TextInputContentType.Number => UITextField.PurposeNumber,
        TextInputContentType.Email => UITextField.PurposeEmail,
        TextInputContentType.Url => UITextField.PurposeUrl,
        TextInputContentType.Name => UITextField.PurposeName,
        TextInputContentType.Password => UITextField.PurposePassword,
        _ => UITextField.PurposeNormal,
    };
}
