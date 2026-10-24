namespace Basin.Capabilities;

/// <summary>The source of the current value of a device setting. A later layer wins over an earlier one.</summary>
public enum InputSettingLayer
{
    /// <summary>The device or compositor default.</summary>
    Default,

    /// <summary>A rule in the configuration file.</summary>
    Config,

    /// <summary>A change at run time, for example through IPC.</summary>
    Runtime,
}
