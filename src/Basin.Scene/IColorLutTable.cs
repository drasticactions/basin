namespace Basin.Scene;

public interface IColorLutTable
{
    IColorLut? LutFor(SceneBuffer node);
}
