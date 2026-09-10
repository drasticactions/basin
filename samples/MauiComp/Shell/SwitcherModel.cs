using System.Collections.ObjectModel;

namespace MauiComp.Shell;

public sealed class SwitcherModel
{
    public ObservableCollection<SwitcherEntry> Entries { get; } = [];
}
