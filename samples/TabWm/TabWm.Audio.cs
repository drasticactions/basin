using Basin.Audio.PipeWire;
using Basin.Diagnostics;

namespace TabWm;

internal sealed partial class TabWm
{
    private TabAudio? _audio;

    private void StartAudio()
    {
        if (Toplevels is not { } toplevels)
        {
            return;
        }

        if (AudioSession.TryCreate(_host.Loop, out var reason, clientName: "tab-wm") is not { } session)
        {
            _log.Info($"audio: no PipeWire, tabs show no speaker: {reason}");
            return;
        }

        _audio = new TabAudio(session, toplevels, LaunchGroup);
        _audio.Changed += RecomputeAudio;
        RecomputeAudio();
    }

    private void StopAudio()
    {
        _audio?.Dispose();
        _audio = null;
    }

    private void RecomputeAudio()
    {
        if (_audio is not { } audio)
        {
            return;
        }

        var playing = new HashSet<Tab>();
        foreach (var (stream, toplevelId) in audio.Owned())
        {
            if (TabOfToplevel(toplevelId) is not { } tab)
            {
                continue;
            }

            audio.SetMuted(stream, tab.Muted);
            if (stream.IsRunning)
            {
                playing.Add(tab);
            }
        }

        foreach (var set in _sets)
        {
            foreach (var tab in set.Tabs)
            {
                var now = playing.Contains(tab);
                if (tab.Playing != now)
                {
                    tab.Playing = now;
                    BasinReport.Line($"AUDIO tab={tab.Id} {(now ? "playing" : "silent")}");
                    RefreshTab(tab);
                }
            }
        }
    }

    private Tab? TabOfToplevel(ulong toplevelId)
    {
        if (Toplevels is not { } model || !model.TryGet(toplevelId, out var info) || info.Surface is not { } surface)
        {
            return null;
        }

        return WindowOwning(surface)?.Tab;
    }

    internal void SetMuted(Tab tab, bool muted)
    {
        if (tab.Muted == muted)
        {
            return;
        }

        tab.Muted = muted;
        BasinReport.Line($"AUDIO tab={tab.Id} {(muted ? "muted" : "unmuted")}");
        RecomputeAudio();
        RefreshTab(tab);
        MarkSessionDirty();
    }
}
