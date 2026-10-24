using Basin.Diagnostics;
using Basin.Themes.MsStyles;
using Basin.UI.Quill;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;
using SkiaSharp;

namespace Basin.Frames.MsStyles.Quill;

/// <summary>
/// Draws and measures frame titles through Prowl.Quill fonts. It reads each theme face from the Skia font that
/// <see cref="MsStylesFonts"/> resolves, and it uses a fallback face when Quill cannot read that font.
/// </summary>
public sealed class MsStylesQuillText : IMsStylesTextMetrics, IDisposable
{
    private const int MaxFitted = 64;
    private const string Ellipsis = "…";

    private static readonly BasinLogger Log = BasinLog.For("msstyles");

    private readonly Dictionary<SKTypeface, FontFile?> _files = [];
    private readonly Dictionary<(string Text, FontFile Font, float Pixels, int Width), (string Text, float Width)> _fitted = [];
    private readonly HashSet<Canvas> _prepared = [];
    private FontFile? _system;

    /// <summary>
    /// Initializes a new instance of the <see cref="MsStylesQuillText"/> class.
    /// </summary>
    /// <param name="fonts">The font matcher. This instance does not own it.</param>
    public MsStylesQuillText(MsStylesFonts fonts)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        Fonts = fonts;
        Raster = new MsStylesSkiaText(fonts);
    }

    internal MsStylesSkiaText Raster { get; }

    /// <summary>
    /// Releases the Skia text engine that draws the glow. It does not dispose <see cref="Fonts"/>.
    /// </summary>
    public void Dispose() => Raster.Dispose();

    /// <summary>
    /// Gets the font matcher that resolves the theme's font names to installed faces.
    /// </summary>
    public MsStylesFonts Fonts { get; }

    /// <summary>
    /// Gets or sets the face that draws a title when Quill cannot read the matched font. When it is <see langword="null"/>, the canvas's system face draws it.
    /// </summary>
    public FontFile? Fallback { get; set; }

    internal Canvas? Canvas { get; set; }

    /// <summary>
    /// Gets the Quill font for a theme font. The result is cached for each resolved face, including a failure.
    /// </summary>
    /// <param name="font">The theme font.</param>
    /// <returns>The font, or <see langword="null"/> when Quill cannot read the matched face.</returns>
    public FontFile? FileFor(in MsStylesFont font)
    {
        var typeface = Fonts.Resolve(font);
        if (_files.TryGetValue(typeface, out var file))
        {
            return file;
        }

        file = null;
        using (var stream = typeface.OpenStream(out var index))
        {
            if (stream is not null && stream.Length > 0)
            {
                var data = new byte[stream.Length];
                if (ReadAll(stream, data) && MsStylesQuillFontData.Standalone(data, index) is { } standalone)
                {
                    try
                    {
                        file = new FontFile(standalone);
                    }
                    catch (InvalidDataException)
                    {
                        Log.Warn($"quill cannot read the face {typeface.FamilyName}; the fallback face draws its text");
                    }
                }
            }
        }

        _files[typeface] = file;
        return file;
    }

    /// <summary>
    /// Measures the width of a text in one line. Outside a draw no canvas is set, so the result is an estimate of half the pixel size for each character.
    /// </summary>
    /// <param name="text">The text to measure.</param>
    /// <param name="font">The theme font.</param>
    /// <param name="pixels">The font size in pixels.</param>
    /// <returns>The width in pixels, or 0 for an empty text.</returns>
    public float Measure(string text, in MsStylesFont font, float pixels)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        if (Canvas is not { } canvas || Face(canvas, font) is not { } file)
        {
            return text.Length * pixels * 0.5f;
        }

        Prepare(canvas, file);
        return canvas.MeasureText(text, Height(file, pixels), file).X;
    }

    internal void Draw(Canvas canvas, in MsStylesDrawOp op, float scale)
    {
        if (op.Kind != MsStylesDrawOpKind.Text || op.Text is not { Length: > 0 } text ||
            Face(canvas, op.Font) is not { } file)
        {
            return;
        }

        Prepare(canvas, file);
        var destination = op.Destination;
        var pixels = Height(file, op.FontPixels / scale);
        var (shown, width) = Fit(canvas, text, file, pixels, destination.Width / scale);
        if (shown.Length == 0)
        {
            return;
        }

        var left = destination.Left / scale;
        var x = op.Align switch
        {
            HorizontalAlign.Center => left + MathF.Floor(((destination.Width / scale) - width) / 2),
            HorizontalAlign.Right => (destination.Right / scale) - width,
            _ => left,
        };
        var y = (destination.Top + (destination.Height / 2)) / scale;
        canvas.SaveState();
        canvas.IntersectScissor(
            left,
            (destination.Top - destination.Height) / scale,
            (destination.Width / scale) + Math.Max(0, op.ShadowOffset.X / scale),
            3 * destination.Height / scale);
        var origin = new Float2(0f, 0.5f);
        if ((op.ShadowColor >> 24) != 0)
        {
            canvas.DrawText(shown, x + (op.ShadowOffset.X / scale), y + (op.ShadowOffset.Y / scale), Color(op.ShadowColor), pixels, file, 0f, origin);
        }

        canvas.DrawText(shown, x, y, Color(op.Color), pixels, file, 0f, origin);
        canvas.RestoreState();
    }

    private (string Text, float Width) Fit(Canvas canvas, string text, FontFile file, float pixels, float room)
    {
        var key = (text, file, pixels, (int)MathF.Floor(room));
        if (_fitted.TryGetValue(key, out var fitted))
        {
            return fitted;
        }

        if (_fitted.Count >= MaxFitted)
        {
            _fitted.Clear();
        }

        var full = canvas.MeasureText(text, pixels, file).X;
        if (full <= room)
        {
            fitted = (text, full);
        }
        else
        {
            var low = 0;
            var high = text.Length;
            fitted = (string.Empty, 0f);
            while (low < high)
            {
                var mid = (low + high + 1) / 2;
                var candidate = string.Concat(text.AsSpan(0, mid).TrimEnd(), Ellipsis);
                var width = canvas.MeasureText(candidate, pixels, file).X;
                if (width <= room)
                {
                    low = mid;
                    fitted = (candidate, width);
                }
                else
                {
                    high = mid - 1;
                }
            }
        }

        _fitted[key] = fitted;
        return fitted;
    }

    private static float Height(FontFile file, float em) =>
        file.UnitsPerEm > 0 && file.Ascent - file.Descent > 0 ? em * (file.Ascent - file.Descent) / file.UnitsPerEm : em;

    private FontFile? Face(Canvas canvas, in MsStylesFont font) =>
        FileFor(font) ?? Fallback ?? (_system ??= QuillFonts.Resolve(canvas));

    private static unsafe bool ReadAll(SKStreamAsset stream, byte[] data)
    {
        var read = 0;
        fixed (byte* bytes = data)
        {
            while (read < data.Length)
            {
                var count = stream.Read((nint)(bytes + read), data.Length - read);
                if (count <= 0)
                {
                    return false;
                }

                read += count;
            }
        }

        return true;
    }

    private void Prepare(Canvas canvas, FontFile file)
    {
        if (_prepared.Add(canvas))
        {
            QuillFonts.AddFallbacks(canvas, file);
        }
    }

    private static Color32 Color(uint argb) => Color(argb, (byte)(argb >> 24));

    private static Color32 Color(uint argb, byte alpha) => new((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, alpha);
}
