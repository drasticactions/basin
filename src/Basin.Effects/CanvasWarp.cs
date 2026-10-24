namespace Basin.Effects;

/// <summary>
/// The map for one edge of one output. It compresses the canvas past a seam into a zone on screen along a smooth curve. It is one axis only.
/// </summary>
public sealed class CanvasWarp
{
    private double[] _table = [];
    private int _seam;
    private int _direction = -1;
    private int _zoneWidth;
    private int _extension;
    private double _edgeScale = 1.0;
    private double _exponent;
    private double _slope;
    private double _center;
    private bool _terrace;
    private int _shelfWidth;
    private double _minFan = 1.0;
    private double _slopeFloor = MinSlope;

    /// <summary>
    /// The least row-fan slope. It keeps the fan positive, so the inverse stays a division.
    /// </summary>
    public const double MinSlope = -0.9;

    /// <summary>
    /// The fixed curve exponent of a terrace slope.
    /// </summary>
    public const double TerraceExponent = 2.0;

    /// <summary>
    /// The least row fan that a terrace slope can reach. <see cref="LayoutTerrace"/> raises the slope to keep the fan at this value or above.
    /// </summary>
    public const double MinTerraceFan = 0.1;

    /// <summary>
    /// Initializes a new instance of the <see cref="CanvasWarp"/> class as the identity map.
    /// </summary>
    /// <param name="direction">-1 for a zone that extends toward lower coordinates, any other value for higher coordinates.</param>
    public CanvasWarp(int direction = -1)
    {
        _direction = direction < 0 ? -1 : 1;
    }

    /// <summary>
    /// Gets the coordinate where the flat span ends and the zone starts. It is the same on the canvas and on the screen.
    /// </summary>
    public int Seam => _seam;

    /// <summary>
    /// Gets -1 when the zone extends toward lower coordinates, or 1 when it extends toward higher coordinates.
    /// </summary>
    public int Direction => _direction;

    /// <summary>
    /// Gets the width of the zone on screen in pixels. It is 0 for the identity.
    /// </summary>
    public int ZoneWidth => _zoneWidth;

    /// <summary>
    /// Gets how far the canvas continues past the seam, in canvas pixels. It is never less than <see cref="ZoneWidth"/>.
    /// </summary>
    public int Extension => _extension;

    /// <summary>
    /// Gets the draw scale at the far edge that is in force after the clamp of <see cref="ClampEdgeScale"/>.
    /// </summary>
    public double EdgeScale => _edgeScale;

    /// <summary>
    /// Gets the curve exponent that makes the zone hold the whole extension. It is 0 for the identity.
    /// </summary>
    public double Exponent => _exponent;

    /// <summary>
    /// Gets the row-fan slope in force, after the floor that keeps the fan positive.
    /// </summary>
    public double Slope => _slope;

    /// <summary>
    /// Gets the cross-axis coordinate of the row that the fan leaves in place.
    /// </summary>
    public double Center => _center;

    /// <summary>
    /// Gets a value indicating whether the zone is a terrace with a flat shelf past the slope.
    /// </summary>
    public bool Terrace => _terrace;

    /// <summary>
    /// Gets the width of the terrace shelf on screen in pixels. It is 0 when the zone is not a terrace.
    /// </summary>
    public int ShelfWidth => _shelfWidth;

    /// <summary>
    /// Gets the least row fan over the zone at the current slope.
    /// </summary>
    public double MinFan => _minFan;

    /// <summary>
    /// Gets a value indicating whether the warp has no zone and maps every coordinate to itself.
    /// </summary>
    public bool IsIdentity => _zoneWidth <= 0 || _extension <= 0;

    /// <summary>
    /// Gets the canvas coordinate of the far edge of the extension.
    /// </summary>
    public int FarEdge => _seam + (_direction * _extension);

    /// <summary>
    /// Gets the screen coordinate of the outer end of the zone.
    /// </summary>
    public int ScreenEdge => _seam + (_direction * _zoneWidth);

    /// <summary>
    /// Gets the screen coordinate where a terrace slope meets its shelf. It equals <see cref="ScreenEdge"/>.
    /// </summary>
    public int Foot => ScreenEdge;

    /// <summary>
    /// Gets the screen coordinate of the outer end of the terrace shelf. It equals <see cref="Foot"/> when there is no shelf.
    /// </summary>
    public int OuterEdge => ScreenEdge + (_direction * _shelfWidth);

