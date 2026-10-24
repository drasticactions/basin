using Tmds.DBus.Protocol;

namespace Basin.Portal;

/// <summary>
/// Reads typed options from an <c>a{sv}</c> dictionary. A value of the wrong type raises an <see cref="PortalError.InvalidArgument"/> reply.
/// </summary>
public static class Vardict
{
    /// <summary>
    /// Checks whether an option is present.
    /// </summary>
    /// <param name="options">The options from the caller.</param>
    /// <param name="key">The option name.</param>
    /// <returns><see langword="true"/> when the key is present, of any type.</returns>
    public static bool Has(Dictionary<string, VariantValue> options, string key) => options.ContainsKey(key);

    /// <summary>
    /// Reads a <c>u</c> option. An <c>i</c> value is accepted and reinterpreted as unsigned.
    /// </summary>
    /// <param name="options">The options from the caller.</param>
    /// <param name="key">The option name.</param>
    /// <param name="fallback">The value when the key is absent.</param>
    /// <returns>The option value, or <paramref name="fallback"/>.</returns>
    /// <exception cref="DBusErrorReplyException">The value has another type.</exception>
    public static uint UInt32(Dictionary<string, VariantValue> options, string key, uint fallback = 0)
    {
        if (!options.TryGetValue(key, out var value))
        {
            return fallback;
        }

        return value.Type switch
        {
            VariantValueType.UInt32 => value.GetUInt32(),
            VariantValueType.Int32 => unchecked((uint)value.GetInt32()),
            _ => throw PortalError.Invalid($"option '{key}' must be a u, got {value.Type}"),
        };
    }

    /// <summary>
    /// Reads a <c>b</c> option.
    /// </summary>
    /// <param name="options">The options from the caller.</param>
    /// <param name="key">The option name.</param>
    /// <param name="fallback">The value when the key is absent.</param>
    /// <returns>The option value, or <paramref name="fallback"/>.</returns>
    /// <exception cref="DBusErrorReplyException">The value has another type.</exception>
    public static bool Bool(Dictionary<string, VariantValue> options, string key, bool fallback = false)
    {
        if (!options.TryGetValue(key, out var value))
        {
            return fallback;
        }

        return value.Type == VariantValueType.Bool
            ? value.GetBool()
            : throw PortalError.Invalid($"option '{key}' must be a b, got {value.Type}");
    }

    /// <summary>
    /// Reads an <c>s</c> option.
    /// </summary>
    /// <param name="options">The options from the caller.</param>
    /// <param name="key">The option name.</param>
    /// <param name="fallback">The value when the key is absent.</param>
    /// <returns>The option value, or <paramref name="fallback"/>.</returns>
    /// <exception cref="DBusErrorReplyException">The value has another type.</exception>
    public static string String(Dictionary<string, VariantValue> options, string key, string fallback = "")
    {
        if (!options.TryGetValue(key, out var value))
        {
            return fallback;
        }

        return value.Type == VariantValueType.String
            ? value.GetString()
            : throw PortalError.Invalid($"option '{key}' must be an s, got {value.Type}");
    }

    /// <summary>
    /// Reads an <c>as</c> option.
    /// </summary>
    /// <param name="options">The options from the caller.</param>
    /// <param name="key">The option name.</param>
    /// <returns>A new array of the strings, which is empty when the key is absent.</returns>
    /// <exception cref="DBusErrorReplyException">The value is not an array of strings.</exception>
    public static string[] Strings(Dictionary<string, VariantValue> options, string key)
    {
        if (!options.TryGetValue(key, out var value))
        {
            return [];
        }

        if (value.Type != VariantValueType.Array || value.ItemType != VariantValueType.String)
        {
            throw PortalError.Invalid($"option '{key}' must be an as, got {value.Type}");
        }

        var strings = new string[value.Count];
        for (var i = 0; i < strings.Length; i++)
        {
            strings[i] = value.GetItem(i).GetString();
        }

        return strings;
    }

    /// <summary>
    /// Reads an <c>(ii)</c> option.
    /// </summary>
    /// <param name="options">The options from the caller.</param>
    /// <param name="key">The option name.</param>
    /// <returns>The two integers, or <see langword="null"/> when the key is absent.</returns>
    /// <exception cref="DBusErrorReplyException">The value is not a struct of two items.</exception>
    public static (int X, int Y)? IntPair(Dictionary<string, VariantValue> options, string key)
    {
        if (!options.TryGetValue(key, out var value))
        {
            return null;
        }

        if (value.Type != VariantValueType.Struct || value.Count != 2)
        {
            throw PortalError.Invalid($"option '{key}' must be an (ii), got {value.Type}");
        }

        return (value.GetItem(0).GetInt32(), value.GetItem(1).GetInt32());
    }

    /// <summary>
    /// Reads the <c>(suv)</c> restore data that the frontend gives back from an earlier session. The inner variant is unwrapped.
    /// </summary>
    /// <param name="options">The options from the caller.</param>
    /// <param name="key">The option name.</param>
    /// <returns>The vendor, the version and the data, or <see langword="null"/> when the key is absent.</returns>
    /// <exception cref="DBusErrorReplyException">The value is not an <c>(suv)</c> struct.</exception>
    public static (string Vendor, uint Version, VariantValue Data)? RestoreData(Dictionary<string, VariantValue> options, string key)
    {
        if (!options.TryGetValue(key, out var value))
        {
            return null;
        }

        if (value.Type != VariantValueType.Struct || value.Count != 3)
        {
            throw PortalError.Invalid($"option '{key}' must be an (suv), got {value.Type}");
        }

        var vendor = value.GetItem(0);
        var version = value.GetItem(1);
        if (vendor.Type != VariantValueType.String || version.Type != VariantValueType.UInt32)
        {
            throw PortalError.Invalid($"option '{key}' must be an (suv)");
        }

        var data = value.GetItem(2);
        if (data.Type == VariantValueType.Variant)
        {
            data = data.GetVariantValue();
        }

        return (vendor.GetString(), version.GetUInt32(), data);
    }

    /// <summary>
    /// Reads a variant value as an <c>a{sv}</c> dictionary.
    /// </summary>
    /// <param name="value">The value to read.</param>
    /// <param name="what">The name of the value for the error text.</param>
    /// <returns>A new dictionary of the entries.</returns>
    /// <exception cref="DBusErrorReplyException">The value is not a dictionary.</exception>
    public static Dictionary<string, VariantValue> Dict(VariantValue value, string what)
    {
        if (value.Type != VariantValueType.Dictionary)
        {
            throw PortalError.Invalid($"{what} must be an a{{sv}}, got {value.Type}");
        }

        return value.GetDictionary<string, VariantValue>();
    }
}
