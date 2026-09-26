using System.Text.Json.Serialization;

namespace Basin.Ipc;

[JsonConverter(typeof(IpcButtonConverter))]
public readonly record struct IpcButton(uint Code)
{
    public static IpcButton Left { get; } = new(0x110);

    public static IpcButton Right { get; } = new(0x111);

    public static IpcButton Middle { get; } = new(0x112);

    public static bool TryParse(string name, out IpcButton button)
    {
        uint? code = name switch { "left" => 0x110, "right" => 0x111, "middle" => 0x112, "side" => 0x113, "extra" => 0x114, _ => null };
        button = new IpcButton(code ?? 0);
        return code is not null;
    }
}
