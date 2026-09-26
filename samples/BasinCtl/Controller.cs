using System.Globalization;
using System.Text;
using System.Text.Json;
using Basin.Diagnostics;
using Basin.Ipc;

namespace BasinCtl;

internal sealed class Controller(string? socketPath, bool raw, bool viaFd, bool detail, int? maxDimension)
{
    private const int Success = 0;
    private const int ErrorReply = 1;
    private const int ConnectionFailure = 2;

    public async Task<int> RunAsync(string[] command)
    {
        BasinIpcClient client;
        try
        {
            client = await BasinIpcClient.ConnectAsync(socketPath).ConfigureAwait(false);
        }
        catch (IOException exception)
        {
            await Console.Error.WriteLineAsync($"basinctl: {exception.Message}").ConfigureAwait(false);
            return ConnectionFailure;
        }

        await using (client.ConfigureAwait(false))
        {
            try
            {
                return await DispatchAsync(client, command).ConfigureAwait(false);
            }
            catch (IpcCallException exception)
            {
                await Console.Error.WriteLineAsync($"basinctl: {exception.Message}").ConfigureAwait(false);
                return ErrorReply;
            }
            catch (IOException exception)
            {
                await Console.Error.WriteLineAsync($"basinctl: {exception.Message}").ConfigureAwait(false);
                return ConnectionFailure;
            }
            catch (FormatException exception)
            {
                await Console.Error.WriteLineAsync($"basinctl: {exception.Message}").ConfigureAwait(false);
                return ErrorReply;
            }
        }
    }

    private async Task<int> DispatchAsync(BasinIpcClient client, string[] command)
    {
        var rest = command[1..];
        switch (command[0])
        {
            case "windows" when !raw:
                PrintWindows(await client.ListWindowsAsync().ConfigureAwait(false));
                return Success;
            case "outputs" when !raw:
                PrintOutputs(await client.ListOutputsAsync().ConfigureAwait(false));
                return Success;
            case "workspaces" when !raw:
                PrintWorkspaces(await client.ListWorkspacesAsync().ConfigureAwait(false));
                return Success;
            case "windows":
                return await GenericAsync(client, IpcMethodNames.WindowsList, rest).ConfigureAwait(false);
            case "outputs":
                return await GenericAsync(client, IpcMethodNames.OutputsList, rest).ConfigureAwait(false);
            case "workspaces":
                return await GenericAsync(client, IpcMethodNames.WorkspacesList, rest).ConfigureAwait(false);
            case "methods" when detail && !raw:
                PrintDetails(await client.MethodsDetailAsync().ConfigureAwait(false));
                return Success;
            case "methods" when detail:
                return await GenericAsync(client, IpcMethodNames.Methods, ["detail=true"]).ConfigureAwait(false);
            case "methods":
                PrintLines(await client.MethodsAsync().ConfigureAwait(false));
                return Success;
            case "events":
                PrintLines(await client.EventsAsync().ConfigureAwait(false));
                return Success;
            case "version":
                return await GenericAsync(client, IpcMethodNames.Version, rest).ConfigureAwait(false);
            case "describe":
                return await GenericAsync(client, IpcMethodNames.SessionDescribe, rest).ConfigureAwait(false);
            case "activate" when rest.Length == 1:
                await client.ActivateAsync(Id(rest[0])).ConfigureAwait(false);
                return Success;
            case "close" when rest.Length == 1:
                await client.CloseAsync(Id(rest[0])).ConfigureAwait(false);
                return Success;
            case "move" when rest.Length == 3:
                await client.MoveAsync(Id(rest[0]), Int(rest[1]), Int(rest[2])).ConfigureAwait(false);
                return Success;
            case "resize" when rest.Length == 3:
                await client.ResizeAsync(Id(rest[0]), Int(rest[1]), Int(rest[2])).ConfigureAwait(false);
                return Success;
            case "wait" when rest.Length == 1:
                PrintWindows([await client.WaitForWindowAsync(appId: rest[0]).ConfigureAwait(false)]);
                return Success;
            case "shot" when rest.Length == 2:
                return await ShotAsync(client, rest[0], rest[1]).ConfigureAwait(false);
            case "chord" when rest.Length == 1:
                await client.ChordAsync(rest[0]).ConfigureAwait(false);
                return Success;
            case "key" when rest.Length == 1:
                await client.KeyAsync((uint)Int(rest[0])).ConfigureAwait(false);
                return Success;
            case "text" when rest.Length > 0:
                await client.TextAsync(string.Join(' ', rest)).ConfigureAwait(false);
                return Success;
            case "spawn" when rest.Length > 0:
                Console.WriteLine(await client.SpawnAsync(rest).ConfigureAwait(false));
                return Success;
            case "clipboard" when rest is [] or ["primary"] && !raw:
                var clipboard = await client.ReadClipboardAsync(primary: rest.Length == 1).ConfigureAwait(false);
                if (clipboard.Text is { } text)
                {
                    Console.Write(text);
                    return Success;
                }

                await Console.Error.WriteLineAsync(clipboard.Types.IsEmpty
                    ? "basinctl: the selection is empty"
                    : $"basinctl: no text type; offered {string.Join(' ', clipboard.Types.Items())}").ConfigureAwait(false);
                return ErrorReply;
            case "clipboard" when rest is [] or ["primary"]:
                return await GenericAsync(client, IpcMethodNames.ClipboardRead, rest.Length == 1 ? ["kind=primary"] : []).ConfigureAwait(false);
            case "quit":
                await client.QuitAsync().ConfigureAwait(false);
                return Success;
            case "subscribe" when rest.Length > 0:
                await SubscribeAsync(client, rest).ConfigureAwait(false);
                return Success;
            case "raw" when rest.Length == 1:
                using (var result = await client.RawAsync(rest[0]).ConfigureAwait(false))
                {
                    Print(result.Json);
                }

                return Success;
            case var method when method.Contains('/', StringComparison.Ordinal):
                return await GenericAsync(client, method, rest).ConfigureAwait(false);
            default:
                await Console.Error.WriteLineAsync($"basinctl: unknown command '{command[0]}' or wrong arguments; see --help").ConfigureAwait(false);
                return ErrorReply;
        }
    }

