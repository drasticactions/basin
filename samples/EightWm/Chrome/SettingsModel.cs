using System.Collections.ObjectModel;

namespace EightWm;

public sealed class SettingsModel : ObservableModel
{
    private bool _dark = true;
    private bool _animations = true;
    private bool _hotCorners = true;
    private uint _accent;

    public event Action<string>? Changed;

    public ObservableCollection<AccentSwatch> Accents { get; } = [];

    public bool Dark
    {
        get => _dark;
        set
        {
            if (Set(ref _dark, value))
            {
                Changed?.Invoke(nameof(Dark));
            }
        }
    }

    public bool Animations
    {
        get => _animations;
        set
        {
            if (Set(ref _animations, value))
            {
                Changed?.Invoke(nameof(Animations));
            }
        }
    }

    public bool HotCorners
    {
        get => _hotCorners;
        set
        {
            if (Set(ref _hotCorners, value))
            {
                Changed?.Invoke(nameof(HotCorners));
            }
        }
    }

    public uint Accent
    {
        get => _accent;
        set
        {
            if (!Set(ref _accent, value))
            {
                return;
            }

            MarkCurrent();
            Changed?.Invoke(nameof(Accent));
        }
    }

    public void SetPalette(IReadOnlyList<uint> palette)
    {
        Accents.Clear();
        foreach (var argb in palette)
        {
            Accents.Add(new AccentSwatch(argb, chosen => Accent = chosen));
        }

        if (!palette.Contains(_accent) && _accent != 0)
        {
            Accents.Add(new AccentSwatch(_accent, chosen => Accent = chosen));
        }

        MarkCurrent();
    }

    public void Load(bool dark, uint accent, bool animations, bool hotCorners)
    {
        _dark = dark;
        _accent = accent;
        _animations = animations;
        _hotCorners = hotCorners;
        Raise(nameof(Dark));
        Raise(nameof(Accent));
        Raise(nameof(Animations));
        Raise(nameof(HotCorners));
        MarkCurrent();
    }

    private void MarkCurrent()
    {
        foreach (var swatch in Accents)
        {
            swatch.IsCurrent = swatch.Argb == _accent;
        }
    }
}
