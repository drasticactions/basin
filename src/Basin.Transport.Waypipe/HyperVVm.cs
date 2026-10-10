using System.Globalization;

namespace Basin.Transport.Waypipe;

public readonly record struct HyperVVm(HyperVVmKind Kind, Guid Id)
{
    public const string Words = "wsl, children, wildcard, loopback, parent or a GUID";

    public static HyperVVm Wsl { get; } = new(HyperVVmKind.Wsl, Guid.Empty);

    public static HyperVVm Children { get; } = Of(HyperVEndPoint.Children);

    public static HyperVVm Wildcard { get; } = Of(HyperVEndPoint.Wildcard);

    public static HyperVVm Loopback { get; } = Of(HyperVEndPoint.Loopback);

    public static HyperVVm Parent { get; } = Of(HyperVEndPoint.Parent);

    public static HyperVVm Of(Guid id) => new(HyperVVmKind.Fixed, id);

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

        throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"'{text}' names no Hyper-V VM; give {Words}"));
    }

    public override string ToString() =>
        Kind == HyperVVmKind.Wsl ? "wsl"
        : Id == HyperVEndPoint.Children ? "children"
        : Id == HyperVEndPoint.Wildcard ? "wildcard"
        : Id == HyperVEndPoint.Loopback ? "loopback"
        : Id == HyperVEndPoint.Parent ? "parent"
        : Id.ToString();
}
