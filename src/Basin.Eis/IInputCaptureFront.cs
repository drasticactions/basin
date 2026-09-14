namespace Basin.Eis;

public interface IInputCaptureFront
{
    void Activated(uint activationId, double x, double y, uint barrierId);

    void Deactivated(uint activationId);

    void Disabled();

    void ZonesChanged();
}
