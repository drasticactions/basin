using System.Numerics;
using Basin.Scene3D;

namespace DungeonWm;

internal sealed class DungeonWmSettings
{
    public MapSettings Map { get; set; } = new();

    public BakeSettings Bake { get; set; } = new();

    public WorldRenderSettings Render { get; } = new();

    public string Renderer { get; set; } = "vulkan";

    public Basin.Config.HandoffSettings Handoff { get; set; } = new();

    public bool XWaylandLowDensity { get; set; } = true;

    public Basin.Config.LaunchRules Launch { get; set; } = Basin.Config.LaunchRules.Empty;

    public int Rooms { get; set; } = 4;

    public Vector3 SkyColor { get; set; } = new(0.03f, 0.03f, 0.05f);

    public float PixelsPerMeter { get; set; } = 1000;

    public float FieldOfView { get; set; } = 70;

    public float Near { get; set; } = 0.05f;

    public float Far { get; set; } = 120;

    public float LookSensitivity { get; set; } = 0.0035f;

    public string? CrosshairOutput { get; set; }

    public float Reach { get; set; } = 4;

    public Vector4 ReticleColor { get; set; } = new(1, 1, 1, 230f / 255);

    public int ReticleSize { get; set; } = 24;

    public float WalkSpeed { get; set; } = 3.2f;

    public float RunSpeed { get; set; } = 6.0f;

    public float EyeHeight { get; set; } = 1.6f;

    public float WindowDepth { get; set; } = 0.02f;

    public float HangHeight { get; set; } = 1.6f;

    public Vector4 EdgeColor { get; set; } = new(0.32f, 0.27f, 0.22f, 1);

    public Vector4 FocusColor { get; set; } = new(0.95f, 0.72f, 0.32f, 1);

    public float UnfocusedOpacity { get; set; } = 1;

    public int OpenMilliseconds { get; set; } = 600;

    public int CloseMilliseconds { get; set; } = 600;

    public Basin.Scene.SceneFitMode? TakeoverScaler { get; set; }

    public Basin.TextureFilter? TakeoverFilter { get; set; }

    public int TakeoverFrameLimit { get; set; }

    public FrameSettings Frames { get; set; } = new();

    public float Gravity { get; set; } = 9.81f;

    public float PullDistance { get; set; } = 0.15f;

    public float RehangDistance { get; set; } = 0.6f;

    public float ThrowScale { get; set; } = 1;

    public float ThrowMinSpeed { get; set; } = 0.6f;

    public float PropDensity { get; set; } = 60;

    public AudioSettings Audio { get; set; } = new();

    public SoundSettings Sounds { get; set; } = new();

    public string? AvatarModel { get; set; }

    public float AvatarScale { get; set; } = 0.75f;

    public string[] AvatarShow { get; set; } = ["Rogue_Cape"];

    public int AvatarSampleHz { get; set; } = 30;

    public Dictionary<string, string> AvatarClips { get; } = new()
    {
        ["idle"] = "Idle",
        ["walk"] = "Walking_A",
        ["run"] = "Running_A",
        ["jump-start"] = "Jump_Start",
        ["airborne"] = "Jump_Idle",
        ["land"] = "Jump_Land",
        ["pick-up"] = "PickUp",
        ["throw"] = "Throw",
        ["interact"] = "Interact",
    };

    public Dictionary<string, object> Bindings { get; } = new(StringComparer.Ordinal)
    {
        ["Super+Escape"] = "leave-takeover",
        ["Super+Tab"] = "next-room",
        ["Super+F"] = "takeover",
        ["Super+Q"] = "close",
        ["Super+Return"] = "spawn fuzzel",
        ["E"] = "use",
        ["Super+E"] = "release",
        ["R"] = "rotate",
        ["Shift+R"] = "square",
        ["Super+F12"] = "inspector",
        ["Super+Alt+S"] = "screen-reader",
        ["Super+Alt+8"] = "magnifier",
        ["Super+Alt+equal"] = "magnifier-in",
        ["Super+Alt+minus"] = "magnifier-out",
        ["Super+Alt+C"] = "screen-curtain",
    };

    public Basin.Config.AccessibilitySettings Accessibility { get; set; } = new();

    public static IReadOnlyList<string> InspectorPanelNames { get; } =
        ["outputs", "windows", "clients", "scene", "memory", "physics", "camera", "rooms", "audio", "pose"];

    public bool InspectorEnabled { get; set; } = true;

    public bool InspectorOpen { get; set; }

    public string? InspectorOutput { get; set; }

    public IReadOnlyList<string> InspectorPanels { get; set; } = ["camera", "physics"];

    public double InspectorFontSize { get; set; } = 15;

    public int InspectorRefreshHz { get; set; } = 10;

    public float FieldOfViewRadians => FieldOfView * MathF.PI / 180;
}
