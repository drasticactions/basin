using Basin.Capabilities;

namespace Basin.Shell.Xdg;

public sealed class XdgPopupDirectory : IPopupDirectory, IDisposable
{
    private readonly XdgShell _shell;
    private readonly List<XdgPopupWindow> _popups = [];

    public XdgPopupDirectory(XdgShell shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _shell = shell;
        shell.NewPopup += OnNewPopup;
    }

    public void CollectPopups(Surface owner, List<Surface> into)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(into);
        foreach (var popup in _popups)
        {
            if (popup.Surface.IsMapped && Owns(owner, popup))
            {
                into.Add(popup.Surface);
            }
        }
    }

    public void Dispose()
    {
        _shell.NewPopup -= OnNewPopup;
        _popups.Clear();
    }

    private bool Owns(Surface owner, XdgPopupWindow popup)
    {
        for (var parent = popup.Parent?.Surface; parent is not null;)
        {
            if (parent == owner)
            {
                return true;
            }

            var next = _popups.Find(candidate => candidate.Surface == parent);
            parent = next?.Parent?.Surface;
        }

        return false;
    }

    private void OnNewPopup(XdgPopupWindow popup)
    {
        _popups.Add(popup);
        popup.Destroyed += () => _popups.Remove(popup);
    }
}
