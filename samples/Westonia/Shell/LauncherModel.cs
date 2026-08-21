using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;

namespace Westonia.Shell;

public sealed class LauncherModel
{
    public LauncherModel(string? displayName, IImage? icon, Action launch)
    {
        DisplayName = displayName;
        Icon = icon;
        Launch = new RelayCommand(launch);
    }

    public string? DisplayName { get; }

    public IImage? Icon { get; }

    public ICommand Launch { get; }
}