    /// <summary>
    /// Returns the extension that makes a terrace slope of a given width end at the shelf scale.
    /// </summary>
    /// <param name="zone">The slope width on screen in pixels.</param>
    /// <param name="scale">The shelf scale. The method clamps it to the range from 0.01 to 1.</param>
    /// <param name="exponent">The curve exponent. The method raises it to at least 1.</param>
    /// <returns>The extension in canvas pixels, never less than <paramref name="zone"/>, or 0 for an empty zone.</returns>
    public static int TerraceExtension(int zone, double scale, double exponent)
    {
        if (zone <= 0)
        {
            return 0;
        }

        scale = Math.Clamp(scale, 0.01, 1.0);
        exponent = Math.Max(1.0, exponent);
        return Math.Max(zone, (int)Math.Round(zone / (scale + ((1.0 - scale) / (exponent + 1.0)))));
    }

    /// <summary>
    /// Returns an edge scale for which a curve exists. A scale at or above the zone width over the extension becomes 0.8 times that ratio.
    /// </summary>
    /// <param name="edgeScale">The requested edge scale.</param>
    /// <param name="zoneWidth">The zone width on screen in pixels.</param>
    /// <param name="extension">The extension in canvas pixels.</param>
    /// <returns>The edge scale in force, or <paramref name="edgeScale"/> unchanged for an empty zone.</returns>
    public static double ClampEdgeScale(double edgeScale, int zoneWidth, int extension)
    {
        if (zoneWidth <= 0 || extension <= 0)
        {
            return edgeScale;
        }

        var ratio = (double)zoneWidth / extension;
        var clamped = Math.Max(edgeScale, 0.01);
        return clamped < ratio ? clamped : 0.8 * ratio;
    }

    /// <summary>
    /// Lays out a zone with flat rows.
    /// </summary>
    /// <param name="seam">The seam coordinate.</param>
    /// <param name="direction">-1 or 1, the side of the seam that the zone is on.</param>
    /// <param name="zoneWidth">The zone width on screen in pixels. Zero or less gives the identity.</param>
    /// <param name="extension">The extension in canvas pixels.</param>
    /// <param name="edgeScale">The draw scale at the far edge.</param>
    /// <returns><see langword="true"/> when the map changed.</returns>
    public bool Layout(int seam, int direction, int zoneWidth, int extension, double edgeScale) =>
        Layout(seam, direction, zoneWidth, extension, edgeScale, 0.0, 0.0);

    /// <summary>
    /// Lays out a zone with a row fan. It solves the inverse table again only when the curve inputs change.
    /// </summary>
    /// <param name="seam">The seam coordinate.</param>
    /// <param name="direction">-1 or 1, the side of the seam that the zone is on.</param>
    /// <param name="zoneWidth">The zone width on screen in pixels. Zero or less gives the identity.</param>
    /// <param name="extension">The extension in canvas pixels.</param>
    /// <param name="edgeScale">The draw scale at the far edge.</param>
    /// <param name="slope">The row-fan slope. The warp raises it to <see cref="MinSlope"/> or above.</param>
    /// <param name="center">The cross-axis coordinate of the row that the fan leaves in place.</param>
    /// <returns><see langword="true"/> when the map, the slope or the center changed.</returns>
    public bool Layout(int seam, int direction, int zoneWidth, int extension, double edgeScale, double slope, double center) =>
        Apply(seam, direction, zoneWidth, extension, edgeScale, slope, center, terrace: false, shelfWidth: 0);

    /// <summary>
    /// Lays out a terrace: a slope with a fixed exponent, then a flat shelf at a uniform scale.
    /// </summary>
    /// <param name="seam">The seam coordinate.</param>
    /// <param name="direction">-1 or 1, the side of the seam that the zone is on.</param>
    /// <param name="zoneWidth">The slope width on screen in pixels.</param>
    /// <param name="shelfWidth">The shelf width on screen in pixels.</param>
    /// <param name="shelfScale">The scale of the shelf. The method clamps it to the range from 0.01 to 1.</param>
    /// <param name="exponent">The curve exponent that sets the extension.</param>
    /// <param name="slope">The bow of the rows. The warp raises it so that the fan stays at <see cref="MinTerraceFan"/> or above.</param>
    /// <param name="center">The cross-axis coordinate of the row that the fan leaves in place.</param>
    /// <returns><see langword="true"/> when the map, the slope or the center changed.</returns>
    public bool LayoutTerrace(
        int seam, int direction, int zoneWidth, int shelfWidth, double shelfScale, double exponent, double slope, double center)
    {
        shelfScale = Math.Clamp(shelfScale, 0.01, 1.0);
        var extension = TerraceExtension(Math.Max(0, zoneWidth), shelfScale, exponent);
        return Apply(seam, direction, zoneWidth, extension, shelfScale, slope, center, terrace: true, Math.Max(0, shelfWidth));
    }

