using System.Numerics;
using Dalamud.Interface.Utility;

namespace xivAMP.Skin;

public static class SkinHelper
{
    public static float SkinScale(Configuration config)
        => Math.Clamp(config.SkinScale <= 0 ? 1.0f : config.SkinScale, 1.0f, 3.0f);

    /// <summary>
    /// Window size for a skinned window. Dalamud's windowing system multiplies Window.Size by
    /// <see cref="ImGuiHelpers.GlobalScale"/> (the user's global font/UI scale), but the skin
    /// is drawn at native pixels (<see cref="SkinScale"/>). Pre-dividing by GlobalScale makes
    /// the on-screen window exactly match the painted art at any global scale - so the art is
    /// never clipped (scale &lt; 100%) nor left with an uncovered margin (scale &gt; 100%).
    /// </summary>
    public static Vector2 Scaled(Configuration config, Vector2 size)
        => size * (SkinScale(config) / GlobalScale());

    private static float GlobalScale()
        => ImGuiHelpers.GlobalScale <= 0 ? 1.0f : ImGuiHelpers.GlobalScale;
}
