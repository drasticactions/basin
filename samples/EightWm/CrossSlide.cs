namespace EightWm;

internal struct CrossSlide
{
    private double _origin;

    public CrossSlide()
    {
    }

    public double SelectThreshold { get; set; } = 40;

    public double DetachThreshold { get; set; } = 110;

    public CrossSlideStage Stage { get; private set; }

    public double Travel { get; private set; }

    public Tile? Tile { get; private set; }

    public readonly bool IsActive => Stage != CrossSlideStage.None;

    public void Begin(Tile tile, double crossAxis)
    {
        Tile = tile;
        _origin = crossAxis;
        Travel = 0;
        Stage = CrossSlideStage.Started;
    }

    public CrossSlideStage Update(double crossAxis)
    {
        if (Stage == CrossSlideStage.None)
        {
            return Stage;
        }

        Travel = crossAxis - _origin;
        var distance = Math.Abs(Travel);
        Stage = distance >= DetachThreshold
            ? CrossSlideStage.Detached
            : distance >= SelectThreshold
                ? CrossSlideStage.Selected
                : CrossSlideStage.Started;
        return Stage;
    }

    public CrossSlideStage Release()
    {
        var stage = Stage;
        Stage = CrossSlideStage.None;
        Tile = null;
        Travel = 0;
        return stage;
    }

    public void Abort()
    {
        Stage = CrossSlideStage.None;
        Tile = null;
        Travel = 0;
    }
}
