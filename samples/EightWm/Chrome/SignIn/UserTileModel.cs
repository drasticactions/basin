using Avalonia.Media;

namespace EightWm;

public sealed class UserTileModel(string name, string displayName, IImage? picture, bool isOther = false) : ObservableModel
{
    public string Name { get; } = name;

    public string DisplayName { get; } = displayName.Length > 0 ? displayName : name;

    public IImage? Picture { get; } = picture;

    public bool HasPicture => Picture is not null;

    public bool IsOther { get; } = isOther;

    public string Initials => InitialsOf(DisplayName);

    public static string InitialsOf(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => string.Concat(parts[0][..1], parts[^1][..1]).ToUpperInvariant(),
        };
    }
}
