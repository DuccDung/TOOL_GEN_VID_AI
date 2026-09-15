namespace TOOL_LOCAL.Vietsub.Domain;

internal sealed class VietsubVideoTransformSettings
{
    public bool FlipHorizontal { get; set; }

    public bool FlipVertical { get; set; }

    public static VietsubVideoTransformSettings CreateDefault() => new();

    public VietsubVideoTransformSettings Copy() => new()
    {
        FlipHorizontal = FlipHorizontal,
        FlipVertical = FlipVertical
    };
}
