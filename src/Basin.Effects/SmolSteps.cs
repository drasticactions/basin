namespace Basin.Effects;

internal struct SmolSteps
{
    private readonly SmolAxis _x;
    private readonly SmolAxis _y;
    private readonly int _count;
    private int _index;
    private int _width;
    private int _height;

    public SmolSteps(in SmolAxis x, in SmolAxis y)
    {
        _x = x;
        _y = y;
        _count = Math.Max(x.Level, y.Level);
        _width = x.Size;
        _height = y.Size;
    }

    public bool Next(out int levelX, out int levelY, out bool fused)
    {
        levelX = 0;
        levelY = 0;
        fused = false;
        if (_index >= _count)
        {
            return false;
        }

        var fromX = Math.Min(_index, _x.Level);
        var fromY = Math.Min(_index, _y.Level);
        if (_index + 2 <= _count)
        {
            var toX = Math.Min(_index + 2, _x.Level);
            var toY = Math.Min(_index + 2, _y.Level);
            var dx = toX - fromX;
            var dy = toY - fromY;
            fused = dx is 0 or 2 && dy is 0 or 2
                && _width == SmolAxis.SizeAt(_x.Size, toX) << dx
                && _height == SmolAxis.SizeAt(_y.Size, toY) << dy;
        }

        _index += fused ? 2 : 1;
        levelX = Math.Min(_index, _x.Level);
        levelY = Math.Min(_index, _y.Level);
        _width = SmolAxis.SizeAt(_x.Size, levelX);
        _height = SmolAxis.SizeAt(_y.Size, levelY);
        return true;
    }
}
