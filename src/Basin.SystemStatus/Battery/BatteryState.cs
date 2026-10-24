namespace Basin.SystemStatus;

/// <summary>
/// The charge state of the UPower display device. The values are those of the UPower <c>State</c> property.
/// </summary>
public enum BatteryState
{
    /// <summary>
    /// UPower does not know the state, or no battery is present.
    /// </summary>
    Unknown = 0,
    /// <summary>
    /// The battery charges.
    /// </summary>
    Charging = 1,
    /// <summary>
    /// The battery discharges.
    /// </summary>
    Discharging = 2,
    /// <summary>
    /// The battery is empty.
    /// </summary>
    Empty = 3,
    /// <summary>
    /// The battery is fully charged.
    /// </summary>
    Full = 4,
    /// <summary>
    /// The battery is on external power and waits to charge.
    /// </summary>
    PendingCharge = 5,
    /// <summary>
    /// The battery waits to discharge.
    /// </summary>
    PendingDischarge = 6,
}
