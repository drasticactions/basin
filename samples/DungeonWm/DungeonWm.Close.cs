using System.Numerics;
using Basin;
using Basin.Diagnostics;
using Basin.Scene3D;

namespace DungeonWm;

internal sealed partial class DungeonWm
{
    private const float FallCapSeconds = 2;
    private const float FallRestSpeed = 0.08f;
    private const float FallRestAfter = 0.25f;
    private const float FallNudge = 0.5f;
    private const float FallTip = 1.2f;

    private readonly List<DungeonWmSnapshot> _snapshots = [];
    private bool _disposing;

    private DungeonWmSnapshot? TakeSnapshot(DungeonWmWindow window)
    {
        if (_disposing || _settings.CloseMilliseconds <= 0 || window.State is DungeonWmWindowState.Hidden or DungeonWmWindowState.Takeover)
        {
            return null;
        }

        SyncLayers(window);
        var source = window.World;
        var snapshot = new DungeonWmSnapshot(window.AppId)
        {
            Room = window.Room,
            FromWall = window.State != DungeonWmWindowState.Body,
        };
        var world = snapshot.World;
        world.Position = source.Position;
        world.Orientation = source.Orientation;
        world.Width = source.Width;
        world.Height = source.Height;
        world.Depth = source.Depth;
        world.SurfaceWidth = source.SurfaceWidth;
        world.SurfaceHeight = source.SurfaceHeight;
        world.EdgeColor = source.EdgeColor;
        world.FocusColor = source.FocusColor;
        world.Opacity = window.Fade;
        world.CanvasOpacity = window.CanvasFade;
        world.Room = source.Room;
        world.Frame = source.ShownFrame?.Clone();
        CopyCanvas(source, snapshot);
        if (snapshot.CanvasTexture is { } texture)
        {
            world.AddLayer(new WorldSurfaceLayer
            {
                Texture = texture,
                Width = world.SurfaceWidth,
                Height = world.SurfaceHeight,
                Source = new FBox(0, 0, world.SurfaceWidth, world.SurfaceHeight),
            });
        }

        return snapshot;
    }

    private void CopyCanvas(WorldWindow source, DungeonWmSnapshot snapshot)
    {
        var width = source.SurfaceWidth;
        var height = source.SurfaceHeight;
        var allocator = _deviceAllocator ?? (_outputs.Views.Count > 0 ? _outputs.Views[0].Allocator : null);
        if (source.LayerCount == 0 || allocator is null || width <= 0 || height <= 0)
        {
            return;
        }

        if (allocator.Allocate(width, height, DrmFormat.Argb8888, [DrmFormatSet.ModifierLinear], BufferUse.Render) is not { } canvas)
        {
            return;
        }

        var pass = _renderer.BeginBufferPass(canvas, new RenderPassOptions());
        var layers = source.Layers;
        var first = true;
        for (var i = 0; i < layers.Length; i++)
        {
            var layer = layers[i];
            if (layer.Texture is not { } texture || layer.Width <= 0 || layer.Height <= 0)
            {
                continue;
            }

            pass.AddTexture(texture, new TextureRenderOptions
            {
                SrcBox = layer.Source,
                DstBox = new Box((int)MathF.Round(layer.X), (int)MathF.Round(layer.Y), (int)MathF.Round(layer.Width), (int)MathF.Round(layer.Height)),
                Opaque = first,
            });
            first = false;
        }

        if (!first && pass.Submit() && _renderer.ImportTexture(canvas) is { } imported)
        {
            snapshot.Canvas = canvas;
            snapshot.CanvasTexture = imported;
            return;
        }

        if (first)
        {
            pass.Submit();
        }

        canvas.Destroy();
    }

    private void AdoptSnapshot(DungeonWmWindow window, DungeonWmSnapshot snapshot)
    {
        _plaques.Transfer(window, snapshot);
        if (_physics is { } physics && _pull is { } pull && ReferenceEquals(pull.Window, window) && pull.Detached)
        {
            physics.World.EndDrag(pull.Body, pull.Body.Velocity);
        }

        _snapshots.Add(snapshot);
        _world.World.Windows.Add(snapshot.World);
        if (snapshot.Room is { } room)
        {
            var worldRoom = _world.RoomOf(room);
            snapshot.World.Room = worldRoom;
            worldRoom.Windows.Add(snapshot.World);
        }

        if (_physics is not { } bodies)
        {
            return;
        }

        if (snapshot.FromWall || !bodies.Adopt(window, snapshot))
        {
            snapshot.FromWall = true;
            var body = bodies.Detach(snapshot);
            var world = snapshot.World;
            var tip = Vector3.Transform(Vector3.UnitX, world.Orientation) * FallTip * (window.Swing >= 0 ? 1 : -1);
            bodies.World.SetVelocity(body, world.Normal * FallNudge, tip);
            BasinReport.Line($"FALLING {snapshot.AppId}");
        }
    }

    private bool StepSnapshots(float seconds)
    {
        for (var i = _snapshots.Count - 1; i >= 0; i--)
        {
            var snapshot = _snapshots[i];
            snapshot.Elapsed += seconds;
            if (snapshot.Fading < 0)
            {
                if (Landed(snapshot))
                {
                    snapshot.Fading = 0;
                    snapshot.FadeFrom = snapshot.World.Opacity;
                    BasinReport.Line($"FADING {snapshot.AppId}");
                }

                continue;
            }

            snapshot.Fading += seconds;
            var progress = snapshot.Fading * 1000 / Math.Max(1, _settings.CloseMilliseconds);
            snapshot.World.Opacity = snapshot.FadeFrom * MathF.Max(0, 1 - progress);
            if (progress >= 1)
            {
                _snapshots.RemoveAt(i);
                DropSnapshot(snapshot);
                BasinReport.Line($"FADED {snapshot.AppId}");
            }
        }

        return _snapshots.Count > 0;
    }

    private bool Landed(DungeonWmSnapshot snapshot)
    {
        if (snapshot.Elapsed >= FallCapSeconds || _physics is not { } physics || !physics.SnapshotBodies.TryGetValue(snapshot, out var body))
        {
            return true;
        }

        foreach (var impact in physics.World.Impacts)
        {
            if (impact.A == body || impact.B == body)
            {
                return true;
            }
        }

        return body.Velocity.Length() < FallRestSpeed && (!snapshot.FromWall || snapshot.Elapsed >= FallRestAfter);
    }

    private void DropSnapshot(DungeonWmSnapshot snapshot)
    {
        AudioSnapshotDropped(snapshot);
        _physics?.Release(snapshot);
        _world.World.Windows.Remove(snapshot.World);
        snapshot.World.Room?.Windows.Remove(snapshot.World);
        _worldRenderer.Forget(snapshot.World);
        _plaques.Forget(snapshot);
        snapshot.World.ClearLayers();
        snapshot.CanvasTexture?.Dispose();
        snapshot.CanvasTexture = null;
        snapshot.Canvas?.Destroy();
        snapshot.Canvas = null;
    }

    private void DropSnapshots()
    {
        foreach (var snapshot in _snapshots)
        {
            DropSnapshot(snapshot);
        }

        _snapshots.Clear();
    }
}
