using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace BasinGreeter.UI;

public sealed class BackgroundModel : Observable
{
    private IBrush _fill = Brushes.Black;
    private Bitmap? _image;
    private string _clock = "";

    public IBrush Fill
    {
        get => _fill;
        set => Set(ref _fill, value);
    }

    public Bitmap? Image
    {
        get => _image;
        set
        {
            if (Set(ref _image, value))
            {
                Raise(nameof(HasImage));
            }
        }
    }

    public bool HasImage => _image is not null;

    public string Clock
    {
        get => _clock;
        set => Set(ref _clock, value);
    }
}