    private bool Apply(
        int seam, int direction, int zoneWidth, int extension, double edgeScale, double slope, double center, bool terrace, int shelfWidth)
    {
        direction = direction < 0 ? -1 : 1;
        zoneWidth = Math.Max(0, zoneWidth);
        extension = Math.Max(zoneWidth, extension);
        if (zoneWidth <= 0 || extension <= 0)
        {
            zoneWidth = 0;
            extension = 0;
            edgeScale = 1.0;
        }
        else
        {
            edgeScale = ClampEdgeScale(edgeScale, zoneWidth, extension);
        }

        if (zoneWidth <= 0)
        {
            terrace = false;
            shelfWidth = 0;
        }

        if (seam == _seam && direction == _direction && zoneWidth == _zoneWidth &&
            extension == _extension && edgeScale == _edgeScale &&
            terrace == _terrace && shelfWidth == _shelfWidth)
        {
            var moved = SetSlope(slope);
            return SetCenter(center) | moved;
        }

        _seam = seam;
        _direction = direction;
        _zoneWidth = zoneWidth;
        _extension = extension;
        _edgeScale = edgeScale;
        _center = center;
        _terrace = terrace;
        _shelfWidth = shelfWidth;
        _slopeFloor = zoneWidth > 0 && terrace ? Math.Max(MinSlope, TerraceSlopeFloor(zoneWidth, edgeScale)) : MinSlope;
        _slope = double.NaN;
        SetSlope(slope);
        if (IsIdentity)
        {
            _exponent = 0;
            return true;
        }

        var ratio = (double)zoneWidth / extension;
        _exponent = ((1.0 - edgeScale) / (ratio - edgeScale)) - 1.0;
        if (_table.Length < zoneWidth + 1)
        {
            _table = new double[zoneWidth + 1];
        }

        _table[0] = 0;
        _table[zoneWidth] = 1;
        for (var i = 1; i < zoneWidth; i++)
        {
            _table[i] = Solve(i);
        }

        return true;
    }

    /// <summary>
    /// Moves the row that the fan leaves in place, without a new solve.
    /// </summary>
    /// <param name="center">The new cross-axis coordinate.</param>
    /// <returns><see langword="true"/> when the value changed.</returns>
    public bool SetCenter(double center)
    {
        if (center == _center)
        {
            return false;
        }

        _center = center;
        return true;
    }

    /// <summary>
    /// Changes the row-fan slope without a new solve. It applies the same floors as the layout.
    /// </summary>
    /// <param name="slope">The new slope. The identity always keeps 0.</param>
    /// <returns><see langword="true"/> when the slope in force changed.</returns>
    public bool SetSlope(double slope)
    {
        slope = _zoneWidth <= 0 ? 0.0 : Math.Max(_slopeFloor, slope);
        if (slope == _slope)
        {
            return false;
        }

        _slope = slope;
        if (IsIdentity)
        {
            _minFan = 1.0;
        }
        else
        {
            _minFan = _terrace ? TerraceMinFan(_zoneWidth, _edgeScale, slope) : Math.Min(1.0, 1.0 + slope);
        }

        return true;
    }

    /// <summary>
    /// Returns whether a canvas coordinate lies past the seam.
    /// </summary>
    /// <param name="canvasX">The canvas coordinate across the seam.</param>
    /// <returns><see langword="false"/> for the identity and on the seam itself.</returns>
    public bool ContainsCanvas(double canvasX) => !IsIdentity && (_direction * (canvasX - _seam)) > 0;

    /// <summary>
    /// Returns whether a screen coordinate lies past the seam.
    /// </summary>
    /// <param name="screenX">The screen coordinate across the seam.</param>
    /// <returns><see langword="false"/> for the identity and on the seam itself.</returns>
    public bool ContainsScreen(double screenX) => !IsIdentity && (_direction * (screenX - _seam)) > 0;

