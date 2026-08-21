namespace Westonia;

public static class BindingModifiers
{
    public static ShellModifiers Parse(string name) => name.ToLowerInvariant() switch
    {
        "ctrl" => ShellModifiers.Ctrl,
        "alt" => ShellModifiers.Alt,
        "super" => ShellModifiers.Super,
        "none" => ShellModifiers.None,
        _ => ShellModifiers.Super,
    };
}
