using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace xivAMP.Skin;

public static class SkinButton
{
    private static readonly Vector4 FallbackColor = new(0.65f, 0.67f, 0.78f, 1.0f);

    public static bool Draw(WinampSkin skin, string id, string normalSprite, string activeSprite, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton($"##{id}", size);
        var active = ImGui.IsItemActive();
        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        if (!SkinRenderer.DrawSprite(skin, active ? activeSprite : normalSprite, pos, size))
            ImGui.GetWindowDrawList().AddRect(pos, pos + size, ImGui.GetColorU32(FallbackColor));

        return clicked;
    }

    public static bool Draw(WinampSkin skin, string id, string spriteName, Vector2 pos, Vector2 size)
        => Draw(skin, id, spriteName, spriteName, pos, size);
}
