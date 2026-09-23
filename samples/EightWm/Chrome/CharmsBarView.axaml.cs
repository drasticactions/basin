using Avalonia.Controls;

namespace EightWm;

public sealed partial class CharmsBarView : UserControl
{
    public CharmsBarView() => InitializeComponent();

    public IEnumerable<Button> Charms => [Search, Share, Start, Devices, Settings];

    public string? Hovered
    {
        get
        {
            foreach (var button in Charms)
            {
                if (button.IsPointerOver)
                {
                    return button.Name;
                }
            }

            return null;
        }
    }
}
