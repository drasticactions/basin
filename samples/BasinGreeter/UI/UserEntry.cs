using Avalonia.Media.Imaging;

namespace BasinGreeter.UI;

public sealed class UserEntry
{
    public UserEntry(string name, string displayName, Bitmap? avatar)
    {
        Name = name;
        DisplayName = displayName;
        Avatar = avatar;
        var initials = new string([.. displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(static part => char.ToUpperInvariant(part[0]))]);
        Initials = initials.Length > 0 ? initials : name[..1].ToUpperInvariant();
    }

    public string Name { get; }

    public string DisplayName { get; }

    public string Initials { get; }

    public Bitmap? Avatar { get; }

    public bool HasAvatar => Avatar is not null;

    public bool IsOther { get; init; }

    public override string ToString() => DisplayName;
}
