using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

namespace Basin.Transport.Waypipe;

[SupportedOSPlatform("windows")]
public static class WslVm
{
    private const string Executable = "wsl.exe";

    public static TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    public static async Task<Guid> QueryIdAsync(CancellationToken cancellation = default)
    {
        var (exit, output, error) = await RunAsync(["--", "wslinfo", "--vm-id"], cancellation).ConfigureAwait(false);
        if (exit == 0 && TryParseId(output, out var id))
        {
            return id;
        }

        var said = (error.Trim().Length > 0 ? error : output).Trim();
        if (said.Contains("--vm-id", StringComparison.OrdinalIgnoreCase) ||
            said.Contains("wslinfo", StringComparison.OrdinalIgnoreCase))
        {
            throw new WslVmException(
                "WSL 2.6.1 or later is needed for `wslinfo --vm-id`; pass --hyperv-vm GUID from `hcsdiag list` (admin) instead"
                + (said.Length > 0 ? $" (wsl.exe said: {said})" : string.Empty));
        }

        if (said.Contains("set-default", StringComparison.OrdinalIgnoreCase) ||
            said.Contains("no installed distributions", StringComparison.OrdinalIgnoreCase) ||
            said.Contains("WSL_E_DEFAULT_DISTRO_NOT_FOUND", StringComparison.OrdinalIgnoreCase))
        {
            throw new WslVmException($"WSL has no default distribution; install one or choose one with `wsl --set-default NAME` (wsl.exe said: {said})");
        }

        throw new WslVmException(exit == 0
            ? $"`wsl.exe -- wslinfo --vm-id` printed no VM id: {said}"
            : $"`wsl.exe -- wslinfo --vm-id` exited {exit}: {said}");
    }

    public static async Task<bool> IsRunningAsync(CancellationToken cancellation = default)
    {
        var (exit, output, _) = await RunAsync(["--list", "--running", "--quiet"], cancellation).ConfigureAwait(false);
        if (exit != 0)
        {
            return false;
        }

        foreach (var line in output.Split('\n'))
        {
            if (line.Trim().Trim('﻿', '\0').Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool TryParseId(ReadOnlySpan<char> output, out Guid id)
    {
        var rest = output;
        while (!rest.IsEmpty)
        {
            var start = 0;
            while (start < rest.Length && IsSeparator(rest[start]))
            {
                start++;
            }

            rest = rest[start..];
            var end = 0;
            while (end < rest.Length && !IsSeparator(rest[end]))
            {
                end++;
            }

            var token = rest[..end].Trim('{').Trim('}');
            if (token.Length == 36 && Guid.TryParseExact(token, "D", out id))
            {
                return true;
            }

            rest = rest[end..];
        }

        id = Guid.Empty;
        return false;
    }

    private static bool IsSeparator(char c) => char.IsWhiteSpace(c) || c is '﻿' or '\0';

    private static async Task<(int Exit, string Output, string Error)> RunAsync(string[] arguments, CancellationToken cancellation)
    {
        var start = new ProcessStartInfo(Executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["WSL_UTF8"] = "1";
        Process process;
        try
        {
            process = Process.Start(start) ?? throw new WslVmException("WSL is not installed: wsl.exe did not start");
        }
        catch (Win32Exception ex)
        {
            throw new WslVmException($"WSL is not installed: {Executable} could not be started ({ex.Message})", ex);
        }

        using (process)
        {
            process.StandardInput.Close();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(Timeout);
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                return (process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                Kill(process);
                throw new WslVmException($"`{Executable} {string.Join(' ', arguments)}` did not answer within {Timeout.TotalSeconds:0} s");
            }
            catch (OperationCanceledException)
            {
                Kill(process);
                throw;
            }
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }
}
