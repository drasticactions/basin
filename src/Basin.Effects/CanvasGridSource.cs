using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// The mesh source for the background grid that makes the canvas warps visible. Each line is one logical pixel wide on screen.
/// </summary>
public sealed class CanvasGridSource : IMeshSource
{
    private const int SegmentWidth = 8;

    private readonly CanvasWarpTransform _map = new();
    private int _cellSize = 64;
    private float _alpha = 1f;
    private double _minSpacing;
    private double _viewScale = 1.0;
    private GridReveal _reveal = GridReveal.Full;
    private GridRevealMask _mask;
    private GridTone _tone;
    private bool _segments;
    private bool _halo;
    private bool _toned;
    private HaloPart? _glow;

    /// <summary>
    /// Gets or sets the warp of the left edge, or <see langword="null"/> for no zone.
    /// </summary>
    public CanvasWarp? Left
    {
        get => _map.Left;
        set => _map.Left = value;
    }

    /// <summary>
    /// Gets or sets the warp of the right edge, or <see langword="null"/> for no zone.
    /// </summary>
    public CanvasWarp? Right
    {
        get => _map.Right;
        set => _map.Right = value;
    }

    /// <summary>
    /// Gets or sets the warp of the top edge, or <see langword="null"/> for no zone.
    /// </summary>
    public CanvasWarp? Top
    {
        get => _map.Top;
        set => _map.Top = value;
    }

    /// <summary>
    /// Gets or sets the warp of the bottom edge, or <see langword="null"/> for no zone.
    /// </summary>
    public CanvasWarp? Bottom
    {
        get => _map.Bottom;
        set => _map.Bottom = value;
    }

