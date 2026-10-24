using Tmds.DBus.Protocol;

namespace Basin.Portal;

/// <summary>
/// Builds the <c>a{sv}</c> results and the variant values that portal methods return.
/// </summary>
public static class Results
{
    /// <summary>
    /// Gets a new empty results dictionary. Each read returns a new instance.
    /// </summary>
    public static Dictionary<string, VariantValue> Empty => [];

    /// <summary>
    /// Creates a results dictionary with one entry.
    /// </summary>
    /// <param name="key">The result key.</param>
    /// <param name="value">The result value.</param>
    /// <returns>A new dictionary that holds the entry.</returns>
    public static Dictionary<string, VariantValue> With(string key, VariantValue value) =>
        new() { [key] = value };

    /// <summary>
    /// Creates an <c>(ii)</c> struct, for example a position or a size.
    /// </summary>
    /// <param name="x">The first integer.</param>
    /// <param name="y">The second integer.</param>
    /// <returns>The struct value.</returns>
    public static VariantValue IntPair(int x, int y) => VariantValue.Struct(VariantValue.Int32(x), VariantValue.Int32(y));

    /// <summary>
    /// Creates the <c>(suv)</c> restore data that the frontend stores for the next session.
    /// </summary>
    /// <param name="vendor">The backend name that owns the data.</param>
    /// <param name="version">The version of the data format.</param>
    /// <param name="data">The backend data, wrapped in a variant.</param>
    /// <returns>The struct value.</returns>
    public static VariantValue Restore(string vendor, uint version, VariantValue data) =>
        VariantValue.Struct(VariantValue.String(vendor), VariantValue.UInt32(version), VariantValue.Variant(data));

    /// <summary>
    /// Creates the <c>a(ua{sv})</c> list of screen cast streams.
    /// </summary>
    /// <param name="streams">The PipeWire node id and the properties of each stream.</param>
    /// <returns>The array value.</returns>
    public static VariantValue Streams(IReadOnlyList<(uint NodeId, Dictionary<string, VariantValue> Properties)> streams)
    {
        var array = new Array<Struct<uint, Dict<string, VariantValue>>>(streams.Count);
        foreach (var (nodeId, properties) in streams)
        {
            array.Add(Struct.Create(nodeId, ToDict(properties)));
        }

        return array.AsVariantValue();
    }

    /// <summary>
    /// Creates the <c>a(sa{sv})</c> list of global shortcuts from an array.
    /// </summary>
    /// <param name="shortcuts">The id and the properties of each shortcut.</param>
    /// <returns>The array value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="shortcuts"/> is <see langword="null"/>.</exception>
    public static VariantValue Shortcuts((string, Dictionary<string, VariantValue>)[] shortcuts)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);
        var array = new Array<Struct<string, Dict<string, VariantValue>>>(shortcuts.Length);
        foreach (var (id, properties) in shortcuts)
        {
            array.Add(Struct.Create(id, ToDict(properties)));
        }

        return array.AsVariantValue();
    }

    /// <summary>
    /// Creates the <c>a(sa{sv})</c> list of global shortcuts from a list.
    /// </summary>
    /// <param name="shortcuts">The id and the properties of each shortcut.</param>
    /// <returns>The array value.</returns>
    public static VariantValue Shortcuts(IReadOnlyList<(string Id, Dictionary<string, VariantValue> Properties)> shortcuts)
    {
        var array = new Array<Struct<string, Dict<string, VariantValue>>>(shortcuts.Count);
        foreach (var (id, properties) in shortcuts)
        {
            array.Add(Struct.Create(id, ToDict(properties)));
        }

        return array.AsVariantValue();
    }

    /// <summary>
    /// Wraps a dictionary as an <c>a{sv}</c> variant value.
    /// </summary>
    /// <param name="properties">The entries to copy.</param>
    /// <returns>The dictionary value.</returns>
    public static VariantValue Dict(Dictionary<string, VariantValue> properties) => ToDict(properties).AsVariantValue();

    private static Dict<string, VariantValue> ToDict(Dictionary<string, VariantValue> properties)
    {
        var dict = new Dict<string, VariantValue>();
        foreach (var (key, value) in properties)
        {
            dict.Add(key, value);
        }

        return dict;
    }
}
