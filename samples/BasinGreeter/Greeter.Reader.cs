namespace BasinGreeter;

internal sealed partial class Greeter
{
    private void WireReader()
    {
        var model = _model.Accessibility;
        var reader = _greeter.Reader!;
        model.ReaderToggled += on =>
        {
            if (on)
            {
                StartReader();
            }
            else
            {
                StopReader();
            }
        };
        reader.Changed += () =>
        {
            model.ReaderAvailable = reader.IsAvailable;
            model.ShowReader(reader.IsRunning);
        };
        model.ReaderAvailable = reader.IsAvailable;
    }

    internal void StartReader() => _greeter.Reader?.Start();

    internal void StopReader() => _greeter.Reader?.Stop();
}
