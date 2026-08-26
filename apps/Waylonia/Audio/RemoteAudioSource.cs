using System.Diagnostics;
using System.Runtime.InteropServices;
using static Waylonia.WayloniaLog;

namespace Waylonia.Audio;

internal sealed class RemoteAudioSource : IDisposable
{
    private const int ReadBytes = 16 * 1024;

    private const int Attempts = 5;

    private const int SilenceMillis = 10_000;

    private readonly AudioRing _ring;
    private readonly string _sshHost;
    private readonly string? _controlPath;
    private readonly string _remote;
    private readonly int _bytesPerFrame;
    private readonly bool _sixteenBit;
    private readonly CancellationTokenSource _stopping = new();
    private readonly byte[] _bytes = new byte[ReadBytes];
    private readonly float[] _samples = new float[ReadBytes / 2];
    private readonly string _monitor;

    private Process? _capture;
    private WayloniaApp.Relay _relay = new("audio");
    private bool _complained;
    private bool _delivered;
    private int _attempts;

    public RemoteAudioSource(
        AudioRing ring,
        string sshHost,
        string? controlPath,
        string sink,
        int rate,
        int channels,
        bool sixteenBit)
    {
        _ring = ring;
        _sshHost = sshHost;
        _controlPath = controlPath;
        _sixteenBit = sixteenBit;
        _bytesPerFrame = channels * (sixteenBit ? 2 : 4);
        _monitor = $"{sink}.monitor";
        var parecFormat = sixteenBit ? "s16le" : "float32le";
        var pipewireFormat = sixteenBit ? "s16" : "f32";
        _remote =
            "if command -v pactl >/dev/null 2>&1 && " +
            $"! pactl list short sources 2>/dev/null | cut -f2 | grep -qx {_monitor}; then " +
            $"echo 'no {_monitor}' >&2; exit 126; fi; " +
            "if command -v parec >/dev/null 2>&1; then " +
            $"exec parec --format={parecFormat} --rate={rate} --channels={channels} " +
            $"--latency-msec=50 -d {_monitor}; " +
            "elif command -v pw-record >/dev/null 2>&1; then " +
            $"exec pw-record --raw --format={pipewireFormat} --rate={rate} --channels={channels} " +
            $"--target={_monitor} -; " +
            "else echo 'no parec and no pw-record' >&2; exit 127; fi";
    }

    public void Start()
    {
        _ = Task.Run(RunAsync);
        _ = Task.Run(WatchSilenceAsync);
    }

    private async Task WatchSilenceAsync()
    {
        try
        {
            await Task.Delay(SilenceMillis, _stopping.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!_delivered)
        {
            Complain($"nothing has been captured from {_monitor} on {_sshHost}, so the session has no sound");
        }
    }

    private async Task RunAsync()
    {
        var backoff = 500;
        while (!_stopping.IsCancellationRequested)
        {
            var relay = new WayloniaApp.Relay("audio");
            _relay = relay;
            Process? capture;
            try
            {
                capture = Process.Start(Info());
            }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                Complain($"the capture channel to {_sshHost} could not start: {error.Message}");
                return;
            }

            if (capture is null)
            {
                Complain($"the capture channel to {_sshHost} could not start");
                return;
            }

            _capture = capture;
            relay.Watch(capture.StandardError);
            try
            {
                await PumpAsync(capture.StandardOutput.BaseStream);
            }
            catch (Exception error) when (error is IOException or ObjectDisposedException)
            {
            }

            await capture.WaitForExitAsync();
            if (_stopping.IsCancellationRequested)
            {
                return;
            }

            if (capture.ExitCode == 127)
            {
                Complain($"{_sshHost} has neither parec nor pw-record, so the session has no sound");
                return;
            }

            if (capture.ExitCode == 126 && _attempts >= Attempts - 1)
            {
                Complain($"{_sshHost} never created {_monitor}, so the session has no sound");
                return;
            }

            if (!_delivered && ++_attempts == Attempts)
            {
                Complain(
                    $"nothing was captured from {_monitor} on {_sshHost} in {Attempts} tries, " +
                    "so the session has no sound");
                return;
            }

            Log.Debug($"the capture channel to {_sshHost} ended with {capture.ExitCode}; opening it again");
            try
            {
                await Task.Delay(backoff, _stopping.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            backoff = Math.Min(backoff * 2, 4000);
        }
    }

    private ProcessStartInfo Info()
    {
        var info = new ProcessStartInfo("ssh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("-o");
        info.ArgumentList.Add("BatchMode=yes");
        if (_controlPath is { } control)
        {
            info.ArgumentList.Add("-o");
            info.ArgumentList.Add($"ControlPath={control}");
            info.ArgumentList.Add("-o");
            info.ArgumentList.Add("ControlMaster=no");
        }

        info.ArgumentList.Add(_sshHost);
        info.ArgumentList.Add(_remote);
        return info;
    }

    private async Task PumpAsync(Stream pcm)
    {
        var carried = 0;
        while (!_stopping.IsCancellationRequested)
        {
            var read = await pcm.ReadAsync(_bytes.AsMemory(carried), _stopping.Token);
            if (read == 0)
            {
                return;
            }

            var total = carried + read;
            var whole = total - (total % _bytesPerFrame);
            if (whole > 0)
            {
                _delivered = true;
                _ring.Write(Decode(_bytes.AsSpan(0, whole)));
            }

            carried = total - whole;
            if (carried > 0)
            {
                _bytes.AsSpan(whole, carried).CopyTo(_bytes);
            }
        }
    }

    private ReadOnlySpan<float> Decode(ReadOnlySpan<byte> pcm)
    {
        if (!_sixteenBit)
        {
            return MemoryMarshal.Cast<byte, float>(pcm);
        }

        var source = MemoryMarshal.Cast<byte, short>(pcm);
        var destination = _samples.AsSpan(0, source.Length);
        for (var i = 0; i < source.Length; i++)
        {
            destination[i] = source[i] / 32768f;
        }

        return destination;
    }

    private void Complain(string message)
    {
        if (_complained)
        {
            return;
        }

        _complained = true;
        Log.Warn($"{message}");
        _relay.Report();
    }

    public void Dispose()
    {
        _stopping.Cancel();
        if (_capture is { } capture)
        {
            try
            {
                if (!capture.HasExited)
                {
                    capture.Kill(entireProcessTree: true);
                }
            }
            catch (Exception error) when (error is InvalidOperationException or NotSupportedException)
            {
            }

            capture.Dispose();
            _capture = null;
        }

        _stopping.Dispose();
    }
}