    /// <summary>
    /// Gets or sets how round the corner rings are, from 0 to 1, as for <see cref="CanvasWarpTransform.CornerRadius"/>.
    /// </summary>
    public double CornerRadius
    {
        get => _map.CornerRadius;
        set => _map.CornerRadius = value;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the ring radius shrinks with depth, as for <see cref="CanvasWarpTransform.CornerTaper"/>.
    /// </summary>
    public bool CornerTaper
    {
        get => _map.CornerTaper;
        set => _map.CornerTaper = value;
    }

    /// <summary>
    /// Gets or sets the grid cell in canvas pixels. The setter keeps it at 2 or above.
    /// </summary>
    public int CellSize
    {
        get => _cellSize;
        set => _cellSize = Math.Max(2, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the map is one curve for each axis, as for <see cref="CanvasWarpTransform.Separable"/>.
    /// </summary>
    public bool Separable
    {
        get => _map.Separable;
        set => _map.Separable = value;
    }

    /// <summary>
    /// Gets or sets the least local screen spacing between drawn lines in a zone. A denser zone draws only every n-th line, with n a power of two. Zero draws every line.
    /// </summary>
    public double MinLineSpacing
    {
        get => _minSpacing;
        set => _minSpacing = Math.Max(0.0, value);
    }

    /// <summary>
    /// Gets or sets the uniform zoom after the warp. The setter reads a value that is not positive and finite as 1.
    /// </summary>
    public double ViewScale
    {
        get => _viewScale;
        set => _viewScale = value > 0 && double.IsFinite(value) ? value : 1.0;
    }

    /// <summary>
    /// Gets or sets the X coordinate of the zoom center on screen.
    /// </summary>
    public double ViewCenterX { get; set; }

    /// <summary>
    /// Gets or sets the Y coordinate of the zoom center on screen.
    /// </summary>
    public double ViewCenterY { get; set; }

    /// <summary>
    /// Gets or sets the line color, before <see cref="Alpha"/>.
    /// </summary>
    public RenderColor Color { get; set; } = new(0.16f, 0.21f, 0.75f, 1f);

    /// <summary>
    /// Gets or sets a value indicating whether lines draw in the flat area between the seams. A line exactly on a seam always draws.
    /// </summary>
    public bool DesktopLines { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether a line along each active side seam closes the rows. It does nothing while <see cref="DesktopLines"/> is set.
    /// </summary>
    public bool SeamLines { get; set; }

    /// <summary>
    /// Gets or sets the fade of every vertex color, from 0 to 1. The setter clamps the value.
    /// </summary>
    public float Alpha
    {
        get => _alpha;
        set => _alpha = Math.Clamp(value, 0f, 1f);
    }

    /// <summary>
    /// Gets or sets the reveal animation. A progress of 1 writes the same vertices as no reveal.
    /// </summary>
    public GridReveal Reveal
    {
        get => _reveal;
        set => _reveal = value;
    }

    /// <summary>
    /// Gets or sets the highlight sources near windows and the pointer.
    /// </summary>
    public GridLights Lights { get; set; } = new();

    /// <summary>
    /// Gets or sets the density dimming and the glow. The default is no effect.
    /// </summary>
    public GridEffects Effects { get; set; } = GridEffects.None;

    /// <summary>
    /// Maps a canvas X coordinate to the screen through the left or right warp alone, without the zoom.
    /// </summary>
    /// <param name="canvasX">The canvas X coordinate.</param>
    /// <returns>The screen X coordinate in the unzoomed frame.</returns>
    public double ToScreen(double canvasX) => _map.ToScreen(canvasX);

    /// <summary>
    /// Maps a screen X coordinate in the unzoomed frame back to the canvas through the left or right warp alone.
    /// </summary>
    /// <param name="screenX">The screen X coordinate.</param>
    /// <returns>The canvas X coordinate.</returns>
    public double ToCanvas(double screenX) => _map.ToCanvas(screenX);

    /// <summary>
    /// Maps a canvas Y coordinate to the screen through the top or bottom warp alone, without the zoom.
    /// </summary>
    /// <param name="canvasY">The canvas Y coordinate.</param>
    /// <returns>The screen Y coordinate in the unzoomed frame.</returns>
    public double ToScreenY(double canvasY)
    {
        if (Top is { } top && top.ContainsCanvas(canvasY))
        {
            return top.ToScreen(canvasY);
        }

        if (Bottom is { } bottom && bottom.ContainsCanvas(canvasY))
        {
            return bottom.ToScreen(canvasY);
        }

        return canvasY;
    }

    /// <summary>
    /// Maps a screen Y coordinate in the unzoomed frame back to the canvas through the top or bottom warp alone.
    /// </summary>
    /// <param name="screenY">The screen Y coordinate.</param>
    /// <returns>The canvas Y coordinate.</returns>
    public double ToCanvasY(double screenY)
    {
        if (Top is { } top && top.ContainsScreen(screenY))
        {
            return top.ToCanvas(screenY);
        }

        if (Bottom is { } bottom && bottom.ContainsScreen(screenY))
        {
            return bottom.ToCanvas(screenY);
        }

        return screenY;
    }

    /// <summary>
    /// Returns the number of vertical lines that the source draws in a mesh box, after the line thinning.
    /// </summary><param name="bounds">The mesh bounds on screen. The source removes the view zoom first.</param>
    /// <returns>The line count, or 0 for an empty box.</returns>
    public int VerticalLines(in Box bounds) => VerticalLinesWarped(WarpBounds(bounds));

    private int VerticalLinesWarped(in Box bounds)
    {
        if (bounds.IsEmpty)
        {
            return 0;
        }

        var first = FirstVertical(bounds);
        var total = Candidates(first, LastVertical(bounds));
        if (_minSpacing <= 0)
        {
            return total;
        }

        var count = 0;
        for (var i = 0; i < total; i++)
        {
            if (DrawsColumn(first + ((long)i * _cellSize)))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Returns the number of horizontal lines that the source draws in a mesh box, after the line thinning.
    /// </summary><param name="bounds">The mesh bounds on screen. The source removes the view zoom first.</param>
    /// <returns>The line count, or 0 for an empty box.</returns>
    public int HorizontalLines(in Box bounds) => HorizontalLinesWarped(WarpBounds(bounds));

    private int HorizontalLinesWarped(in Box bounds)
    {
        if (bounds.IsEmpty)
        {
            return 0;
        }

        var first = FirstHorizontal(bounds);
        var total = Candidates(first, LastHorizontal(bounds));
        if (_minSpacing <= 0)
        {
            return total;
        }

        var count = 0;
        for (var i = 0; i < total; i++)
        {
            if (DrawsRow(first + ((long)i * _cellSize)))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Returns the number of segments in each vertical line: the main segment and the pieces through the top and bottom corners.
    /// </summary><param name="bounds">The mesh bounds on screen. The source removes the view zoom first.</param>
    /// <returns>The segment count, or 0 for an empty box.</returns>
    public int ColumnSegments(in Box bounds) => ColumnSegmentsWarped(WarpBounds(bounds));

    private int ColumnSegmentsWarped(in Box bounds)
    {
        if (bounds.IsEmpty)
        {
            return 0;
        }

        return 1 + CornerSegments(Top) + CornerSegments(Bottom);
    }

    /// <summary>
    /// Returns the number of segments in each horizontal line between the top and bottom zones: the flat segment, the side zone pieces and the shelf segments.
    /// </summary><param name="bounds">The mesh bounds on screen. The source removes the view zoom first.</param>
    /// <returns>The segment count, or 0 for an empty box.</returns>
    public int RowSegments(in Box bounds) => RowSegmentsWarped(WarpBounds(bounds));

    private int RowSegmentsWarped(in Box bounds)
    {
        if (bounds.IsEmpty)
        {
            return 0;
        }

        return 1 + ZoneSegments(Left, bounds.X, bounds.Right) + ZoneSegments(Right, bounds.X, bounds.Right)
            + ShelfSegment(Left, bounds) + ShelfSegment(Right, bounds);
    }

    /// <summary>
    /// Returns the number of segments in each horizontal line inside a top or bottom zone: the main segment and the pieces through the left and right corners.
    /// </summary><param name="bounds">The mesh bounds on screen. The source removes the view zoom first.</param>
    /// <returns>The segment count, or 0 for an empty box.</returns>
    public int ZoneRowSegments(in Box bounds) => ZoneRowSegmentsWarped(WarpBounds(bounds));

    private int ZoneRowSegmentsWarped(in Box bounds)
    {
        if (bounds.IsEmpty)
        {
            return 0;
        }

        return 1 + CornerSegments(Left) + CornerSegments(Right);
    }

    /// <summary>
    /// Gets the second mesh source that writes the glow halo beside each line. It writes nothing unless <see cref="Effects"/> asks for the mesh halo. Draw it with additive blending.
    /// </summary>
    public IMeshSource Glow => _glow ??= new HaloPart(this);

    /// <inheritdoc/>
    public int VertexCount(in Box bounds)
    {
        Prepare();
        return VertexCountWarped(WarpBounds(bounds));
    }

    private sealed class HaloPart(CanvasGridSource grid) : IMeshSource
    {
        public int VertexCount(in Box bounds)
        {
            grid.Prepare();
            return grid._halo ? 2 * grid.VertexCountWarped(grid.WarpBounds(bounds)) : 0;
        }

        public void WriteVertices(in Box bounds, Span<MeshVertex> into)
        {
            grid.Prepare();
            if (grid._halo)
            {
                grid.Write(bounds, into, halo: true);
            }
        }
    }

    private void Prepare()
    {
        var effects = Effects;
        _segments = effects.Density || Lights.IsLit;
        _halo = effects.DrawsHalo;
    }

    private int VertexCountWarped(in Box bounds)
    {
        if (bounds.IsEmpty)
        {
            return 0;
        }

        var extra = GridRevealMask.ClipsFor(_reveal) ? GridRevealMask.LineVertices - 6 : 0;
        var seamPieces = Pieces(ColumnLength(bounds));
        if (Separable)
        {
            return (StraightSegments(bounds) + (SeamLineCount * seamPieces)) * (6 + extra);
        }

        var verticals = VerticalLinesWarped(bounds);
        var columnPieces = Pieces(ColumnLength(bounds));
        var count = (verticals * (ColumnSegmentsWarped(bounds) - 1 + columnPieces)) + (SeamLineCount * seamPieces);
        var lines = (verticals * columnPieces) + (SeamLineCount * seamPieces);
        if (!DesktopLines)
        {
            var hidden = HiddenColumns(bounds) * columnPieces;
            count -= hidden;
            lines -= hidden;
        }

        var zoneRowSegments = ZoneRowSegmentsWarped(bounds);
        var leftSegments = ZoneSegments(Left, bounds.X, bounds.Right);
        var rightSegments = ZoneSegments(Right, bounds.X, bounds.Right);
        var flatLeft = leftSegments > 0 ? Left!.Seam : bounds.X;
        var flatRight = rightSegments > 0 ? Right!.Seam : bounds.Right;
        var rowPieces = Pieces(flatRight - flatLeft);
        var endRowPieces = Pieces(EndRowLength(bounds));
        var plateauParts = PlateauParts(bounds, flatLeft, flatRight, out var firstPart, out var secondPart);
        var plateauPieces = (plateauParts > 0 ? Pieces(firstPart.To - firstPart.From) : 0) +
            (plateauParts > 1 ? Pieces(secondPart.To - secondPart.From) : 0);
        var leftShelf = ShelfSegment(Left, bounds);
        var rightShelf = ShelfSegment(Right, bounds);
        var leftShelfPieces = leftShelf > 0 ? Pieces(Left!.Foot - bounds.X) : 0;
        var rightShelfPieces = rightShelf > 0 ? Pieces(bounds.Right - Right!.Foot) : 0;
        var first = FirstHorizontal(bounds);
        var total = Candidates(first, LastHorizontal(bounds));
        for (var i = 0; i < total; i++)
        {
            var y = first + ((long)i * _cellSize);
            if (!DrawsRow(y))
            {
                continue;
            }

            if (EndAt(y) is not null)
            {
                count += zoneRowSegments - 1 + endRowPieces;
                lines += endRowPieces;
                continue;
            }

            var hides = HidesRow(y);
            count += leftSegments + rightSegments + (hides ? plateauPieces : rowPieces);
            lines += hides ? plateauPieces : rowPieces;
            if (leftShelf > 0 && DrawsShelfRow(Left!, y))
            {
                count += leftShelfPieces;
                lines += leftShelfPieces;
            }

            if (rightShelf > 0 && DrawsShelfRow(Right!, y))
            {
                count += rightShelfPieces;
                lines += rightShelfPieces;
            }
        }

        return (count * 6) + (lines * extra);
    }

    /// <inheritdoc/>
    public void WriteVertices(in Box bounds, Span<MeshVertex> into)
    {
        Prepare();
        Write(bounds, into, halo: false);
    }

    private void Write(in Box bounds, Span<MeshVertex> into, bool halo)
    {
        _mask = GridRevealMask.Of(_reveal, bounds);
        _tone = GridTone.Of(Color, _alpha, _mask.Scale, Lights, Effects);
        _tone.HaloPart = halo;
        _toned = halo || _tone.Segments;
        WriteVerticesWarped(WarpBounds(bounds), into);
    }

    private int Pieces(double length) => GridTone.Pieces(length * _viewScale, _segments);

    private double ColumnLength(in Box bounds) =>
        (Bottom is { IsIdentity: false } bottom ? bottom.Seam : bounds.Bottom) - (Top is { IsIdentity: false } top ? top.Seam : bounds.Y);

    private double EndRowLength(in Box bounds) =>
        (Right is { IsIdentity: false } right ? right.Seam : bounds.Right) - (Left is { IsIdentity: false } left ? left.Seam : bounds.X);

    private double FlatX => Left is { IsIdentity: false } left ? left.Seam : Right is { IsIdentity: false } right ? right.Seam : 0.0;

    private double FlatY => Top is { IsIdentity: false } top ? top.Seam : Bottom is { IsIdentity: false } bottom ? bottom.Seam : 0.0;

    private float DensityAt(double canvasX, double canvasY, bool column)
    {
        if (!_tone.Density)
        {
            return 1f;
        }

        var (scaleX, scaleY) = _map.LocalScale(canvasX, canvasY);
        var inSide = (Left is { IsIdentity: false } left && left.ContainsCanvas(canvasX)) ||
            (Right is { IsIdentity: false } right && right.ContainsCanvas(canvasX));
        var inEnd = EndAt(canvasY) is { IsIdentity: false };
        var rowFan = Separable || !inSide ? 1.0 : _map.FanAt(canvasX);
        var columnFan = Separable || !inEnd ? 1.0 : _map.ColumnFanAt(canvasY);
        var unit = _cellSize * _viewScale;
        var across = unit * scaleX * columnFan * (inSide ? StrideOf(scaleX) : 1);
        var along = unit * scaleY * rowFan * (inEnd ? StrideOf(scaleY) : 1);
        var spacing = inSide && inEnd ? Math.Min(across, along) : column ? across : along;
        return _tone.DensityOf(spacing);
    }

    private long StrideOf(double scale)
    {
        var stride = 1L;
        while (_minSpacing > 0 && scale * _viewScale * _cellSize * stride < _minSpacing && stride < 1L << 20)
        {
            stride <<= 1;
        }

        return stride;
    }

    private float DensityOf(in LineDensity density, (float X, float Y) point, float t) => density.Kind switch
    {
        LineDensity.PairKind => density.From + ((density.To - density.From) * t),
        LineDensity.RowKind => DensityAt(ToCanvas(point.X), density.Canvas, column: false),
        LineDensity.ColumnKind => DensityAt(density.Canvas, ToCanvasY(point.Y), column: true),
        _ => 1f,
    };

    private readonly struct LineDensity(int kind, double canvas, float from, float to)
    {
        public const int PairKind = 1;

        public const int RowKind = 2;

        public const int ColumnKind = 3;

        public int Kind => kind;

        public double Canvas => canvas;

        public float From => from;

        public float To => to;

        public static LineDensity Fixed(float density) => new(PairKind, 0, density, density);

        public static LineDensity Pair(float from, float to) => new(PairKind, 0, from, to);

        public static LineDensity Row(double canvasY) => new(RowKind, canvasY, 1f, 1f);

        public static LineDensity Column(double canvasX) => new(ColumnKind, canvasX, 1f, 1f);
    }

    private void WriteVerticesWarped(in Box bounds, Span<MeshVertex> into)
    {
        if (bounds.IsEmpty)
        {
            return;
        }

        var alpha = _alpha * _mask.Scale;
        var color = new RenderColor(Color.R * alpha, Color.G * alpha, Color.B * alpha, Color.A * alpha);
        var write = 0;
        if (Separable)
        {
            WriteStraight(bounds, into, color);
            return;
        }

        var first = FirstVertical(bounds);
        var count = Candidates(first, LastVertical(bounds));
        var columnPieces = Pieces(ColumnLength(bounds));
        var topActive = Top is { IsIdentity: false };
        var bottomActive = Bottom is { IsIdentity: false };
        for (var i = 0; i < count; i++)
        {
            var column = first + ((long)i * _cellSize);
            if (!DrawsColumn(column))
            {
                continue;
            }

            var canvasX = (double)column;
            var exact = ToScreen(canvasX);
            var x = (float)Snap(exact, ViewCenterX);
            var shift = x - exact;
            var top = topActive ? (float)_map.ToScreenPoint(canvasX, Top!.Seam).Y : bounds.Y;
            var bottom = bottomActive ? (float)_map.ToScreenPoint(canvasX, Bottom!.Seam).Y : bounds.Bottom;
            if (!HidesColumn(column))
            {
                WriteColumn(
                    into, ref write, (x, top), (x, bottom), color, line: true, columnPieces,
                    _tone.Density ? LineDensity.Fixed(DensityAt(canvasX, FlatY, column: true)) : default);
            }

            if (topActive)
            {
                WriteColumnCorner(into, ref write, Top!, canvasX, shift, color);
            }

            if (bottomActive)
            {
                WriteColumnCorner(into, ref write, Bottom!, canvasX, shift, color);
            }
        }

        var firstRow = FirstHorizontal(bounds);
        var rows = Candidates(firstRow, LastHorizontal(bounds));
        var leftShelf = ShelfSegment(Left, bounds) > 0;
        var rightShelf = ShelfSegment(Right, bounds) > 0;
        var leftSegments = ZoneSegments(Left, bounds.X, bounds.Right);
        var rightSegments = ZoneSegments(Right, bounds.X, bounds.Right);
        var flatLeft = leftSegments > 0 ? Left!.Seam : bounds.X;
        var flatRight = rightSegments > 0 ? Right!.Seam : bounds.Right;
        var leftActive = Left is { IsIdentity: false };
        var rightActive = Right is { IsIdentity: false };
        var endRowPieces = Pieces(EndRowLength(bounds));
        var rowPieces = Pieces(flatRight - flatLeft);
        var leftShelfPieces = leftShelf ? Pieces(Left!.Foot - bounds.X) : 0;
        var rightShelfPieces = rightShelf ? Pieces(bounds.Right - Right!.Foot) : 0;
        for (var i = 0; i < rows; i++)
        {
            var y = firstRow + ((long)i * _cellSize);
            if (!DrawsRow(y))
            {
                continue;
            }

            if (EndAt(y) is { } end)
            {
                var screenY = end.ToScreen(y);
                var start = leftActive ? _map.ToScreenPoint(Left!.Seam, y) : (bounds.X, screenY);
                var stop = rightActive ? _map.ToScreenPoint(Right!.Seam, y) : (bounds.Right, screenY);
                WriteRow(
                    into, ref write, start, stop, color, line: true, endRowPieces,
                    _tone.Density ? LineDensity.Fixed(DensityAt(FlatX, y, column: false)) : default);
                if (leftActive)
                {
                    WriteRowCorner(into, ref write, Left!, end, y, color);
                }

                if (rightActive)
                {
                    WriteRowCorner(into, ref write, Right!, end, y, color);
                }

                continue;
            }

            if (!HidesRow(y))
            {
                WriteRow(into, ref write, flatLeft, flatRight, y, color, line: true, rowPieces);
            }
            else
            {
                var parts = PlateauParts(bounds, flatLeft, flatRight, out var firstPart, out var secondPart);
                if (parts > 0)
                {
                    WriteRow(into, ref write, firstPart.From, firstPart.To, y, color, line: true, Pieces(firstPart.To - firstPart.From));
                }

                if (parts > 1)
                {
                    WriteRow(into, ref write, secondPart.From, secondPart.To, y, color, line: true, Pieces(secondPart.To - secondPart.From));
                }
            }

            for (var segment = 0; segment < leftSegments; segment++)
            {
                var x1 = Left!.Seam - (segment * SegmentWidth);
                var x0 = Math.Max(Left.Terrace ? Math.Max(bounds.X, Left.Foot) : bounds.X, x1 - SegmentWidth);
                WriteRow(into, ref write, x0, x1, y, color, line: false);
            }

            for (var segment = 0; segment < rightSegments; segment++)
            {
                var x0 = Right!.Seam + (segment * SegmentWidth);
                var x1 = Math.Min(Right.Terrace ? Math.Min(bounds.Right, Right.Foot) : bounds.Right, x0 + SegmentWidth);
                WriteRow(into, ref write, x0, x1, y, color, line: false);
            }

            if (leftShelf && DrawsShelfRow(Left!, y))
            {
                WriteRow(into, ref write, bounds.X, Left!.Foot, y, color, line: true, leftShelfPieces);
            }

            if (rightShelf && DrawsShelfRow(Right!, y))
            {
                WriteRow(into, ref write, Right!.Foot, bounds.Right, y, color, line: true, rightShelfPieces);
            }
        }

        WriteSeamLines(bounds, into, ref write, color);
    }

    private void WriteStraight(in Box bounds, Span<MeshVertex> into, in RenderColor color)
    {
        var write = 0;
        var first = FirstVertical(bounds);
        var columns = Candidates(first, LastVertical(bounds));
        var fullColumn = Pieces(bounds.Height);
        var fullRow = Pieces(bounds.Width);
        for (var i = 0; i < columns; i++)
        {
            var column = first + ((long)i * _cellSize);
            if (!DrawsColumn(column))
            {
                continue;
            }

            var x = (float)Snap(ToScreen(column), ViewCenterX);
            var density = LineDensity.Column(column);
            if (!HidesColumn(column))
            {
                WriteColumn(into, ref write, (x, bounds.Y), (x, bounds.Bottom), color, line: true, fullColumn, density);
                continue;
            }

            if (Top is { IsIdentity: false } top)
            {
                WriteColumn(into, ref write, (x, bounds.Y), (x, top.Seam), color, line: true, Pieces(top.Seam - bounds.Y), density);
            }

            if (Bottom is { IsIdentity: false } bottom)
            {
                WriteColumn(
                    into, ref write, (x, bottom.Seam), (x, bounds.Bottom), color, line: true, Pieces(bounds.Bottom - bottom.Seam), density);
            }
        }

        var firstRow = FirstHorizontal(bounds);
        var rows = Candidates(firstRow, LastHorizontal(bounds));
        for (var i = 0; i < rows; i++)
        {
            var row = firstRow + ((long)i * _cellSize);
            if (!DrawsRow(row))
            {
                continue;
            }

            var y = (float)Snap(ToScreenY(row), ViewCenterY);
            var density = LineDensity.Row(row);
            if (!HidesRow(row))
            {
                WriteSegment(into, ref write, bounds.X, y, bounds.Right, y, color, line: true, fullRow, density);
                continue;
            }

            if (Left is { IsIdentity: false } left)
            {
                WriteSegment(into, ref write, bounds.X, y, left.Seam, y, color, line: true, Pieces(left.Seam - bounds.X), density);
            }

            if (Right is { IsIdentity: false } right)
            {
                WriteSegment(into, ref write, right.Seam, y, bounds.Right, y, color, line: true, Pieces(bounds.Right - right.Seam), density);
            }
        }

        WriteSeamLines(bounds, into, ref write, color);
    }

    private int SeamLineCount =>
        !SeamLines || DesktopLines ? 0 : (Left is { IsIdentity: false } ? 1 : 0) + (Right is { IsIdentity: false } ? 1 : 0);

    private void WriteSeamLines(in Box bounds, Span<MeshVertex> into, ref int write, in RenderColor color)
    {
        if (SeamLineCount == 0)
        {
            return;
        }

        var top = Top is { IsIdentity: false } upper ? upper.Seam : bounds.Y;
        var bottom = Bottom is { IsIdentity: false } lower ? lower.Seam : bounds.Bottom;
        var pieces = Pieces(ColumnLength(bounds));
        if (Left is { IsIdentity: false } left)
        {
            WriteColumn(
                into, ref write, (left.Seam - 1, top), (left.Seam - 1, bottom), color, line: true, pieces,
                _tone.Density ? LineDensity.Fixed(DensityAt(left.Seam, FlatY, column: true)) : default);
        }

        if (Right is { IsIdentity: false } right)
        {
            WriteColumn(
                into, ref write, (right.Seam, top), (right.Seam, bottom), color, line: true, pieces,
                _tone.Density ? LineDensity.Fixed(DensityAt(right.Seam, FlatY, column: true)) : default);
        }
    }

    private int StraightSegments(in Box bounds)
    {
        var fullColumn = Pieces(bounds.Height);
        var fullRow = Pieces(bounds.Width);
        var count = (VerticalLinesWarped(bounds) * fullColumn) + (HorizontalLinesWarped(bounds) * fullRow);
        if (DesktopLines)
        {
            return count;
        }

        var topPart = Top is { IsIdentity: false } top ? Pieces(top.Seam - bounds.Y) : 0;
        var bottomPart = Bottom is { IsIdentity: false } bottom ? Pieces(bounds.Bottom - bottom.Seam) : 0;
        var leftPart = Left is { IsIdentity: false } left ? Pieces(left.Seam - bounds.X) : 0;
        var rightPart = Right is { IsIdentity: false } right ? Pieces(bounds.Right - right.Seam) : 0;
        count += HiddenColumns(bounds) * (topPart + bottomPart - fullColumn);
        var first = FirstHorizontal(bounds);
        var rows = Candidates(first, LastHorizontal(bounds));
        for (var i = 0; i < rows; i++)
        {
            var row = first + ((long)i * _cellSize);
            if (DrawsRow(row) && HidesRow(row))
            {
                count += leftPart + rightPart - fullRow;
            }
        }

        return count;
    }

    private int HiddenColumns(in Box bounds)
    {
        var hidden = 0;
        var first = FirstVertical(bounds);
        var columns = Candidates(first, LastVertical(bounds));
        for (var i = 0; i < columns; i++)
        {
            var column = first + ((long)i * _cellSize);
            if (DrawsColumn(column) && HidesColumn(column))
            {
                hidden++;
            }
        }

        return hidden;
    }

    private bool HidesColumn(long canvasX) => !DesktopLines && Inside(Left, canvasX) && Inside(Right, canvasX);

    private bool HidesRow(long canvasY) => !DesktopLines && Inside(Top, canvasY) && Inside(Bottom, canvasY);

    private static bool Inside(CanvasWarp? warp, long canvas) =>
        warp is not { IsIdentity: false } zone || zone.Direction * (canvas - zone.Seam) < 0;

    private int PlateauParts(in Box bounds, int flatLeft, int flatRight, out (int From, int To) first, out (int From, int To) second)
    {
        var plateauLeft = Left is { IsIdentity: false } left ? left.Seam : bounds.X;
        var plateauRight = Right is { IsIdentity: false } right ? right.Seam : bounds.Right;
        first = default;
        second = default;
        var parts = 0;
        if (Math.Min(flatRight, plateauLeft) > flatLeft)
        {
            first = (flatLeft, Math.Min(flatRight, plateauLeft));
            parts++;
        }

        if (Math.Max(flatLeft, plateauRight) < flatRight)
        {
            var part = (Math.Max(flatLeft, plateauRight), flatRight);
            if (parts == 0)
            {
                first = part;
            }
            else
            {
                second = part;
            }

            parts++;
        }

        return parts;
    }

    private Box WarpBounds(in Box bounds)
    {
        if (_viewScale == 1.0 || bounds.IsEmpty)
        {
            return bounds;
        }

        var left = (int)Math.Floor(ViewCenterX + ((bounds.X - ViewCenterX) / _viewScale));
        var top = (int)Math.Floor(ViewCenterY + ((bounds.Y - ViewCenterY) / _viewScale));
        var right = (int)Math.Ceiling(ViewCenterX + ((bounds.Right - ViewCenterX) / _viewScale));
        var bottom = (int)Math.Ceiling(ViewCenterY + ((bounds.Bottom - ViewCenterY) / _viewScale));
        return new Box(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    private double Snap(double warp, double center) =>
        _viewScale == 1.0
            ? Math.Round(warp)
            : center + ((Math.Round(center + ((warp - center) * _viewScale)) - center) / _viewScale);

    private (float X, float Y) Zoom(float x, float y) =>
        _viewScale == 1.0
            ? (x, y)
            : ((float)(ViewCenterX + ((x - ViewCenterX) * _viewScale)), (float)(ViewCenterY + ((y - ViewCenterY) * _viewScale)));

    private int Candidates(long first, long last) => last < first ? 0 : (int)((last - first) / _cellSize) + 1;

    private bool DrawsColumn(long canvasX)
    {
        if (_minSpacing <= 0)
        {
            return true;
        }

        var warp = Left is { IsIdentity: false } left && left.ContainsCanvas(canvasX) ? left
            : Right is { IsIdentity: false } right && right.ContainsCanvas(canvasX) ? right
            : null;
        return warp is null || OnStride(canvasX, warp.ScaleAt(canvasX));
    }

    private bool DrawsRow(long canvasY)
    {
        if (_minSpacing <= 0)
        {
            return true;
        }

        return EndAt(canvasY) is not { IsIdentity: false } end || OnStride(canvasY, end.ScaleAt(canvasY));
    }

    private bool DrawsShelfRow(CanvasWarp side, long canvasY) => _minSpacing <= 0 || OnStride(canvasY, side.EdgeScale);

    private bool OnStride(long canvas, double scale)
    {
        var stride = 1L;
        while (scale * _viewScale * _cellSize * stride < _minSpacing && stride < 1L << 20)
        {
            stride <<= 1;
        }

        return canvas / _cellSize % stride == 0;
    }

    private static int ShelfSegment(CanvasWarp? warp, in Box bounds)
    {
        if (warp is not { IsIdentity: false, Terrace: true } shelf)
        {
            return 0;
        }

        return shelf.Direction < 0 ? (shelf.Foot > bounds.X ? 1 : 0) : (shelf.Foot < bounds.Right ? 1 : 0);
    }

    private static int ZoneSegments(CanvasWarp? warp, int boundsStart, int boundsEnd)
    {
        if (warp is not { IsIdentity: false } zone || (zone.Slope == 0 && !zone.Terrace))
        {
            return 0;
        }

        var start = Math.Max(boundsStart, Math.Min(zone.Seam, zone.ScreenEdge));
        var end = Math.Min(boundsEnd, Math.Max(zone.Seam, zone.ScreenEdge));
        return end <= start ? 0 : (end - start + SegmentWidth - 1) / SegmentWidth;
    }

    private static int CornerSegments(CanvasWarp? warp) =>
        warp is { IsIdentity: false } zone
            ? Math.Max(1, ((int)Math.Ceiling(zone.ZoneWidth * CornerDepth(zone)) + SegmentWidth - 1) / SegmentWidth)
            : 0;

    private static double CornerDepth(CanvasWarp zone) =>
        zone.Terrace && zone.ZoneWidth > 0 ? (zone.ZoneWidth + (2.0 * zone.ShelfWidth)) / zone.ZoneWidth : 1.0;

    private static double TerraceFan(CanvasWarp? low, CanvasWarp? high)
    {
        var fan = 1.0;
        if (low is { IsIdentity: false, Terrace: true })
        {
            fan = Math.Min(fan, low.MinFan);
        }

        if (high is { IsIdentity: false, Terrace: true })
        {
            fan = Math.Min(fan, high.MinFan);
        }

        return fan;
    }

    private static double TerraceCenter(CanvasWarp? low, CanvasWarp? high) =>
        low is { IsIdentity: false, Terrace: true } ? low.Center : high?.Center ?? 0.0;

    private double ReachY(double screenY)
    {
        var canvasY = ToCanvasY(screenY);
        var fan = TerraceFan(Left, Right);
        if (fan >= 1.0 || Separable)
        {
            return canvasY;
        }

        var center = TerraceCenter(Left, Right);
        if ((screenY < center ? Top : Bottom) is { IsIdentity: false })
        {
            return canvasY;
        }

        var reach = center + ((screenY - center) / fan);
        return screenY < center ? Math.Min(canvasY, reach) : Math.Max(canvasY, reach);
    }

    private double ReachX(double screenX)
    {
        var canvasX = ToCanvas(screenX);
        var fan = TerraceFan(Top, Bottom);
        if (fan >= 1.0 || Separable)
        {
            return canvasX;
        }

        var center = TerraceCenter(Top, Bottom);
        if ((screenX < center ? Left : Right) is { IsIdentity: false })
        {
            return canvasX;
        }

        var reach = center + ((screenX - center) / fan);
        return screenX < center ? Math.Min(canvasX, reach) : Math.Max(canvasX, reach);
    }

    private CanvasWarp? EndAt(double canvasY) =>
        Top is { } top && top.ContainsCanvas(canvasY) ? top
        : Bottom is { } bottom && bottom.ContainsCanvas(canvasY) ? bottom
        : null;

    private static double Across(CanvasWarp? warp, double canvas) =>
        warp is { IsIdentity: false } && warp.ContainsCanvas(canvas)
            ? Math.Min(1.0, warp.Direction * (canvas - warp.Seam) / warp.Extension)
            : 0.0;

    private void WriteColumnCorner(Span<MeshVertex> into, ref int write, CanvasWarp end, double canvasX, double shift, in RenderColor color)
    {
        var across = Math.Max(Across(Left, canvasX), Across(Right, canvasX));
        var reach = _map.CornerReach(across);
        var depth = end.DepthAt(end.Seam + (end.Direction * reach * end.Extension));
        var segments = CornerSegments(end);
        if (end.Terrace)
        {
            depth = CornerDepth(end);
        }

        var crease = end.Terrace ? -1 : CreaseStep(end, across, reach, depth, segments);
        var previous = ColumnPoint(end, canvasX, shift, 0.0, out var previousY);
        var previousDensity = DensityAt(canvasX, previousY, column: true);
        for (var j = 1; j <= segments; j++)
        {
            var next = ColumnPoint(end, canvasX, shift, j == crease ? CreaseDepth(end, across) : depth * j / segments, out var nextY);
            var nextDensity = DensityAt(canvasX, nextY, column: true);
            WriteColumn(into, ref write, previous, next, color, line: false, 1, LineDensity.Pair(previousDensity, nextDensity));
            previous = next;
            previousDensity = nextDensity;
        }
    }

    private (float X, float Y) ColumnPoint(CanvasWarp end, double canvasX, double shift, double depth, out double canvasY)
    {
        canvasY = end.ToCanvas(end.Seam + (end.Direction * depth * end.ZoneWidth));
        var (x, y) = _map.ToScreenPoint(canvasX, canvasY);
        return ((float)(x + shift), (float)y);
    }

    private void WriteRowCorner(Span<MeshVertex> into, ref int write, CanvasWarp side, CanvasWarp end, double canvasY, in RenderColor color)
    {
        var across = Math.Min(1.0, end.Direction * (canvasY - end.Seam) / end.Extension);
        var reach = _map.CornerReach(across);
        var depth = side.DepthAt(side.Seam + (side.Direction * reach * side.Extension));
        var segments = CornerSegments(side);
        if (side.Terrace)
        {
            depth = CornerDepth(side);
        }

        var crease = side.Terrace ? -1 : CreaseStep(side, across, reach, depth, segments);
        var previous = RowPoint(side, canvasY, 0.0, out var previousX);
        var previousDensity = DensityAt(previousX, canvasY, column: false);
        for (var j = 1; j <= segments; j++)
        {
            var next = RowPoint(side, canvasY, j == crease ? CreaseDepth(side, across) : depth * j / segments, out var nextX);
            var nextDensity = DensityAt(nextX, canvasY, column: false);
            WriteRow(into, ref write, previous, next, color, line: false, 1, LineDensity.Pair(previousDensity, nextDensity));
            previous = next;
            previousDensity = nextDensity;
        }
    }

    private int CreaseStep(CanvasWarp warp, double across, double reach, double depth, int segments)
    {
        if (_map.CornerRadius > 0 || across <= 0 || across >= reach || depth <= 0 || segments < 2)
        {
            return -1;
        }

        var step = (int)Math.Round(CreaseDepth(warp, across) / depth * segments);
        return Math.Clamp(step, 1, segments - 1);
    }

    private static double CreaseDepth(CanvasWarp warp, double across) =>
        warp.DepthAt(warp.Seam + (warp.Direction * across * warp.Extension));

    private (double X, double Y) RowPoint(CanvasWarp side, double canvasY, double depth, out double canvasX)
    {
        canvasX = side.ToCanvas(side.Seam + (side.Direction * depth * side.ZoneWidth));
        return _map.ToScreenPoint(canvasX, canvasY);
    }

    private double CenterAt(double screenX)
    {
        if (Left is { } left && left.ContainsScreen(screenX))
        {
            return left.Center;
        }

        if (Right is { } right && right.ContainsScreen(screenX))
        {
            return right.Center;
        }

        return 0.0;
    }

    private double RowY(double screenX, double y)
    {
        var fan = _map.FanAtScreen(screenX);
        if (fan == 1.0)
        {
            return y;
        }

        var center = CenterAt(screenX);
        return center + ((y - center) * fan);
    }

    private void WriteColumn(
        Span<MeshVertex> into, ref int write, (float X, float Y) from, (float X, float Y) to, in RenderColor color, bool line,
        int pieces = 1, in LineDensity density = default)
    {
        if (_toned)
        {
            WritePieces(into, ref write, from, to, vertical: true, line, pieces, density);
            return;
        }

        from = Zoom(from.X, from.Y);
        to = Zoom(to.X, to.Y);
        if (Terraced && Math.Abs(to.X - from.X) > Math.Abs(to.Y - from.Y))
        {
            WriteQuad(into, ref write, from, to, 0, 1, vertical: true, line, color);
            return;
        }

        WriteQuad(into, ref write, from, to, 1, 0, vertical: true, line, color);
    }

    private void WriteRow(
        Span<MeshVertex> into, ref int write, (double X, double Y) from, (double X, double Y) to, in RenderColor color, bool line,
        int pieces = 1, in LineDensity density = default) =>
        WriteSegment(into, ref write, (float)from.X, (float)from.Y, (float)to.X, (float)to.Y, color, line, pieces, density);

    private void WriteRow(Span<MeshVertex> into, ref int write, int x0, int x1, long y, in RenderColor color, bool line, int pieces = 1) =>
        WriteSegment(
            into, ref write, x0, (float)RowY(x0, y), x1, (float)RowY(x1, y), color, line, pieces,
            _tone.Density ? LineDensity.Row(y) : default);

    private void WriteSegment(
        Span<MeshVertex> into, ref int write, float x0, float y0, float x1, float y1, in RenderColor color, bool line,
        int pieces = 1, in LineDensity density = default)
    {
        if (_toned)
        {
            WritePieces(into, ref write, (x0, y0), (x1, y1), vertical: false, line, pieces, density);
            return;
        }

        (x0, y0) = Zoom(x0, y0);
        (x1, y1) = Zoom(x1, y1);
        if (Terraced && Math.Abs(y1 - y0) > Math.Abs(x1 - x0))
        {
            WriteQuad(into, ref write, (x0, y0), (x1, y1), 1, 0, vertical: false, line, color);
            return;
        }

        WriteQuad(into, ref write, (x0, y0), (x1, y1), 0, 1, vertical: false, line, color);
    }

    private void WritePieces(
        Span<MeshVertex> into, ref int write, (float X, float Y) from, (float X, float Y) to, bool vertical, bool line,
        int pieces, in LineDensity density)
    {
        var start = Zoom(from.X, from.Y);
        var end = Zoom(to.X, to.Y);
        var turned = Terraced && (vertical
            ? Math.Abs(end.X - start.X) > Math.Abs(end.Y - start.Y)
            : Math.Abs(end.Y - start.Y) > Math.Abs(end.X - start.X));
        var nx = vertical != turned ? 1f : 0f;
        var ny = 1f - nx;
        pieces = Math.Max(1, pieces);
        var previous = from;
        var previousDensity = DensityOf(density, from, 0f);
        for (var i = 1; i <= pieces; i++)
        {
            var t = (float)i / pieces;
            (float X, float Y) next = i == pieces ? to : (from.X + ((to.X - from.X) * t), from.Y + ((to.Y - from.Y) * t));
            var nextDensity = DensityOf(density, next, t);
            write += GridQuad.Write(
                into[write..], _mask, line, Zoom(previous.X, previous.Y), Zoom(next.X, next.Y), nx, ny, vertical, _tone,
                previousDensity, nextDensity);
            previous = next;
            previousDensity = nextDensity;
        }
    }

    private void WriteQuad(
        Span<MeshVertex> into, ref int write, (float X, float Y) from, (float X, float Y) to, float nx, float ny,
        bool vertical, bool line, in RenderColor color)
    {
        if (_mask.Clips && line)
        {
            _mask.WriteLine(into.Slice(write, GridRevealMask.LineVertices), from, to, nx, ny, vertical, color);
            write += GridRevealMask.LineVertices;
            return;
        }

        var fromOffset = (from.X + nx, from.Y + ny);
        var toOffset = (to.X + nx, to.Y + ny);
        if (_mask.Clips)
        {
            if (ny == 0)
            {
                _mask.WriteCell(into.Slice(write, 6), from, fromOffset, toOffset, to, vertical, color);
            }
            else
            {
                _mask.WriteCell(into.Slice(write, 6), from, to, toOffset, fromOffset, vertical, color);
            }
        }
        else if (ny == 0)
        {
            MeshGrid.WriteCell(into.Slice(write, 6), 0, 0, 0, 0, from, fromOffset, toOffset, to, color);
        }
        else
        {
            MeshGrid.WriteCell(into.Slice(write, 6), 0, 0, 0, 0, from, to, toOffset, fromOffset, color);
        }

        write += 6;
    }

    private bool Terraced =>
        Left is { IsIdentity: false, Terrace: true } || Right is { IsIdentity: false, Terrace: true } ||
        Top is { IsIdentity: false, Terrace: true } || Bottom is { IsIdentity: false, Terrace: true };

    private long FirstHorizontal(in Box bounds) => Ceiling((long)Math.Ceiling(ReachY(bounds.Y)), _cellSize);

    private long LastHorizontal(in Box bounds) => Floor((long)Math.Floor(ReachY(bounds.Bottom - 1)), _cellSize);

    private long FirstVertical(in Box bounds) => Ceiling((long)Math.Ceiling(ReachX(bounds.X)), _cellSize);

    private long LastVertical(in Box bounds) => Floor((long)Math.Floor(ReachX(bounds.Right - 1)), _cellSize);

    private static long Ceiling(long value, int cell)
    {
        var remainder = value % cell;
        return remainder == 0 ? value : remainder > 0 ? value + (cell - remainder) : value - remainder;
    }

    private static long Floor(long value, int cell)
    {
        var remainder = value % cell;
        return remainder == 0 ? value : remainder > 0 ? value - remainder : value - (cell + remainder);
    }
}
