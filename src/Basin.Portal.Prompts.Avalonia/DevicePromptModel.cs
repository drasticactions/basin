using Basin.Capabilities;

namespace Basin.Portal.Prompts.Avalonia;

public sealed class DevicePromptModel : PromptModel
{
    private bool _keyboard;
    private bool _pointer;
    private bool _touch;
    private bool _clipboard;
    private bool _persist;

    public DevicePromptModel(in DevicePrompt prompt)
        : base("Allow remote control?", prompt.AppId, prompt.DisplayName, prompt.IconPath)
    {
        OffersKeyboard = (prompt.Requested & InputDeviceCapability.Keyboard) != 0;
        OffersPointer = (prompt.Requested & InputDeviceCapability.Pointer) != 0;
        OffersTouch = (prompt.Requested & InputDeviceCapability.Touch) != 0;
        OffersClipboard = prompt.ClipboardRequested;
        OfferPersist = prompt.OfferPersist;
        _keyboard = OffersKeyboard;
        _pointer = OffersPointer;
        _touch = OffersTouch;
        _clipboard = OffersClipboard;
    }

    public bool OffersKeyboard { get; }

    public bool OffersPointer { get; }

    public bool OffersTouch { get; }

    public bool OffersClipboard { get; }

    public bool OfferPersist { get; }

    public override string DenyLabel => "Cancel";

    public bool Keyboard
    {
        get => _keyboard;
        set
        {
            _keyboard = value;
            Changed();
        }
    }

    public bool Pointer
    {
        get => _pointer;
        set
        {
            _pointer = value;
            Changed();
        }
    }

    public bool Touch
    {
        get => _touch;
        set
        {
            _touch = value;
            Changed();
        }
    }

    public bool Clipboard
    {
        get => _clipboard;
        set
        {
            _clipboard = value;
            Changed();
        }
    }

    public bool Persist
    {
        get => _persist;
        set
        {
            _persist = value;
            Changed();
        }
    }

    public InputDeviceCapability Devices =>
        (Keyboard ? InputDeviceCapability.Keyboard : 0) | (Pointer ? InputDeviceCapability.Pointer : 0) | (Touch ? InputDeviceCapability.Touch : 0);

    protected override bool CanAccept => Devices != 0;
}
