using Basin.Plasma.Protocol;

namespace Basin.Plasma;

internal sealed class FakeInputDevice
{
    internal FakeInputDevice(OrgKdeKwinFakeInputResource resource)
    {
        Resource = resource;
    }

    internal OrgKdeKwinFakeInputResource Resource { get; }

    internal bool Authorized { get; set; }

    internal HashSet<uint> PressedButtons { get; } = [];

    internal List<uint> PressedKeys { get; } = [];

    internal HashSet<uint> ActiveTouches { get; } = [];
}
