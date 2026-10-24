using Basin.Diagnostics;

namespace Basin.Scene;

/// <summary>
/// Maps toplevel ids to the scene trees that show their content and popups, and maps those trees back to ids.
/// </summary>
public sealed class ToplevelSceneIndex
{
    private readonly ThreadAffinity _affinity = ThreadAffinity.Capture();
    private readonly Dictionary<ulong, ToplevelCaptureTrees> _trees = [];
    private readonly Dictionary<SceneNode, ulong> _ids = [];
    private readonly Dictionary<SceneNode, ulong> _popupIds = [];

    /// <summary>
    /// Records the trees of a toplevel, and replaces any trees that the id had before.
    /// </summary>
    /// <param name="toplevelId">The toplevel id.</param>
    /// <param name="trees">The content and popup trees.</param>
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

    /// <summary>
    /// Forgets a toplevel and its trees. An unknown id does nothing.
    /// </summary>
    /// <param name="toplevelId">The toplevel id.</param>
    public void Remove(ulong toplevelId)
    {
        _affinity.Assert();
        if (_trees.Remove(toplevelId, out var trees))
        {
            Forget(trees);
        }
    }

    /// <summary>
    /// Gets the trees recorded for a toplevel.
    /// </summary>
    /// <param name="toplevelId">The toplevel id.</param>
    /// <param name="trees">The trees, or the default value when the id is unknown.</param>
    /// <returns><see langword="true"/> when the id is known.</returns>
    public bool TryGet(ulong toplevelId, out ToplevelCaptureTrees trees) => _trees.TryGetValue(toplevelId, out trees);

    /// <summary>
    /// Gets the toplevel whose content tree is exactly this node. Popup trees and descendants do not match.
    /// </summary>
    /// <param name="node">The node to look up.</param>
    /// <param name="toplevelId">The toplevel id, or 0 when there is no match.</param>
    /// <returns><see langword="true"/> when the node is a content tree.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is <see langword="null"/>.</exception>
    public bool TryIdOf(SceneNode node, out ulong toplevelId)
    {
        ArgumentNullException.ThrowIfNull(node);
        return _ids.TryGetValue(node, out toplevelId);
    }

    /// <summary>
    /// Gets the toplevel whose content or popup tree is this node or one of its ancestors.
    /// </summary>
    /// <param name="node">The node to look up.</param>
    /// <param name="toplevelId">The toplevel id, or 0 when there is no match.</param>
    /// <returns><see langword="true"/> when a toplevel owns the node.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is <see langword="null"/>.</exception>
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
