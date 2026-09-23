using Avalonia.Media;

namespace EightWm;

public sealed class SplashModel : ObservableModel
{
    private string _title = string.Empty;
    private IBrush _fill = Brushes.Black;

    public string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    public IBrush Fill
    {
        get => _fill;
        set => Set(ref _fill, value);
    }
}
