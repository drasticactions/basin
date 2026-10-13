using Basin;
using Basin.Capabilities;
using Basin.Scene;
using Basin.Shell.Xdg;

namespace LunaWm;

internal sealed class Card : ILayoutCard, IDisposable
{
    private readonly CardScene _parts;
    private IPixelShader? _shader;
    private Motion _motion;
    private CardPose _from;
    private double _shaderRadius = -1;
    private double _shaderBrightness = -1;
    private SceneSnapshot? _flight;
    private bool _disposePending;

    public Card(IToplevelHandle handle, XdgToplevelWindow? xdg, Basin.XWayland.XWaylandWindow? x11, CardScene parts, SceneSurface scene)
    {
        Handle = handle;
        Xdg = xdg;
        X11 = x11;
        _parts = parts;
        Scene = scene;
        Id = ++_ids;
    }

    private static int _ids;

    public int Id { get; }

    public IToplevelHandle Handle { get; }

    public XdgToplevelWindow? Xdg { get; }

    public Basin.XWayland.XWaylandWindow? X11 { get; }

    public SceneSurface Scene { get; }

    public SceneTree Slot => _parts.Slot;

    public SceneTransform Frame => _parts.Frame;

    public SceneTree Content => _parts.Content;

    public SceneTree Transients => _parts.Transients;

    public CardShadow? Shadow => _parts.Shadow;

    public Surface? Surface => Handle.Surface;

    public string Title => Handle.Title;

    public string AppId => Handle.AppId;

    public string Label => Title.Length > 0 ? Title : AppId;

    private Basin.ProcessIdentity? _process;

    public Basin.Config.WindowFacts Facts => X11 is { } x11
        ? Basin.Config.WindowFacts.ForX11(x11.Class, x11.Instance, x11.Title, x11.SteamGame, x11.WindowType, x11.Process)
        : Basin.Config.WindowFacts.ForWayland(
            AppId, Title, Xdg?.Parent is not null, _process ??= Surface is { } surface ? Basin.ProcessIdentity.Of(surface) : default);

    public LunaView? Home { get; set; }

    public Card? Parent { get; set; }

    public List<Card> Children { get; } = [];

    public bool IsTransient => Parent is not null;

    public bool Mapped { get; set; }

    public bool Closing { get; set; }

    public bool Fullscreen { get; set; }

    public bool ShellLaunched { get; set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public CardPose Target { get; set; }

    public CardPose Current { get; private set; }

    public CardPose PoseA { get; set; }

    public CardPose PoseB { get; set; }

    public Basin.Kinematics.SpringHandoff Spring;

    private long _springMillis;

    public bool Springing => Spring.IsMoving;

    public bool Animating => _motion.IsRunning || Spring.IsMoving;

    public double Alpha { get; set; } = 1;

    public long FocusStamp { get; set; }

    public Basin.AppMenu.AppMenuImport? MenuImport { get; set; }

    public Basin.AppMenu.LocalAppMenu? LocalMenu { get; set; }

    public int Pid
    {
        get
        {
            if (X11 is not null || Surface is not { IsDestroyed: false } surface)
            {
                return 0;
            }

            try
            {
                return surface.Resource.Client.Credentials.Pid;
            }
            catch (NotSupportedException)
            {
                return 0;
            }
        }
    }

    public void Resize(int width, int height, int x, int y)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Content.ClipBox = new Box(0, 0, Width, Height);
        Shadow?.Place(Width, Height);
        _parts.Size(Width, Height);
        Handle.Configure(x, y, Width, Height);
    }

    public void SetScrim(bool shown) => _parts.ScrimTree.Enabled = shown;

    public void UseShader(IRenderer renderer)
    {
        if (_shader is not null || Scene.IsDestroyed)
        {
            return;
        }

        _shader = renderer.CompilePixelShader(CardShader.Source, CardShader.Uniforms);
        if (_shader is not null)
        {
            Scene.Content.TextureShader = _shader;
        }
    }

    public void Jump(in CardPose pose)
    {
        Spring.Stop();
        Target = pose;
        Current = pose;
        _motion.Jump(1);
        Apply();
    }

