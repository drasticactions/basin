namespace Basin.Capabilities;

public interface ILayerDirectory
{
    void CollectLayers(List<LayerInfo> into);
}
