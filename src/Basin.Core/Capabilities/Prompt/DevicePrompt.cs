namespace Basin.Capabilities;

public readonly record struct DevicePrompt(
    string AppId,
    string ParentWindow,
    bool Modal,
    InputDeviceCapability Requested,
    bool ClipboardRequested,
    bool OfferPersist)
{
    public string DisplayName { get; init; } = "";

    public string IconPath { get; init; } = "";
}