    private async Task<int> GenericAsync(BasinIpcClient client, string method, string[] pairs)
    {
        var parameters = Params(pairs);
        using var result = await client.CallAsync(
            method, parameters.Length == 0 ? null : writer => writer.WriteRawValue(parameters)).ConfigureAwait(false);
        Print(result.Json);
        return Success;
    }

    private async Task<int> ShotAsync(BasinIpcClient client, string output, string file)
    {
        if (!viaFd)
        {
            var shot = await client.CaptureOutputAsync(output, IpcCaptureTarget.ToPath(file), maxDimension: maxDimension).ConfigureAwait(false);
            Console.WriteLine($"{shot.Path} {shot.Width}x{shot.Height}");
            return Success;
        }

        var captured = await client.CaptureOutputAsync(output, IpcCaptureTarget.Fd).ConfigureAwait(false);
        using var fd = captured.Fd ?? throw new IOException("the compositor sent no descriptor");
        var pixels = new byte[captured.Stride * captured.Height];
        _ = RandomAccess.Read(fd, pixels, 0);
        var opaque = captured.Format?.StartsWith('x') == true;
        var rgba = new byte[captured.Width * captured.Height * 4];
        for (var y = 0; y < captured.Height; y++)
        {
            for (var x = 0; x < captured.Width; x++)
            {
                var pixel = BitConverter.ToUInt32(pixels, (y * captured.Stride) + (x * 4));
                var i = ((y * captured.Width) + x) * 4;
                rgba[i] = (byte)(pixel >> 16);
                rgba[i + 1] = (byte)(pixel >> 8);
                rgba[i + 2] = (byte)pixel;
                rgba[i + 3] = opaque ? (byte)0xFF : (byte)(pixel >> 24);
            }
        }

        await File.WriteAllBytesAsync(file, PngCodec.Encode(rgba, captured.Width, captured.Height)).ConfigureAwait(false);
        Console.WriteLine($"{Path.GetFullPath(file)} {captured.Width}x{captured.Height} via fd");
        return Success;
    }

    private static async Task SubscribeAsync(BasinIpcClient client, string[] events)
    {
        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };

