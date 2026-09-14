namespace Basin.Capabilities;

[Flags]
public enum PromptSourceKinds
{
    None = 0,

    Monitor = 1,

    Window = 2,

    Virtual = 4,
}
