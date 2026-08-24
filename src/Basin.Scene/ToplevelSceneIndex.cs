using Basin.Diagnostics;

namespace Basin.Scene;

public sealed class ToplevelSceneIndex
{
    private readonly ThreadAffinity _affinity = ThreadAffinity.Capture();
    private readonly Dictionary<ulong, ToplevelCaptureTrees> _trees = [];
    private readonly Dictionary<SceneNode, ulong> _ids = [];

    public void Set(ulong toplevelId, in ToplevelCaptureTrees trees)
    {
        _affinity.Assert();
        if (_trees.TryGetValue(toplevelId, out var current) && current.Content is { } previous)
        {
            _ids.Remove(previous);
        }

        _trees[toplevelId] = trees;
        if (trees.Content is { } content)
        {
            _ids[content] = toplevelId;
        }
    }

    public void Remove(ulong toplevelId)
    {
        _affinity.Assert();
        if (_trees.Remove(toplevelId, out var trees) && trees.Content is { } content)
        {
            _ids.Remove(content);
        }
    }

    public bool TryGet(ulong toplevelId, out ToplevelCaptureTrees trees) => _trees.TryGetValue(toplevelId, out trees);

    public bool TryIdOf(SceneNode node, out ulong toplevelId)
    {
        ArgumentNullException.ThrowIfNull(node);
        return _ids.TryGetValue(node, out toplevelId);
    }
}
