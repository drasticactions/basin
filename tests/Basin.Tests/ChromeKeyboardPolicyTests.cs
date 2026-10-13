using Basin.Capabilities;
using Basin.SoftKeyboard;
using Xunit;

namespace Basin.Tests;

public sealed class ChromeKeyboardPolicyTests
{
    private sealed class Fields : IUITextFields
    {
        public UITextField? Focused { get; private set; }

        public event Action? FocusChanged;

        public event Action<UITouchTarget>? Touched;

        public void Focus(UITextField? field)
        {
            Focused = field;
            FocusChanged?.Invoke();
        }

        public void Touch(UITouchTarget target) => Touched?.Invoke(target);
    }

    private static readonly UITextField Password = new(UITextField.PurposePassword, UITextField.HintHiddenText);

    [Fact]
    public void A_field_focused_without_touch_does_not_raise_the_keyboard_and_a_touch_on_it_does()
    {
        var fields = new Fields();
        using var policy = new ChromeKeyboardPolicy(fields);
        fields.Focus(Password);
        Assert.False(policy.Wanted);

        fields.Touch(UITouchTarget.Field);
        Assert.True(policy.Wanted);
        Assert.Equal(FieldKind.Password, policy.Field.Kind);
        Assert.False(policy.Field.AutoCap);
    }

    [Fact]
    public void A_touch_elsewhere_hides_and_only_a_touch_on_the_field_brings_it_back()
    {
        var fields = new Fields();
        using var policy = new ChromeKeyboardPolicy(fields);
        var log = new List<string>();
        policy.WantedChanged += (wanted, why) => log.Add($"{wanted}:{why}");
        fields.Focus(Password);
        fields.Touch(UITouchTarget.Field);
        fields.Touch(UITouchTarget.InputPane);
        fields.Touch(UITouchTarget.Elsewhere);
        fields.Touch(UITouchTarget.Elsewhere);
        fields.Touch(UITouchTarget.InputPane);
        Assert.False(policy.Wanted);

        fields.Touch(UITouchTarget.Field);
        policy.Hide("hide");
        fields.Touch(UITouchTarget.Elsewhere);
        Assert.False(policy.Wanted);
        Assert.Equal(["True:touch", "False:touch-away", "True:touch", "False:hide"], log);
    }

    [Fact]
    public void A_touch_that_moves_focus_to_another_field_raises_it_and_a_blur_or_a_key_hides_it()
    {
        var fields = new Fields();
        using var policy = new ChromeKeyboardPolicy(fields);
        fields.Touch(UITouchTarget.Elsewhere);
        fields.Focus(Password);
        Assert.True(policy.Wanted);

        fields.Focus(null);
        Assert.False(policy.Wanted);

        fields.Focus(Password);
        Assert.True(policy.Wanted);
        policy.NoteHardwareKey();
        Assert.False(policy.Wanted);
        fields.Focus(new UITextField(UITextField.PurposeNormal, 0));
        Assert.False(policy.Wanted);
    }

    [Fact]
    public void Present_raises_only_after_a_touch_unless_any_focus_is_the_policy()
    {
        var fields = new Fields();
        using var touchOnly = new ChromeKeyboardPolicy(fields);
        using var anyFocus = new ChromeKeyboardPolicy(fields, SoftKeyboardPolicy.OnAnyFocus);
        fields.Focus(Password);
        touchOnly.Present();
        Assert.False(touchOnly.Wanted);
        Assert.True(anyFocus.Wanted);

        touchOnly.NoteTouch();
        touchOnly.Present();
        Assert.True(touchOnly.Wanted);
    }
}
