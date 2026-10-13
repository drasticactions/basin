namespace Basin.Capabilities;

public interface IUITextFields
{
    UITextField? Focused { get; }

    event Action? FocusChanged;

    event Action<UITouchTarget>? Touched;
}
