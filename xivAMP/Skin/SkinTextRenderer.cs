using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace xivAMP.Skin;

public static class SkinTextRenderer
{
    private const float CharacterWidth = 5;
    private const float CharacterHeight = 6;

    private static readonly IReadOnlyDictionary<char, (int Row, int Column)> FontLookup = new Dictionary<char, (int Row, int Column)>
    {
        ['a'] = (0, 0), ['b'] = (0, 1), ['c'] = (0, 2), ['d'] = (0, 3), ['e'] = (0, 4),
        ['f'] = (0, 5), ['g'] = (0, 6), ['h'] = (0, 7), ['i'] = (0, 8), ['j'] = (0, 9),
        ['k'] = (0, 10), ['l'] = (0, 11), ['m'] = (0, 12), ['n'] = (0, 13), ['o'] = (0, 14),
        ['p'] = (0, 15), ['q'] = (0, 16), ['r'] = (0, 17), ['s'] = (0, 18), ['t'] = (0, 19),
        ['u'] = (0, 20), ['v'] = (0, 21), ['w'] = (0, 22), ['x'] = (0, 23), ['y'] = (0, 24),
        ['z'] = (0, 25), ['"'] = (0, 26), ['@'] = (0, 27), [' '] = (0, 30),
        ['0'] = (1, 0), ['1'] = (1, 1), ['2'] = (1, 2), ['3'] = (1, 3), ['4'] = (1, 4),
        ['5'] = (1, 5), ['6'] = (1, 6), ['7'] = (1, 7), ['8'] = (1, 8), ['9'] = (1, 9),
        ['~'] = (1, 10), ['.'] = (1, 11), [':'] = (1, 12), ['('] = (1, 13), [')'] = (1, 14),
        ['-'] = (1, 15), ['\''] = (1, 16), ['!'] = (1, 17), ['_'] = (1, 18), ['+'] = (1, 19),
        ['\\'] = (1, 20), ['/'] = (1, 21), ['['] = (1, 22), [']'] = (1, 23), ['^'] = (1, 24),
        ['&'] = (1, 25), ['%'] = (1, 26), [','] = (1, 27), ['='] = (1, 28), ['$'] = (1, 29),
        ['#'] = (1, 30), ['?'] = (2, 3), ['*'] = (2, 4), ['<'] = (1, 22), ['>'] = (1, 23),
        ['{'] = (1, 22), ['}'] = (1, 23),
    };

    public static bool DrawText(WinampSkin skin, string text, Vector2 position, float maxWidth, float scale)
    {
        if (!skin.TryGetTexture("TEXT", out _))
            return false;

        var maxCharacters = Math.Max(0, (int)MathF.Floor(maxWidth / (CharacterWidth * scale)));
        var displayText = PrepareText(text, maxCharacters);
        for (var i = 0; i < displayText.Length; i++)
        {
            var character = char.ToLowerInvariant(displayText[i]);
            if (!FontLookup.TryGetValue(character, out var coords))
                coords = FontLookup['?'];

            var sprite = new SkinSprite(
                "TEXT",
                coords.Column * CharacterWidth,
                coords.Row * CharacterHeight,
                CharacterWidth,
                CharacterHeight);
            SkinRenderer.DrawSprite(skin, sprite, position + new Vector2(i * CharacterWidth * scale, 0), new Vector2(CharacterWidth, CharacterHeight) * scale);
        }

        return true;
    }

    public static bool DrawScrollingText(WinampSkin skin, string text, Vector2 position, float maxWidth, float scale, ref float scrollOffset)
    {
        if (!skin.TryGetTexture("TEXT", out _))
            return false;

        var maxCharacters = Math.Max(0, (int)MathF.Floor(maxWidth / (CharacterWidth * scale)));
        if (string.IsNullOrWhiteSpace(text) || text.Length <= maxCharacters)
            return DrawText(skin, text, position, maxWidth, scale);

        const string separator = "   ***   ";
        var loopText = text + separator;
        var loopWidth = loopText.Length * CharacterWidth * scale;

        scrollOffset += ImGui.GetIO().DeltaTime * 30f;
        if (scrollOffset >= loopWidth)
            scrollOffset -= loopWidth;

        ImGui.PushClipRect(position, position + new Vector2(maxWidth, CharacterHeight * scale), true);
        var x = -scrollOffset;
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < loopText.Length; i++)
            {
                var charX = x + i * CharacterWidth * scale;
                if (charX >= maxWidth)
                    break;

                if (charX + CharacterWidth * scale <= 0)
                    continue;

                var character = char.ToLowerInvariant(loopText[i]);
                if (!FontLookup.TryGetValue(character, out var coords))
                    coords = FontLookup['?'];

                var sprite = new SkinSprite(
                    "TEXT",
                    coords.Column * CharacterWidth,
                    coords.Row * CharacterHeight,
                    CharacterWidth,
                    CharacterHeight);
                SkinRenderer.DrawSprite(skin, sprite, position + new Vector2(charX, 0), new Vector2(CharacterWidth, CharacterHeight) * scale);
            }

            x += loopWidth;
        }

        ImGui.PopClipRect();
        return true;
    }

    private static string PrepareText(string text, int maxCharacters)
    {
        if (maxCharacters <= 0)
            return string.Empty;

        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalized = text.Replace('…', '~');
        if (normalized.Length <= maxCharacters)
            return normalized;

        return maxCharacters == 1 ? "~" : normalized[..(maxCharacters - 1)] + "~";
    }
}