    public void AnimateTo(in CardPose pose, in MotionSpec spec, long nowMillis, double timeScale)
    {
        Target = pose;
        _from = Current;
        _motion.Start(spec, 0, 1, nowMillis, timeScale);
        if (!_motion.IsRunning)
        {
            Current = pose;
        }

        Apply();
    }

    public void Follow(in CardPose pose)
    {
        Spring.Stop();
        Target = pose;
        Current = pose;
        _motion.Jump(1);
        Apply();
    }

    public void SpringTo(in CardPose pose, double velocityX, double velocityY, long nowMillis)
    {
        Target = pose;
        _motion.Jump(1);
        Spring.Start(Current.X, Current.Y, velocityX / 1000, velocityY / 1000, pose.X, pose.Y);
        _springMillis = nowMillis;
        Current = pose with { X = Current.X, Y = Current.Y };
        Apply();
    }

    public bool Advance(long nowMillis)
    {
        if (Spring.IsMoving)
        {
            Spring.Advance((nowMillis - _springMillis) * 1_000_000);
            _springMillis = nowMillis;
            Current = Target with { X = Spring.X, Y = Spring.Y };
            if (!Spring.IsMoving)
            {
                Current = Target;
            }

            Apply();
            return true;
        }

        if (!_motion.Advance(nowMillis))
        {
            return false;
        }

        Current = CardPose.Lerp(_from, Target, _motion.Value);
        Apply();
        return true;
    }

    public void Settle()
    {
        if (Spring.IsMoving)
        {
            Spring.Stop();
            Current = Target;
            Apply();
            return;
        }

        if (!_motion.IsRunning)
        {
            return;
        }

        _motion.Settle();
        Current = Target;
        Apply();
    }

    public void Apply()
    {
        if (Frame.IsDestroyed)
        {
            return;
        }

        var pose = Current;
        var matrix = RenderTransform.Multiply(
            RenderTransform.Translation(pose.X, pose.Y),
            RenderTransform.Multiply(
                RenderTransform.RotationAbout(pose.Rotation * Math.PI / 180, 0, 0),
                RenderTransform.Multiply(
                    RenderTransform.Scale(pose.Scale, pose.Scale),
                    RenderTransform.Translation(-Width / 2.0, -Height / 2.0))));
        Frame.Matrix = matrix;
        Frame.Alpha = (float)Math.Clamp(Alpha, 0, 1);
        ApplyBrightness(pose.Brightness);
    }

    private void ApplyBrightness(double brightness)
    {
        if (_shader is not null)
        {
            var radius = CardShader.Radius * (Home?.Scale ?? 1);
            if (Math.Abs(radius - _shaderRadius) > 0.001 || Math.Abs(brightness - _shaderBrightness) > 0.001)
            {
                _shaderRadius = radius;
                _shaderBrightness = brightness;
                _shader.SetUniforms([(float)radius, (float)brightness]);
                Scene.Content.NotifyContentChanged();
            }

            return;
        }

        _parts.Dim.Enabled = brightness < 0.999;
        _parts.Dim.Alpha = (float)Math.Clamp(1 - brightness, 0, 1);
    }

    public void RequestClose() => Handle.Close();

    public bool Flying => _flight is not null;

    public void BeginFlight()
    {
        if (_flight is not null || Content.IsDestroyed)
        {
            return;
        }

        _flight = SceneSnapshot.Capture(Content, Frame);
        Content.Enabled = false;
    }

    public bool EndFlight()
    {
        if (_flight is null)
        {
            return Slot.IsDestroyed;
        }

        _flight.Destroy();
        _flight = null;
        if (_disposePending)
        {
            Dispose();
            return true;
        }

        if (!Content.IsDestroyed)
        {
            Content.Enabled = true;
        }

        return false;
    }

    public void Dispose()
    {
        if (_flight is not null)
        {
            _disposePending = true;
            return;
        }

        MenuImport?.Dispose();
        MenuImport = null;
        _shader?.Dispose();
        _shader = null;
        if (!Slot.IsDestroyed)
        {
            Slot.Destroy();
        }
    }
}
