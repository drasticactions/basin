using System.ComponentModel;
using Microsoft.Maui.Graphics;

namespace MauiComp.Shell;

public sealed class BackgroundModel : INotifyPropertyChanged
{
    private Color _fill = Color.FromRgb(0x2b, 0x3a, 0x55);
    private string? _imagePath;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Color Fill
    {
        get => _fill;
        set => Set(ref _fill, value);
    }

    public string? ImagePath
    {
        get => _imagePath;
        set
        {
            if (Set(ref _imagePath, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasImage)));
            }
        }
    }

    public bool HasImage => _imagePath is not null;

    private bool Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
