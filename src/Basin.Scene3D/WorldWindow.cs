using System.Numerics;

namespace Basin.Scene3D;

/// <summary>
/// A window in the world: a slab whose front face shows its surface layers. Its local +z is the front normal and its local y is up.
/// </summary>
public sealed class WorldWindow
{
    private WorldSurfaceLayer[] _layers = new WorldSurfaceLayer[4];

    /// <summary>
    /// Gets or sets the center of the front face in world space.
    /// </summary>
    public Vector3 Position { get; set; }

    /// <summary>
    /// Gets or sets the rotation that turns local +z into the front normal.
    /// </summary>
    public Quaternion Orientation { get; set; } = Quaternion.Identity;

    /// <summary>
    /// Gets or sets the width of the canvas in meters.
    /// </summary>
    public float Width { get; set; } = 1;

    /// <summary>
    /// Gets or sets the height of the canvas in meters.
    /// </summary>
    public float Height { get; set; } = 1;

    /// <summary>
    /// Gets or sets how far the slab reaches behind the front face, in meters.
    /// </summary>
    public float Depth { get; set; } = 0.02f;

    /// <summary>
    /// Gets or sets the width of the main surface in logical pixels. It maps the canvas width to surface coordinates.
    /// </summary>
    public int SurfaceWidth { get; set; } = 1;

    /// <summary>
    /// Gets or sets the height of the main surface in logical pixels.
    /// </summary>
    public int SurfaceHeight { get; set; } = 1;

    /// <summary>
    /// Gets or sets the opacity of the whole window: slab or frame, plaque and canvas. Below 1, the pass draws the window with the translucent windows.
    /// </summary>
    public float Opacity { get; set; } = 1;

