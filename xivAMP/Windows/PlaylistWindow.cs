using System.Numerics;
using System.Globalization;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using xivAMP.Services;
using xivAMP.Skin;

namespace xivAMP.Windows;

public sealed class PlaylistWindow : Window
{
    private static readonly Vector2 MinSize = new(275, 232);
    private static readonly Vector2 RemovePopupSize = new(320, 110);
    private static readonly Vector2 ClearPopupSize = new(270, 105);
    private const float WidthStep = 25;
    private const float HeightStep = 29;
    private const float ShadeHeight = 14;

    private readonly Plugin plugin;
    private readonly XivAmpController controller;
    private string renameBuffer = string.Empty;
    private string durationBuffer = string.Empty;
    private string bitrateBuffer = string.Empty;
    private string khzBuffer = string.Empty;
    private string propertiesError = string.Empty;
    private string addFilter = string.Empty;
    private string addGroupFilter = string.Empty;
    private string addGroup = string.Empty;
    private readonly HashSet<string> checkedAddOptions = new(StringComparer.OrdinalIgnoreCase);
    private float addScrollOffset;
    private bool draggingAddScrollbar;
    private float addScrollbarDragStartY;
    private float addScrollbarDragStartOffset;
    private float addGroupScrollOffset;
    private bool draggingAddGroupScrollbar;
    private float addGroupScrollbarDragStartY;
    private float addGroupScrollbarDragStartOffset;
    private int renameIndex = -1;
    private int pendingRemoveIndex = -1;
    private bool remMenuOpen;
    private int remMenuOpenedFrame = -1;
    private Vector2 remMenuPosition;
    private Vector2? dockedPosition;
    private float scrollOffset;
    private bool draggingScrollbar;
    private float scrollbarDragStartY;
    private float scrollbarDragStartOffset;
    private bool resizing;
    private Vector2 resizeStartMouse;
    private Vector2 resizeStartSize;
    private int lastScrolledToIndex = -1;
    private float shadeScrollOffset;

    public PlaylistWindow(Plugin plugin, XivAmpController controller)
        : base("xivAMP Playlist###xivAMPPlaylist", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize)
    {
        this.plugin = plugin;
        this.controller = controller;
        this.Size = SkinHelper.Scaled(plugin.Configuration, this.BaseSize());
        this.SizeCondition = ImGuiCond.Always;
    }

    public override void PreDraw()
    {
        // Allow the shaded playlist to collapse below ImGui's default 32px minimum,
        // and zero the padding so the thin window's content area stays valid.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        this.IsOpen = this.plugin.Configuration.PlaylistWindowVisible && this.plugin.PlayerWindow.IsOpen;
        this.Size = SkinHelper.Scaled(this.plugin.Configuration, this.CurrentSize());
        if (this.dockedPosition is { } position)
        {
            this.Position = position;
            this.PositionCondition = ImGuiCond.Always;
        }
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(2);
    }

    private Vector2 CurrentSize()
    {
        var baseSize = this.BaseSize();
        return this.plugin.Configuration.PlaylistWindowShade
            ? new Vector2(baseSize.X, ShadeHeight)
            : baseSize;
    }

    /// <summary>Screen position just below the playlist window, where the Add panel docks.</summary>
    public Vector2 AddDockPosition(float scale)
    {
        var config = this.plugin.Configuration;
        var mainBottom = config.PlayerWindowY + this.plugin.PlayerWindow.CurrentSize.Y * scale;
        var playlistBottom = mainBottom + this.CurrentSize().Y * scale;
        return new Vector2(config.PlayerWindowX, playlistBottom);
    }

