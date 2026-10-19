namespace Basin.UI.ImGui;

public readonly record struct ImGuiModifierMasks(uint Control, uint Shift, uint Alt, uint Super)
{
    public static ImGuiModifierMasks Standard { get; } = new(Control: 1u << 2, Shift: 1u << 0, Alt: 1u << 3, Super: 1u << 6);

    public static ImGuiModifierMasks FromIndices(uint? control, uint? shift, uint? alt, uint? super) =>
        new(Bit(control), Bit(shift), Bit(alt), Bit(super));

    private static uint Bit(uint? index) => index is { } i and < 32 ? 1u << (int)i : 0;
}
