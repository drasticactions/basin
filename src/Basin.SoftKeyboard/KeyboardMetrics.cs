namespace Basin.SoftKeyboard;

/// <summary>
/// The vertical measurements of the soft keyboard, in logical pixels.
/// </summary>
/// <param name="Height">The full keyboard height, padding included.</param>
/// <param name="KeymapHeight">The height of the key rows, which is 335/340 of <paramref name="Height"/>.</param>
/// <param name="Padding">The space above the key rows, which is the rest of the height.</param>
public readonly record struct KeyboardMetrics(int Height, int KeymapHeight, int Padding)
{
    /// <summary>
    /// The height that the keyboard always leaves free at the top of the output, so the status bar stays visible.
    /// </summary>
    public const int StatusBarReserve = 28;
    /// <summary>
    /// The height of a candidate bar above the keyboard. The library does not use it. A view can use it.
    /// </summary>
    public const int CandidateBarHeight = 55;
    /// <summary>
    /// The full height of the medium size, which is the reference for the keymap ratio.
    /// </summary>
    public const int FullHeight = 340;
    /// <summary>
    /// The keymap height of the medium size.
    /// </summary>
    public const int FullKeymapHeight = 335;

    /// <summary>
    /// Gives the preset height of a size before any cap. An undefined value gives the medium height.
    /// </summary>
    /// <param name="size">The size.</param>
    /// <returns>The height in logical pixels.</returns>
    public static int PresetHeight(KeyboardSize size) => size switch
    {
        KeyboardSize.ExtraSmall => 243,
        KeyboardSize.Small => (340 + 243) / 2,
        KeyboardSize.Large => 393,
        _ => FullHeight,
    };

    /// <summary>
    /// Measures the keyboard for a size on an output. A portrait output with no chosen size uses width × 200/320, at most 300.
    /// </summary>
    /// <param name="size">The size preset.</param>
    /// <param name="outputWidth">The output width in logical pixels.</param>
    /// <param name="outputHeight">The output height in logical pixels.</param>
    /// <param name="sizeChosen"><see langword="true"/> when the user chose the size. A portrait output then keeps the preset height.</param>
    /// <returns>The measurements, capped by <see cref="ForHeight"/>.</returns>
    public static KeyboardMetrics For(KeyboardSize size, int outputWidth, int outputHeight, bool sizeChosen = true)
    {
        var height = PresetHeight(size);
        if (outputHeight > outputWidth && !sizeChosen)
        {
            height = Math.Min(outputWidth * 200 / 320, 300);
        }

        return ForHeight(height, outputHeight);
    }

    /// <summary>
    /// Measures the keyboard for a requested height. The height is capped at the output height minus <see cref="StatusBarReserve"/>, and it is at least 1.
    /// </summary>
    /// <param name="height">The requested height in logical pixels.</param>
    /// <param name="outputHeight">The output height in logical pixels.</param>
    /// <returns>The measurements for the capped height.</returns>
    public static KeyboardMetrics ForHeight(int height, int outputHeight)
    {
        var max = outputHeight - StatusBarReserve;
        if (height > max)
        {
            height = max;
        }

        height = Math.Max(height, 1);
        var keymap = height * FullKeymapHeight / FullHeight;
        return new KeyboardMetrics(height, keymap, Math.Max(height - keymap, 0));
    }
}
