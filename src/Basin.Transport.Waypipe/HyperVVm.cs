using System.Globalization;

namespace Basin.Transport.Waypipe;

/// <summary>
/// The VM that a Hyper-V listener binds, as the --hyperv-vm option names it.
/// </summary>
/// <param name="Kind">Tells whether the id is fixed or comes from the running WSL2 VM.</param>
/// <param name="Id">The fixed VM id. It is <see cref="Guid.Empty"/> for <see cref="HyperVVmKind.Wsl"/>.</param>
public readonly record struct HyperVVm(HyperVVmKind Kind, Guid Id)
{
    /// <summary>
    /// The list of accepted words, for help and error text.
    /// </summary>
    public const string Words = "wsl, children, wildcard, loopback, parent or a GUID";

    /// <summary>
    /// Gets the value for the running WSL2 VM. Its id is resolved at bind.
    /// </summary>
    public static HyperVVm Wsl { get; } = new(HyperVVmKind.Wsl, Guid.Empty);

    /// <summary>
    /// Gets the value for every child partition.
    /// </summary>
    public static HyperVVm Children { get; } = Of(HyperVEndPoint.Children);

    /// <summary>
    /// Gets the value for every partition.
    /// </summary>
    public static HyperVVm Wildcard { get; } = Of(HyperVEndPoint.Wildcard);

    /// <summary>
    /// Gets the value for this partition itself.
    /// </summary>
    public static HyperVVm Loopback { get; } = Of(HyperVEndPoint.Loopback);

    /// <summary>
    /// Gets the value for the parent partition.
    /// </summary>
    public static HyperVVm Parent { get; } = Of(HyperVEndPoint.Parent);

    /// <summary>
    /// Makes a value for a fixed VM id.
    /// </summary>
    /// <param name="id">The VM id.</param>
    /// <returns>A value of kind <see cref="HyperVVmKind.Fixed"/>.</returns>
    public static HyperVVm Of(Guid id) => new(HyperVVmKind.Fixed, id);

    /// <summary>
    /// Reads "wsl", "children", "wildcard", "loopback", "parent" or a GUID, without regard to case and surrounding white space.
    /// </summary>
    /// <param name="text">The text to read.</param>
    /// <returns>The VM value.</returns>
    /// <exception cref="FormatException">The text is not one of the words and not a GUID.</exception>
    public static HyperVVm Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var word = text.Trim();
        switch (word.ToLowerInvariant())
        {
            case "wsl":
                return Wsl;
            case "children":
                return Children;
            case "wildcard":
                return Wildcard;
            case "loopback":
                return Loopback;
            case "parent":
                return Parent;
        }

        if (Guid.TryParse(word, out var id))
        {
            return Of(id);
        }

        throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"'{text}' names no Hyper-V VM. Give {Words}."));
    }

    /// <summary>
    /// Formats the value as the word that <see cref="Parse"/> reads, or as the GUID.
    /// </summary>
    /// <returns>The value as text.</returns>
    public override string ToString() =>
        Kind == HyperVVmKind.Wsl ? "wsl"
        : Id == HyperVEndPoint.Children ? "children"
        : Id == HyperVEndPoint.Wildcard ? "wildcard"
        : Id == HyperVEndPoint.Loopback ? "loopback"
        : Id == HyperVEndPoint.Parent ? "parent"
        : Id.ToString();
}