    /// <summary>
    /// Gets or sets the opacity of the canvas layers alone. It multiplies <see cref="Opacity"/>.
    /// </summary>
    public float CanvasOpacity { get; set; } = 1;

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="WorldPicker.Pick"/> tests the window. A window that is not pickable takes no input and blocks nothing.
    /// </summary>
    public bool Pickable { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the window has focus. A pass draws the canvas of a focused window unlit and tints its edge or frame with <see cref="FocusColor"/>.
    /// </summary>
    public bool Focused { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the visibility walk and the picker include the window.
    /// </summary>
    public bool Visible { get; set; } = true;

    /// <summary>
    /// Gets or sets the linear RGBA color of the slab edges of an unfocused window with no shown frame.
    /// </summary>
    public Vector4 EdgeColor { get; set; } = new(0.25f, 0.22f, 0.2f, 1);

    /// <summary>
    /// Gets or sets the linear RGBA color that marks focus: the slab edge color, or the tint of a shown frame.
    /// </summary>
    public Vector4 FocusColor { get; set; } = new(0.95f, 0.75f, 0.35f, 1);

    /// <summary>
    /// Gets or sets how far the edge or frame color moves toward white, from 0 to 1. Zero shows no flash.
    /// </summary>
    public float Flash { get; set; }

    /// <summary>
    /// Gets or sets the room that holds the window. A window in a room is visible only when the room is visible.
    /// </summary>
    public WorldRoom? Room { get; set; }

    /// <summary>
    /// Gets or sets the picture frame round the window, or <see langword="null"/> for a plain slab.
    /// </summary>
    public WorldWindowFrame? Frame { get; set; }

    /// <summary>
    /// Gets <see cref="Frame"/> when it is visible, and <see langword="null"/> otherwise. Drawing and picking read this property.
    /// </summary>
    public WorldWindowFrame? ShownFrame => Frame is { Visible: true } frame ? frame : null;

    /// <summary>
    /// Gets the distance in meters that the canvas sits behind the front plane. It is 0 with no shown frame.
    /// </summary>
    public float Recess => ShownFrame?.Recess ?? 0;

    /// <summary>
    /// Gets or sets a value that the consumer owns. The library does not read it.
    /// </summary>
    public object? Tag { get; set; }

    /// <summary>
    /// Gets the number of layers in <see cref="Layers"/>.
    /// </summary>
    public int LayerCount { get; private set; }

    /// <summary>
    /// Gets the surface layers in draw order. Layer 0 is the main surface. The span is invalid after <see cref="AddLayer"/>.
    /// </summary>
    public ReadOnlySpan<WorldSurfaceLayer> Layers => _layers.AsSpan(0, LayerCount);

    /// <summary>
    /// Gets the surface layers for changes in place. The span is invalid after <see cref="AddLayer"/>.
    /// </summary>
    public Span<WorldSurfaceLayer> EditableLayers => _layers.AsSpan(0, LayerCount);

    /// <summary>
    /// Gets the surface of layer 0, or <see langword="null"/> when the window has no layer.
    /// </summary>
    public Surface? Surface => LayerCount > 0 ? _layers[0].Surface : null;

    /// <summary>
    /// Gets the model matrix from the window's local space to world space. It has no scale.
    /// </summary>
    public Matrix4x4 Model => Matrix4x4.CreateFromQuaternion(Orientation) * Matrix4x4.CreateTranslation(Position);

    /// <summary>
    /// Gets a value indicating whether <see cref="Opacity"/> or <see cref="CanvasOpacity"/> is below 1. Focus does not change it.
    /// </summary>
    public bool IsTranslucent => Opacity < 0.999f || CanvasOpacity < 0.999f;

    /// <summary>
    /// Gets the unit front normal in world space.
    /// </summary>
    public Vector3 Normal => Vector3.Transform(Vector3.UnitZ, Orientation);

    /// <summary>
    /// Gets the canvas width in meters divided by the surface width in logical pixels. It is 0 when the surface width is not positive.
    /// </summary>
    public float MetersPerPixel => SurfaceWidth > 0 ? Width / SurfaceWidth : 0;

    /// <summary>
    /// Gets the local-space box round the slab, or round the shown frame and its plaque.
    /// </summary>
    public WorldBounds LocalBounds => ShownFrame is { } frame
        ? frame.HasPlaque ? frame.OuterBox(Width, Height).Union(frame.PlaqueBox(Height)) : frame.OuterBox(Width, Height)
        : new WorldBounds(new Vector3(-Width * 0.5f, -Height * 0.5f, -Depth), new Vector3(Width * 0.5f, Height * 0.5f, 0));

    /// <summary>
    /// Gets the world-space box round <see cref="LocalBounds"/>. The visibility walk culls the window by it.
    /// </summary>
    public WorldBounds Bounds => LocalBounds.Transformed(Model);

    /// <summary>
    /// Gives the local z of a layer's plane. A layer inside the canvas of a shown frame sits at the recess. A layer that reaches past the canvas stays on the front plane.
    /// </summary>
    /// <param name="layer">The layer, in the main surface's logical coordinates.</param>
    /// <returns>The level times <see cref="WorldPicker.LayerLift"/>, minus the recess for a layer inside a framed canvas.</returns>
    public float LayerDepth(in WorldSurfaceLayer layer)
    {
        var lift = layer.Level * WorldPicker.LayerLift;
        if (ShownFrame is not { } frame)
        {
            return lift;
        }

        var inside = layer.X >= -0.5f && layer.Y >= -0.5f &&
            layer.X + layer.Width <= SurfaceWidth + 0.5f && layer.Y + layer.Height <= SurfaceHeight + 0.5f;
        return inside ? lift - frame.Recess : lift;
    }

    /// <summary>
    /// Removes every layer. It keeps the storage, so the next <see cref="AddLayer"/> calls do not allocate.
    /// </summary>
    public void ClearLayers() => LayerCount = 0;

    /// <summary>
    /// Appends a layer. The storage doubles when it is full, so a window with more layers than before allocates one time.
    /// </summary>
    /// <param name="layer">The layer, in the main surface's logical coordinates.</param>
    public void AddLayer(in WorldSurfaceLayer layer)
    {
        if (LayerCount == _layers.Length)
        {
            Array.Resize(ref _layers, _layers.Length * 2);
        }

        _layers[LayerCount++] = layer;
    }

    /// <summary>
    /// Maps a point on the main surface to world space, on the canvas plane.
    /// </summary>
    /// <param name="surfaceX">The x coordinate in the main surface's logical pixels.</param>
    /// <param name="surfaceY">The y coordinate in the main surface's logical pixels.</param>
    /// <param name="lift">The distance in meters in front of the canvas plane.</param>
    /// <returns>The world-space point.</returns>
    public Vector3 SurfacePoint(float surfaceX, float surfaceY, float lift = 0)
    {
        var local = new Vector3(
            (surfaceX * MetersPerPixel) - (Width * 0.5f),
            (Height * 0.5f) - (surfaceY * (SurfaceHeight > 0 ? Height / SurfaceHeight : 0)),
            lift - Recess);
        return Vector3.Transform(local, Model);
    }
}
