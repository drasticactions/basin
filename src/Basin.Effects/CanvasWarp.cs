namespace Basin.Effects;

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

    public const double MinSlope = -0.9;

    public const double TerraceExponent = 2.0;

    public const double MinTerraceFan = 0.1;

    public CanvasWarp(int direction = -1)
    {
        _direction = direction < 0 ? -1 : 1;
    }

    public int Seam => _seam;

    public int Direction => _direction;

    public int ZoneWidth => _zoneWidth;

    public int Extension => _extension;

    public double EdgeScale => _edgeScale;

    public double Exponent => _exponent;

    public double Slope => _slope;

    public double Center => _center;

    public bool Terrace => _terrace;

    public int ShelfWidth => _shelfWidth;

    public double MinFan => _minFan;

    public bool IsIdentity => _zoneWidth <= 0 || _extension <= 0;

    public int FarEdge => _seam + (_direction * _extension);

    public int ScreenEdge => _seam + (_direction * _zoneWidth);

    public int Foot => ScreenEdge;

    public int OuterEdge => ScreenEdge + (_direction * _shelfWidth);

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

    public bool Layout(int seam, int direction, int zoneWidth, int extension, double edgeScale) =>
        Layout(seam, direction, zoneWidth, extension, edgeScale, 0.0, 0.0);

    public bool Layout(int seam, int direction, int zoneWidth, int extension, double edgeScale, double slope, double center) =>
        Apply(seam, direction, zoneWidth, extension, edgeScale, slope, center, terrace: false, shelfWidth: 0);

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
        slope = Math.Max(MinSlope, slope);
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
            slope = 0.0;
            terrace = false;
            shelfWidth = 0;
        }
        else if (terrace)
        {
            slope = Math.Max(slope, TerraceSlopeFloor(zoneWidth, edgeScale));
        }

        if (seam == _seam && direction == _direction && zoneWidth == _zoneWidth &&
            extension == _extension && edgeScale == _edgeScale && slope == _slope && center == _center &&
            terrace == _terrace && shelfWidth == _shelfWidth)
        {
            return false;
        }

        _seam = seam;
        _direction = direction;
        _zoneWidth = zoneWidth;
        _extension = extension;
        _edgeScale = edgeScale;
        _slope = slope;
        _center = center;
        _terrace = terrace;
        _shelfWidth = shelfWidth;
        _minFan = terrace ? TerraceMinFan(zoneWidth, edgeScale, slope) : Math.Min(1.0, 1.0 + slope);
        if (IsIdentity)
        {
            _exponent = 0;
            _minFan = 1.0;
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

    public bool ContainsCanvas(double canvasX) => !IsIdentity && (_direction * (canvasX - _seam)) > 0;

    public bool ContainsScreen(double screenX) => !IsIdentity && (_direction * (screenX - _seam)) > 0;

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

    public double DepthAtScreen(double screen)
    {
        if (IsIdentity)
        {
            return 0.0;
        }

        return Math.Clamp(_direction * (screen - _seam) / _zoneWidth, 0.0, 1.0);
    }

    public double ToScreenY(double canvasX, double canvasY) => _center + ((canvasY - _center) * FanAt(canvasX));

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
