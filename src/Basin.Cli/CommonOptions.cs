using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using Basin.Diagnostics;

namespace Basin.Cli;

/// <summary>
/// Creates the command-line options that basin programs share, so every program spells, describes and validates them the same way. Each call returns a new option.
/// </summary>
public static class CommonOptions
{
    private static readonly string[] LogLevelNames = ["trace", "debug", "info", "warn", "error"];

    private const string VideoBitsPerFrame = "bpf=";

    private const string RendererVariable = "BASIN_RENDERER";

    private const string TransportVariable = "BASIN_TRANSPORT";

    private static readonly string[] HandoffNames = ["auto", "retain", "off"];

    private static readonly Dictionary<string, TransportKind> TransportNames = new(StringComparer.Ordinal)
    {
        ["libwayland"] = TransportKind.LibWayland,
        ["managed"] = TransportKind.Managed,
    };

    /// <summary>
    /// Creates <c>--log-level</c>. It accepts <c>trace</c>, <c>debug</c>, <c>info</c>, <c>warn</c> or <c>error</c>.
    /// </summary>
    /// <returns>The option. Its default is <c>debug</c> when basin tracing is on, otherwise <c>info</c>.</returns>
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

    /// <summary>
    /// Creates <c>--renderer</c>. The <c>BASIN_RENDERER</c> environment variable, when set, replaces <paramref name="defaultName"/> as the default.
    /// </summary>
    /// <param name="names">The renderer names that the program accepts.</param>
    /// <param name="defaultName">The default renderer name. It is always accepted, also when <paramref name="names"/> does not contain it.</param>
    /// <returns>The option. It reports an unknown name as a parse error, also when the name comes from the environment.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="names"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Creates <c>--backend</c>. The default is <see cref="BackendKind.Nested"/> when <c>WAYLAND_DISPLAY</c> is set, otherwise <see cref="BackendKind.Drm"/>, or the first allowed kind when that one is not allowed.
    /// </summary>
    /// <param name="allowed">The backends that the program supports.</param>
    /// <param name="acceptsSocketFd">Whether the value can take a <c>:FD</c> suffix that names an inherited listening socket.</param>
    /// <returns>The option.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="allowed"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="allowed"/> is empty.</exception>
    public static Option<BackendChoice> Backend(BackendKind[] allowed, bool acceptsSocketFd = false)
    {
        ArgumentNullException.ThrowIfNull(allowed);
        if (allowed.Length == 0)
        {
            throw new ArgumentException("The list of allowed backends is empty. A program must allow at least one backend.", nameof(allowed));
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

    /// <summary>
    /// Creates <c>--handoff</c>. It accepts <c>auto</c>, <c>retain</c> or <c>off</c>.
    /// </summary>
    /// <returns>The option. Its value is <see langword="null"/> when the flag is absent.</returns>
    public static Option<HandoffMode?> Handoff() => new("--handoff")
    {
        Description = $"what a DRM start inherits and a DRM exit leaves lit: {string.Join(" | ", HandoffNames)} (beats [handoff] mode; {HandoffChoice.Variable} sets the default)",
        HelpName = "MODE",
        CustomParser = result =>
        {
            var token = result.Tokens[0].Value;
            if (HandoffChoice.Parse(token) is { } mode)
            {
                return mode;
            }

            result.AddError($"unknown handoff mode '{token}' (expected {string.Join(", ", HandoffNames)})");
            return null;
        },
    };

    /// <summary>
    /// Reads <c>--handoff</c> and takes the handoff environment variable. The call removes the variable from the process environment, so a program must call it once.
    /// </summary>
    /// <param name="result">The parse result.</param>
    /// <param name="option">The option that <see cref="Handoff"/> created.</param>
    /// <returns>The flag and the environment value together.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    public static HandoffChoice ReadHandoff(ParseResult result, Option<HandoffMode?> option)
    {
        ArgumentNullException.ThrowIfNull(result);
        return HandoffChoice.Take(result.GetValue(option));
    }

    /// <summary>
    /// Creates <c>--transport</c>. It accepts <c>libwayland</c> or <c>managed</c>. The <c>BASIN_TRANSPORT</c> environment variable sets the default.
    /// </summary>
    /// <returns>The option. Its default is <see cref="TransportKind.LibWayland"/>. An unknown name in the environment is a parse error.</returns>
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

    /// <summary>
    /// Creates <c>--outputs</c>, the number of outputs to create.
    /// </summary>
    /// <param name="defaultOutputs">The default count.</param>
    /// <returns>The option.</returns>
    public static Option<int> Outputs(int defaultOutputs = 1) => new("--outputs")
    {
        Description = "how many outputs to create",
        HelpName = "N",
        DefaultValueFactory = _ => defaultOutputs,
    };

    /// <summary>
    /// Creates <c>--scale</c> for one output scale.
    /// </summary>
    /// <returns>The option. Its value is <see langword="null"/> when the flag is absent, which means the scale comes from the output.</returns>
    public static Option<double?> Scale() => new("--scale")
    {
        Description = "output scale, otherwise taken from the output",
        HelpName = "S",
    };

    /// <summary>
    /// Creates <c>--scale</c> for one scale per output. The flag can repeat and can take more than one value.
    /// </summary>
    /// <returns>The option. Its default is an empty array, which means each scale comes from its output.</returns>
    public static Option<double[]> Scales() => new("--scale")
    {
        Description = "output scale, repeated once per output, otherwise taken from the outputs",
        HelpName = "S",
        AllowMultipleArgumentsPerToken = true,
        DefaultValueFactory = _ => [],
    };

    /// <summary>
    /// Creates <c>--frames</c>, the number of frames to render before the program exits. Zero means run until stopped.
    /// </summary>
    /// <param name="defaultFrames">The default count.</param>
    /// <returns>The option.</returns>
    public static Option<long> Frames(long defaultFrames = 0) => new("--frames")
    {
        Description = "render this many frames and exit, or 0 to run until stopped",
        HelpName = "N",
        DefaultValueFactory = _ => defaultFrames,
    };

    /// <summary>
    /// Creates <c>--screenshot</c>, the PNG path for the last frame.
    /// </summary>
    /// <param name="defaultPath">The default path, or <see langword="null"/> for no screenshot.</param>
    /// <returns>The option.</returns>
    public static Option<string?> Screenshot(string? defaultPath = null) => new("--screenshot")
    {
        Description = "write a PNG of the last frame here",
        HelpName = "PNG",
        DefaultValueFactory = _ => defaultPath,
    };

    /// <summary>
    /// Creates <c>--waypipe-listen</c>, the endpoint that accepts waypipe channels. It takes <c>ADDRESS:PORT</c>, a socket path or <c>vsock:PORT</c>.
    /// </summary>
    /// <returns>The option. Its value is <see langword="null"/> when the flag is absent.</returns>
    public static Option<string?> WaypipeListen() => new("--waypipe-listen")
    {
        Description = "bind this endpoint and replay every waypipe channel that connects into the compositor",
        HelpName = "ADDRESS:PORT|PATH|vsock:PORT",
    };

    /// <summary>
    /// Creates <c>--hyperv-vm</c>, the Hyper-V VM that a vsock listener binds to on Windows. Other platforms ignore it.
    /// </summary>
    /// <returns>The option. It accepts <c>wsl</c>, <c>children</c>, <c>wildcard</c>, <c>loopback</c>, <c>parent</c> or a GUID, and its default is <c>wsl</c>.</returns>
    public static Option<string> HyperVVm()
    {
        var option = new Option<string>("--hyperv-vm")
        {
            Description = "the Hyper-V VM a vsock: listener binds on Windows: wsl, children, wildcard, loopback, parent or a GUID. Ignored elsewhere.",
            HelpName = "VM",
            DefaultValueFactory = _ => "wsl",
        };

        option.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string>()?.Trim() ?? string.Empty;
            if (value.ToLowerInvariant() is not ("wsl" or "children" or "wildcard" or "loopback" or "parent") &&
                !Guid.TryParse(value, out _))
            {
                result.AddError("--hyperv-vm takes wsl, children, wildcard, loopback, parent or a GUID");
            }
        });

        return option;
    }

