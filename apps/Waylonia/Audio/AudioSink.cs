using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MiniAudioEx.Core.AdvancedAPI;
using MiniAudioEx.Native;

namespace Waylonia.Audio;

internal sealed unsafe class AudioSink : IDisposable
{
    private readonly MaDevice _device;
    private readonly AudioRing _ring;
    private readonly int _channels;
    private GCHandle _self;
    private bool _started;

    private AudioSink(MaDevice device, AudioRing ring, int channels)
    {
        _device = device;
        _ring = ring;
        _channels = channels;
    }

    public static AudioSink? TryCreate(AudioRing ring, int rate, int channels, out string whyNot)
    {
        var sink = new AudioSink(new MaDevice(), ring, channels);
        sink._self = GCHandle.Alloc(sink);
        var config = sink._device.GetConfig(ma_device_type.playback);
        config.sampleRate = (uint)rate;
        config.playback.format = ma_format.f32;
        config.playback.channels = (uint)channels;
        config.dataCallback = (nint)(delegate* unmanaged[Cdecl]<ma_device_ptr, nint, nint, uint, void>)&OnData;
        config.pUserData = GCHandle.ToIntPtr(sink._self);

        var initialized = sink._device.Initialize(config);
        if (initialized != ma_result.success)
        {
            whyNot = initialized.ToString();
            sink.Dispose();
            return null;
        }

        var started = sink._device.Start();
        if (started != ma_result.success)
        {
            whyNot = started.ToString();
            sink.Dispose();
            return null;
        }

        sink._started = true;
        whyNot = string.Empty;
        return sink;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnData(ma_device_ptr device, nint output, nint input, uint frames)
    {
        try
        {
            var userData = device.Get()->pUserData;
            if (userData == 0 || output == 0)
            {
                return;
            }

            if (GCHandle.FromIntPtr(userData).Target is not AudioSink sink)
            {
                return;
            }

            sink._ring.Read(new Span<float>((void*)output, (int)frames * sink._channels));
        }
        catch (Exception)
        {
        }
    }

    public void Dispose()
    {
        if (_started)
        {
            _device.Stop();
            _started = false;
        }

        _device.Dispose();
        if (_self.IsAllocated)
        {
            _self.Free();
        }
    }
}
