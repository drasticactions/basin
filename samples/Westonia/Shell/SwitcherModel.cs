using System.Collections.ObjectModel;
using System.ComponentModel;
namespace Westonia.Shell;

public sealed class SwitcherModel
{
    public ObservableCollection<SwitcherEntry> Entries { get; } = [];
}
