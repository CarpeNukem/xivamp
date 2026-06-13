using System.Numerics;

namespace xivAMP.Skin;

public sealed class GenSkinColors
{
    public Vector4 ItemBackground { get; set; } = new(0.08f, 0.08f, 0.13f, 1.0f);

    public Vector4 ItemForeground { get; set; } = new(0.0f, 1.0f, 0.18f, 1.0f);

    public Vector4 WindowBackground { get; set; } = new(0.0f, 0.0f, 0.0f, 1.0f);

    public Vector4 ButtonText { get; set; } = new(0.0f, 1.0f, 0.18f, 1.0f);

    public Vector4 WindowText { get; set; } = new(0.0f, 1.0f, 0.18f, 1.0f);

    public Vector4 Divider { get; set; } = new(0.42f, 0.42f, 0.58f, 1.0f);

    public Vector4 Selection { get; set; } = new(0.08f, 0.13f, 0.35f, 1.0f);
}
