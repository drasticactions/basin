using Basin;
using Basin.Scene;

namespace Westonia;

internal sealed partial class Westonia
{
    private readonly List<SceneSurfaceBox> _presence = [];

    private void UpdateSurfacePresence()
    {
        if (_fractionalScale is null)
        {
            return;
        }

        _scene.CollectSurfaces(_presence);
        foreach (var (surface, box) in _presence)
        {
            var preferred = 1.0;
            var onAnyOutput = false;
            foreach (var view in _outputs.Views)
            {
                var outputBox = _layout.BoxOf(view.Output);
                var overlaps = box.X < outputBox.Right && box.Right > outputBox.X &&
                               box.Y < outputBox.Bottom && box.Bottom > outputBox.Y;
                surface.SetOutputPresence(view.Global, overlaps);
                if (overlaps)
                {
                    onAnyOutput = true;
                    preferred = Math.Max(preferred, view.Output.Scale);
                }
            }

            if (onAnyOutput)
            {
                _fractionalScale.AnnounceScale(surface, preferred);
            }
        }

        _presence.Clear();
    }
}
