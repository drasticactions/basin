using System.Collections;

namespace Basin.Capabilities;

/// <summary>
/// An ordered list of workspaces with one active workspace, for a policy that implements <see cref="IWorkspaceModel"/>.
/// </summary>
/// <typeparam name="T">The workspace type of the policy.</typeparam>
public sealed class WorkspaceSet<T> : IReadOnlyList<T>
    where T : class
{
    private readonly List<T> _items = [];
    private ulong _nextId;

    /// <summary>
    /// Gets or sets the function that <see cref="Fill"/> uses to describe a workspace. <see cref="Fill"/> writes nothing while it is <see langword="null"/>.
    /// </summary>
    public Func<T, WorkspaceInfo>? Describe { get; set; }

    /// <summary>
    /// Gets or sets the function that <see cref="ById"/> uses to read a workspace id. <see cref="ById"/> finds nothing while it is <see langword="null"/>.
    /// </summary>
    public Func<T, ulong>? IdOf { get; set; }

    /// <summary>
    /// Gets or sets the active workspace. An add sets it when it is <see langword="null"/>, and a removal of the active workspace moves it to a neighbor.
    /// </summary>
    public T? Active { get; set; }

    /// <summary>
    /// Gets the index of <see cref="Active"/>, or -1 when it is <see langword="null"/> or not in the set.
    /// </summary>
    public int ActiveIndex => Active is null ? -1 : _items.IndexOf(Active);

    /// <inheritdoc/>
    public int Count => _items.Count;

    /// <inheritdoc/>
    public T this[int index] => _items[index];

    /// <summary>
    /// Allocates a workspace id. The ids start at 1 and the set never gives the same id twice.
    /// </summary>
    /// <returns>A new id.</returns>
    public ulong NextId() => ++_nextId;

    /// <summary>
    /// Adds a workspace at the end. It becomes active when no workspace is active.
    /// </summary>
    /// <param name="workspace">The workspace to add.</param>
    public void Add(T workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _items.Add(workspace);
        Active ??= workspace;
    }

    /// <summary>
    /// Inserts a workspace at an index. It becomes active when no workspace is active.
    /// </summary>
    /// <param name="index">The zero-based position of the new workspace.</param>
    /// <param name="workspace">The workspace to insert.</param>
    public void Insert(int index, T workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _items.Insert(index, workspace);
        Active ??= workspace;
    }

    /// <summary>
    /// Removes a workspace. When it was active, the workspace that takes its index becomes active, or else the new last workspace.
    /// </summary>
    /// <param name="workspace">The workspace to remove.</param>
    /// <returns><see langword="true"/> when the set held the workspace.</returns>
    public bool Remove(T workspace)
    {
        var index = _items.IndexOf(workspace);
        if (index < 0)
        {
            return false;
        }

        RemoveAt(index);
        return true;
    }

    /// <summary>
    /// Removes the workspace at an index. When it was active, the workspace that takes its index becomes active, or else the new last workspace.
    /// </summary>
    /// <param name="index">The zero-based index of the workspace to remove.</param>
    public void RemoveAt(int index)
    {
        var wasActive = ReferenceEquals(_items[index], Active);
        _items.RemoveAt(index);
        if (!wasActive)
        {
            return;
        }

        Active = _items.Count == 0 ? null : _items[Math.Min(index, _items.Count - 1)];
    }

    /// <summary>
    /// Removes every workspace and sets <see cref="Active"/> to <see langword="null"/>.
    /// </summary>
    public void Clear()
    {
        _items.Clear();
        Active = null;
    }

    /// <summary>
    /// Finds whether the set holds a workspace.
    /// </summary>
    /// <param name="workspace">The workspace to look for.</param>
    /// <returns><see langword="true"/> when the set holds the workspace.</returns>
    public bool Contains(T workspace) => _items.Contains(workspace);

    /// <summary>
    /// Finds the index of a workspace.
    /// </summary>
    /// <param name="workspace">The workspace to look for.</param>
    /// <returns>The zero-based index, or -1 when the set does not hold the workspace.</returns>
    public int IndexOf(T workspace) => _items.IndexOf(workspace);

    /// <summary>
    /// Finds the workspace that has an id, through <see cref="IdOf"/>.
    /// </summary>
    /// <param name="id">The workspace id.</param>
    /// <returns>The workspace, or <see langword="null"/> when no workspace has the id or <see cref="IdOf"/> is <see langword="null"/>.</returns>
    public T? ById(ulong id)
    {
        if (IdOf is not { } idOf)
        {
            return null;
        }

        foreach (var item in _items)
        {
            if (idOf(item) == id)
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>
    /// Writes a description of each workspace in order, through <see cref="Describe"/>.
    /// </summary>
    /// <param name="workspaces">The destination. It must have space for every workspace.</param>
    /// <returns>The number written, 0 when <see cref="Describe"/> is <see langword="null"/>, or -1 when the span is too small.</returns>
    public int Fill(Span<WorkspaceInfo> workspaces)
    {
        if (Describe is not { } describe)
        {
            return 0;
        }

        if (_items.Count > workspaces.Length)
        {
            return -1;
        }

        for (var i = 0; i < _items.Count; i++)
        {
            workspaces[i] = describe(_items[i]);
        }

        return _items.Count;
    }

    /// <inheritdoc/>
    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
