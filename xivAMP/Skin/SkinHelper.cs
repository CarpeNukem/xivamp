using System.Numerics;

namespace xivAMP.Skin;

public static class SkinHelper
{
    public static float SkinScale(Configuration config)
        => Math.Clamp(config.SkinScale <= 0 ? 1.0f : config.SkinScale, 1.0f, 3.0f);

    public static Vector2 Scaled(Configuration config, Vector2 size)
        => size * SkinScale(config);
}
