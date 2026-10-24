namespace Basin.Portal;

/// <summary>
/// The portal source type bits of a screen cast.
/// </summary>
public enum ScreenCastSourceKind : uint
{
    /// <summary>
    /// An output.
    /// </summary>
    Monitor = 1,

    /// <summary>
    /// A window.
    /// </summary>
    Window = 2,

    /// <summary>
    /// A virtual output. The module does not offer it.
    /// </summary>
    Virtual = 4,
}