        try
        {
            await foreach (var item in client.SubscribeAsync(events, cancel.Token).ConfigureAwait(false))
            {
                Console.WriteLine($"{{\"event\":\"{item.Name}\",\"data\":{Encoding.UTF8.GetString(item.Data)}}}");
                Console.Out.Flush();
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
        }
    }

    private static byte[] Params(string[] pairs)
    {
        if (pairs.Length == 0)
        {
            return [];
        }

        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var pair in pairs)
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0)
            {
                throw new FormatException($"'{pair}' is not key=value");
            }

            var value = pair[(equals + 1)..];
            values[pair[..equals]] = IsJson(value)
                ? JsonSerializer.Deserialize(value, IpcJsonContext.Default.JsonElement)
                : JsonSerializer.SerializeToElement(value, IpcJsonContext.Default.String);
        }

        return JsonSerializer.SerializeToUtf8Bytes(values, IpcJsonContext.Default.DictionaryStringJsonElement);
    }

    private static bool IsJson(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        try
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(value));
            reader.Read();
            reader.Skip();
            return !reader.Read();
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void Print(byte[] json)
    {
        if (raw)
        {
            Console.WriteLine(Encoding.UTF8.GetString(json));
            return;
        }

        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
               {
                   Indented = true,
                   Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
               }))
        {
            document.WriteTo(writer);
        }

        Console.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
    }

    private static void PrintLines(IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
        {
            Console.WriteLine(line);
        }
    }

    private static void PrintDetails(IReadOnlyList<IpcMethodDetail> methods)
    {
        foreach (var method in methods)
        {
            var traits = string.Join(',', new[]
            {
                method.ReadOnly ? "read-only" : null,
                method.Destructive ? "destructive" : null,
                method.Idempotent ? "idempotent" : null,
            }.Where(trait => trait is not null));
            Console.WriteLine($"{method.Name,-28} {(traits.Length == 0 ? "-" : traits),-22} {method.Description ?? method.Line ?? "-"}");
        }
    }

    private static void PrintWindows(IReadOnlyList<IpcWindow> windows)
    {
        Console.WriteLine($"{"ID",-20} {"APP",-24} {"WS",-6} {"OUTPUT",-10} {"STATE",-10} TITLE");
        foreach (var window in windows)
        {
            var state = window.State.Activated ? "focused" : window.State.Minimized ? "minimized" : window.State.Maximized ? "maximized" : window.State.Fullscreen ? "fullscreen" : "-";
            Console.WriteLine($"{window.Id,-20} {Clip(window.AppId, 24),-24} {window.Workspace?.ToString(CultureInfo.InvariantCulture) ?? "-",-6} {window.Output ?? "-",-10} {state,-10} {window.Title}");
        }
    }

    private static void PrintOutputs(IReadOnlyList<IpcOutput> outputs)
    {
        Console.WriteLine($"{"NAME",-12} {"MODE",-22} {"SCALE",-6} {"POSITION",-12} {"POWER",-6} DESCRIPTION");
        foreach (var output in outputs)
        {
            var mode = FormattableString.Invariant($"{output.Mode.Width}x{output.Mode.Height}@{output.Mode.RefreshMhz / 1000.0:F2}");
            var position = output.Geometry.Value is { } box ? $"{box.X},{box.Y}" : "-";
            var power = output.Power is { } on ? (on ? "on" : "off") : "-";
            Console.WriteLine(FormattableString.Invariant($"{output.Name + (output.Pointer ? "*" : string.Empty),-12} {mode,-22} {output.Scale,-6:0.##} {position,-12} {power,-6} {output.Description}"));
        }
    }

    private static void PrintWorkspaces(IReadOnlyList<IpcWorkspaceGroup> groups)
    {
        foreach (var group in groups)
        {
            Console.WriteLine($"group {group.Id} on {string.Join(',', group.Outputs.Items())}");
            foreach (var workspace in group.Workspaces.Items())
            {
                Console.WriteLine($"  {workspace.Id,-8} {(workspace.Active ? "*" : " ")} {workspace.Name,-16} windows={workspace.Members.Length}");
            }
        }
    }

    private static string Clip(string text, int width) => text.Length <= width ? text : text[..(width - 1)] + "…";

    private static ulong Id(string text) =>
        ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : throw new FormatException($"'{text}' is not a window id");

    private static int Int(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new FormatException($"'{text}' is not an integer");
}
