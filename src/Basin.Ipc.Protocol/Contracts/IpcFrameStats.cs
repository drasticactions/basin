using System.Text.Json.Serialization;

namespace Basin.Ipc;

public readonly record struct IpcFrameStats(
    ulong Id,
    int CommitsPerSecond,
    int PresentsPerSecond,
    double PresentIntervalMeanMs,
    double PresentIntervalP99Ms,
    double LatencyMeanMs,
    double LatencyP99Ms,
    string? Output,
    int RefreshMhz,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Filter = null);
