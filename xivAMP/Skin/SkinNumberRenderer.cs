using System.Numerics;

namespace xivAMP.Skin;

public static class SkinNumberRenderer
{
    private const float DigitWidth = 9;
    private const float DigitHeight = 13;

    // Fixed X-offsets for the four timer digits, relative to the time area at (48,26).
    // Exact Webamp layout: minute-first 48, minute-second 60, second-first 78,
    // second-second 90 → offsets 0/12/30/42. No colon is drawn (Webamp leaves the gap).
    private static readonly float[] TimerOffsets = [0, 12, 30, 42];

    public static bool DrawTime(WinampSkin skin, string text, Vector2 position, float scale)
    {
        // Skin digits come from NUMBERS.bmp, or NUMS_EX.bmp on newer skins.
        var sheet = skin.TryGetTexture("NUMBERS", out _) ? "NUMBERS"
            : skin.TryGetTexture("NUMS_EX", out _) ? "NUMS_EX"
            : null;
        if (sheet is null)
            return false;

        // Extract up to 4 digits from text (skip any colon/separators).
        Span<int> digits = stackalloc int[4];
        var count = 0;
        foreach (var ch in text)
        {
            if (ch >= '0' && ch <= '9' && count < 4)
                digits[count++] = ch - '0';
        }

        for (var i = 0; i < count; i++)
        {
            var x = position.X + TimerOffsets[i] * scale;
            var sprite = new SkinSprite(sheet, digits[i] * DigitWidth, 0, DigitWidth, DigitHeight);
            SkinRenderer.DrawSprite(skin, sprite, new Vector2(x, position.Y), new Vector2(DigitWidth, DigitHeight) * scale);
        }

        return true;
    }
}
