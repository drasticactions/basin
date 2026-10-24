using Basin.Scene;
using TabWm.Chrome;

namespace TabWm;

internal sealed class Tab
{
    public const string EmptyLabel = "New Tab";

    public Tab(ulong id, TabSet set, SceneTree tabs, SceneTree fullscreen)
    {
        Id = id;
        Set = set;
        Tree = new SceneTree(tabs) { Enabled = false };
        FullscreenTree = new SceneTree(fullscreen) { Enabled = false };
        Model = new TabModel(id);
    }

    public ulong Id { get; }

    public TabSet Set { get; set; }

    public SceneTree Tree { get; }

    public SceneTree FullscreenTree { get; }

    public TabModel Model { get; }

    public List<TabWindow> Windows { get; } = [];

    public string? UserLabel { get; set; }

    public bool Muted { get; set; }

    public bool Playing { get; set; }

    public bool Attention { get; set; }

    public bool Shown { get; private set; }

    public List<TabLaunch> Launches { get; } = [];

    public long LaunchedAtMs { get; set; } = long.MinValue / 2;

    public TabCloser<TabWindow>? Closer { get; set; }

    public int CascadeStep { get; set; }

    public bool IsEmpty
    {
        get
        {
            foreach (var window in Windows)
            {
                if (window.Mapped)
                {
                    return false;
                }
            }

            return true;
        }
    }

    public TabWindow? Focused
    {
        get
        {
            foreach (var window in Windows)
            {
                if (window.Mapped && !window.Minimized)
                {
                    return window;
                }
            }

            return null;
        }
    }

    public TabWindow? Labeled
    {
        get
        {
            foreach (var window in Windows)
            {
                if (window.Mapped)
                {
                    return window;
                }
            }

            return null;
        }
    }

    public string Label => UserLabel ?? (Labeled is { } window && window.Title.Length > 0 ? window.Title : Labeled?.AppId is { Length: > 0 } app ? app : EmptyLabel);

    public TabAudioState Audio => Muted ? TabAudioState.Muted : Playing ? TabAudioState.Playing : TabAudioState.None;

    public bool HasFullscreen
    {
        get
        {
            foreach (var window in Windows)
            {
                if (window.Mapped && window.Fullscreen && !window.Minimized)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public void Show(bool shown)
    {
        Shown = shown;
        Tree.Enabled = shown;
        FullscreenTree.Enabled = shown;
    }

    public void Destroy()
    {
        if (!Tree.IsDestroyed)
        {
            Tree.Destroy();
        }

        if (!FullscreenTree.IsDestroyed)
        {
            FullscreenTree.Destroy();
        }
    }
}
