using System.Numerics;
using Basin.Diagnostics;
using Basin.Scene3D.Gltf;

namespace DungeonWm;

internal sealed partial class DungeonWm
{
    private DungeonWmAvatar? _avatar;

    private void StartAvatar()
    {
        _avatar = LoadAvatar(_settings.AvatarModel) ?? LoadAvatar(null);
        if (_avatar is not null)
        {
            _world.World.Avatar = _avatar.Avatar;
            BasinReport.Line($"AVATAR joints={_avatar.Model.Skeleton.Joints.Length} clips={_avatar.Model.Clips.Count} triangles={_avatar.Model.Mesh.TriangleCount}");
        }
    }

    private DungeonWmAvatar? LoadAvatar(string? path)
    {
        try
        {
            var bytes = path is { Length: > 0 } ? File.ReadAllBytes(path) : Kit.ReadResource(KitManifest.Avatar);
            var model = GlbAvatar.Load(bytes, _settings.AvatarSampleHz, _settings.AvatarShow, Kit.DecodeImage);
            return new DungeonWmAvatar(model, _settings, message => BasinReport.Line($"AVATAR {message}"));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException or System.Text.Json.JsonException)
        {
            _log.Warn(error, $"avatar: cannot load {(path is { Length: > 0 } ? path : "the built-in avatar")}");
            BasinReport.Line($"AVATAR {(path is { Length: > 0 } ? path : "built-in")} failed: {error.Message}");
            return null;
        }
    }

    private void UpdateAvatar(float seconds)
    {
        if (_avatar is not { } avatar || _physics is not { } physics)
        {
            return;
        }

        var center = physics.Player.Body.Pose.Position;
        var feet = center - new Vector3(0, DungeonWmPhysics.PlayerHeight * 0.5f, 0);
        var velocity = physics.Player.Velocity;
        var speed = new Vector2(velocity.X, velocity.Z).Length();
        avatar.Update(seconds, feet, _camera.Yaw + MathF.PI, speed, _walker.Run, physics.Player.Supported);
    }

    private bool AvatarSeen()
    {
        if (_avatar is null)
        {
            return false;
        }

        foreach (var output in _worldOutputs.Values)
        {
            if (output.Visible.Portals.Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    private partial void OnInteract(DungeonWmWindow window) => _avatar?.Trigger("interact");

    private partial void OnJump()
    {
        _avatar?.Jumped();
        SoundJump();
    }

    private partial void OnLand()
    {
        _avatar?.Landed();
        SoundLand();
    }

    private partial void OnPickUp(DungeonWmWindow window) => _avatar?.Trigger("pick-up");

    private partial void OnThrow(DungeonWmWindow window) => _avatar?.Trigger("throw");
}
