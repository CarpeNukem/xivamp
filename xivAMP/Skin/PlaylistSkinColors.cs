using System.Numerics;

namespace xivAMP.Skin;

public sealed class PlaylistSkinColors
{
    public Vector4 Text { get; set; } = new(0.0f, 1.0f, 0.18f, 1.0f);

    public Vector4 Current { get; set; } = new(1.0f, 1.0f, 0.1f, 1.0f);

    public Vector4 Background { get; set; } = new(0.0f, 0.0f, 0.0f, 1.0f);

    public Vector4 SelectedBackground { get; set; } = new(0.08f, 0.13f, 0.35f, 1.0f);
}

