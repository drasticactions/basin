using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.VisualTree;
using Basin.Capabilities;

namespace Basin.UI.Avalonia;

/// <summary>
/// Reports the focused Avalonia text field of the chrome and what each chrome touch hits.
/// </summary>
public sealed class AvaloniaTextFields : IUITextFields
{
    /// <summary>
    /// The attached property that marks a visual as part of the input pane. A touch on it reports <see cref="UITouchTarget.InputPane"/>.
    /// </summary>
    public static readonly AttachedProperty<bool> KeepsKeyboardProperty =
        AvaloniaProperty.RegisterAttached<AvaloniaTextFields, Visual, bool>("KeepsKeyboard");

    private BasinTextInputMethod? _owner;
    private TextInputMethodClient? _client;
    private TextInputOptions _options = TextInputOptions.Default;

    /// <inheritdoc/>
    public UITextField? Focused { get; private set; }

    /// <inheritdoc/>
    public event Action? FocusChanged;

    /// <summary>
    /// Occurs on the UI thread for each touch down on a chrome surface, with what the touch hit.
    /// </summary>
    public event Action<UITouchTarget>? Touched;

    /// <summary>
    /// Gets the <see cref="KeepsKeyboardProperty"/> value of a visual.
    /// </summary>
    /// <param name="visual">The visual to read.</param>
    /// <returns><see langword="true"/> when a touch on the visual keeps the soft keyboard open.</returns>
    public static bool GetKeepsKeyboard(Visual visual)
    {
        ArgumentNullException.ThrowIfNull(visual);
        return visual.GetValue(KeepsKeyboardProperty);
    }

    /// <summary>
    /// Sets the <see cref="KeepsKeyboardProperty"/> value of a visual.
    /// </summary>
    /// <param name="visual">The visual to change.</param>
    /// <param name="value"><see langword="true"/> to treat touches on the visual and its children as input pane touches.</param>
    public static void SetKeepsKeyboard(Visual visual, bool value)
    {
        ArgumentNullException.ThrowIfNull(visual);
        visual.SetValue(KeepsKeyboardProperty, value);
    }

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
            if (visual is IInputPaneView || visual.GetValue(KeepsKeyboardProperty))
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
