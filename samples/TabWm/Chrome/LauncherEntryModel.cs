using Avalonia.Media;

namespace TabWm.Chrome;

public sealed class LauncherEntryModel : ObservableModel
{
    private bool _pinned;

    public LauncherEntryModel(string id, string name, string? iconName, IImage? icon, bool run = false)
    {
        Id = id;
        Name = name;
        IconName = iconName;
        Icon = icon;
        IsRun = run;
    }

    public string Id { get; }

    public string Name { get; }

    public string? IconName { get; }

    public IImage? Icon { get; }

    public bool HasImage => Icon is not null;

    public bool HasIconName => Icon is null;

    public bool IsRun { get; }

    public bool IsPinned
    {
        get => _pinned;
        set => Set(ref _pinned, value);
    }
}
