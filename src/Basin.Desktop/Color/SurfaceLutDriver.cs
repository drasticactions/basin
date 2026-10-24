using Basin.Capabilities;
using Basin.Shell.Xdg;

namespace Basin.Desktop;

/// <summary>
/// Gives each scene surface the image description that its client set, so the scene can attach a color LUT. It refreshes when a description changes.
/// </summary>
public sealed class SurfaceLutDriver
{
    private readonly Scene.Scene _scene;
    private readonly ColorManager _color;
    private readonly Func<Surface, ImageDescription?> _describe;
    private int _lastCount = -1;

    /// <summary>
    /// Initializes a new instance of the <see cref="SurfaceLutDriver"/> class. It sets <paramref name="resolver"/> as the scene's color transform resolver.
    /// </summary>
    /// <param name="scene">The scene whose surfaces get descriptions.</param>
    /// <param name="color">The color manager that supplies the descriptions.</param>
    /// <param name="resolver">The resolver that turns descriptions into LUTs.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public SurfaceLutDriver(Scene.Scene scene, ColorManager color, IColorTransformResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(color);
        ArgumentNullException.ThrowIfNull(resolver);
        _scene = scene;
        _color = color;
        _describe = Describe;
        scene.ColorTransforms = resolver;
        color.SurfaceDescriptionChanged += (_, _) => Refresh();
        color.OutputDescriptionChanged += (_, _) => Refresh();
        color.OutputDescriptionRemoved += _ => Refresh();
    }

    /// <summary>
    /// Occurs after a refresh when the number of surfaces with a LUT changed. The argument is the new number.
    /// </summary>
    public event Action<int>? CountChanged;

    /// <summary>
    /// Gets or sets a function that marks surfaces whose SDR content must not be tone-mapped up to HDR.
    /// </summary>
    public Func<Surface, bool>? DeclinesInverseToneMapping { get; set; }

    private readonly Dictionary<ImageDescription, ImageDescription> _declining = new(ImageDescription.ContentComparer);

    /// <summary>
    /// Limits the supported transfer functions to the SDR set plus extended linear, and the primaries to sRGB.
    /// </summary>
    /// <param name="color">The color manager to configure.</param>
    /// <exception cref="ArgumentNullException"><paramref name="color"/> is <see langword="null"/>.</exception>
    public static void DeclareSrgb(ColorManager color)
    {
        ArgumentNullException.ThrowIfNull(color);
        color.SupportedTransferFunctions =
        [
            ColorTransferFunction.CompoundPower24, ColorTransferFunction.Srgb, ColorTransferFunction.Gamma22,
            ColorTransferFunction.ExtLinear,
        ];
        color.SupportedPrimaries = [ColorPrimaries.Srgb];
    }

    /// <summary>
    /// Sets the supported transfer functions and primaries to the SDR set plus each named value that the outputs use.
    /// </summary>
    /// <param name="color">The color manager to configure.</param>
    /// <param name="outputs">The output descriptions to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="color"/> or <paramref name="outputs"/> is <see langword="null"/>.</exception>
    public static void Declare(ColorManager color, IEnumerable<ImageDescription> outputs)
    {
        ArgumentNullException.ThrowIfNull(color);
        ArgumentNullException.ThrowIfNull(outputs);

        var transfers = new List<ColorTransferFunction>
        {
            ColorTransferFunction.CompoundPower24, ColorTransferFunction.Srgb, ColorTransferFunction.Gamma22,
            ColorTransferFunction.ExtLinear,
        };
        var primaries = new List<ColorPrimaries> { ColorPrimaries.Srgb };

        foreach (var description in outputs)
        {
            if (description.TransferNamed is { } transfer && !transfers.Contains(transfer))
            {
                transfers.Add(transfer);
            }

            if (description.PrimariesNamed is { } named && !primaries.Contains(named))
            {
                primaries.Add(named);
            }
        }

        color.SupportedTransferFunctions = transfers;
        color.SupportedPrimaries = primaries;
    }

    /// <summary>
    /// Refreshes each time an xdg toplevel maps.
    /// </summary>
    /// <param name="shell">The shell to watch.</param>
    /// <exception cref="ArgumentNullException"><paramref name="shell"/> is <see langword="null"/>.</exception>
    public void WatchToplevels(XdgShell shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.NewToplevel += toplevel => toplevel.Xdg.Mapped += Refresh;
    }

    /// <summary>
    /// Describes every scene surface again. A surface with the default SDR description gets no description.
    /// </summary>
    public void Refresh()
    {
        _scene.DescribeSurfaces(_describe);
        var attached = _scene.LutCount;
        if (attached != _lastCount)
        {
            _lastCount = attached;
            CountChanged?.Invoke(attached);
        }
    }

    private ImageDescription? Describe(Surface surface)
    {
        var description = _color.DescriptionOf(surface);
        if (DeclinesInverseToneMapping?.Invoke(surface) == true)
        {
            if (!_declining.TryGetValue(description, out var declining))
            {
                declining = description with { DeclinesInverseToneMapping = true };
                _declining[description] = declining;
            }

            return declining;
        }

        return ReferenceEquals(description, ImageDescription.SdrDefault) ? null : description;
    }
}
