using System.Text.Json.Serialization;

namespace Basin.Ipc;

public readonly record struct IpcX11DensityResult(
    string Outcome,
    string Rule,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? LaunchId = null)
{
    public const string Relaunched = "relaunched";

    public const string AppliesNextStart = "applies-next-start";

    public const string StillRunning = "still-running";

    public const string Unchanged = "unchanged";
}
