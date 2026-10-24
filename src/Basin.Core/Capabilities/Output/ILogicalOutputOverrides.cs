using Wayland.Server;

namespace Basin.Capabilities;

/// <summary>
/// Gives one client a different logical position and size for an output than other clients see, for example an X
/// server that runs at another scale.
/// </summary>
public interface ILogicalOutputOverrides
{
    /// <summary>
    /// Sets the logical box that one client sees for an output and sends it again. The override ends when the client disconnects.
    /// </summary>
    /// <param name="client">The client that sees the override.</param>
    /// <param name="output">The output global to override.</param>
    /// <param name="logical">The logical position and size to report.</param>
    void SetClientOverride(WlClient client, OutputGlobal output, Box logical);

    /// <summary>
    /// Removes an override and sends the real logical box again. A missing override is ignored.
    /// </summary>
    /// <param name="client">The client that saw the override.</param>
    /// <param name="output">The output global.</param>
    void ClearClientOverride(WlClient client, OutputGlobal output);
}
