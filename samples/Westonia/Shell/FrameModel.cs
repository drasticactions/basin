using System.ComponentModel;
using Avalonia;
using Avalonia.Media;

namespace Westonia.Shell;

public sealed class FrameModel : INotifyPropertyChanged
{
    public const int Margin = 32;

    public const int BorderWidth = 6;

    public const int TitlebarHeight = 27;

    private string _title = string.Empty;
    private bool _active = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title
    {
        get => _title;
        set => Set(ref _title, value, nameof(Title));
    }

    public bool Active
    {
        get => _active;
        set => Set(ref _active, value, nameof(Active));
    }

    public bool HasClose { get; set; } = true;

    public Thickness TitleMargin { get; } = new(Margin, Margin, Margin, 0);

    public Thickness TitleShadowMargin { get; } = new(Margin - 6, Margin - 4, Margin - 6, 0);

    public double TitlebarHeightValue => TitlebarHeight;

    private bool Set<T>(ref T field, T value, string name)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(name);
        return true;
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
