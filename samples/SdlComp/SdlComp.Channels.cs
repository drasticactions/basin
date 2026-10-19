using Basin;
using Basin.Diagnostics;
using Basin.Transport.Waypipe;

namespace SdlComp;

internal sealed partial class SdlComp
{
    private WaypipeListenSession? _listening;

    public WaypipeListenSession? Listening => _listening;

    private void StartListening()
    {
        if (_options.WaypipeListen is not { Length: > 0 } endpoint)
        {
            return;
        }

        var vm = _options.HyperVVm is { Length: > 0 } word ? HyperVVm.Parse(word) : (HyperVVm?)null;
        var options = new WaypipeChannelOptions { CarriesDmabuf = _host.Dmabuf is not null };
        _ = WaypipeListenSession.StartAsync(
                _host.Display,
                _host.Services.Require<ClientBufferRegistry>(),
                _host.Services.Require<CompositorGlobal>(),
                () => _host.Dmabuf is not null,
                _host.Loop,
                _loop.Post,
                endpoint,
                vm,
                options)
            .ContinueWith(started => _loop.Post(() => Listen(endpoint, started)), TaskScheduler.Default);
    }

    private void Listen(string endpoint, Task<WaypipeListenSession> started)
    {
        if (started.Exception is { } failure)
        {
            _log.Error($"--waypipe-listen {endpoint} failed: {failure.GetBaseException().Message}");
            return;
        }

        var session = started.Result;
        if (_disposed)
        {
            session.Dispose();
            return;
        }

        _listening = session;
        session.Changed += () => BasinReport.Line($"CHANNELS {session.Clients}");
        session.Ended += error => _log.Info($"the channel listener ended{(error is null ? string.Empty : $": {error.Message}")}");
        BasinReport.Line($"LISTEN {session.Listener.LocalEndPoint}");
    }
}
