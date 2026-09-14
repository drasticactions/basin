using Tmds.DBus.Protocol;

namespace Basin.Portal;

public static class Vardict
{
    public static bool Has(Dictionary<string, VariantValue> options, string key) => options.ContainsKey(key);

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

    public static Dictionary<string, VariantValue> Dict(VariantValue value, string what)
    {
        if (value.Type != VariantValueType.Dictionary)
        {
            throw PortalError.Invalid($"{what} must be an a{{sv}}, got {value.Type}");
        }

        return value.GetDictionary<string, VariantValue>();
    }
}
