using Avalonia.Media;
using Basin;
using Basin.Host;
using Basin.Scene;

namespace EightWm;

internal sealed partial class Shell
{
    private sealed class SignOutOutro : IHandoffOutro, IDisposable
    {
        private readonly Shell _shell;
        private readonly Dictionary<ShellView, Slide> _slides = [];

        public SignOutOutro(Shell shell)
        {
            _shell = shell;
        }

        public TimeSpan Duration { get; } = TimeSpan.FromMilliseconds(350);

        public bool Ready
        {
            get
            {
                foreach (var slide in _slides.Values)
                {
                    if (slide.Chrome.Node.Buffer is null)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public void Begin(OutputView view)
        {
            var shell = ViewOf(view);
            if (_slides.ContainsKey(shell))
            {
                return;
            }

            if (_slides.Count == 0 && Avalonia.Application.Current is { } app)
            {
                SignInTheme.Install(app, Color.FromUInt32(_shell.AccentNow));
            }

            var box = shell.Box;
            var holder = new SceneTree(_shell._scene.Root) { ClipBox = new Box(0, 0, box.Width, box.Height) };
            holder.SetPosition(box.X, box.Y);
            var sheet = new SceneTransform(holder) { Matrix = RenderTransform.Translation(box.Width, 0) };
            var chrome = new AvaloniaChrome(sheet, _shell._ui, _shell._chromeIndex, new SignOutView()) { Enabled = true, InputEnabled = false };
            chrome.Place(new Box(0, 0, box.Width, box.Height), shell.Scale);
            holder.RaiseToTop();
            _slides[shell] = new Slide(holder, sheet, chrome, box.Width);
            _shell._report.Line($"OUTRO sign-out {view.Output.Name}");
        }

        public void Step(OutputView view, double progress)
        {
            if (_slides.TryGetValue(ViewOf(view), out var slide))
            {
                var eased = CrossfadeTransition.OutCubic(Math.Clamp(progress, 0, 1));
                slide.Sheet.Matrix = RenderTransform.Translation(slide.Width * (1 - eased), 0);
            }
        }

        public void End(OutputView view)
        {
            if (_slides.TryGetValue(ViewOf(view), out var slide))
            {
                slide.Sheet.Matrix = RenderTransform.Identity;
            }
        }

        public void Dispose()
        {
            foreach (var slide in _slides.Values)
            {
                slide.Chrome.Dispose();
                slide.Holder.Destroy();
            }

            _slides.Clear();
        }

        private sealed record Slide(SceneTree Holder, SceneTransform Sheet, AvaloniaChrome Chrome, int Width);
    }
}