    /// <summary>
    /// Creates <c>--compress</c>, the compression of a waypipe channel. The value must match the peer's.
    /// </summary>
    /// <returns>The option. It accepts <c>lz4</c>, <c>zstd</c> or <c>none</c>, and its default is <c>lz4</c>.</returns>
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

    /// <summary>
    /// Creates <c>--gpu</c>, which advertises dmabuf to channel clients and backs each remote buffer with a host region.
    /// </summary>
    /// <returns>The option.</returns>
    public static Option<bool> Gpu() => new("--gpu")
    {
        Description = "advertise dmabuf to channel clients, backing each remote buffer with a host region",
    };

    /// <summary>
    /// Creates <c>--video</c>, the codec for per-buffer video from the channel peer. <see cref="IsVideoChoice"/> validates the value.
    /// </summary>
    /// <returns>The option. Its default is <c>none</c>.</returns>
    public static Option<string> Video()
    {
        var option = new Option<string>("--video")
        {
            Description = "decode per-buffer video from the channel peer: h264, vp9, av1 or none. "
                + "',hw' decodes on this host's GPU; ',hwenc'/',swenc' and ',hwdec'/',swdec' say "
                + "where the peer encodes and decodes, and ',bpf=B' the bits per frame it targets.",
            HelpName = "CODEC[,OPTION...]",
            DefaultValueFactory = _ => "none",
        };

        option.Validators.Add(result =>
        {
            if (!IsVideoChoice(result.GetValueOrDefault<string>()))
            {
                result.AddError(
                    "--video takes none, h264, vp9 or av1, each with any of ',hw', "
                    + "',hwenc'/',swenc', ',hwdec'/',swdec' and ',bpf=B', where B is a "
                    + "positive number such as 7.5e5");
            }
        });

        return option;
    }

