using Basin.Diagnostics;

namespace Basin.Scene;

public sealed class ToplevelSceneIndex
{
    private readonly ThreadAffinity _affinity = ThreadAffinity.Capture();
    private readonly Dictionary<ulong, ToplevelCaptureTrees> _trees = [];
    private readonly Dictionary<SceneNode, ulong> _ids = [];
    private readonly Dictionary<SceneNode, ulong> _popupIds = [];

    public void Set(ulong toplevelId, in ToplevelCaptureTrees trees)
    {
        _affinity.Assert();
        if (_trees.TryGetValue(toplevelId, out var current))
        {
            Forget(current);
        }

        _trees[toplevelId] = trees;
        if (trees.Content is { } content)
        {
            _ids[content] = toplevelId;
        }

        if (trees.Popups is { } popups)
        {
            _popupIds[popups] = toplevelId;
        }
    }

    public void Remove(ulong toplevelId)
    {
        _affinity.Assert();
        if (_trees.Remove(toplevelId, out var trees))
        {
            Forget(trees);
        }
    }

    public bool TryGet(ulong toplevelId, out ToplevelCaptureTrees trees) => _trees.TryGetValue(toplevelId, out trees);

    public bool TryIdOf(SceneNode node, out ulong toplevelId)
    {
        ArgumentNullException.ThrowIfNull(node);
        return _ids.TryGetValue(node, out toplevelId);
    }

    public bool TryOwnerOf(SceneNode node, out ulong toplevelId)
    {
        ArgumentNullException.ThrowIfNull(node);
        for (SceneNode? candidate = node; candidate is not null; candidate = candidate.Parent)
        {
            if (_ids.TryGetValue(candidate, out toplevelId) || _popupIds.TryGetValue(candidate, out toplevelId))
            {
                return true;
            }
        }

        toplevelId = 0;
        return false;
    }

    private void Forget(in ToplevelCaptureTrees trees)
    {
        if (trees.Content is { } content)
        {
            _ids.Remove(content);
        }

        if (trees.Popups is { } popups)
        {
            _popupIds.Remove(popups);
        }
    }
}