    /// <summary>
    /// Maps a canvas coordinate to the screen. Past the far edge the map continues at the edge scale.
    /// </summary>
    /// <param name="canvasX">The canvas coordinate across the seam.</param>
    /// <returns>The screen coordinate. It equals the input on the flat side of the seam.</returns>
    public double ToScreen(double canvasX)
    {
        if (IsIdentity)
        {
            return canvasX;
        }

        var u = _direction * (canvasX - _seam) / _extension;
        if (u <= 0)
        {
            return canvasX;
        }

        if (u >= 1)
        {
            return _seam + (_direction * (_zoneWidth + (_edgeScale * (u - 1.0) * _extension)));
        }

        return _seam + (_direction * Distance(u));
    }

    /// <summary>
    /// Maps a screen coordinate back to the canvas through the inverse table. Past the zone the inverse continues at the edge scale.
    /// </summary>
    /// <param name="screenX">The screen coordinate across the seam.</param>
    /// <returns>The canvas coordinate. It equals the input on the flat side of the seam.</returns>
    public double ToCanvas(double screenX)
    {
        if (IsIdentity)
        {
            return screenX;
        }

        var distance = _direction * (screenX - _seam);
        if (distance <= 0)
        {
            return screenX;
        }

        if (distance >= _zoneWidth)
        {
            return FarEdge + (_direction * (distance - _zoneWidth) / _edgeScale);
        }

        var index = (int)Math.Floor(distance);
        var fraction = distance - index;
        var u = _table[index] + ((_table[index + 1] - _table[index]) * fraction);
        return _seam + (_direction * u * _extension);
    }

    /// <summary>
    /// Returns the local draw scale across the seam at a canvas coordinate. It is the derivative of <see cref="ToScreen"/>.
    /// </summary>
    /// <param name="canvasX">The canvas coordinate across the seam.</param>
    /// <returns>1 on the flat side, the edge scale past the far edge, and the curve between.</returns>
    public double ScaleAt(double canvasX)
    {
        if (IsIdentity)
        {
            return 1.0;
        }

        var u = _direction * (canvasX - _seam) / _extension;
        if (u <= 0)
        {
            return 1.0;
        }

        if (u >= 1)
        {
            return _edgeScale;
        }

        return _edgeScale + ((1.0 - _edgeScale) * Math.Pow(1.0 - u, _exponent));
    }

    /// <summary>
    /// Returns the row fan at a canvas coordinate. A row at distance d from <see cref="Center"/> draws at distance d times the fan.
    /// </summary>
    /// <param name="canvasX">The canvas coordinate across the seam.</param>
    /// <returns>1 on the flat side and with no slope. For a terrace, the shelf scale past the far edge.</returns>
    public double FanAt(double canvasX)
    {
        if (IsIdentity || (_slope == 0 && !_terrace))
        {
            return 1.0;
        }

        var u = _direction * (canvasX - _seam) / _extension;
        if (u <= 0)
        {
            return 1.0;
        }

        if (u >= 1)
        {
            return _terrace ? _edgeScale : 1.0 + _slope;
        }

        var t = Distance(u) / _zoneWidth;
        return _terrace ? TerraceFan(t, _edgeScale, _slope) : 1.0 + (_slope * SmoothStep(t));
    }

    /// <summary>
    /// Returns the row fan at a screen coordinate.
    /// </summary>
    /// <param name="screenX">The screen coordinate across the seam.</param>
    /// <returns>1 on the flat side and with no slope. For a terrace, the shelf scale past the zone.</returns>
    public double FanAtScreen(double screenX)
    {
        if (IsIdentity || (_slope == 0 && !_terrace))
        {
            return 1.0;
        }

        var distance = _direction * (screenX - _seam);
        if (distance <= 0)
        {
            return 1.0;
        }

        if (distance >= _zoneWidth)
        {
            return _terrace ? _edgeScale : 1.0 + _slope;
        }

        var t = distance / _zoneWidth;
        return _terrace ? TerraceFan(t, _edgeScale, _slope) : 1.0 + (_slope * SmoothStep(t));
    }

