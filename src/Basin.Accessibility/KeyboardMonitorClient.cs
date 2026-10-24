namespace Basin.Accessibility;

internal sealed class KeyboardMonitorClient
{
    public bool Watched { get; set; }

    public bool Grabbed { get; set; }

    public HashSet<uint> Modifiers { get; set; } = [];

    public List<(uint Keysym, uint Modifiers)> Keystrokes { get; set; } = [];

    public bool IsEmpty => !Watched && !Grabbed && Modifiers.Count == 0 && Keystrokes.Count == 0;

    public bool ShouldGrab(HashSet<uint> suppressed, uint modifiers, uint keysym)
    {
        if (Grabbed)
        {
            return true;
        }

        foreach (var modifier in Modifiers)
        {
            if (modifier == keysym || suppressed.Contains(modifier))
            {
                return true;
            }
        }

        foreach (var (grabbed, mask) in Keystrokes)
        {
            if (grabbed == keysym && mask == modifiers)
            {
                return true;
            }
        }

        return false;
    }

    public bool ShouldWatch(HashSet<uint> suppressed, uint modifiers, uint keysym) => Watched || ShouldGrab(suppressed, modifiers, keysym);
}
