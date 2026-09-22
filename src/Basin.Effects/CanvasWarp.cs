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

    public const double MinSlope = -0.9;

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

    public bool IsIdentity => _zoneWidth <= 0 || _extension <= 0;

    public int FarEdge => _seam + (_direction * _extension);

    public int ScreenEdge => _seam + (_direction * _zoneWidth);

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

    public bool Layout(int seam, int direction, int zoneWidth, int extension, double edgeScale, double slope, double center)
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
        }

        if (seam == _seam && direction == _direction && zoneWidth == _zoneWidth &&
            extension == _extension && edgeScale == _edgeScale && slope == _slope && center == _center)
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
            return _seam + (_direction * _zoneWidth);
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
            return FarEdge;
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
        if (IsIdentity || _slope == 0)
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
            return 1.0 + _slope;
        }

        return 1.0 + (_slope * SmoothStep(Distance(u) / _zoneWidth));
    }

    public double FanAtScreen(double screenX)
    {
        if (IsIdentity || _slope == 0)
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
            return 1.0 + _slope;
        }

        return 1.0 + (_slope * SmoothStep(distance / _zoneWidth));
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
