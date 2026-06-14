using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace xivAMP.Skin;

public static class SkinnedPanel
{
    private const int ColorCount = 12;
    private const int VarCount = 7;
    private const float TitleHeight = 20;
    private const float LeftChrome = 11;
    private const float RightChrome = 8;
    private const float BottomChrome = 14;
    private const float ContentPaddingLeft = 34;
    private const float ContentPaddingTop = 8;
    private const float ContentPaddingRight = 12;
    private const float ContentPaddingBottom = 10;
    private const float ResizeStepX = 25;
    private const float ResizeStepY = 29;
    private const float GripSize = 14;
    private const float DefaultRowGap = 5;
    private const float DefaultSectionGap = 7;
    private static string resizingPopupId = string.Empty;

    // Last-frame geometry per resizable popup, so we can decide (before ImGui.Begin)
    // whether a mouse-down lands on the resize grip and must suppress window-move.
    private static readonly Dictionary<string, Vector2> popupPositions = new();
    private static readonly Dictionary<string, Vector2> popupSizes = new();

    public static bool BeginPopup(WinampSkin skin, string id)
        => BeginPopup(skin, id, null, null, false, null);

    public static bool BeginPopup(
        WinampSkin skin,
        string id,
        Vector2? desiredSize,
        Vector2? minSize,
        bool resizable,
        Action<Vector2>? onResize)
    {
        PushStyle(skin);
        var minimum = minSize ?? new Vector2(250, 120);
        var size = desiredSize ?? new Vector2(285, 110);
        ImGui.SetNextWindowSize(new Vector2(MathF.Max(minimum.X, size.X), MathF.Max(minimum.Y, size.Y)), ImGuiCond.Always);

        var flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

        // Popups have no title bar, so ImGui moves them when you drag anywhere in the body -
        // including the bottom-right resize grip. Detect a mouse-down on the grip (using last
        // frame's geometry) and add NoMove for this frame so the manual resize wins instead.
        if (resizable && StartingResize(id))
            flags |= ImGuiWindowFlags.NoMove;

        if (ImGui.BeginPopup(id, flags))
        {
            DrawChrome(skin);
            DrawCloseButton(skin, id);

            if (resizable)
            {
                HandleResize(id, minimum, onResize);
                popupPositions[id] = ImGui.GetWindowPos();
                popupSizes[id] = ImGui.GetWindowSize();
            }

            ImGui.SetCursorPos(ContentCursorPosition());

            return true;
        }

        PopStyle();
        return false;
    }

    public static void EndPopup(WinampSkin skin)
    {
        if (!skin.HasGenTexture)
            DrawBorder(skin);

        ImGui.EndPopup();
        PopStyle();
    }

    // Window-mode chrome (for a persistent Dalamud window rather than a popup).
    // PushWindowStyle must run in the window's PreDraw (before ImGui.Begin) and
    // PopWindowStyle in PostDraw so the style wraps the window's Begin/End.
    public static void PushWindowStyle(WinampSkin skin) => PushStyle(skin);

    public static void PopWindowStyle() => PopStyle();

    public static void BeginWindowBody(WinampSkin skin, string id, Vector2 minSize, bool resizable, Action<Vector2>? onResize)
    {
        DrawChrome(skin);
        if (resizable)
            HandleResize(id, minSize, onResize);

        ImGui.SetCursorPos(ContentCursorPosition());
    }

    public static void EndWindowBody(WinampSkin skin)
    {
        if (!skin.HasGenTexture)
            DrawBorder(skin);
    }

