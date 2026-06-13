using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace xivAMP.Skin;

public static class SkinRenderer
{
    public static bool DrawSprite(WinampSkin skin, SkinSprite sprite, Vector2 position, Vector2 size)
        => DrawSpriteInternal(skin, sprite, position, size);

    public static bool DrawSprite(WinampSkin skin, string spriteName, Vector2 position, float scale)
    {
        if (!WinampSprites.TryGet(spriteName, out var sprite))
            return false;

        return DrawSpriteInternal(skin, sprite, position, sprite.Size * scale);
    }

    public static bool DrawSprite(WinampSkin skin, string spriteName, Vector2 position, Vector2 size)
    {
        if (!WinampSprites.TryGet(spriteName, out var sprite))
            return false;

        return DrawSpriteInternal(skin, sprite, position, size);
    }

    public static void TileHorizontal(WinampSkin skin, string spriteName, Vector2 position, float width, float scale)
    {
        if (!WinampSprites.TryGet(spriteName, out var sprite))
            return;

        var x = Snap(position.X);
        var end = Snap(position.X + width);
        while (x < end)
        {
            var drawWidth = MathF.Min(sprite.Width * scale, end - x);
            var clipped = sprite with { Width = drawWidth / scale };
            DrawSpriteInternal(skin, clipped, new Vector2(x, Snap(position.Y)), new Vector2(drawWidth, sprite.Height * scale));
            x += drawWidth;
        }
    }

    public static void TileVertical(WinampSkin skin, string spriteName, Vector2 position, float height, float scale)
    {
        if (!WinampSprites.TryGet(spriteName, out var sprite))
            return;

        var y = Snap(position.Y);
        var end = Snap(position.Y + height);
        while (y < end)
        {
            var drawHeight = MathF.Min(sprite.Height * scale, end - y);
            var clipped = sprite with { Height = drawHeight / scale };
            DrawSpriteInternal(skin, clipped, new Vector2(Snap(position.X), y), new Vector2(sprite.Width * scale, drawHeight));
            y += drawHeight;
        }
    }

    private static bool DrawSpriteInternal(WinampSkin skin, SkinSprite sprite, Vector2 position, Vector2 size)
    {
        if (!skin.TryGetTexture(sprite.Sheet, out var texture))
            return false;

        position = Snap(position);
        size = Snap(size);
        var uv0 = new Vector2(sprite.X / texture.Width, sprite.Y / texture.Height);
        var uv1 = new Vector2((sprite.X + sprite.Width) / texture.Width, (sprite.Y + sprite.Height) / texture.Height);
        ImGui.GetWindowDrawList().AddImage(texture.Handle, position, position + size, uv0, uv1);
        return true;
    }

    private static Vector2 Snap(Vector2 value)
        => new(Snap(value.X), Snap(value.Y));

    private static float Snap(float value)
        => MathF.Floor(value + 0.001f);
}
