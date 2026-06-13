using System.Numerics;

namespace xivAMP.Skin;

public static class GenTextRenderer
{
    private const float GlyphHeight = 7;
    private const float SpaceWidth = 4;
    private const float Spacing = 1;

    public static bool DrawText(WinampSkin skin, string text, Vector2 position, float scale, bool active = true)
    {
        if (!skin.HasGenTexture)
            return false;

        var prefix = active ? "GEN_HEADER_ACTIVE_" : "GEN_HEADER_INACTIVE_";
        var x = MathF.Floor(position.X);
        var y = MathF.Floor(position.Y);
        foreach (var raw in text.ToUpperInvariant())
        {
            if (raw is < 'A' or > 'Z' || !WinampSprites.TryGet(prefix + raw, out var sprite))
            {
                x += SpaceWidth * scale;
                continue;
            }

            SkinRenderer.DrawSprite(skin, sprite, new Vector2(x, y), sprite.Size * scale);
            x += (sprite.Width + Spacing) * scale;
        }

        return true;
    }

    /// <summary>True when every character can be drawn by the GEN header font (A–Z and space).</summary>
    public static bool CanRender(string text)
    {
        foreach (var raw in text.ToUpperInvariant())
        {
            if (raw != ' ' && raw is < 'A' or > 'Z')
                return false;
        }

        return true;
    }

    public static Vector2 Measure(string text, float scale)
    {
        var width = 0.0f;
        foreach (var raw in text.ToUpperInvariant())
        {
            if (raw is >= 'A' and <= 'Z' && WinampSprites.TryGet("GEN_HEADER_ACTIVE_" + raw, out var sprite))
                width += (sprite.Width + Spacing) * scale;
            else
                width += SpaceWidth * scale;
        }

        return new Vector2(width, GlyphHeight * scale);
    }
}
