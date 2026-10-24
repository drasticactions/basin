using System.Collections.ObjectModel;
using Avalonia.Media;

namespace TabWm.Chrome;

public sealed class TabModel : ObservableModel
{
    private string _label = "New Tab";
    private string? _iconName = "tab-new";
    private IImage? _icon;
    private TabAudioState _audio;
    private bool _attention;
    private bool _dropTarget;
    private bool _busy;
    private bool _editing;
    private string _editText = string.Empty;

    public TabModel(ulong id)
    {
        Id = id;
    }

    public ulong Id { get; }

    public string Label
    {
        get => _label;
        set => Set(ref _label, value);
    }

    public string? IconName
    {
        get => _iconName;
        set
        {
            if (Set(ref _iconName, value))
            {
                Raise(nameof(HasIconName));
            }
        }
    }

    public bool HasIconName => _iconName is not null && _icon is null && !_busy;

    public IImage? Icon
    {
        get => _icon;
        set
        {
            if (Set(ref _icon, value))
            {
                Raise(nameof(HasImage));
                Raise(nameof(HasIconName));
            }
        }
    }

    public bool HasImage => _icon is not null && !_busy;

    internal TabAudioState Audio
    {
        get => _audio;
        set
        {
            if (Set(ref _audio, value))
            {
                Raise(nameof(ShowAudio));
                Raise(nameof(AudioIcon));
            }
        }
    }

    public bool ShowAudio => _audio != TabAudioState.None;

    public string AudioIcon => _audio == TabAudioState.Muted ? "audio-volume-muted" : "audio-volume-high";

    public bool Attention
    {
        get => _attention;
        set => Set(ref _attention, value);
    }

    public bool DropTarget
    {
        get => _dropTarget;
        set => Set(ref _dropTarget, value);
    }

    public bool Busy
    {
        get => _busy;
        set
        {
            if (Set(ref _busy, value))
            {
                Raise(nameof(HasIconName));
                Raise(nameof(HasImage));
            }
        }
    }

    public bool IsEditing
    {
        get => _editing;
        set => Set(ref _editing, value);
    }

    public string EditText
    {
        get => _editText;
        set => Set(ref _editText, value);
    }

    public ObservableCollection<HiddenWindowModel> Hidden { get; } = [];

    public bool HasHidden => Hidden.Count > 0;

    internal void RaiseHidden() => Raise(nameof(HasHidden));
}
