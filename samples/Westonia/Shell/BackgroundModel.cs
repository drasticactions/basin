using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;

namespace Westonia.Shell;

public sealed class BackgroundModel
{
    public IBrush Fill { get; set; } = Brushes.Black;

    public Bitmap? Image { get; set; }

    public bool HasImage => Image is not null;

    public Stretch Stretch { get; set; } = Stretch.UniformToFill;

    public HorizontalAlignment ImageAlignment { get; set; } = HorizontalAlignment.Stretch;

    public VerticalAlignment ImageVerticalAlignment { get; set; } = VerticalAlignment.Stretch;
}