    public override void Draw()
    {
        var scale = SkinHelper.SkinScale(this.plugin.Configuration);
        var origin = ImGui.GetWindowPos();
        var baseSize = this.BaseSize();
        this.UpdateDocking(scale);

        // Keep any drawing/handler exception from escaping Draw and unbalancing ImGui.
        try
        {
            if (this.plugin.Configuration.PlaylistWindowShade)
            {
                this.DrawPlaylistShade(origin, baseSize, scale);
                return;
            }

            var size = baseSize * scale;
            this.DrawChrome(origin, size, scale);

            var listOrigin = origin + new Vector2(12, 23) * scale;
            var listSize = this.ListSize(baseSize) * scale;
            this.DrawRows(listOrigin, listSize, scale);
            this.DrawScrollbar(origin, baseSize, scale);
            this.DrawBottomButtons(origin, baseSize, scale);
            this.DrawRemMenu(scale);
            this.DrawPlaylistInfo(origin, baseSize, scale);

            this.DrawTitleBarButtons(origin, baseSize, scale);

            this.HandleResize(origin, baseSize, scale);
            this.DrawRemovePopup();
            this.DrawCropPopup();
            this.DrawMiscPopup();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "xivAMP playlist window draw failed.");
            this.controller.SetStatus($"Error: {ex.Message}");
        }
    }

    private void DrawTitleBarButtons(Vector2 origin, Vector2 baseSize, float scale)
    {
        if (SkinButton.Draw(this.plugin.CurrentSkin, "shade_playlist", "PLAYLIST_COLLAPSE_SELECTED", origin + new Vector2(baseSize.X - 21, 3) * scale, new Vector2(9, 9) * scale))
            this.ToggleShade();

        if (SkinButton.Draw(this.plugin.CurrentSkin, "close_playlist", "PLAYLIST_CLOSE_SELECTED", origin + new Vector2(baseSize.X - 11, 3) * scale, new Vector2(9, 9) * scale))
        {
            this.plugin.Configuration.PlaylistWindowVisible = false;
            this.plugin.Save();
            this.IsOpen = false;
        }
    }

    private void ToggleShade()
    {
        this.plugin.Configuration.PlaylistWindowShade = !this.plugin.Configuration.PlaylistWindowShade;
        this.plugin.Save();
        var scale = SkinHelper.SkinScale(this.plugin.Configuration);
        this.Size = this.CurrentSize() * scale;
    }

    private void DrawPlaylistShade(Vector2 origin, Vector2 baseSize, float scale)
    {
        var width = baseSize.X;
        var colors = this.plugin.CurrentSkin.PlaylistColors;

        // Dedicated windowshade title bar from PLEDIT.bmp: fixed 25px left cap,
        // stretched middle (black title display), fixed 50px right cap that bakes the
        // mini scrollbar + collapse + close buttons.
        if (this.plugin.CurrentSkin.HasPlaylistTexture)
        {
            SkinRenderer.DrawSprite(this.plugin.CurrentSkin, "PLAYLIST_SHADE_LEFT", origin, new Vector2(25, ShadeHeight) * scale);
            SkinRenderer.TileHorizontal(this.plugin.CurrentSkin, "PLAYLIST_SHADE_MIDDLE", origin + new Vector2(25, 0) * scale, Math.Max(0, width - 75) * scale, scale);
            SkinRenderer.DrawSprite(this.plugin.CurrentSkin, "PLAYLIST_SHADE_RIGHT", origin + new Vector2(width - 50, 0) * scale, new Vector2(50, ShadeHeight) * scale);
        }
        else
        {
            var dl = ImGui.GetWindowDrawList();
            dl.AddRectFilled(origin, origin + new Vector2(width, ShadeHeight) * scale, ImGui.GetColorU32(colors.Background));
            dl.AddRect(origin, origin + new Vector2(width, ShadeHeight) * scale, ImGui.GetColorU32(new Vector4(0.72f, 0.72f, 0.82f, 1.0f)));
        }

        // Running track label (with playlist position), in the "now playing" colour.
        var entry = this.controller.AppliedEntry() ?? this.controller.CurrentEntry();
        var index = this.plugin.Configuration.CurrentIndex;
        var label = entry is null
            ? "xivAMP playlist"
            : (index >= 0 ? $"{index + 1}. {entry.Label}" : entry.Label);

        // Title + time use the Winamp bitmap font (TEXT.bmp), like the main window
        // track display. The font is 5x6px, so it sits cleanly in the 14px bar.
        var barHeight = ShadeHeight * scale;
        var fontTop = origin.Y + (barHeight - 6 * scale) * 0.5f;

        // Time at the right of the black display, just before the right cap's controls.
        var timeLabel = this.ShadeTimeLabel();
        var displayRight = width - 54f;
        if (!string.IsNullOrEmpty(timeLabel))
        {
            var timeWidthPx = timeLabel.Length * 5f;
            var timeX = origin.X + (displayRight - timeWidthPx) * scale;
            if (!SkinTextRenderer.DrawText(this.plugin.CurrentSkin, timeLabel, new Vector2(timeX, fontTop), timeWidthPx * scale, scale))
            {
                var ts = ImGui.CalcTextSize(timeLabel);
                ImGui.GetWindowDrawList().AddText(new Vector2(timeX, origin.Y + (barHeight - ts.Y) * 0.5f), ImGui.GetColorU32(colors.Current), timeLabel);
            }

            displayRight -= timeWidthPx + 4;
        }

        // Scrolling track title in the bitmap font.
        var titleX = origin.X + 6 * scale;
        var titleMaxWidth = Math.Max(0, displayRight - 6) * scale;
        if (!SkinTextRenderer.DrawScrollingText(this.plugin.CurrentSkin, label, new Vector2(titleX, fontTop), titleMaxWidth, scale, ref this.shadeScrollOffset))
            this.DrawShadeScrollingText(label, new Vector2(titleX, origin.Y), titleMaxWidth, barHeight, ImGui.GetColorU32(colors.Current));

        // Collapse + close (overlaid on the right cap's baked buttons for press feedback).
        this.DrawTitleBarButtons(origin, baseSize, scale);
    }

    private void DrawShadeScrollingText(string text, Vector2 pos, float maxWidth, float height, uint color)
    {
        if (maxWidth <= 0 || string.IsNullOrEmpty(text))
            return;

        var drawList = ImGui.GetWindowDrawList();
        var textSize = ImGui.CalcTextSize(text);
        var y = pos.Y + MathF.Max(0, (height - textSize.Y) * 0.5f);
        ImGui.PushClipRect(pos, new Vector2(pos.X + maxWidth, pos.Y + height), true);

        if (textSize.X <= maxWidth)
        {
            this.shadeScrollOffset = 0;
            drawList.AddText(new Vector2(pos.X, y), color, text);
        }
        else
        {
            const string separator = "    ";
            var loop = text + separator;
            var loopWidth = ImGui.CalcTextSize(loop).X;
            this.shadeScrollOffset += ImGui.GetIO().DeltaTime * 30f;
            if (this.shadeScrollOffset >= loopWidth)
                this.shadeScrollOffset -= loopWidth;

            drawList.AddText(new Vector2(pos.X - this.shadeScrollOffset, y), color, loop);
            drawList.AddText(new Vector2(pos.X - this.shadeScrollOffset + loopWidth, y), color, loop);
        }

        ImGui.PopClipRect();
    }

    private string ShadeTimeLabel()
    {
        if (this.plugin.Configuration.IsStopped
            || string.IsNullOrWhiteSpace(this.plugin.Configuration.LastAppliedOptionName)
            || this.plugin.Configuration.LastAppliedAtUtc == default)
            return string.Empty;

        var appliedEntry = this.controller.AppliedEntry() ?? this.controller.CurrentEntry();
        var duration = appliedEntry?.DurationSeconds ?? this.plugin.Configuration.FallbackTrackDurationSeconds;
        if (duration <= 0)
            duration = 180;

        var elapsed = this.plugin.Configuration.EstimatedSeekOffsetSeconds
            + (DateTime.UtcNow - this.plugin.Configuration.LastAppliedAtUtc).TotalSeconds;
        if (this.plugin.Configuration.RepeatEnabled)
            elapsed %= duration;
        else
            elapsed = Math.Min(elapsed, duration);

        var time = TimeSpan.FromSeconds(Math.Max(0, elapsed));
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }

    public void DockTo(Vector2 position)
        => this.dockedPosition = position;

    private void DrawRows(Vector2 listOrigin, Vector2 listSize, float scale)
    {
        var colors = this.plugin.CurrentSkin.PlaylistColors;
        var playlist = this.plugin.Configuration.Playlist;
        var rowHeight = MathF.Max(15 * scale, ImGui.GetTextLineHeight() + 2 * scale);
        var totalContentHeight = playlist.Count * rowHeight;
        var maxScroll = Math.Max(0, totalContentHeight - listSize.Y);
        this.scrollOffset = Math.Clamp(this.scrollOffset, 0, maxScroll);

        // Auto-scroll to current track when it changes (e.g. shuffle, next/prev).
        var currentIndex = this.plugin.Configuration.CurrentIndex;
        if (currentIndex >= 0 && currentIndex < playlist.Count && currentIndex != this.lastScrolledToIndex)
        {
            this.lastScrolledToIndex = currentIndex;
            var trackTop = currentIndex * rowHeight;
            var trackBottom = trackTop + rowHeight;
            if (trackTop < this.scrollOffset)
                this.scrollOffset = trackTop;
            else if (trackBottom > this.scrollOffset + listSize.Y)
                this.scrollOffset = trackBottom - listSize.Y;
            this.scrollOffset = Math.Clamp(this.scrollOffset, 0, maxScroll);
        }

        // Handle mouse wheel scrolling when hovering the list area.
        var mouse = ImGui.GetIO().MousePos;
        if (mouse.X >= listOrigin.X && mouse.X <= listOrigin.X + listSize.X + 20 * scale
            && mouse.Y >= listOrigin.Y && mouse.Y <= listOrigin.Y + listSize.Y)
        {
            var wheel = ImGui.GetIO().MouseWheel;
            if (wheel != 0)
                this.scrollOffset = Math.Clamp(this.scrollOffset - wheel * rowHeight * 3, 0, maxScroll);
        }

        ImGui.SetCursorScreenPos(listOrigin);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, colors.Background);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        ImGui.BeginChild("playlist_rows", listSize, false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        // Skip rows above the visible area.
        var firstVisible = Math.Max(0, (int)(this.scrollOffset / rowHeight));
        var lastVisible = Math.Min(playlist.Count - 1, (int)((this.scrollOffset + listSize.Y) / rowHeight));

        if (firstVisible > 0)
        {
            ImGui.SetCursorPosY(firstVisible * rowHeight - this.scrollOffset);
        }
        else
        {
            ImGui.SetCursorPosY(-this.scrollOffset);
        }

        for (var i = firstVisible; i <= lastVisible && i < playlist.Count; i++)
        {
            var entry = playlist[i];
            var selected = i == this.plugin.Configuration.CurrentIndex;
            var nowPlaying = IsEntryIdentity(entry, this.plugin.Configuration.LastAppliedOptionGroup, this.plugin.Configuration.LastAppliedOptionName);
            var label = $"{i + 1}. {entry.Label}";
            if (this.PlaylistUsesMultipleGroups())
                label = $"{label} [{entry.OptionGroup}]";
            var durationLabel = DurationLabel(entry);

            ImGui.PushID(i);
            var rowStart = ImGui.GetCursorScreenPos();
            var rowWidth = ImGui.GetContentRegionAvail().X;
            ImGui.InvisibleButton("row", new Vector2(rowWidth, rowHeight));
            var hovered = ImGui.IsItemHovered();
            var clicked = ImGui.IsItemClicked();
            var doubleClicked = hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left);
            var rightClicked = ImGui.IsItemClicked(ImGuiMouseButton.Right);

            if (selected)
            {
                var rowEnd = rowStart + new Vector2(rowWidth, rowHeight);
                ImGui.GetWindowDrawList().AddRectFilled(rowStart, rowEnd, ImGui.GetColorU32(colors.SelectedBackground));
            }

            this.DrawRowText(rowStart, rowWidth, rowHeight, label, durationLabel, nowPlaying, scale);
            if (clicked)
            {
                this.plugin.Configuration.CurrentIndex = i;
                this.plugin.Save();
            }

            if (doubleClicked)
            {
                this.plugin.Configuration.CurrentIndex = i;
                this.plugin.Save();
                this.controller.ApplyCurrent();
            }

            if (rightClicked)
                this.StartRenaming(i, entry);

            if (ImGui.BeginDragDropSource())
            {
                var payloadBytes = new byte[sizeof(int)];
                BitConverter.TryWriteBytes(payloadBytes, i);
                ImGui.SetDragDropPayload("XIVAMP_PLAYLIST", payloadBytes);
                ImGui.TextUnformatted(entry.Label);
                ImGui.EndDragDropSource();
            }

            if (ImGui.BeginDragDropTarget())
            {
                // Draw insertion line indicator.
                var dropColors = this.plugin.CurrentSkin.PlaylistColors;
                var lineColor = ImGui.GetColorU32(dropColors.Current);
                var lineY = mouse.Y < rowStart.Y + rowHeight * 0.5f ? rowStart.Y : rowStart.Y + rowHeight;
                ImGui.GetWindowDrawList().AddLine(
                    new Vector2(rowStart.X, lineY),
                    new Vector2(rowStart.X + rowWidth, lineY),
                    lineColor,
                    2.0f * scale);

                var payload = ImGui.AcceptDragDropPayload("XIVAMP_PLAYLIST");
                if (TryReadPayloadIndex(payload, out var sourceIndex))
                    this.controller.MovePlaylistEntry(sourceIndex, i);

                ImGui.EndDragDropTarget();
            }

            this.DrawRenamePopup(i, entry);
            ImGui.PopID();
        }

        if (playlist.Count == 0)
            ImGui.TextDisabled("Empty");

        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }

    private void DrawScrollbar(Vector2 origin, Vector2 baseSize, float scale)
    {
        var playlist = this.plugin.Configuration.Playlist;
        var rowHeight = MathF.Max(15 * scale, ImGui.GetTextLineHeight() + 2 * scale);
        var listHeight = this.ListSize(baseSize).Y * scale;
        var totalContentHeight = playlist.Count * rowHeight;
        if (totalContentHeight <= listHeight)
            return;

        var maxScroll = totalContentHeight - listHeight;
        var trackTop = origin.Y + 23 * scale;
        var trackHeight = listHeight;
        var scrollbarX = origin.X + (baseSize.X - 15) * scale;  // Centered within right tile
        var handleWidth = 8 * scale;
        var handleHeight = Math.Min(18 * scale, trackHeight);
        var trackUsable = Math.Max(1, trackHeight - handleHeight);
        var handleY = trackTop + (maxScroll > 0 ? (this.scrollOffset / maxScroll) * trackUsable : 0);

        // Handle mouse interaction.
        var mouse = ImGui.GetIO().MousePos;
        var handleRect = new Vector2(scrollbarX, handleY);
        var handleSize = new Vector2(handleWidth, handleHeight);
        var overHandle = mouse.X >= handleRect.X && mouse.X <= handleRect.X + handleSize.X
            && mouse.Y >= handleRect.Y && mouse.Y <= handleRect.Y + handleSize.Y;
        var overTrack = mouse.X >= scrollbarX && mouse.X <= scrollbarX + handleWidth
            && mouse.Y >= trackTop && mouse.Y <= trackTop + trackHeight;

        if (overHandle && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            this.draggingScrollbar = true;
            this.scrollbarDragStartY = mouse.Y;
            this.scrollbarDragStartOffset = this.scrollOffset;
        }
        else if (overTrack && !overHandle && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            // Click on track — jump to that position.
            var clickRatio = Math.Clamp((mouse.Y - trackTop - handleHeight * 0.5f) / trackUsable, 0, 1);
            this.scrollOffset = clickRatio * maxScroll;
        }

        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            this.draggingScrollbar = false;

        if (this.draggingScrollbar)
        {
            var delta = mouse.Y - this.scrollbarDragStartY;
            var scrollDelta = trackUsable > 0 ? (delta / trackUsable) * maxScroll : 0;
            this.scrollOffset = Math.Clamp(this.scrollbarDragStartOffset + scrollDelta, 0, maxScroll);
            handleY = trackTop + (this.scrollOffset / maxScroll) * trackUsable;
        }

        // Draw handle.
        var spriteName = this.draggingScrollbar ? "PLAYLIST_SCROLLBAR_HANDLE_ACTIVE" : "PLAYLIST_SCROLLBAR_HANDLE";
        if (!SkinRenderer.DrawSprite(this.plugin.CurrentSkin, spriteName, new Vector2(scrollbarX, handleY), handleSize))
        {
            // Fallback: draw a simple colored rectangle.
            var colors = this.plugin.CurrentSkin.PlaylistColors;
            ImGui.GetWindowDrawList().AddRectFilled(
                new Vector2(scrollbarX, handleY),
                new Vector2(scrollbarX + handleWidth, handleY + handleHeight),
                ImGui.GetColorU32(this.draggingScrollbar ? colors.Current : colors.Text));
        }
    }

    private void DrawRowText(Vector2 rowStart, float rowWidth, float rowHeight, string label, string durationLabel, bool nowPlaying, float scale)
    {
        var colors = this.plugin.CurrentSkin.PlaylistColors;
        var drawList = ImGui.GetWindowDrawList();
        var textOffset = MathF.Max(0, (rowHeight - ImGui.GetTextLineHeight()) * 0.5f);
        var textColor = nowPlaying
            ? ImGui.GetColorU32(colors.Current)
            : ImGui.GetColorU32(colors.Text);
        var padding = 2 * scale;
        var durationWidth = string.IsNullOrWhiteSpace(durationLabel)
            ? 0
            : ImGui.CalcTextSize(durationLabel).X + 6 * scale;
        var titleMax = rowStart + new Vector2(Math.Max(0, rowWidth - durationWidth), rowHeight);

        ImGui.PushClipRect(rowStart, titleMax, true);
        drawList.AddText(rowStart + new Vector2(padding, textOffset), textColor, label);
        ImGui.PopClipRect();

        if (string.IsNullOrWhiteSpace(durationLabel))
            return;

        var durationSize = ImGui.CalcTextSize(durationLabel);
        var durationPos = rowStart + new Vector2(rowWidth - durationSize.X - padding, textOffset);
        ImGui.PushClipRect(rowStart, rowStart + new Vector2(rowWidth, rowHeight), true);
        drawList.AddText(durationPos, textColor, durationLabel);
        ImGui.PopClipRect();
    }

    private void DrawBottomButtons(Vector2 origin, Vector2 baseSize, float scale)
    {
        // Buttons in PLEDIT bottom corners are 22x18, 12px above the window bottom
        // (graphic row at baseSize.Y - 30), spaced 29px apart starting at x=14.
        var buttonY = baseSize.Y - 30;
        if (this.BottomButton("add", origin + new Vector2(14, buttonY) * scale, new Vector2(22, 18) * scale))
            this.plugin.AddTracksWindow.IsOpen = !this.plugin.AddTracksWindow.IsOpen;

        if (this.BottomButton("rem", origin + new Vector2(43, buttonY) * scale, new Vector2(22, 18) * scale))
        {
            this.remMenuOpen = !this.remMenuOpen;
            this.remMenuOpenedFrame = ImGui.GetFrameCount();
            this.remMenuPosition = origin + new Vector2(43, buttonY) * scale;
        }

        if (this.BottomButton("sel", origin + new Vector2(72, buttonY) * scale, new Vector2(22, 18) * scale))
            this.controller.SetStatus("SEL is visual-only for now.");

        if (this.BottomButton("misc", origin + new Vector2(101, buttonY) * scale, new Vector2(22, 18) * scale))
            this.controller.SetStatus("MISC is visual-only for now.");

        if (this.BottomButton("list_opts", origin + new Vector2(baseSize.X - 45, buttonY) * scale, new Vector2(25, 18) * scale))
            this.plugin.SetupPopupRequested = true;

        // Mini transport controls baked into the bottom-right corner. Webamp layout:
        // action buttons at top:22, left:3 within the 150px corner, 10x10 each.
        var transportY = baseSize.Y - 16;
        var corner = baseSize.X - 150;
        if (this.BottomButton("pl_prev", origin + new Vector2(corner + 3, transportY) * scale, new Vector2(10, 10) * scale))
            this.controller.ApplyRelative(-1);
        if (this.BottomButton("pl_play", origin + new Vector2(corner + 13, transportY) * scale, new Vector2(10, 10) * scale))
            this.controller.ApplyCurrent();
        if (this.BottomButton("pl_pause", origin + new Vector2(corner + 23, transportY) * scale, new Vector2(10, 10) * scale))
            this.controller.PauseCurrent();
        if (this.BottomButton("pl_stop", origin + new Vector2(corner + 33, transportY) * scale, new Vector2(10, 10) * scale))
            this.controller.StopCurrent();
        if (this.BottomButton("pl_next", origin + new Vector2(corner + 43, transportY) * scale, new Vector2(10, 10) * scale))
            this.controller.ApplyRelative(1);
        if (this.BottomButton("pl_eject", origin + new Vector2(corner + 53, transportY) * scale, new Vector2(10, 10) * scale))
            this.plugin.AddTracksWindow.IsOpen = !this.plugin.AddTracksWindow.IsOpen;
    }

    private void DrawRemMenu(float scale)
    {
        if (!this.remMenuOpen)
            return;

        // Winamp layout: menu items share the button's x and stack upward, with the
        // bottom item covering the button itself; the sidebar bar sits 3px to the
        // left, bottom-aligned with the button. Top→bottom: REM ALL, CROP, REM SEL.
        var sidebarSize = new Vector2(3, 54) * scale;
        var itemSize = new Vector2(22, 18) * scale;
        var buttonPos = this.remMenuPosition;
        var selPos = buttonPos;
        var cropPos = selPos - new Vector2(0, itemSize.Y);
        var allPos = cropPos - new Vector2(0, itemSize.Y);
        var barPos = new Vector2(buttonPos.X - sidebarSize.X, buttonPos.Y + itemSize.Y - sidebarSize.Y);
        var mouse = ImGui.GetIO().MousePos;
        var menuHovered = IsInRect(mouse, barPos, buttonPos + itemSize);

        this.DrawRemMenuSidebar(barPos, sidebarSize);
        var clickedAll = this.DrawRemMenuItem("rem_all", allPos, itemSize, "PLAYLIST_REM_ALL", "PLAYLIST_REM_ALL_PRESSED", "REM ALL");
        var clickedCrop = this.DrawRemMenuItem("rem_crop", cropPos, itemSize, "PLAYLIST_REM_CROP", "PLAYLIST_REM_CROP_PRESSED", "CROP");
        var clickedSel = this.DrawRemMenuItem("rem_sel", selPos, itemSize, "PLAYLIST_REM_SEL", "PLAYLIST_REM_SEL_PRESSED", "REM SEL");

        if (clickedSel)
        {
            this.pendingRemoveIndex = this.plugin.Configuration.CurrentIndex;
            this.remMenuOpen = false;
            if (this.pendingRemoveIndex >= 0 && this.pendingRemoveIndex < this.plugin.Configuration.Playlist.Count)
                ImGui.OpenPopup("remove_track");
            else
                this.controller.SetStatus("Select a playlist entry first.");
        }

        if (clickedCrop)
        {
            this.remMenuOpen = false;
            var index = this.plugin.Configuration.CurrentIndex;
            if (index >= 0 && index < this.plugin.Configuration.Playlist.Count)
                ImGui.OpenPopup("crop_playlist");
            else
                this.controller.SetStatus("Select a playlist entry first.");
        }

        if (clickedAll)
        {
            this.remMenuOpen = false;
            ImGui.OpenPopup("clear_playlist");
        }

        if (ImGui.GetFrameCount() > this.remMenuOpenedFrame
            && !menuHovered
            && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            this.remMenuOpen = false;
        }
    }

    private bool DrawRemMenuItem(string id, Vector2 pos, Vector2 size, string sprite, string pressedSprite, string fallbackLabel)
    {
        var mouse = ImGui.GetIO().MousePos;
        var hovered = IsInRect(mouse, pos, pos + size);
        var clicked = hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left);
        var active = hovered && ImGui.IsMouseDown(ImGuiMouseButton.Left);
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var spriteName = active ? pressedSprite : sprite;
        if (!this.DrawSpriteForeground(spriteName, pos, size))
        {
            var colors = this.plugin.CurrentSkin.GenColors;
            var drawList = ImGui.GetForegroundDrawList();
            drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(active ? colors.Selection : colors.ItemBackground));
            drawList.AddRect(pos, pos + size, ImGui.GetColorU32(colors.Divider));
            var textSize = ImGui.CalcTextSize(fallbackLabel);
            drawList.AddText(pos + (size - textSize) * 0.5f, ImGui.GetColorU32(colors.ButtonText), fallbackLabel);
        }

        return clicked;
    }

    private void DrawRemMenuSidebar(Vector2 pos, Vector2 size)
    {
        if (this.DrawSpriteForeground("PLAYLIST_REM_SIDEBAR", pos, size))
            return;

        var colors = this.plugin.CurrentSkin.PlaylistColors;
        var drawList = ImGui.GetForegroundDrawList();
        drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(colors.SelectedBackground));
        drawList.AddLine(pos, pos + new Vector2(0, size.Y), ImGui.GetColorU32(colors.Text));
    }

    private bool DrawSpriteForeground(string spriteName, Vector2 position, Vector2 size)
    {
        if (!WinampSprites.TryGet(spriteName, out var sprite)
            || !this.plugin.CurrentSkin.TryGetTexture(sprite.Sheet, out var texture))
            return false;

        position = Snap(position);
        size = Snap(size);
        var uv0 = new Vector2(sprite.X / texture.Width, sprite.Y / texture.Height);
        var uv1 = new Vector2((sprite.X + sprite.Width) / texture.Width, (sprite.Y + sprite.Height) / texture.Height);
        ImGui.GetForegroundDrawList().AddImage(texture.Handle, position, position + size, uv0, uv1);
        return true;
    }

    private static Vector2 Snap(Vector2 value)
        => new(MathF.Floor(value.X + 0.001f), MathF.Floor(value.Y + 0.001f));

    private void DrawPlaylistInfo(Vector2 origin, Vector2 baseSize, float scale)
    {
        var playlist = this.plugin.Configuration.Playlist;

        // Calculate total playlist time.
        var totalSeconds = 0.0;
        foreach (var entry in playlist)
        {
            if (entry.DurationSeconds > 0)
                totalSeconds += entry.DurationSeconds;
        }

        // Track count + total time label.
        var totalLabel = totalSeconds > 0
            ? $"{playlist.Count} tracks/{FormatDuration(totalSeconds)}"
            : $"{playlist.Count} tracks";

        // Top field: centered between list bottom and buttons.
        SkinTextRenderer.DrawText(this.plugin.CurrentSkin, totalLabel, origin + new Vector2(baseSize.X - 140, baseSize.Y - 28) * scale, 90 * scale, scale);

        // Elapsed timer in bottom field (y≈218, at button row level).
        if (!this.plugin.Configuration.IsStopped
            && !string.IsNullOrWhiteSpace(this.plugin.Configuration.LastAppliedOptionName)
            && this.plugin.Configuration.LastAppliedAtUtc != default)
        {
            var appliedEntry = this.controller.AppliedEntry() ?? this.controller.CurrentEntry();
            var duration = appliedEntry?.DurationSeconds ?? this.plugin.Configuration.FallbackTrackDurationSeconds;
            if (duration <= 0)
                duration = 180;

            // Keep in sync with PlayerWindow.EstimatedElapsedSeconds: include the seek
            // offset and only wrap around when repeat is on; otherwise clamp at track end.
            var elapsed = this.plugin.Configuration.EstimatedSeekOffsetSeconds
                + (DateTime.UtcNow - this.plugin.Configuration.LastAppliedAtUtc).TotalSeconds;
            if (this.plugin.Configuration.RepeatEnabled)
                elapsed %= duration;
            else
                elapsed = Math.Min(elapsed, duration);

            var time = TimeSpan.FromSeconds(Math.Max(0, elapsed));
            var timerLabel = time.TotalHours >= 1
                ? time.ToString(@"h\:mm\:ss")
                : time.ToString(@"mm\:ss");

            var timerX = baseSize.X - 53 - timerLabel.Length * 5;

            // Clear pre-drawn colon from bottom sprite.
            var colors = this.plugin.CurrentSkin.PlaylistColors;
            var clearPos = origin + new Vector2(timerX, baseSize.Y - 15) * scale;
            var clearSize = new Vector2(timerLabel.Length * 5, 6) * scale;
            ImGui.GetWindowDrawList().AddRectFilled(clearPos, clearPos + clearSize, ImGui.GetColorU32(colors.Background));

            SkinTextRenderer.DrawText(this.plugin.CurrentSkin, timerLabel, origin + new Vector2(timerX, baseSize.Y - 15) * scale, 40 * scale, scale);
        }
    }

    private bool BottomButton(string id, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton($"##playlist_{id}", size);
        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        return clicked;
    }

    public void DrawAddWindowBody()
    {
        var skin = this.plugin.CurrentSkin;
        SkinnedPanel.BeginWindowBody(skin, "add_track", AddTracksWindow.MinSize, resizable: true, onResize: size =>
        {
            this.plugin.Configuration.AddPopupWidth = size.X;
            this.plugin.Configuration.AddPopupHeight = size.Y;
            this.plugin.Save();
        });

        SkinnedPanel.Title(skin, "ADD TO PLAYLIST");
        if (SkinnedPanel.WindowCloseClicked(skin))
            this.plugin.AddTracksWindow.IsOpen = false;

        // Drop the column headers below the title bar so they don't collide with the title.
        var contentStart = SkinnedPanel.ContentCursorScreenPosition(this.plugin.CurrentSkin) + new Vector2(0, 16);
        var contentWidth = SkinnedPanel.ContentWidth(this.plugin.CurrentSkin);
        var contentBottom = SkinnedPanel.ContentBottomY(this.plugin.CurrentSkin);
        var gap = 8.0f;
        var groupWidth = MathF.Max(145, MathF.Min(190, contentWidth * 0.36f));
        var trackWidth = MathF.Max(220, contentWidth - groupWidth - gap);
        var rightX = contentStart.X + groupWidth + gap;
        var rowHeight = MathF.Max(18, ImGui.GetTextLineHeight() + 3);

        if (ImGui.IsWindowAppearing())
        {
            this.addFilter = string.Empty;
            this.addGroupFilter = string.Empty;
            this.checkedAddOptions.Clear();
            this.addScrollOffset = 0;
            this.addGroupScrollOffset = 0;
            this.addGroup = string.IsNullOrWhiteSpace(this.plugin.Configuration.SelectedOptionGroup)
                ? this.controller.Groups.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).FirstOrDefault() ?? string.Empty
                : this.plugin.Configuration.SelectedOptionGroup;
        }

        this.DrawColumnHeader(contentStart, groupWidth, "groups");
        this.DrawColumnHeader(new Vector2(rightX, contentStart.Y), trackWidth, "tracks");

        var filterY = contentStart.Y + ImGui.GetTextLineHeight() + 7;
        ImGui.SetCursorScreenPos(new Vector2(contentStart.X, filterY));
        ImGui.SetNextItemWidth(groupWidth);
        ImGui.InputTextWithHint("##addgroupfilter", "filter groups", ref this.addGroupFilter, 128);

        ImGui.SetCursorScreenPos(new Vector2(rightX, filterY));
        ImGui.SetNextItemWidth(trackWidth);
        ImGui.InputTextWithHint("##addfilter", "filter options", ref this.addFilter, 128);

        var listTop = filterY + ImGui.GetFrameHeight() + 6;
        var buttonRowY = contentBottom - 17;
        var listHeight = MathF.Max(60, buttonRowY - listTop - 8);
        var groups = this.controller.Groups.Keys.Where(this.MatchesGroupFilter).OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToList();
        this.DrawAddGroupList(contentStart + new Vector2(0, listTop - contentStart.Y), new Vector2(groupWidth, listHeight), groups, rowHeight);

        if (string.IsNullOrWhiteSpace(this.addGroup) || !this.controller.Groups.TryGetValue(this.addGroup, out var options))
        {
            ImGui.SetCursorScreenPos(new Vector2(rightX, listTop));
            ImGui.TextDisabled("Choose a Penumbra option group first.");
        }
        else
        {
            this.DrawAddOptionsList(new Vector2(rightX, listTop), new Vector2(trackWidth, listHeight), options.Where(this.MatchesAddFilter).ToList(), rowHeight);
        }

        var buttonTotal = 96 + gap + 112 + gap + 64;
        ImGui.SetCursorScreenPos(new Vector2(contentStart.X + MathF.Max(0, (contentWidth - buttonTotal) * 0.5f), buttonRowY));
        if (SkinnedPanel.Button(this.plugin.CurrentSkin, "##add_group", "ADD GROUP", new Vector2(96, 15)))
            this.controller.AddPlaylistGroup(this.addGroup);

        SkinnedPanel.SameRow(gap);
        if (SkinnedPanel.Button(this.plugin.CurrentSkin, "##add_checked", "ADD CHECKED", new Vector2(112, 15)))
        {
            this.controller.AddPlaylistEntries(this.addGroup, this.checkedAddOptions);
            this.checkedAddOptions.Clear();
        }

        SkinnedPanel.SameRow(gap);
        if (SkinnedPanel.Button(this.plugin.CurrentSkin, "##close_add", "CLOSE", new Vector2(64, 15)))
            this.plugin.AddTracksWindow.IsOpen = false;

        SkinnedPanel.EndWindowBody(this.plugin.CurrentSkin);
    }

    private void DrawColumnHeader(Vector2 pos, float width, string label)
    {
        var drawList = ImGui.GetWindowDrawList();
        var dividerColor = ImGui.GetColorU32(this.plugin.CurrentSkin.GenColors.Divider);

        // Prefer the GEN.bmp active-letter font (the generic-window titlebar font);
        // fall back to plain ImGui text if the skin has no GEN sheet. The header sits
        // above a clean full-width underline that separates it from the list below.
        if (GenTextRenderer.DrawText(this.plugin.CurrentSkin, label, pos + new Vector2(1, 1), 1f, active: true))
        {
            var underlineY = pos.Y + 11;
            drawList.AddLine(new Vector2(pos.X, underlineY), new Vector2(pos.X + width, underlineY), dividerColor);
            return;
        }

        var text = label.ToUpperInvariant();
        var size = ImGui.CalcTextSize(text);
        drawList.AddText(pos, ImGui.GetColorU32(this.plugin.CurrentSkin.GenColors.WindowText), text);
        var lineY = pos.Y + size.Y + 2;
        drawList.AddLine(new Vector2(pos.X, lineY), new Vector2(pos.X + width, lineY), dividerColor);
    }

    private void DrawAddGroupList(Vector2 listOrigin, Vector2 listSize, IReadOnlyList<string> groups, float rowHeight)
    {
        var colors = this.plugin.CurrentSkin.GenColors;
        var scrollbarWidth = 14.0f;
        var contentHeight = groups.Count * rowHeight;
        var needsScrollbar = contentHeight > listSize.Y;
        var childWidth = MathF.Max(20, listSize.X - (needsScrollbar ? scrollbarWidth + 2 : 0));
        var maxScroll = Math.Max(0, contentHeight - listSize.Y);
        this.addGroupScrollOffset = Math.Clamp(this.addGroupScrollOffset, 0, maxScroll);

        var wheel = ImGui.GetIO().MouseWheel;
        if (ImGui.IsMouseHoveringRect(listOrigin, listOrigin + listSize) && wheel != 0)
            this.addGroupScrollOffset = Math.Clamp(this.addGroupScrollOffset - wheel * rowHeight * 3, 0, maxScroll);

        ImGui.GetWindowDrawList().AddRectFilled(listOrigin, listOrigin + listSize, ImGui.GetColorU32(colors.ItemBackground));
        ImGui.SetCursorScreenPos(listOrigin);
        ImGui.BeginChild("add_groups", new Vector2(childWidth, listSize.Y), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.SetCursorPosY(-this.addGroupScrollOffset);

        foreach (var group in groups)
        {
            var selected = string.Equals(group, this.addGroup, StringComparison.OrdinalIgnoreCase);
            var rowStart = ImGui.GetCursorScreenPos();
            ImGui.PushID(group);
            ImGui.InvisibleButton("##group", new Vector2(childWidth, rowHeight));
            if (selected)
                ImGui.GetWindowDrawList().AddRectFilled(rowStart, rowStart + new Vector2(childWidth, rowHeight), ImGui.GetColorU32(colors.Selection));

            var textColor = selected ? colors.ButtonText : colors.ItemForeground;
            ImGui.PushClipRect(rowStart, rowStart + new Vector2(childWidth, rowHeight), true);
            ImGui.GetWindowDrawList().AddText(rowStart + new Vector2(3, MathF.Max(0, (rowHeight - ImGui.GetTextLineHeight()) * 0.5f)), ImGui.GetColorU32(textColor), group);
            ImGui.PopClipRect();

            if (ImGui.IsItemClicked())
            {
                this.addGroup = group;
                this.addFilter = string.Empty;
                this.checkedAddOptions.Clear();
                this.addScrollOffset = 0;
            }

            ImGui.PopID();
        }

        if (groups.Count == 0)
            ImGui.TextDisabled("No groups");

        ImGui.EndChild();

        if (needsScrollbar)
        {
            this.DrawAddScrollbar(
                "add_group",
                listOrigin + new Vector2(listSize.X - scrollbarWidth, 0),
                new Vector2(scrollbarWidth, listSize.Y),
                contentHeight,
                listSize.Y,
                rowHeight,
                ref this.addGroupScrollOffset,
                ref this.draggingAddGroupScrollbar,
                ref this.addGroupScrollbarDragStartY,
                ref this.addGroupScrollbarDragStartOffset);
        }
    }

    private void DrawAddOptionsList(Vector2 listOrigin, Vector2 listSize, IReadOnlyList<string> options, float rowHeight)
    {
        var listHeight = listSize.Y;
        var listWidth = listSize.X;
        var scrollbarWidth = 14.0f;
        var contentHeight = options.Count * rowHeight;
        var needsScrollbar = contentHeight > listHeight;
        var childWidth = MathF.Max(20, listWidth - (needsScrollbar ? scrollbarWidth + 2 : 0));
        var maxScroll = Math.Max(0, contentHeight - listHeight);
        this.addScrollOffset = Math.Clamp(this.addScrollOffset, 0, maxScroll);

        var wheel = ImGui.GetIO().MouseWheel;
        if (ImGui.IsMouseHoveringRect(listOrigin, listOrigin + new Vector2(listWidth, listHeight)) && wheel != 0)
            this.addScrollOffset = Math.Clamp(this.addScrollOffset - wheel * rowHeight * 3, 0, maxScroll);

        ImGui.SetCursorScreenPos(listOrigin);
        ImGui.GetWindowDrawList().AddRectFilled(listOrigin, listOrigin + listSize, ImGui.GetColorU32(this.plugin.CurrentSkin.GenColors.ItemBackground));
        ImGui.BeginChild("add_options", new Vector2(childWidth, listHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.SetCursorPosY(-this.addScrollOffset);
        for (var index = 0; index < options.Count; index++)
        {
            var option = options[index];
            var exists = this.plugin.Configuration.Playlist.Any(entry => IsEntryIdentity(entry, this.addGroup, option));
            ImGui.PushID(option);
            var selected = this.checkedAddOptions.Contains(option);
            if (exists)
                this.checkedAddOptions.Remove(option);

            if (exists)
                ImGui.BeginDisabled();

            if (SkinnedPanel.ToggleButton(this.plugin.CurrentSkin, "##check", "+", selected, new Vector2(14, 14)))
            {
                if (selected)
                    this.checkedAddOptions.Remove(option);
                else
                    this.checkedAddOptions.Add(option);
            }

            ImGui.SameLine();
            if (ImGui.Selectable(exists ? $"{option} (added)" : option, false))
            {
                this.controller.AddPlaylistEntry(this.addGroup, option);
                this.checkedAddOptions.Remove(option);
            }

            ImGui.PopID();
            if (exists)
                ImGui.EndDisabled();
        }

        ImGui.EndChild();
        if (needsScrollbar)
        {
            this.DrawAddScrollbar(
                "add_track",
                listOrigin + new Vector2(listWidth - scrollbarWidth, 0),
                new Vector2(scrollbarWidth, listHeight),
                contentHeight,
                listHeight,
                rowHeight,
                ref this.addScrollOffset,
                ref this.draggingAddScrollbar,
                ref this.addScrollbarDragStartY,
                ref this.addScrollbarDragStartOffset);
        }
    }

    private void DrawAddScrollbar(
        string id,
        Vector2 pos,
        Vector2 size,
        float contentHeight,
        float viewHeight,
        float rowHeight,
        ref float scrollOffset,
        ref bool dragging,
        ref float scrollbarDragStartY,
        ref float scrollbarDragStartOffset)
    {
        var maxScroll = Math.Max(0, contentHeight - viewHeight);
        const float ArrowSize = 14.0f;
        const float ThumbHeight = 28.0f;
        var arrowSize = ArrowSize;
        ImGui.SetCursorScreenPos(pos);
        ImGui.InvisibleButton($"##{id}_scrollbar_capture", size);
        var upRect = new Vector2(pos.X, pos.Y);
        var downRect = new Vector2(pos.X, pos.Y + size.Y - arrowSize);
        var trackTop = pos.Y + arrowSize;
        var trackHeight = Math.Max(1, size.Y - arrowSize * 2);
        var mouse = ImGui.GetIO().MousePos;
        var upHovered = IsInRect(mouse, upRect, upRect + new Vector2(size.X, arrowSize));
        var downHovered = IsInRect(mouse, downRect, downRect + new Vector2(size.X, arrowSize));
        var upPressed = upHovered && ImGui.IsMouseDown(ImGuiMouseButton.Left);
        var downPressed = downHovered && ImGui.IsMouseDown(ImGuiMouseButton.Left);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(this.plugin.CurrentSkin.GenColors.ItemBackground));
        SkinRenderer.DrawSprite(this.plugin.CurrentSkin, upPressed ? "GENEX_SCROLL_UP_PRESSED" : "GENEX_SCROLL_UP", upRect, new Vector2(size.X, arrowSize));
        SkinRenderer.DrawSprite(this.plugin.CurrentSkin, downPressed ? "GENEX_SCROLL_DOWN_PRESSED" : "GENEX_SCROLL_DOWN", downRect, new Vector2(size.X, arrowSize));
        drawList.AddRectFilled(
            new Vector2(pos.X, trackTop),
            new Vector2(pos.X + size.X, trackTop + trackHeight),
            ImGui.GetColorU32(this.plugin.CurrentSkin.GenColors.ItemBackground));
        if (upHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            scrollOffset = Math.Clamp(scrollOffset - rowHeight * 3, 0, maxScroll);
        if (downHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            scrollOffset = Math.Clamp(scrollOffset + rowHeight * 3, 0, maxScroll);

        if (maxScroll <= 0)
            return;

        var handleHeight = Math.Min(ThumbHeight, trackHeight);
        var track = Math.Max(1, trackHeight - handleHeight);
        var handleY = trackTop + (scrollOffset / maxScroll) * track;
        var handlePos = new Vector2(pos.X, handleY);
        var handleSize = new Vector2(size.X, handleHeight);
        var overHandle = mouse.X >= handlePos.X && mouse.X <= handlePos.X + handleSize.X
            && mouse.Y >= handlePos.Y && mouse.Y <= handlePos.Y + handleSize.Y;
        var overTrack = mouse.X >= pos.X && mouse.X <= pos.X + size.X && mouse.Y >= trackTop && mouse.Y <= trackTop + trackHeight;

        if (overHandle && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            dragging = true;
            scrollbarDragStartY = mouse.Y;
            scrollbarDragStartOffset = scrollOffset;
        }
        else if (overTrack && !overHandle && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            scrollOffset = Math.Clamp(((mouse.Y - trackTop - handleHeight * 0.5f) / track) * maxScroll, 0, maxScroll);
        }

        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            dragging = false;

        if (dragging)
            scrollOffset = Math.Clamp(scrollbarDragStartOffset + ((mouse.Y - scrollbarDragStartY) / track) * maxScroll, 0, maxScroll);

        handleY = trackTop + (scrollOffset / maxScroll) * track;
        handlePos = new Vector2(pos.X, handleY);
        SkinRenderer.DrawSprite(this.plugin.CurrentSkin, dragging ? "GENEX_SCROLL_VERTICAL_THUMB_PRESSED" : "GENEX_SCROLL_VERTICAL_THUMB", handlePos, handleSize);
    }

    private void DrawRemovePopup()
    {
        if (!SkinnedPanel.BeginPopup(this.plugin.CurrentSkin, "remove_track", RemovePopupSize, RemovePopupSize, false, null))
            return;

        this.CenterCompactPopupContent(58);
        var entry = this.pendingRemoveIndex >= 0 && this.pendingRemoveIndex < this.plugin.Configuration.Playlist.Count
            ? this.plugin.Configuration.Playlist[this.pendingRemoveIndex]
            : null;
        SkinnedPanel.Title(this.plugin.CurrentSkin, "Remove selected track");
        //SkinnedPanel.TextCentered(this.plugin.CurrentSkin, "Remove selected track?");
        if (entry is not null)
            SkinnedPanel.TextCentered(this.plugin.CurrentSkin, entry.Label);

        this.ConfirmButtonRow(80 + 5 + 80);
        if (SkinnedPanel.Button(this.plugin.CurrentSkin, "##confirm_remove", "REMOVE", new Vector2(80, 15)))
        {
            this.controller.RemovePlaylistEntry(this.pendingRemoveIndex);
            this.pendingRemoveIndex = -1;
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine(0, 5);
        if (SkinnedPanel.Button(this.plugin.CurrentSkin, "##cancel_remove", "CANCEL", new Vector2(80, 15)))
        {
            this.pendingRemoveIndex = -1;
            ImGui.CloseCurrentPopup();
        }

        SkinnedPanel.EndPopup(this.plugin.CurrentSkin);
    }

    private void DrawCropPopup()
    {
        if (!SkinnedPanel.BeginPopup(this.plugin.CurrentSkin, "crop_playlist", ClearPopupSize, ClearPopupSize, false, null))
            return;

        this.CenterCompactPopupContent(58);
        var index = this.plugin.Configuration.CurrentIndex;
        var entry = index >= 0 && index < this.plugin.Configuration.Playlist.Count
            ? this.plugin.Configuration.Playlist[index]
            : null;
        SkinnedPanel.Title(this.plugin.CurrentSkin, "Remove all except this");
        if (entry is not null)
            SkinnedPanel.TextCentered(this.plugin.CurrentSkin, entry.Label);

        this.ConfirmButtonRow(80 + 5 + 80);
        if (SkinnedPanel.Button(this.plugin.CurrentSkin, "##confirm_crop", "CROP", new Vector2(80, 15)))
        {
            this.controller.CropToEntry(index);
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine(0, 5);
        if (SkinnedPanel.Button(this.plugin.CurrentSkin, "##cancel_crop", "CANCEL", new Vector2(80, 15)))
            ImGui.CloseCurrentPopup();

        SkinnedPanel.EndPopup(this.plugin.CurrentSkin);
    }

    private void DrawMiscPopup()
    {
        if (!SkinnedPanel.BeginPopup(this.plugin.CurrentSkin, "clear_playlist", ClearPopupSize, ClearPopupSize, false, null))
            return;

        this.CenterCompactPopupContent(58);
        SkinnedPanel.Title(this.plugin.CurrentSkin, "Clear active playlist");
        SkinnedPanel.TextCentered(this.plugin.CurrentSkin, "Saved playlist presets are not deleted.", true);
        this.ConfirmButtonRow(80 + 5 + 80);
        if (SkinnedPanel.Button(this.plugin.CurrentSkin, "##confirm_clear", "CLEAR", new Vector2(80, 15)))
        {
            this.controller.ClearPlaylist();
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine(0, 5);
        if (SkinnedPanel.Button(this.plugin.CurrentSkin, "##cancel_clear", "CANCEL", new Vector2(80, 15)))
            ImGui.CloseCurrentPopup();

        SkinnedPanel.EndPopup(this.plugin.CurrentSkin);
    }

    private bool MatchesAddFilter(string option)
        => string.IsNullOrWhiteSpace(this.addFilter) || option.Contains(this.addFilter, StringComparison.OrdinalIgnoreCase);

    private bool MatchesGroupFilter(string group)
        => string.IsNullOrWhiteSpace(this.addGroupFilter) || group.Contains(this.addGroupFilter, StringComparison.OrdinalIgnoreCase);

    private void StartRenaming(int index, PlaylistEntry entry)
    {
        this.renameIndex = index;
        this.renameBuffer = entry.DisplayName;
        this.durationBuffer = FormatDurationForEdit(entry);
        this.bitrateBuffer = entry.BitrateKbps > 0 ? entry.BitrateKbps.ToString(CultureInfo.InvariantCulture) : string.Empty;
        this.khzBuffer = entry.SampleRate > 0 ? (entry.SampleRate / 1000.0).ToString("0.###", CultureInfo.InvariantCulture) : string.Empty;
        this.propertiesError = string.Empty;
        ImGui.OpenPopup("rename");
    }

    private void DrawRenamePopup(int index, PlaylistEntry entry)
    {
        if (!SkinnedPanel.BeginPopup(
                this.plugin.CurrentSkin,
                "rename",
                new Vector2(this.plugin.Configuration.TrackPropertiesPopupWidth, this.plugin.Configuration.TrackPropertiesPopupHeight),
                new Vector2(455, 260),
                true,
                size =>
                {
                    this.plugin.Configuration.TrackPropertiesPopupWidth = size.X;
                    this.plugin.Configuration.TrackPropertiesPopupHeight = size.Y;
                    this.plugin.Save();
                }))
            return;

        if (this.renameIndex != index)
        {
            SkinnedPanel.EndPopup(this.plugin.CurrentSkin);
            return;
        }

        SkinnedPanel.Title(this.plugin.CurrentSkin, "track properties");
        var contentWidth = MathF.Max(260, SkinnedPanel.ContentWidth(this.plugin.CurrentSkin));
        var fieldOffset = 50.0f;
        var fieldWidth = MathF.Max(150, contentWidth - fieldOffset);

        // ── Header ──
        ImGui.TextUnformatted(entry.OptionName);
        if (!string.IsNullOrWhiteSpace(entry.OptionGroup))
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"[{entry.OptionGroup}]");
        }

        ImGui.Separator();

        // ── Metadata fields ──
        ImGui.TextDisabled("name");
        ImGui.SameLine(fieldOffset);
        ImGui.SetNextItemWidth(fieldWidth);
        if (ImGui.IsWindowAppearing())
            ImGui.SetKeyboardFocusHere();

        var save = ImGui.InputTextWithHint("##rename", "custom display name", ref this.renameBuffer, 96, ImGuiInputTextFlags.EnterReturnsTrue);

        ImGui.TextDisabled("length");
        ImGui.SameLine(fieldOffset);
        ImGui.SetNextItemWidth(80);
        save |= ImGui.InputTextWithHint("##duration", "mm:ss", ref this.durationBuffer, 32, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        ImGui.TextDisabled("mm:ss or seconds");

        ImGui.TextDisabled("kbps");
        ImGui.SameLine(fieldOffset);
        ImGui.SetNextItemWidth(80);
        save |= ImGui.InputTextWithHint("##kbps", "192", ref this.bitrateBuffer, 16, ImGuiInputTextFlags.EnterReturnsTrue);

        ImGui.SameLine();
        ImGui.Dummy(new Vector2(12, 0));
        ImGui.SameLine();
        ImGui.TextDisabled("kHz");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(60);
        save |= ImGui.InputTextWithHint("##khz", "44", ref this.khzBuffer, 16, ImGuiInputTextFlags.EnterReturnsTrue);

        // ── Status line ──
        if (!string.IsNullOrWhiteSpace(this.propertiesError))
        {
            var isError = this.propertiesError.StartsWith("No SCD", StringComparison.Ordinal)
                || this.propertiesError.StartsWith("SCD found but", StringComparison.Ordinal)
                || this.propertiesError.StartsWith("Invalid", StringComparison.Ordinal);
            var color = isError
                ? new Vector4(1.0f, 0.4f, 0.3f, 1.0f)
                : new Vector4(0.4f, 1.0f, 0.5f, 1.0f);
            ImGui.TextColored(color, this.propertiesError);
        }

        ImGui.Dummy(new Vector2(0, 2));

        // ── Primary actions ──
        if (save || SkinnedPanel.Button(this.plugin.CurrentSkin, "##save_track_props", "SAVE", new Vector2(64, 15)))
        {
            if (this.TrySaveProperties(entry))
                ImGui.CloseCurrentPopup();
        }

        SkinnedPanel.SameRow();
        if (SkinnedPanel.Button(this.plugin.CurrentSkin, "##cancel_track_props", "CANCEL", new Vector2(72, 15)))
            ImGui.CloseCurrentPopup();

        ImGui.Dummy(new Vector2(0, 2));

        // ── Secondary actions ──
        if (SkinnedPanel.Button(this.plugin.CurrentSkin, "##scan_track_meta", "SCAN", new Vector2(52, 15)))
        {
            entry.DurationSeconds = 0;
            entry.SampleRate = 0;
            entry.BitrateKbps = 0;
            entry.ScdPath = string.Empty;
            this.controller.AudioMetadata.InvalidateCache();
            this.controller.AudioMetadata.Populate(entry, this.plugin.Configuration.SelectedModDirectory, this.plugin.Penumbra);
            this.plugin.Save();
            if (entry.DurationSeconds > 0)
            {
                this.durationBuffer = FormatDurationForEdit(entry);
                this.bitrateBuffer = entry.BitrateKbps > 0 ? entry.BitrateKbps.ToString(CultureInfo.InvariantCulture) : string.Empty;
                this.khzBuffer = entry.SampleRate > 0 ? (entry.SampleRate / 1000.0).ToString("0.###", CultureInfo.InvariantCulture) : string.Empty;
                this.propertiesError = $"Found: {FormatDuration(entry.DurationSeconds)}, {entry.BitrateKbps}kbps, {entry.SampleRate / 1000.0:0.#}kHz";
            }
            else
            {
                this.propertiesError = string.IsNullOrWhiteSpace(entry.ScdPath)
                    ? "No SCD file found in mod directory."
                    : $"SCD found but could not read metadata: {Path.GetFileName(entry.ScdPath)}";
            }
        }

        if (ImGui.IsItemHovered())
        {
            var tip = "Scan for SCD file and extract metadata.";
            if (!string.IsNullOrWhiteSpace(entry.ScdPath))
                tip += $"\nCurrent: {Path.GetFileName(entry.ScdPath)}";
            ImGui.SetTooltip(tip);
        }

        SkinnedPanel.SameRow();
        if (SkinnedPanel.Button(this.plugin.CurrentSkin, "##clear_track_meta", "CLEAR META", new Vector2(92, 15)))
        {
            entry.Duration = string.Empty;
            entry.DurationSeconds = 0;
            entry.BitrateKbps = 0;
            entry.SampleRate = 0;
            entry.ScdPath = string.Empty;
            this.durationBuffer = string.Empty;
            this.bitrateBuffer = string.Empty;
            this.khzBuffer = string.Empty;
            this.propertiesError = string.Empty;
            this.plugin.Save();
        }

        SkinnedPanel.SameRow();
        if (SkinnedPanel.Button(this.plugin.CurrentSkin, "##clear_track_name", "CLEAR NAME", new Vector2(92, 15)))
        {
            entry.DisplayName = string.Empty;
            this.renameBuffer = string.Empty;
            this.plugin.Save();
        }

        SkinnedPanel.EndPopup(this.plugin.CurrentSkin);
    }

    private bool TrySaveProperties(PlaylistEntry entry)
    {
        this.propertiesError = string.Empty;

        var durationText = this.durationBuffer.Trim();
        var bitrateText = this.bitrateBuffer.Trim();
        var khzText = this.khzBuffer.Trim();

        var durationSeconds = 0.0;
        if (!string.IsNullOrWhiteSpace(durationText) && !TryParseDuration(durationText, out durationSeconds))
        {
            this.propertiesError = "Invalid length. Use mm:ss, hh:mm:ss, or seconds.";
            return false;
        }

        var bitrate = 0;
        if (!string.IsNullOrWhiteSpace(bitrateText)
            && (!int.TryParse(bitrateText, NumberStyles.Integer, CultureInfo.InvariantCulture, out bitrate) || bitrate <= 0))
        {
            this.propertiesError = "Invalid kbps.";
            return false;
        }

        var sampleRate = 0;
        if (!string.IsNullOrWhiteSpace(khzText))
        {
            if (!double.TryParse(khzText, NumberStyles.Float, CultureInfo.InvariantCulture, out var khz) || khz <= 0)
            {
                this.propertiesError = "Invalid kHz.";
                return false;
            }

            sampleRate = (int)Math.Round(khz * 1000);
        }

        entry.DisplayName = this.renameBuffer.Trim();
        entry.DurationSeconds = durationSeconds;
        entry.Duration = durationSeconds > 0 ? FormatDuration(durationSeconds) : string.Empty;
        entry.BitrateKbps = bitrate;
        entry.SampleRate = sampleRate;
        if (durationSeconds == 0 && bitrate == 0 && sampleRate == 0)
            entry.ScdPath = string.Empty;

        this.durationBuffer = entry.Duration;
        this.bitrateBuffer = entry.BitrateKbps > 0 ? entry.BitrateKbps.ToString(CultureInfo.InvariantCulture) : string.Empty;
        this.khzBuffer = entry.SampleRate > 0 ? (entry.SampleRate / 1000.0).ToString("0.###", CultureInfo.InvariantCulture) : string.Empty;
        this.plugin.Save();
        return true;
    }

    private bool PlaylistUsesMultipleGroups()
        => this.plugin.Configuration.Playlist
            .Select(entry => entry.OptionGroup)
            .Where(group => !string.IsNullOrWhiteSpace(group))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Skip(1)
            .Any();

    private static bool IsEntryIdentity(PlaylistEntry entry, string optionGroup, string optionName)
        => PlaylistFormat.IsEntryIdentity(entry, optionGroup, optionName);

    private static bool IsInRect(Vector2 point, Vector2 min, Vector2 max)
        => point.X >= min.X && point.X <= max.X && point.Y >= min.Y && point.Y <= max.Y;

    private static string FormatDurationForEdit(PlaylistEntry entry)
        => entry.DurationSeconds > 0 ? FormatDuration(entry.DurationSeconds) : entry.Duration;

    private static string DurationLabel(PlaylistEntry entry)
        => entry.DurationSeconds > 0
            ? FormatDuration(entry.DurationSeconds)
            : entry.Duration.Trim();

    private static string FormatDuration(double seconds)
        => PlaylistFormat.FormatDuration(seconds);

    private static bool TryParseDuration(string value, out double seconds)
        => PlaylistFormat.TryParseDuration(value, out seconds);

    private void DrawChrome(Vector2 origin, Vector2 size, float scale)
    {
        var baseSize = size / scale;
        if (this.plugin.CurrentSkin.HasPlaylistTexture)
        {
            var selectedSuffix = string.Empty;
            SkinRenderer.DrawSprite(this.plugin.CurrentSkin, $"PLAYLIST_TOP_LEFT_CORNER{selectedSuffix}", origin, new Vector2(25, 20) * scale);
            var titleX = MathF.Max(25, MathF.Floor((baseSize.X - 100) * 0.5f));
            SkinRenderer.TileHorizontal(this.plugin.CurrentSkin, $"PLAYLIST_TOP_TILE{selectedSuffix}", origin + new Vector2(25, 0) * scale, (titleX - 25) * scale, scale);
            SkinRenderer.DrawSprite(this.plugin.CurrentSkin, $"PLAYLIST_TITLE_BAR{selectedSuffix}", origin + new Vector2(titleX, 0) * scale, new Vector2(100, 20) * scale);
            SkinRenderer.TileHorizontal(this.plugin.CurrentSkin, $"PLAYLIST_TOP_TILE{selectedSuffix}", origin + new Vector2(titleX + 100, 0) * scale, (baseSize.X - titleX - 125) * scale, scale);
            SkinRenderer.DrawSprite(this.plugin.CurrentSkin, $"PLAYLIST_TOP_RIGHT_CORNER{selectedSuffix}", origin + new Vector2(baseSize.X - 25, 0) * scale, new Vector2(25, 20) * scale);

            SkinRenderer.TileVertical(this.plugin.CurrentSkin, "PLAYLIST_LEFT_TILE", origin + new Vector2(0, 20) * scale, (baseSize.Y - 58) * scale, scale);
            SkinRenderer.TileVertical(this.plugin.CurrentSkin, "PLAYLIST_RIGHT_TILE", origin + new Vector2(baseSize.X - 20, 20) * scale, (baseSize.Y - 58) * scale, scale);
            SkinRenderer.DrawSprite(this.plugin.CurrentSkin, "PLAYLIST_BOTTOM_LEFT_CORNER", origin + new Vector2(0, baseSize.Y - 38) * scale, new Vector2(125, 38) * scale);
            SkinRenderer.TileHorizontal(this.plugin.CurrentSkin, "PLAYLIST_BOTTOM_TILE", origin + new Vector2(125, baseSize.Y - 38) * scale, Math.Max(0, baseSize.X - 275) * scale, scale);
            SkinRenderer.DrawSprite(this.plugin.CurrentSkin, "PLAYLIST_BOTTOM_RIGHT_CORNER", origin + new Vector2(baseSize.X - 150, baseSize.Y - 38) * scale, new Vector2(150, 38) * scale);
            ImGui.GetWindowDrawList().AddRectFilled(origin + new Vector2(12, 23) * scale, origin + new Vector2(baseSize.X - 20, baseSize.Y - 38) * scale, ImGui.GetColorU32(this.plugin.CurrentSkin.PlaylistColors.Background));
            return;
        }

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(origin, origin + size, ImGui.GetColorU32(new Vector4(0.11f, 0.11f, 0.16f, 1.0f)));
        drawList.AddRectFilled(origin + new Vector2(8, 20) * scale, origin + new Vector2(baseSize.X - 8, baseSize.Y - 26) * scale, ImGui.GetColorU32(new Vector4(0.0f, 0.0f, 0.0f, 1.0f)));
        drawList.AddRect(origin, origin + size, ImGui.GetColorU32(new Vector4(0.72f, 0.72f, 0.82f, 1.0f)));
    }

    private void UpdateDocking(float scale)
    {
        // The playlist is always docked directly below the player window, taking the
        // player's current (full or shaded) height into account.
        if (!this.plugin.Configuration.HasPlayerWindowPosition)
            return;

        var mainHeight = this.plugin.PlayerWindow.CurrentSize.Y * scale;
        this.DockTo(new Vector2(this.plugin.Configuration.PlayerWindowX, this.plugin.Configuration.PlayerWindowY + mainHeight));
    }

    private void HandleResize(Vector2 origin, Vector2 baseSize, float scale)
    {
        var mouse = ImGui.GetIO().MousePos;
        var gripSize = new Vector2(18, 18) * scale;
        var gripMin = origin + baseSize * scale - gripSize;
        var gripMax = origin + baseSize * scale;
        var hovered = mouse.X >= gripMin.X && mouse.X <= gripMax.X && mouse.Y >= gripMin.Y && mouse.Y <= gripMax.Y;
        if (hovered || this.resizing)
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);

        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            this.resizing = true;
            this.resizeStartMouse = mouse;
            this.resizeStartSize = baseSize;
        }

        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            if (this.resizing)
                this.plugin.Save();

            this.resizing = false;
        }

        if (!this.resizing)
            return;

        var delta = (mouse - this.resizeStartMouse) / scale;
        var next = this.SnapSize(this.resizeStartSize + delta);
        this.plugin.Configuration.PlaylistWidth = next.X;
        this.plugin.Configuration.PlaylistHeight = next.Y;
        this.Size = next * scale;
        this.SizeCondition = ImGuiCond.Always;
    }

    private Vector2 BaseSize()
        => this.SnapSize(new Vector2(
            this.plugin.Configuration.PlaylistWidth <= 0 ? MinSize.X : this.plugin.Configuration.PlaylistWidth,
            this.plugin.Configuration.PlaylistHeight <= 0 ? MinSize.Y : this.plugin.Configuration.PlaylistHeight));

    private Vector2 ListSize(Vector2 baseSize)
        => new(Math.Max(1, baseSize.X - 32), Math.Max(1, baseSize.Y - 61));

    private Vector2 SnapSize(Vector2 size)
    {
        var width = MinSize.X + MathF.Round(MathF.Max(0, size.X - MinSize.X) / WidthStep) * WidthStep;
        var height = MinSize.Y + MathF.Round(MathF.Max(0, size.Y - MinSize.Y) / HeightStep) * HeightStep;
        return new Vector2(Math.Max(MinSize.X, width), Math.Max(MinSize.Y, height));
    }

    private static unsafe bool TryReadPayloadIndex(ImGuiPayloadPtr payload, out int index)
    {
        index = -1;
        if (payload.Handle == null)
            return false;

        var bytes = new ReadOnlySpan<byte>(payload.Data, payload.DataSize);
        if (bytes.Length < sizeof(int))
            return false;

        index = MemoryMarshal.Read<int>(bytes);
        return true;
    }

    private void CenterCompactPopupContent(float blockHeight)
    {
        var content = SkinnedPanel.ContentCursorScreenPosition(this.plugin.CurrentSkin);
        var bodyTop = ImGui.GetWindowPos().Y + 20;
        var bodyBottom = SkinnedPanel.ContentBottomY(this.plugin.CurrentSkin);
        var y = bodyTop + MathF.Max(0, (bodyBottom - bodyTop - blockHeight) * 0.5f);
        ImGui.SetCursorScreenPos(new Vector2(content.X, y));
    }

    /// <summary>Place the next (centered) button row just above the window's bottom border.</summary>
    private void ConfirmButtonRow(float buttonsWidth)
    {
        var content = SkinnedPanel.ContentCursorScreenPosition(this.plugin.CurrentSkin);
        var y = SkinnedPanel.ContentBottomY(this.plugin.CurrentSkin) - 16;
        ImGui.SetCursorScreenPos(new Vector2(content.X, y));
        SkinnedPanel.CenterNextItem(this.plugin.CurrentSkin, buttonsWidth);
    }

}
