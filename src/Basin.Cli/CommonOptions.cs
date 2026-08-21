using System.CommandLine;
using System.CommandLine.Parsing;
using Basin.Diagnostics;

namespace Basin.Cli;

public static class CommonOptions
{
    private static readonly string[] LogLevelNames = ["trace", "debug", "info", "warn", "error"];

    private const string RendererVariable = "BASIN_RENDERER";

    private const string TransportVariable = "BASIN_TRANSPORT";

    private static readonly Dictionary<string, TransportKind> TransportNames = new(StringComparer.Ordinal)
    {
        ["libwayland"] = TransportKind.LibWayland,
        ["managed"] = TransportKind.Managed,
    };

    public static Option<string> LogLevel()
    {
        var option = new Option<string>("--log-level")
        {
            Description = $"discard diagnostics below this: {string.Join(", ", LogLevelNames)}",
            HelpName = "LEVEL",
            DefaultValueFactory = _ => BasinDiagnostics.TraceEnabled ? "debug" : "info",
        };

        option.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string?>();
            if (value is not null && !LogLevelNames.Contains(value))
            {
                result.AddError($"unknown log level '{value}' (expected {string.Join(", ", LogLevelNames)})");
            }
        });

        return option;
    }

    public static Option<string> Renderer(
        IReadOnlyList<string> names,
        string defaultName)
    {
        ArgumentNullException.ThrowIfNull(names);

        var fromEnvironment = Environment.GetEnvironmentVariable(RendererVariable);
        var chosen = string.IsNullOrEmpty(fromEnvironment) ? defaultName : fromEnvironment;

        var option = new Option<string>("--renderer")
        {
            Description = $"renderer to draw with: {string.Join(", ", names)} ({RendererVariable} sets the default)",
            HelpName = "NAME",
            DefaultValueFactory = _ => chosen,
        };

        option.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string?>();
            if (value is null || value == defaultName || names.Contains(value))
            {
                return;
            }

            var origin = result.Implicit ? $" in {RendererVariable}" : string.Empty;
            result.AddError($"unknown renderer '{value}'{origin} (expected {string.Join(", ", names)})");
        });

        return option;
    }

    public static Option<BackendChoice> Backend(BackendKind[] allowed, bool acceptsSocketFd = false)
    {
        ArgumentNullException.ThrowIfNull(allowed);
        if (allowed.Length == 0)
        {
            throw new ArgumentException("a program must allow at least one backend", nameof(allowed));
        }

        var spellings = allowed.Select(kind => new BackendChoice(kind).ToString()).ToArray();
        return new Option<BackendChoice>("--backend")
        {
            Description = $"where the outputs go: {string.Join(" | ", spellings)}"
                + (acceptsSocketFd ? ", each taking an optional :FD naming an inherited listening socket" : string.Empty),
            HelpName = acceptsSocketFd ? "KIND[:FD]" : "KIND",
            DefaultValueFactory = _ => new BackendChoice(DefaultKind(allowed)),
            CustomParser = result =>
            {
                var token = result.Tokens[0].Value;
                var colon = token.IndexOf(':', StringComparison.Ordinal);
                var name = colon < 0 ? token : token[..colon];
                var socketFd = -1;

                if (colon >= 0 && !acceptsSocketFd)
                {
                    result.AddError($"'{token}' names an inherited socket, which this program does not take");
                    return default;
                }

                if (colon >= 0 && (!int.TryParse(token[(colon + 1)..], out socketFd) || socketFd < 0))
                {
                    result.AddError($"'{token}' names no inherited socket: expected {name}:FD with a file descriptor number");
                    return default;
                }

                foreach (var kind in allowed)
                {
                    if (new BackendChoice(kind).ToString() == name)
                    {
                        return new BackendChoice(kind, socketFd);
                    }
                }

                result.AddError($"unknown backend '{name}' (expected {string.Join(", ", spellings)})");
                return default;
            },
        };
    }

    public static Option<TransportChoice> Transport()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(TransportVariable);
        var spellings = TransportNames.Keys.ToArray();

        return new Option<TransportChoice>("--transport")
        {
            Description =
                $"wire the protocol travels on: {string.Join(" | ", spellings)} ({TransportVariable} sets the default)",
            HelpName = "NAME",
            DefaultValueFactory = result =>
            {
                if (string.IsNullOrEmpty(fromEnvironment))
                {
                    return new TransportChoice(TransportKind.LibWayland);
                }

                if (TransportNames.TryGetValue(fromEnvironment, out var kind))
                {
                    return new TransportChoice(kind);
                }

                result.AddError(
                    $"unknown transport '{fromEnvironment}' in {TransportVariable} "
                    + $"(expected {string.Join(", ", spellings)})");
                return new TransportChoice(TransportKind.LibWayland);
            },
            CustomParser = result =>
            {
                var name = result.Tokens[0].Value;
                if (TransportNames.TryGetValue(name, out var kind))
                {
                    return new TransportChoice(kind);
                }

                result.AddError($"unknown transport '{name}' (expected {string.Join(", ", spellings)})");
                return default;
            },
        };
    }

    public static Option<double?> Scale() => new("--scale")
    {
        Description = "output scale, otherwise taken from the output",
        HelpName = "S",
    };

    public static Option<double[]> Scales() => new("--scale")
    {
        Description = "output scale, repeated once per output, otherwise taken from the outputs",
        HelpName = "S",
        AllowMultipleArgumentsPerToken = true,
        DefaultValueFactory = _ => [],
    };

    public static Option<long> Frames(long defaultFrames = 0) => new("--frames")
    {
        Description = "render this many frames and exit, or 0 to run until stopped",
        HelpName = "N",
        DefaultValueFactory = _ => defaultFrames,
    };

    public static Option<string?> Screenshot(string? defaultPath = null) => new("--screenshot")
    {
        Description = "write a PNG of the last frame here",
        HelpName = "PNG",
        DefaultValueFactory = _ => defaultPath,
    };

    public static Option<string?> WaypipeListen() => new("--waypipe-listen")
    {
        Description = "bind this endpoint and replay one waypipe channel into the compositor",
        HelpName = "ADDRESS:PORT|PATH",
    };

    public static Option<string> Compress()
    {
        var option = new Option<string>("--compress")
        {
            Description = "the channel's compression: lz4, zstd or none. A hard match with the peer.",
            HelpName = "NAME",
            DefaultValueFactory = _ => "lz4",
        };

        option.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<string>() is not ("lz4" or "zstd" or "none"))
            {
                result.AddError("--compress takes lz4, zstd or none");
            }
        });

        return option;
    }

    public static Option<bool> Gpu() => new("--gpu")
    {
        Description = "advertise dmabuf to channel clients, backing each remote buffer with a host region",
    };

    public static Option<string> Video()
    {
        var option = new Option<string>("--video")
        {
            Description = "decode per-buffer video from the channel peer: h264, vp9, av1 or none, "
                + "Include ',hw' suffix for decoding on GPU.",
            HelpName = "CODEC[,hw]",
            DefaultValueFactory = _ => "none",
        };

        option.Validators.Add(result =>
        {
            if (!IsVideoChoice(result.GetValueOrDefault<string>()))
            {
                result.AddError("Invalid --video, ignoring.");
            }
        });

        return option;
    }

    public static bool IsVideoChoice(string? value) =>
        value is "none" or "h264" or "vp9" or "av1" or "h264,hw" or "vp9,hw" or "av1,hw";

    public static Option<string?> Client() => new("--client")
    {
        Description = "spawn this client once the socket is up",
        HelpName = "CMD",
    };

    public static Option<string?> Socket() => new("--socket")
    {
        Description = "compositor socket to connect to, otherwise WAYLAND_DISPLAY",
        HelpName = "NAME",
    };

    public static Option<bool> Trace() => new("--trace")
    {
        Description = "report every decision on stderr",
    };

    public static Option<bool> AllocReport() => new("--alloc-report")
    {
        Description = "report what the run allocated, and whether it collected, on stdout at exit",
    };

    public static Option<int> ExitAfter()
    {
        var option = new Option<int>("--exit-after")
        {
            Description = "stop once this many windows have been managed, and exit non-zero if the session ends first",
            HelpName = "N",
        };

        option.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<int>() < 1)
            {
                result.AddError("--exit-after wants a positive window count");
            }
        });

        return option;
    }

    public static Option<int> Width(int defaultWidth) => new("--width")
    {
        Description = "output width in pixels",
        HelpName = "N",
        DefaultValueFactory = _ => defaultWidth,
    };

    public static Option<int> Height(int defaultHeight) => new("--height")
    {
        Description = "output height in pixels",
        HelpName = "N",
        DefaultValueFactory = _ => defaultHeight,
    };

    private static BackendKind DefaultKind(BackendKind[] allowed)
    {
        var wanted = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is { Length: > 0 }
            ? BackendKind.Nested
            : BackendKind.Drm;
        return Array.IndexOf(allowed, wanted) >= 0 ? wanted : allowed[0];
    }
}