    public static bool WindowCloseClicked(WinampSkin skin)
    {
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var closePos = pos + new Vector2(size.X - 11, 4);
        var closeSize = new Vector2(9, 9);
        var hovered = IsInRect(ImGui.GetIO().MousePos, closePos, closePos + closeSize);

        if (!SkinRenderer.DrawSprite(skin, "GEN_CLOSE_SELECTED", closePos, closeSize))
        {
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRectFilled(closePos, closePos + closeSize, ImGui.GetColorU32(skin.GenColors.ItemBackground));
            drawList.AddRect(closePos, closePos + closeSize, ImGui.GetColorU32(skin.GenColors.Divider));
        }

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        return hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left);
    }

    public static bool Button(WinampSkin skin, string id, string label, Vector2 size)
        => Button(skin, id, label, size, false);

    public static bool Button(WinampSkin skin, string id, string label, Vector2 size, bool pressed)
    {
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, size);
        var active = ImGui.IsItemActive();
        var hovered = ImGui.IsItemHovered();
        var visuallyPressed = pressed || active || clicked;
        var buttonHeight = 15.0f;
        var buttonSize = new Vector2(size.X, Math.Min(buttonHeight, size.Y <= 0 ? buttonHeight : size.Y));
        var buttonPos = pos + new Vector2(0, MathF.Max(0, (size.Y - buttonSize.Y) * 0.5f));
        if (!DrawGenExButton(skin, buttonPos, buttonSize, visuallyPressed))
        {
            ImGui.GetWindowDrawList().AddRectFilled(
                buttonPos,
                buttonPos + buttonSize,
                ImGui.GetColorU32(visuallyPressed || hovered ? skin.GenColors.Divider : skin.GenColors.ItemBackground));
            ImGui.GetWindowDrawList().AddRect(buttonPos, buttonPos + buttonSize, ImGui.GetColorU32(skin.GenColors.Divider));
        }

        var textSize = ImGui.CalcTextSize(label);
        var textPos = buttonPos + (buttonSize - textSize) * 0.5f - new Vector2(0, 2) + new Vector2(0, visuallyPressed ? 2 : 0);
        ImGui.GetWindowDrawList().AddText(textPos, ImGui.GetColorU32(skin.GenColors.ButtonText), label);
        return clicked;
    }

    public static bool ToggleButton(WinampSkin skin, string id, string marker, bool selected, Vector2 size)
    {
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, size);
        var active = ImGui.IsItemActive();
        var hovered = ImGui.IsItemHovered();
        var pressed = selected || active || clicked;
        if (!DrawGenExButton(skin, pos, size, pressed))
        {
            ImGui.GetWindowDrawList().AddRectFilled(
                pos,
                pos + size,
                ImGui.GetColorU32(pressed ? skin.GenColors.Selection : skin.GenColors.ItemBackground));
            ImGui.GetWindowDrawList().AddRect(pos, pos + size, ImGui.GetColorU32(skin.GenColors.Divider));
        }

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        if (selected || clicked)
        {
            var textSize = ImGui.CalcTextSize(marker);
            var textPos = pos + (size - textSize) * 0.5f - new Vector2(0, 1) + new Vector2(0, pressed ? 2 : 0);
            ImGui.GetWindowDrawList().AddText(textPos, ImGui.GetColorU32(skin.GenColors.ButtonText), marker);
        }

        return clicked;
    }

    public static float ContentWidth(WinampSkin skin)
    {
        var width = ImGui.GetWindowSize().X;
        return MathF.Max(1, width - LeftChrome - RightChrome - ContentPaddingLeft - ContentPaddingRight);
    }

    public static float ContentBottomY(WinampSkin skin)
    {
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();

        return windowPos.Y + windowSize.Y - BottomChrome - ContentPaddingBottom;
    }

    public static Vector2 ContentCursorScreenPosition(WinampSkin skin)
    {
        var windowPos = ImGui.GetWindowPos();
        return windowPos + ContentCursorPosition();
    }

    public static void CenterNextItem(WinampSkin skin, float itemWidth)
    {
        // Center symmetrically about the window's mid-line so centered text/buttons line
        // up with the (window-centered) title. The content area itself is asymmetric
        // (wider left chrome/padding than right), so centering within it skews right.
        var cursor = ImGui.GetCursorScreenPos();
        var windowPos = ImGui.GetWindowPos();
        var windowWidth = ImGui.GetWindowSize().X;
        var centeredX = windowPos.X + MathF.Max(LeftChrome + 2, (windowWidth - itemWidth) * 0.5f);
        ImGui.SetCursorScreenPos(new Vector2(centeredX, cursor.Y));
    }

    public static float CenterContentColumn(WinampSkin skin, float requestedWidth)
    {
        var width = MathF.Min(requestedWidth, ContentWidth(skin));
        CenterNextItem(skin, width);
        return width;
    }

    public static void TextCentered(WinampSkin skin, string text, bool disabled = false)
    {
        var width = ContentWidth(skin);
        var size = ImGui.CalcTextSize(text);
        if (size.X > width)
        {
            if (disabled)
                ImGui.TextDisabled(text);
            else
                ImGui.TextWrapped(text);

            return;
        }

        CenterNextItem(skin, size.X);
        if (disabled)
            ImGui.TextDisabled(text);
        else
            ImGui.TextUnformatted(text);
    }

    public static void Title(WinampSkin skin, string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return;

        var pos = ImGui.GetWindowPos();
        var width = ImGui.GetWindowSize().X;

        // Use the GEN.bmp titlebar font when the title is all letters; otherwise (e.g.
        // confirmation questions with "?" or names) fall back to plain text.
        if (skin.HasGenTexture && GenTextRenderer.CanRender(title))
        {
            var measure = GenTextRenderer.Measure(title, 1f);
            var glyphX = pos.X + MathF.Max(28, (width - measure.X) * 0.5f);
            GenTextRenderer.DrawText(skin, title, new Vector2(glyphX, pos.Y + 4), 1f, active: true);
            return;
        }

        var text = title.ToUpperInvariant();
        var size = ImGui.CalcTextSize(text);
        var textPos = pos + new Vector2(MathF.Max(28, (width - size.X) * 0.5f), -2);
        ImGui.GetWindowDrawList().AddText(textPos, ImGui.GetColorU32(skin.GenColors.WindowText), text);
    }

    public static void Section(WinampSkin skin, string label)
        => Section(skin, label, ContentWidth(skin));

    public static void Section(WinampSkin skin, string label, float width)
    {
        ImGui.Dummy(new Vector2(1, DefaultSectionGap));
        if (width < ContentWidth(skin))
            CenterNextItem(skin, width);

        var start = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var dividerColor = ImGui.GetColorU32(skin.GenColors.Divider);

        // GEN.bmp active-letter header above a full-width underline; fall back to text.
        if (GenTextRenderer.DrawText(skin, label, start + new Vector2(1, 1), 1f, active: true))
        {
            drawList.AddLine(new Vector2(start.X, start.Y + 11), new Vector2(start.X + width, start.Y + 11), dividerColor);
            ImGui.Dummy(new Vector2(width, 13));
        }
        else
        {
            var text = label.ToUpperInvariant();
            var size = ImGui.CalcTextSize(text);
            drawList.AddText(start, ImGui.GetColorU32(skin.GenColors.WindowText), text);
            drawList.AddLine(new Vector2(start.X, start.Y + size.Y + 2), new Vector2(start.X + width, start.Y + size.Y + 2), dividerColor);
            ImGui.Dummy(new Vector2(width, size.Y + 2));
        }

        if (width < ContentWidth(skin))
            CenterNextItem(skin, width);
    }

    public static void SameRow(float spacing = DefaultRowGap)
        => ImGui.SameLine(0, spacing);

    public static void ButtonRow(WinampSkin skin, float totalWidth)
        => CenterNextItem(skin, totalWidth);

    /// <summary>Move the cursor just below the title bar (for centered confirm-dialog text).</summary>
    public static void BodyTopCursor(WinampSkin skin, float offset = 6)
    {
        var x = ContentCursorScreenPosition(skin).X;
        ImGui.SetCursorScreenPos(new Vector2(x, ImGui.GetWindowPos().Y + TitleHeight + offset));
    }

    /// <summary>Place the next centered button row just above the window's bottom border.</summary>
    public static void BottomButtonRow(WinampSkin skin, float totalWidth)
    {
        var x = ContentCursorScreenPosition(skin).X;
        ImGui.SetCursorScreenPos(new Vector2(x, ContentBottomY(skin) - 16));
        CenterNextItem(skin, totalWidth);
    }

    public static void BeginClippedBody(WinampSkin skin)
    {
        if (!skin.HasGenTexture)
            return;

        var min = ContentCursorScreenPosition(skin) - new Vector2(8, 6);
        var max = new Vector2(min.X + ContentWidth(skin) + 16, ContentBottomY(skin));
        ImGui.PushClipRect(min, max, true);
    }

    public static void EndClippedBody(WinampSkin skin)
    {
        if (skin.HasGenTexture)
            ImGui.PopClipRect();
    }

    private static void PushStyle(WinampSkin skin)
    {
        var colors = skin.GenColors;
        var panelBg = colors.WindowBackground;
        var frameBg = colors.ItemBackground;
        var buttonBg = Lerp(colors.ItemBackground, colors.Divider, 0.45f);
        var buttonHovered = Lerp(buttonBg, colors.ButtonText, 0.16f);
        var buttonActive = Lerp(buttonBg, colors.Selection, 0.32f);

        ImGui.PushStyleColor(ImGuiCol.PopupBg, panelBg);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, panelBg);
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.Text, colors.WindowText);
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, Lerp(colors.WindowText, colors.Divider, 0.62f));
        ImGui.PushStyleColor(ImGuiCol.Separator, colors.Divider);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, frameBg);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Lerp(frameBg, colors.ItemForeground, 0.10f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Lerp(frameBg, colors.Selection, 0.20f));
        ImGui.PushStyleColor(ImGuiCol.Button, buttonBg);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, buttonHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, buttonActive);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 8));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(5, 3));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(5, 4));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 0);
    }

    private static void PopStyle()
    {
        ImGui.PopStyleVar(VarCount);
        ImGui.PopStyleColor(ColorCount);
    }

    private static void DrawBorder(WinampSkin skin)
    {
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var drawList = ImGui.GetWindowDrawList();
        var min = pos;
        var max = pos + size;
        drawList.AddRect(min, max, ImGui.GetColorU32(new Vector4(0.64f, 0.64f, 0.78f, 1.0f)));
        drawList.AddRect(min + Vector2.One, max - Vector2.One, ImGui.GetColorU32(new Vector4(0.04f, 0.04f, 0.07f, 1.0f)));
        drawList.AddLine(min + new Vector2(2, 2), new Vector2(max.X - 2, min.Y + 2), ImGui.GetColorU32(new Vector4(0.28f, 0.28f, 0.42f, 1.0f)));
        drawList.AddLine(min + new Vector2(2, 2), new Vector2(min.X + 2, max.Y - 2), ImGui.GetColorU32(new Vector4(0.28f, 0.28f, 0.42f, 1.0f)));
    }

    private static void DrawChrome(WinampSkin skin)
    {
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(skin.GenColors.WindowBackground));

        if (skin.HasGenTexture && size.X >= 154 && size.Y >= 58)
        {
            DrawGenChrome(skin, pos, size);
            return;
        }

        var outerLight = new Vector4(0.63f, 0.63f, 0.77f, 1.0f);
        var mid = new Vector4(0.24f, 0.24f, 0.36f, 1.0f);
        var dark = new Vector4(0.05f, 0.05f, 0.08f, 1.0f);
        var shadow = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);

        drawList.AddRect(pos, pos + size, ImGui.GetColorU32(outerLight));
        drawList.AddRect(pos + Vector2.One, pos + size - Vector2.One, ImGui.GetColorU32(shadow));
        drawList.AddLine(pos + new Vector2(2, 2), pos + new Vector2(size.X - 3, 2), ImGui.GetColorU32(mid));
        drawList.AddLine(pos + new Vector2(2, 2), pos + new Vector2(2, size.Y - 3), ImGui.GetColorU32(mid));
        drawList.AddLine(pos + new Vector2(2, size.Y - 3), pos + new Vector2(size.X - 3, size.Y - 3), ImGui.GetColorU32(dark));
        drawList.AddLine(pos + new Vector2(size.X - 3, 2), pos + new Vector2(size.X - 3, size.Y - 3), ImGui.GetColorU32(dark));
        drawList.AddRectFilled(pos + new Vector2(4, 4), pos + size - new Vector2(4, 4), ImGui.GetColorU32(skin.GenColors.WindowBackground));
    }

    private static void DrawGenChrome(WinampSkin skin, Vector2 pos, Vector2 size)
    {
        const float bottomCornerWidth = 125;
        var drawList = ImGui.GetWindowDrawList();
        var width = MathF.Floor(size.X);
        var height = MathF.Floor(size.Y);

        // Six-piece titlebar: left, fixed, title tile, fixed, stretch tile, right.
        SkinRenderer.DrawSprite(skin, "GEN_TOP_LEFT_SELECTED", pos, new Vector2(25, TitleHeight));
        SkinRenderer.DrawSprite(skin, "GEN_TOP_LEFT_END_SELECTED", pos + new Vector2(25, 0), new Vector2(25, TitleHeight));
        var rightStart = width - 25;
        SkinRenderer.DrawSprite(skin, "GEN_TOP_RIGHT_SELECTED", pos + new Vector2(rightStart, 0), new Vector2(25, TitleHeight));
        SkinRenderer.DrawSprite(skin, "GEN_TOP_RIGHT_END_SELECTED", pos + new Vector2(Math.Max(50, rightStart - 25), 0), new Vector2(25, TitleHeight));
        var fillStart = 50;
        var fillEnd = Math.Max(fillStart, rightStart - 25);
        SkinRenderer.TileHorizontal(skin, "GEN_TOP_CENTER_FILL_SELECTED", pos + new Vector2(fillStart, 0), fillEnd - fillStart, 1.0f);

        var sideHeight = Math.Max(0, height - TitleHeight - BottomChrome);
        SkinRenderer.TileVertical(skin, "GEN_MIDDLE_LEFT", pos + new Vector2(0, TitleHeight), sideHeight, 1.0f);
        SkinRenderer.TileVertical(skin, "GEN_MIDDLE_RIGHT", pos + new Vector2(width - RightChrome, TitleHeight), sideHeight, 1.0f);

        SkinRenderer.DrawSprite(skin, "GEN_BOTTOM_LEFT", pos + new Vector2(0, height - BottomChrome), new Vector2(bottomCornerWidth, BottomChrome));
        SkinRenderer.DrawSprite(skin, "GEN_BOTTOM_RIGHT", pos + new Vector2(width - bottomCornerWidth, height - BottomChrome), new Vector2(bottomCornerWidth, BottomChrome));
        SkinRenderer.TileHorizontal(skin, "GEN_BOTTOM_FILL", pos + new Vector2(bottomCornerWidth, height - BottomChrome), Math.Max(0, width - bottomCornerWidth * 2), 1.0f);

        var bodyMin = pos + new Vector2(LeftChrome, TitleHeight);
        var bodyMax = pos + new Vector2(width - RightChrome, height - BottomChrome);
        drawList.AddRectFilled(bodyMin, bodyMax, ImGui.GetColorU32(skin.GenColors.WindowBackground));
    }

    private static bool DrawGenExButton(WinampSkin skin, Vector2 pos, Vector2 size, bool pressed)
    {
        if (!skin.TryGetTexture("GENEX", out _))
            return false;

        var prefix = pressed ? "GENEX_BUTTON_PRESSED" : "GENEX_BUTTON_ACTIVE";
        var width = MathF.Floor(size.X);
        var height = MathF.Floor(size.Y);
        SkinRenderer.DrawSprite(skin, $"{prefix}_LEFT", pos, new Vector2(3, height));
        SkinRenderer.TileHorizontal(skin, $"{prefix}_CENTER", pos + new Vector2(3, 0), Math.Max(0, width - 6), 1.0f);
        SkinRenderer.DrawSprite(skin, $"{prefix}_RIGHT", pos + new Vector2(width - 3, 0), new Vector2(3, height));
        return true;
    }

    private static Vector4 Lerp(Vector4 left, Vector4 right, float amount)
        => left + (right - left) * amount;

    private static Vector2 ContentCursorPosition()
        => new(LeftChrome + ContentPaddingLeft, ContentPaddingTop);

    private static void DrawCloseButton(WinampSkin skin, string id)
    {
        if (!skin.HasGenTexture)
            return;

        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var closePos = pos + new Vector2(size.X - 11, 4);
        var closeSize = new Vector2(9, 9);
        var mouse = ImGui.GetIO().MousePos;
        var hovered = IsInRect(mouse, closePos, closePos + closeSize);
        SkinRenderer.DrawSprite(skin, "GEN_CLOSE_SELECTED", closePos, closeSize);

        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            if (string.Equals(resizingPopupId, id, StringComparison.Ordinal))
                resizingPopupId = string.Empty;

            ImGui.CloseCurrentPopup();
        }

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
    }

    // True when the user has just pressed (or is holding) the left button over the resize
    // grip, judged from last frame's geometry - used to set NoMove before ImGui.Begin.
    private static bool StartingResize(string id)
    {
        if (string.Equals(resizingPopupId, id, StringComparison.Ordinal))
            return true;

        if (!popupPositions.TryGetValue(id, out var pos) || !popupSizes.TryGetValue(id, out var size))
            return false;

        var gripSize = new Vector2(GripSize, GripSize);
        var gripPos = pos + size - gripSize;
        var mouse = ImGui.GetIO().MousePos;
        return IsInRect(mouse, gripPos, gripPos + gripSize)
            && ImGui.IsMouseClicked(ImGuiMouseButton.Left);
    }

    private static void HandleResize(string id, Vector2 minSize, Action<Vector2>? onResize)
    {
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var gripSize = new Vector2(GripSize, GripSize);
        var gripPos = pos + size - gripSize;
        var mouse = ImGui.GetIO().MousePos;
        var hovered = IsInRect(mouse, gripPos, gripPos + gripSize);
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);

        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            resizingPopupId = id;

        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left) && string.Equals(resizingPopupId, id, StringComparison.Ordinal))
            resizingPopupId = string.Empty;

        if (!string.Equals(resizingPopupId, id, StringComparison.Ordinal))
            return;

        var next = new Vector2(
            SnapToStep(MathF.Max(minSize.X, mouse.X - pos.X), ResizeStepX),
            SnapToStep(MathF.Max(minSize.Y, mouse.Y - pos.Y), ResizeStepY));

        if (Math.Abs(next.X - size.X) < 0.5f && Math.Abs(next.Y - size.Y) < 0.5f)
            return;

        ImGui.SetWindowSize(next);
        onResize?.Invoke(next);
    }

    private static float SnapToStep(float value, float step)
        => MathF.Round(value / step) * step;

    private static bool IsInRect(Vector2 point, Vector2 min, Vector2 max)
        => point.X >= min.X && point.X <= max.X && point.Y >= min.Y && point.Y <= max.Y;
}
