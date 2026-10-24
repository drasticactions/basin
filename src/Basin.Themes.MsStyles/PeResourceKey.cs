namespace Basin.Themes.MsStyles;

/// <summary>
/// Identifies a resource type or a resource name in a PE file. A key is an integer ID or a string name. Names compare without case, as <c>FindResource</c> does.
/// </summary>
public readonly struct PeResourceKey : IEquatable<PeResourceKey>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PeResourceKey"/> struct that holds an integer ID.
    /// </summary>
    /// <param name="id">The resource ID.</param>
    public PeResourceKey(int id)
    {
        Id = id;
        Name = null;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PeResourceKey"/> struct that holds a string name.
    /// </summary>
    /// <param name="name">The resource name.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public PeResourceKey(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Id = -1;
        Name = name;
    }

    /// <summary>
    /// Gets the integer ID. It is -1 for a string name.
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// Gets the string name. It is <see langword="null"/> for an integer ID.
    /// </summary>
    public string? Name { get; }

    /// <summary>
    /// Gets a value indicating whether the key is a string name.
    /// </summary>
    public bool IsName => Name is not null;

    /// <summary>
    /// Converts an integer ID to a key.
    /// </summary>
    /// <param name="id">The resource ID.</param>
    /// <returns>A key that holds the ID.</returns>
    public static implicit operator PeResourceKey(int id) => new(id);

    /// <summary>
    /// Converts a string name to a key.
    /// </summary>
    /// <param name="name">The resource name. It must not be <see langword="null"/>.</param>
    /// <returns>A key that holds the name.</returns>
    public static implicit operator PeResourceKey(string name) => new(name);

    /// <summary>
    /// Compares two keys. Two IDs are equal when the numbers are equal. Two names are equal when they match without case. An ID never equals a name.
    /// </summary>
    /// <param name="other">The key to compare with.</param>
    /// <returns><see langword="true"/> when the keys identify the same resource.</returns>
    public bool Equals(PeResourceKey other) =>
        Name is null
            ? other.Name is null && Id == other.Id
            : other.Name is not null && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is PeResourceKey other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        Name is null ? Id : StringComparer.OrdinalIgnoreCase.GetHashCode(Name);

    /// <summary>
    /// Compares two keys with <see cref="Equals(PeResourceKey)"/>.
    /// </summary>
    /// <param name="left">The first key.</param>
    /// <param name="right">The second key.</param>
    /// <returns><see langword="true"/> when the keys are equal.</returns>
    public static bool operator ==(PeResourceKey left, PeResourceKey right) => left.Equals(right);

    /// <summary>
    /// Compares two keys with <see cref="Equals(PeResourceKey)"/>.
    /// </summary>
    /// <param name="left">The first key.</param>
    /// <param name="right">The second key.</param>
    /// <returns><see langword="true"/> when the keys are not equal.</returns>
    public static bool operator !=(PeResourceKey left, PeResourceKey right) => !left.Equals(right);

    /// <summary>
    /// Formats the key as its name, or as its ID in invariant culture.
    /// </summary>
    /// <returns>The name or the ID as text.</returns>
    public override string ToString() => Name ?? Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