    /// <summary>
    /// Returns the row fan of a terrace slope at a fraction of the slope width.
    /// </summary>
    /// <param name="t">The screen fraction across the slope, 0 at the seam and 1 at the foot.</param>
    /// <param name="shelfScale">The shelf scale, which is the fan at the foot.</param>
    /// <param name="slope">The bow of the rows at the middle of the slope.</param>
    /// <returns>1 at the seam, <paramref name="shelfScale"/> at the foot, and a smooth blend with a bump between.</returns>
    public static double TerraceFan(double t, double shelfScale, double slope)
    {
        if (t <= 0)
        {
            return 1.0;
        }

        if (t >= 1)
        {
            return shelfScale;
        }

        var bump = 16.0 * t * t * (1.0 - t) * (1.0 - t);
        return 1.0 + ((shelfScale - 1.0) * SmoothStep(t)) + (slope * bump);
    }

    private static double TerraceSlopeFloor(int zoneWidth, double shelfScale)
    {
        var floor = double.NegativeInfinity;
        for (var i = 1; i < zoneWidth; i++)
        {
            var t = (double)i / zoneWidth;
            var bump = 16.0 * t * t * (1.0 - t) * (1.0 - t);
            if (bump <= 0)
            {
                continue;
            }

            var plain = 1.0 + ((shelfScale - 1.0) * SmoothStep(t));
            floor = Math.Max(floor, (MinTerraceFan - plain) / bump);
        }

        return floor;
    }

    private static double TerraceMinFan(int zoneWidth, double shelfScale, double slope)
    {
        var least = Math.Min(1.0, shelfScale);
        for (var i = 1; i < zoneWidth; i++)
        {
            least = Math.Min(least, TerraceFan((double)i / zoneWidth, shelfScale, slope));
        }

        return least;
    }

    /// <summary>
    /// Returns the screen depth of a canvas coordinate as a fraction of the zone width.
    /// </summary>
    /// <param name="canvas">The canvas coordinate across the seam.</param>
    /// <returns>0 on the flat side or for the identity, 1 at the far edge and past it.</returns>
    public double DepthAt(double canvas)
    {
        if (IsIdentity)
        {
            return 0.0;
        }

        var u = _direction * (canvas - _seam) / _extension;
        if (u <= 0)
        {
            return 0.0;
        }

        return u >= 1 ? 1.0 : Distance(u) / _zoneWidth;
    }

    /// <summary>
    /// Returns the depth of a screen coordinate as a fraction of the zone width.
    /// </summary>
    /// <param name="screen">The screen coordinate across the seam.</param>
    /// <returns>A value clamped to the range from 0 to 1. It is 0 for the identity.</returns>
    public double DepthAtScreen(double screen)
    {
        if (IsIdentity)
        {
            return 0.0;
        }

        return Math.Clamp(_direction * (screen - _seam) / _zoneWidth, 0.0, 1.0);
    }

    /// <summary>
    /// Maps the cross-axis coordinate of a canvas point to the screen through the row fan.
    /// </summary>
    /// <param name="canvasX">The canvas coordinate across the seam.</param>
    /// <param name="canvasY">The canvas coordinate along the seam.</param>
    /// <returns>The screen coordinate along the seam.</returns>
    public double ToScreenY(double canvasX, double canvasY) => _center + ((canvasY - _center) * FanAt(canvasX));

    /// <summary>
    /// Maps the cross-axis coordinate of a screen point back to the canvas. The fan depends on the screen coordinate across the seam alone, so the inverse is one division.
    /// </summary>
    /// <param name="screenX">The screen coordinate across the seam.</param>
    /// <param name="screenY">The screen coordinate along the seam.</param>
    /// <returns>The canvas coordinate along the seam.</returns>
    public double ToCanvasY(double screenX, double screenY) => _center + ((screenY - _center) / FanAtScreen(screenX));

    private static double SmoothStep(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return t * t * (3.0 - (2.0 * t));
    }

    private double Distance(double u)
    {
        var e = _edgeScale;
        var p1 = _exponent + 1.0;
        return _extension * ((e * u) + ((1.0 - e) * (1.0 - Math.Pow(1.0 - u, p1)) / p1));
    }

    private double Solve(double distance)
    {
        var low = 0.0;
        var high = 1.0;
        for (var i = 0; i < 48; i++)
        {
            var middle = 0.5 * (low + high);
            if (Distance(middle) < distance)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return 0.5 * (low + high);
    }
}