    /// <summary>
    /// Checks a <c>--video</c> value. It is <c>none</c>, or a codec followed by at most one each of <c>hw</c>, an encoder flag, a decoder flag and <c>bpf=B</c>.
    /// </summary>
    /// <param name="value">The comma-separated value to check.</param>
    /// <returns><see langword="true"/> when the value is valid. A null value, a repeated option or a <c>bpf</c> that is not a finite positive number is not valid.</returns>
    public static bool IsVideoChoice(string? value)
    {
        if (value is null)
        {
            return false;
        }

        var parts = value.Split(',');
        if (parts[0] is not ("none" or "h264" or "vp9" or "av1"))
        {
            return false;
        }

        if (parts[0] == "none")
        {
            return parts.Length == 1;
        }

        var hardware = false;
        var encoder = false;
        var decoder = false;
        var bits = false;
        for (var i = 1; i < parts.Length; i++)
        {
            if (parts[i] == "hw")
            {
                if (hardware)
                {
                    return false;
                }

                hardware = true;
            }
            else if (parts[i] is "hwenc" or "swenc")
            {
                if (encoder)
                {
                    return false;
                }

                encoder = true;
            }
            else if (parts[i] is "hwdec" or "swdec")
            {
                if (decoder)
                {
                    return false;
                }

                decoder = true;
            }
            else if (parts[i].StartsWith(VideoBitsPerFrame, StringComparison.Ordinal))
            {
                if (bits || !float.TryParse(
                        parts[i].AsSpan(VideoBitsPerFrame.Length),
                        NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                        CultureInfo.InvariantCulture,
                        out var perFrame) || !float.IsFinite(perFrame) || perFrame <= 0)
                {
                    return false;
                }

                bits = true;
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Extracts the parts of a <c>--video</c> value that the channel peer applies: the encoder flag, the decoder flag and <c>bpf=B</c>.
    /// </summary>
    /// <param name="value">The <c>--video</c> value.</param>
    /// <returns>The parts joined with commas in their original order, or <see langword="null"/> when there are none.</returns>
    public static string? VideoRemoteSetting(string? value)
    {
        if (value is null)
        {
            return null;
        }

        string? remote = null;
        foreach (var part in value.Split(','))
        {
            if (part is "hwenc" or "swenc" or "hwdec" or "swdec"
                || part.StartsWith(VideoBitsPerFrame, StringComparison.Ordinal))
            {
                remote = remote is null ? part : remote + "," + part;
            }
        }

        return remote;
    }

    /// <summary>
    /// Checks whether a <c>--video</c> value asks this host to decode on its GPU.
    /// </summary>
    /// <param name="value">The <c>--video</c> value.</param>
    /// <returns><see langword="true"/> when the value contains the <c>hw</c> part.</returns>
    public static bool VideoDecodesOnGpu(string? value)
    {
        if (value is null)
        {
            return false;
        }

        foreach (var part in value.Split(','))
        {
            if (part == "hw")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Creates <c>--config</c>, a TOML file that replaces the default <c>~/.config/NAME/NAME.toml</c>. The program interprets the value <c>false</c> as no file.
    /// </summary>
    /// <param name="name">The program's binary name, which the help text uses in the default path.</param>
    /// <returns>The option. Its value is <see langword="null"/> when the flag is absent.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/> or empty.</exception>
    public static Option<string?> Config(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return new Option<string?>("--config")
        {
            Description = $"read this TOML file instead of ~/.config/{name}/{name}.toml, or false to read none",
            HelpName = "PATH",
        };
    }

    /// <summary>
    /// Creates <c>--command</c> and its alias <c>-c</c>, a shell command to run on startup.
    /// </summary>
    /// <param name="description">The help text.</param>
    /// <returns>The option. Its value is <see langword="null"/> when the flag is absent.</returns>
    public static Option<string?> Command(string description = "run `sh -c <command>` on startup") => new("--command", "-c")
    {
        Description = description,
        HelpName = "CMD",
    };

    /// <summary>
    /// Creates <c>--client</c>, a client command to start when the socket is ready.
    /// </summary>
    /// <returns>The option. Its value is <see langword="null"/> when the flag is absent.</returns>
    public static Option<string?> Client() => new("--client")
    {
        Description = "spawn this client once the socket is up",
        HelpName = "CMD",
    };

    /// <summary>
    /// Creates <c>--socket</c>, the compositor socket that a client program connects to instead of <c>WAYLAND_DISPLAY</c>.
    /// </summary>
    /// <returns>The option. Its value is <see langword="null"/> when the flag is absent.</returns>
    public static Option<string?> Socket() => new("--socket")
    {
        Description = "compositor socket to connect to, otherwise WAYLAND_DISPLAY",
        HelpName = "NAME",
    };

    /// <summary>
    /// Creates <c>--trace</c>, which reports every decision on standard error.
    /// </summary>
    /// <returns>The option.</returns>
    public static Option<bool> Trace() => new("--trace")
    {
        Description = "report every decision on stderr",
    };

    /// <summary>
    /// Creates <c>--ipc</c>. Use <see cref="IpcCli.AddOption"/> to add it, so that the program adds it once and reports it.
    /// </summary>
    /// <returns>The option. It takes <c>true</c>, <c>false</c> or a socket path, and its default is <c>true</c>.</returns>
    public static Option<string> Ipc() => new("--ipc")
    {
        Description = "bind the control socket, at this path instead of $XDG_RUNTIME_DIR/basin-SOCKET.sock, or false to bind none",
        HelpName = "true|false|PATH",
        DefaultValueFactory = _ => "true",
    };

    /// <summary>
    /// Creates <c>--exit-with-command</c>, which exits with the status of the <c>-c</c> command when that process exits. The program must also turn on the subreaper.
    /// </summary>
    /// <returns>The option.</returns>
    public static Option<bool> ExitWithCommand() => new("--exit-with-command")
    {
        Description = "exit when the -c command's process exits, with its status (implies --subreaper)",
    };

    /// <summary>
    /// Creates <c>--subreaper</c>, which runs the <c>-c</c> command under a reaper that adopts orphans and ends the whole process tree at exit.
    /// </summary>
    /// <param name="defaultOn">The value when the flag is absent. The flag without a value means <see langword="true"/>.</param>
    /// <returns>The option.</returns>
    public static Option<bool> Subreaper(bool defaultOn = false) => new("--subreaper")
    {
        Description = "run the -c command under a reaper that adopts every orphan and ends the whole tree at exit",
        HelpName = "true|false",
        Arity = ArgumentArity.ZeroOrOne,
        DefaultValueFactory = _ => defaultOn,
    };

    /// <summary>
    /// Creates <c>--ready-fd</c>, a file descriptor that gets <c>READY=1</c> and is closed when the socket listens and every output has presented.
    /// </summary>
    /// <returns>The option. Its value is <see langword="null"/> when the flag is absent.</returns>
    public static Option<int?> ReadyFd() => new("--ready-fd")
    {
        Description = "write READY=1 to this file descriptor and close it once the socket listens and every output has presented",
        HelpName = "FD",
    };

    /// <summary>
    /// Creates <c>--alloc-report</c>, which writes the <c>ALLOC</c> line on standard output at exit. <see cref="BasinCommand"/> adds it to every program.
    /// </summary>
    /// <returns>The option.</returns>
    public static Option<bool> AllocReport() => new("--alloc-report")
    {
        Description = "report what the run allocated, and whether it collected, on stdout at exit",
    };

    /// <summary>
    /// Creates <c>--exit-after</c>, the number of managed windows after which the program stops. The program exits with a nonzero status when the session ends first.
    /// </summary>
    /// <returns>The option. A value less than 1 is a parse error.</returns>
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

    /// <summary>
    /// Creates <c>--width</c>, the output width in pixels.
    /// </summary>
    /// <param name="defaultWidth">The default width in pixels.</param>
    /// <returns>The option.</returns>
    public static Option<int> Width(int defaultWidth) => new("--width")
    {
        Description = "output width in pixels",
        HelpName = "N",
        DefaultValueFactory = _ => defaultWidth,
    };

    /// <summary>
    /// Creates <c>--height</c>, the output height in pixels.
    /// </summary>
    /// <param name="defaultHeight">The default height in pixels.</param>
    /// <returns>The option.</returns>
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
