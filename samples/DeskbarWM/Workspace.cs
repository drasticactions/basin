namespace DeskbarWm;

internal static class Workspace
{
    public const uint AllMask = uint.MaxValue;

    public static uint MaskOf(int index) => 1u << Math.Clamp(index, 0, 31);

    public static bool Includes(uint mask, int index) => (mask & MaskOf(index)) != 0;
}
