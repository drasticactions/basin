using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace Basin.Video.WebCodecs;

[SupportedOSPlatform("browser")]
internal static partial class WebCodecsInterop
{
    private static Task? _import;

    internal static Task ImportAsync() =>
        _import ??= JSHost.ImportAsync(
            WebCodecsModule.Name,
            "data:text/javascript;charset=utf-8," + Uri.EscapeDataString(WebCodecsModule.Source));

    [JSImport("available", WebCodecsModule.Name)]
    internal static partial bool Available();

    [JSImport("supports", WebCodecsModule.Name)]
    internal static partial Task<bool> Supports(string codec);

    [JSImport("create", WebCodecsModule.Name)]
    internal static partial int Create(
        string codec,
        int width,
        int height,
        bool bgra,
        [JSMarshalAs<JSType.Function<JSType.Number, JSType.Boolean>>] Action<int, bool> onOutput);

    [JSImport("decode", WebCodecsModule.Name)]
    internal static partial bool Decode(int id, nint packet, int length, bool key, nint destination, int stride);

    [JSImport("close", WebCodecsModule.Name)]
    internal static partial void Close(int id);
}
