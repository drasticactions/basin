using Tmds.DBus.Protocol;

namespace Basin.SystemStatus;

/// <summary>
/// Reads the UPower display device, which UPower composes from all batteries. The values read as zero when no battery is present.
/// </summary>
public sealed class BatteryStatus : SystemStatusReader
{
    /// <summary>
    /// The bus name of UPower.
    /// </summary>
    public const string ServiceName = "org.freedesktop.UPower";
    /// <summary>
    /// The object path of the display device that the reader reads.
    /// </summary>
    public const string DisplayDevicePath = "/org/freedesktop/UPower/devices/DisplayDevice";

    private const string DeviceInterface = "org.freedesktop.UPower.Device";
    private const uint BatteryType = 2;

    private BatteryStatus(SystemStatusBus bus)
        : base(bus, ServiceName, "battery")
    {
    }

    /// <summary>
    /// Gets a value indicating whether the display device is a battery that is present. A desktop computer reads false.
    /// </summary>
    public bool IsPresent { get; private set; }

    /// <summary>
    /// Gets the charge from 0 to 100, or 0 when no battery is present.
    /// </summary>
    public double Percentage { get; private set; }

    /// <summary>
    /// Gets the charge state, or <see cref="BatteryState.Unknown"/> when no battery is present or UPower sends a value outside the enum.
    /// </summary>
    public BatteryState State { get; private set; }

    /// <summary>
    /// Gets the time that UPower estimates until the battery is empty, or zero when it gives none.
    /// </summary>
    public TimeSpan TimeToEmpty { get; private set; }

    /// <summary>
    /// Gets the time that UPower estimates until the battery is full, or zero when it gives none.
    /// </summary>
    public TimeSpan TimeToFull { get; private set; }

    /// <summary>
    /// Gets the icon name that UPower selects for the state, or <see langword="null"/>.
    /// </summary>
    public string? IconName { get; private set; }

    private protected override string? AvailableReason => IsPresent ? null : "UPower reports no battery";

    /// <summary>
    /// Creates the reader and starts its first read in the background. The reader is created also when UPower is not on the bus, and then it reads as not available.
    /// </summary>
    /// <param name="bus">The shared system bus connection.</param>
    /// <param name="reason">Why no reader was created, or <see langword="null"/>.</param>
    /// <returns>The reader, or <see langword="null"/> when the bus is disposed or has no address.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bus"/> is <see langword="null"/>.</exception>
    public static BatteryStatus? TryCreate(SystemStatusBus bus, out string? reason)
    {
        if (!CheckBus(bus, out reason))
        {
            return null;
        }

        var status = new BatteryStatus(bus);
        status.Start();
        return status;
    }

    private protected override MatchRule[] Rules() =>
    [
        new()
        {
            Type = MessageType.Signal,
            Path = DisplayDevicePath,
            Interface = DBusProperties.Interface,
            Member = "PropertiesChanged",
        },
    ];

    private protected override async Task<Action> ReadAsync(DBusConnection connection)
    {
        var map = await DBusProperties.GetAllAsync(connection, ServiceName, DisplayDevicePath, DeviceInterface).ConfigureAwait(false);
        var type = DBusProperties.UInt(map, "Type");
        var present = DBusProperties.Bool(map, "IsPresent") && type == BatteryType;
        var percentage = Math.Clamp(DBusProperties.Double(map, "Percentage"), 0, 100);
        var stateValue = DBusProperties.UInt(map, "State");
        var state = stateValue <= (uint)BatteryState.PendingDischarge ? (BatteryState)stateValue : BatteryState.Unknown;
        var toEmpty = TimeSpan.FromSeconds(Math.Max(0, DBusProperties.Long(map, "TimeToEmpty")));
        var toFull = TimeSpan.FromSeconds(Math.Max(0, DBusProperties.Long(map, "TimeToFull")));
        var icon = DBusProperties.String(map, "IconName");
        return () =>
        {
            IsPresent = present;
            Percentage = present ? percentage : 0;
            State = present ? state : BatteryState.Unknown;
            TimeToEmpty = present ? toEmpty : TimeSpan.Zero;
            TimeToFull = present ? toFull : TimeSpan.Zero;
            IconName = icon is { Length: > 0 } ? icon : null;
        };
    }

    private protected override void Clear()
    {
        IsPresent = false;
        Percentage = 0;
        State = BatteryState.Unknown;
        TimeToEmpty = TimeSpan.Zero;
        TimeToFull = TimeSpan.Zero;
        IconName = null;
    }
}
