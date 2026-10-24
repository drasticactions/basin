using Basin.Capabilities;

namespace Basin.Shell.Xdg;

/// <summary>
/// The <see cref="IPopupDirectory"/> over one <see cref="XdgShell"/>. It records each popup that the shell makes and forgets it when the popup is destroyed.
/// </summary>
public sealed class XdgPopupDirectory : IPopupDirectory, IDisposable
{
    private readonly XdgShell _shell;
    private readonly List<XdgPopupWindow> _popups = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="XdgPopupDirectory"/> class. It records only the popups that the shell makes after this call.
    /// </summary>
    /// <param name="shell">The shell to watch.</param>
    public XdgPopupDirectory(XdgShell shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _shell = shell;
        shell.NewPopup += OnNewPopup;
    }

    /// <summary>
    /// Adds the surface of each mapped popup whose <see cref="XdgPopupWindow.Parent"/> chain reaches <paramref name="owner"/>. A popup that only a layer surface parents is not included.
    /// </summary>
    /// <param name="owner">The surface that owns the popups.</param>
    /// <param name="into">The list to add the surfaces to. The call does not clear it.</param>
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

    /// <summary>
    /// Stops watching the shell and forgets every recorded popup.
    /// </summary>
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
