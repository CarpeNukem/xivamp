using System.Numerics;

namespace xivAMP.Skin;

public readonly record struct SkinSprite(string Sheet, float X, float Y, float Width, float Height)
{
    public Vector2 Size => new(this.Width, this.Height);
}

