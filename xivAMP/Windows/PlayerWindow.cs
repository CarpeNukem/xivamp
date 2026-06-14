using System.Numerics;
using Dalamud.Game.Config;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using xivAMP.Services;
using xivAMP.Skin;

namespace xivAMP.Windows;

public sealed class PlayerWindow : Window
{
    private static readonly Vector2 BaseSize = new(275, 116);
    private static readonly Vector2 ShadeSize = new(275, 14);
    private const int VisualizerBars = 19;
    private const int VisualizerRows = 16;

    private readonly Plugin plugin;
    private readonly XivAmpController controller;
    private readonly SetupPopup setupPopup;
    private readonly float[] visualizerPeaks = new float[VisualizerBars];
    private bool appliedInitialPosition;
    private bool draggingTitlebar;
    private Vector2 dragOffset;
    private Vector2? pendingPosition;
    private float scrollOffset;
    private DateTime lastPositionSave;
    private string autoAdvancedOptionName = string.Empty;

    public PlayerWindow(Plugin plugin, XivAmpController controller, FileDialogManager fileDialogManager)
        : base("xivAMP###xivAMPPlayer", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize)
    {
        this.plugin = plugin;
        this.controller = controller;
        this.setupPopup = new SetupPopup(plugin, controller, fileDialogManager);
        this.Size = SkinHelper.Scaled(plugin.Configuration, BaseSize);
        this.SizeCondition = ImGuiCond.Always;
    }

    public Vector2 CurrentSize => this.plugin.Configuration.MainWindowShade ? ShadeSize : BaseSize;

    public override void OnClose()
    {
        // Closing the player UI fully releases control over the music mod:
        // restore the default option and clear xivAMP's temporary Penumbra settings.
        this.controller.ReleaseControl();
    }

    public override void PreDraw()
    {
        // ImGui's default WindowMinSize (32px) would otherwise clamp the shaded
        // window, leaving an empty strip with a border below the 14px title bar.
        // Zeroing WindowPadding is required too: the default 8px padding makes the
        // 14px-tall window's content area degenerate, which breaks hit-testing on
        // the mini transport/seek controls.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        this.Size = SkinHelper.Scaled(this.plugin.Configuration, this.CurrentSize);
        if (this.pendingPosition is { } position)
        {
            this.Position = position;
            this.PositionCondition = ImGuiCond.Always;
            this.pendingPosition = null;
            return;
        }

        if (!this.appliedInitialPosition && this.plugin.Configuration.HasPlayerWindowPosition)
        {
            this.Position = new Vector2(this.plugin.Configuration.PlayerWindowX, this.plugin.Configuration.PlayerWindowY);
            this.PositionCondition = ImGuiCond.Always;
            this.appliedInitialPosition = true;
        }
    }

    public override void PostDraw()
    {
        // Balance the WindowMinSize + WindowPadding style vars pushed in PreDraw.
        ImGui.PopStyleVar(2);
    }

    public override void Draw()
    {
        var scale = SkinHelper.SkinScale(this.plugin.Configuration);
        this.Size = this.CurrentSize * scale;
        var origin = ImGui.GetWindowPos();

        if (!this.draggingTitlebar)
            this.PositionCondition = ImGuiCond.None;

        this.ThrottledSavePosition(origin);
        this.plugin.PlaylistWindow.DockTo(origin + new Vector2(0, this.CurrentSize.Y * scale));

        // Never let a drawing/handler exception escape Draw: if it did, ImGui's
        // Begin/End and style-var stacks would be left unbalanced and crash the game.
        try
        {
            if (this.plugin.Configuration.MainWindowShade)
            {
                this.DrawShade(origin, scale);
            }
            else
            {
                this.DrawChrome(origin, scale);
                this.DrawVisualizer(origin, scale);
                this.DrawClock(origin, scale);
                this.DrawMainText(origin, scale);
                this.DrawMediaInfo(origin, scale);
                this.DrawSliders(origin, scale);
                this.DrawPositionBar(origin, scale);
                this.DrawTransport(origin, scale);
                this.DrawShuffleRepeat(origin, scale);
                this.DrawWindowButtons(origin, scale);
                this.DrawLogoLink(origin, scale);
            }

            this.HandleTitlebarDrag(origin, scale);
            this.CheckAutoAdvance();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "xivAMP player window draw failed.");
            this.controller.SetStatus($"Error: {ex.Message}");
        }

        if (this.plugin.SetupPopupRequested)
        {
            this.plugin.SetupPopupRequested = false;
            ImGui.OpenPopup("xivamp_setup");
        }

        if (SkinnedPanel.BeginPopup(
                this.plugin.CurrentSkin,
                "xivamp_setup",
                new Vector2(this.plugin.Configuration.SetupPopupWidth, this.plugin.Configuration.SetupPopupHeight),
                new Vector2(405, 305),
                true,
                size =>
                {
                    this.plugin.Configuration.SetupPopupWidth = size.X;
                    this.plugin.Configuration.SetupPopupHeight = size.Y;
                    this.plugin.Save();
                }))
        {
            this.setupPopup.Draw();
            SkinnedPanel.EndPopup(this.plugin.CurrentSkin);
        }
    }

    private const string DiscordUrl = "https://discord.gg/kxZMbP3C5B";

    private void DrawLogoLink(Vector2 origin, float scale)
    {
        // The Winamp lightning logo (bottom-right of the main window) links to Discord.
        var pos = origin + new Vector2(249, 90) * scale;
        var size = new Vector2(20, 18) * scale;
        ImGui.SetCursorScreenPos(pos);
        if (ImGui.InvisibleButton("##xivamp_discord", size))
            Dalamud.Utility.Util.OpenLink(DiscordUrl);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip("Join the //n_root Discord");
        }
    }

    private void DrawShade(Vector2 origin, float scale)
    {
        // Window-shade ("minimized") main window: the whole player collapses to the
        // 14px title bar, as in the original Winamp. The 3rd row of TITLEBAR.bmp
        // already bakes in the menu icon, the title/time display boxes, the mini
        // transport icons, the eject button and the seek-bar frame, so we only draw
        // the active background and overlay the live values and click targets.
        if (!SkinRenderer.DrawSprite(this.plugin.CurrentSkin, "MAIN_SHADE_BACKGROUND_SELECTED", origin, ShadeSize * scale))
        {
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRectFilled(origin, origin + ShadeSize * scale, ImGui.GetColorU32(new Vector4(0.13f, 0.13f, 0.18f, 1.0f)));
            drawList.AddRect(origin, origin + ShadeSize * scale, ImGui.GetColorU32(new Vector4(0.72f, 0.72f, 0.82f, 1.0f)));
        }

        // Options / main menu button.
        if (SkinButton.Draw(this.plugin.CurrentSkin, "shade_options", "MAIN_OPTIONS_BUTTON", "MAIN_OPTIONS_BUTTON_DEPRESSED", origin + new Vector2(6, 3) * scale, new Vector2(9, 9) * scale))
            ImGui.OpenPopup("xivamp_setup");

        // Scrolling track title (left display box).
        var current = this.controller.AppliedEntry() ?? this.controller.CurrentEntry();
        var title = current?.Label ?? "no track loaded";
        if (!SkinTextRenderer.DrawScrollingText(this.plugin.CurrentSkin, title, origin + new Vector2(82, 4) * scale, 32 * scale, scale, ref this.scrollOffset))
            DrawDisplayText(origin + new Vector2(82, 4) * scale, new Vector2(32, 8) * scale, title);

        // Time (right display box). The baked colon sits at x≈144.
        this.DrawShadeTime(origin, scale);

        // Playing / paused indicator over the left edge of the title box.
        if (this.plugin.Configuration.IsPaused)
        {
            if (this.BlinkVisible())
                SkinRenderer.DrawSprite(this.plugin.CurrentSkin, "MAIN_PAUSED_INDICATOR", origin + new Vector2(69, 3) * scale, scale);
        }
        else if (!string.IsNullOrWhiteSpace(this.plugin.Configuration.LastAppliedOptionName))
        {
            SkinRenderer.DrawSprite(this.plugin.CurrentSkin, "MAIN_PLAYING_INDICATOR", origin + new Vector2(69, 3) * scale, scale);
        }

        // Mini transport controls (icons are baked into the background; overlay hit areas).
        if (this.ShadeHit(origin, scale, "s_prev", 166, 10))
            this.controller.ApplyRelative(-1);
        if (this.ShadeHit(origin, scale, "s_play", 176, 10))
            this.controller.ApplyCurrent();
        if (this.ShadeHit(origin, scale, "s_pause", 186, 10))
            this.controller.PauseCurrent();
        if (this.ShadeHit(origin, scale, "s_stop", 196, 10))
            this.controller.StopCurrent();
        if (this.ShadeHit(origin, scale, "s_next", 206, 9))
            this.controller.ApplyRelative(1);
        if (this.ShadeHit(origin, scale, "s_eject", 215, 10))
            ImGui.OpenPopup("xivamp_setup");

        // Mini position / seek bar.
        this.DrawShadePositionBar(origin, scale);

        // Restore (un-shade) and close buttons.
        if (SkinButton.Draw(this.plugin.CurrentSkin, "unshade", "MAIN_UNSHADE_BUTTON", "MAIN_UNSHADE_BUTTON_DEPRESSED", origin + new Vector2(254, 3) * scale, new Vector2(9, 9) * scale))
            this.ToggleShade();

        if (SkinButton.Draw(this.plugin.CurrentSkin, "shade_close", "MAIN_CLOSE_BUTTON", "MAIN_CLOSE_BUTTON_DEPRESSED", origin + new Vector2(264, 3) * scale, new Vector2(9, 9) * scale))
        {
            this.IsOpen = false;
            this.plugin.PlaylistWindow.IsOpen = false;
        }
    }

    private void DrawShadeTime(Vector2 origin, float scale)
    {
        if (this.plugin.Configuration.IsStopped)
            return;

        string label;
        if (this.plugin.Configuration.IsPaused)
        {
            if (!this.BlinkVisible())
                return;

            label = "00:00";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(this.plugin.Configuration.LastAppliedOptionName)
                || this.plugin.Configuration.LastAppliedAtUtc == default)
                return;

            var duration = this.TrackDurationSeconds();
            var totalSeconds = (int)Math.Max(0, this.EstimatedElapsedSeconds(duration));
            var minutes = Math.Min(totalSeconds / 60, 99);
            label = $"{minutes:00}:{totalSeconds % 60:00}";
        }

        // Position so the ':' (3rd glyph) lands on the baked colon at x≈144.
        if (!SkinTextRenderer.DrawText(this.plugin.CurrentSkin, label, origin + new Vector2(134, 4) * scale, 30 * scale, scale))
            DrawDisplayText(origin + new Vector2(134, 4) * scale, new Vector2(30, 8) * scale, label);
    }

    private void DrawShadePositionBar(Vector2 origin, float scale)
    {
        // The seek frame is baked into the shade background; overlay the thumb + hit area.
        var trackPos = origin + new Vector2(226, 4) * scale;
        var trackSize = new Vector2(17, 7) * scale;

        var duration = this.TrackDurationSeconds();
        if (!this.plugin.Configuration.IsPaused && !this.plugin.Configuration.IsStopped && !string.IsNullOrWhiteSpace(this.plugin.Configuration.LastAppliedOptionName))
        {
            var progress = this.EstimatedProgress(duration);
            var thumbWidth = 10 * scale;
            var thumbX = trackPos.X + Math.Clamp(progress, 0, 1) * Math.Max(0, trackSize.X - thumbWidth);
            if (!SkinRenderer.DrawSprite(this.plugin.CurrentSkin, "MAIN_SHADE_POSITION_THUMB", new Vector2(thumbX, trackPos.Y), new Vector2(10, 7) * scale))
                ImGui.GetWindowDrawList().AddRectFilled(new Vector2(thumbX, trackPos.Y), new Vector2(thumbX + thumbWidth, trackPos.Y + trackSize.Y), ImGui.GetColorU32(new Vector4(0.65f, 0.67f, 0.78f, 1.0f)));
        }

        ImGui.SetCursorScreenPos(trackPos);
        if (ImGui.InvisibleButton("##shade_position", trackSize) && duration > 0)
        {
            var local = Math.Clamp((ImGui.GetIO().MousePos.X - trackPos.X) / trackSize.X, 0, 1);
            this.controller.SetEstimatedSeek(local * duration);
        }
    }

    private bool ShadeHit(Vector2 origin, float scale, string id, float localX, float width)
    {
        ImGui.SetCursorScreenPos(origin + new Vector2(localX, 3) * scale);
        return ImGui.InvisibleButton($"##{id}", new Vector2(width, 9) * scale);
    }

    private void DrawMainText(Vector2 origin, float scale)
    {
        var current = this.controller.AppliedEntry() ?? this.controller.CurrentEntry();
        var title = current?.Label ?? "no track loaded";
        if (!SkinTextRenderer.DrawScrollingText(this.plugin.CurrentSkin, title, origin + new Vector2(111, 27) * scale, 154 * scale, scale, ref this.scrollOffset))
            DrawDisplayText(origin + new Vector2(111, 24) * scale, new Vector2(154, 15) * scale, title);

        if (this.plugin.Configuration.IsStopped)
        {
            SkinRenderer.DrawSprite(this.plugin.CurrentSkin, "MAIN_STOPPED_INDICATOR", origin + new Vector2(26, 28) * scale, scale);
        }
        else if (this.plugin.Configuration.IsPaused)
        {
            if (this.BlinkVisible())
                SkinRenderer.DrawSprite(this.plugin.CurrentSkin, "MAIN_PAUSED_INDICATOR", origin + new Vector2(26, 28) * scale, scale);
        }
        else if (!string.IsNullOrWhiteSpace(this.plugin.Configuration.LastAppliedOptionName))
        {
            SkinRenderer.DrawSprite(this.plugin.CurrentSkin, "MAIN_PLAYING_INDICATOR", origin + new Vector2(26, 28) * scale, scale);
        }
    }

    private void DrawClock(Vector2 origin, float scale)
    {
        // Stopped: no time display (matches Webamp's .stop #time { display: none }).
        if (this.plugin.Configuration.IsStopped)
            return;

        if (this.plugin.Configuration.IsPaused)
        {
            if (this.BlinkVisible())
                this.DrawTimerLabel(origin, scale, "00:00");

            return;
        }

        if (string.IsNullOrWhiteSpace(this.plugin.Configuration.LastAppliedOptionName)
            || this.plugin.Configuration.LastAppliedAtUtc == default)
            return;

        var duration = this.TrackDurationSeconds();
        var elapsed = this.EstimatedElapsedSeconds(duration);

        // Winamp main clock shows total minutes (e.g. 62:03), capped at 99:59.
        var totalSeconds = (int)Math.Max(0, elapsed);
        var minutes = Math.Min(totalSeconds / 60, 99);
        var label = $"{minutes:00}:{totalSeconds % 60:00}";

        this.DrawTimerLabel(origin, scale, label);
    }

    private void DrawTransport(Vector2 origin, float scale)
    {
        if (SkinButton.Draw(this.plugin.CurrentSkin, "prev", "MAIN_PREVIOUS_BUTTON", "MAIN_PREVIOUS_BUTTON_ACTIVE", origin + new Vector2(16, 88) * scale, new Vector2(23, 18) * scale))
            this.controller.ApplyRelative(-1);

        if (SkinButton.Draw(this.plugin.CurrentSkin, "play", "MAIN_PLAY_BUTTON", "MAIN_PLAY_BUTTON_ACTIVE", origin + new Vector2(39, 88) * scale, new Vector2(23, 18) * scale))
            this.controller.ApplyCurrent();

        if (SkinButton.Draw(this.plugin.CurrentSkin, "pause", "MAIN_PAUSE_BUTTON", "MAIN_PAUSE_BUTTON_ACTIVE", origin + new Vector2(62, 88) * scale, new Vector2(23, 18) * scale))
            this.controller.PauseCurrent();

        if (SkinButton.Draw(this.plugin.CurrentSkin, "stop", "MAIN_STOP_BUTTON", "MAIN_STOP_BUTTON_ACTIVE", origin + new Vector2(85, 88) * scale, new Vector2(23, 18) * scale))
            this.controller.StopCurrent();

        if (SkinButton.Draw(this.plugin.CurrentSkin, "next", "MAIN_NEXT_BUTTON", "MAIN_NEXT_BUTTON_ACTIVE", origin + new Vector2(108, 88) * scale, new Vector2(22, 18) * scale))
            this.controller.ApplyRelative(1);

        if (SkinButton.Draw(this.plugin.CurrentSkin, "setup_eject", "MAIN_EJECT_BUTTON", "MAIN_EJECT_BUTTON_ACTIVE", origin + new Vector2(136, 89) * scale, new Vector2(22, 16) * scale))
            ImGui.OpenPopup("xivamp_setup");

        if (SkinButton.Draw(this.plugin.CurrentSkin, "eq", "MAIN_EQ_BUTTON", "MAIN_EQ_BUTTON_DEPRESSED", origin + new Vector2(219, 58) * scale, new Vector2(23, 12) * scale))
            this.controller.SetStatus("EQ is visual-only for now.");

        var playlistNormal = this.plugin.Configuration.PlaylistWindowVisible ? "MAIN_PLAYLIST_BUTTON_SELECTED" : "MAIN_PLAYLIST_BUTTON";
        var playlistActive = this.plugin.Configuration.PlaylistWindowVisible ? "MAIN_PLAYLIST_BUTTON_DEPRESSED_SELECTED" : "MAIN_PLAYLIST_BUTTON_DEPRESSED";
        if (SkinButton.Draw(this.plugin.CurrentSkin, "playlist", playlistNormal, playlistActive, origin + new Vector2(242, 58) * scale, new Vector2(23, 12) * scale))
        {
            this.plugin.Configuration.PlaylistWindowVisible = !this.plugin.Configuration.PlaylistWindowVisible;
            this.plugin.PlaylistWindow.IsOpen = this.plugin.Configuration.PlaylistWindowVisible;
            this.plugin.Save();
        }
    }

    private void DrawShuffleRepeat(Vector2 origin, float scale)
    {
        var shuffleNormal = this.plugin.Configuration.ShuffleEnabled ? "MAIN_SHUFFLE_BUTTON_SELECTED" : "MAIN_SHUFFLE_BUTTON";
        var shuffleActive = this.plugin.Configuration.ShuffleEnabled ? "MAIN_SHUFFLE_BUTTON_SELECTED_DEPRESSED" : "MAIN_SHUFFLE_BUTTON_DEPRESSED";
        if (SkinButton.Draw(this.plugin.CurrentSkin, "shuffle", shuffleNormal, shuffleActive, origin + new Vector2(164, 89) * scale, new Vector2(47, 15) * scale))
        {
            this.plugin.Configuration.ShuffleEnabled = !this.plugin.Configuration.ShuffleEnabled;
            this.plugin.Save();
            this.controller.SetStatus(this.plugin.Configuration.ShuffleEnabled ? "Shuffle on." : "Shuffle off.");
        }

        var repeatNormal = this.plugin.Configuration.RepeatEnabled ? "MAIN_REPEAT_BUTTON_SELECTED" : "MAIN_REPEAT_BUTTON";
        var repeatActive = this.plugin.Configuration.RepeatEnabled ? "MAIN_REPEAT_BUTTON_SELECTED_DEPRESSED" : "MAIN_REPEAT_BUTTON_DEPRESSED";
        if (SkinButton.Draw(this.plugin.CurrentSkin, "repeat", repeatNormal, repeatActive, origin + new Vector2(210, 89) * scale, new Vector2(28, 15) * scale))
        {
            this.plugin.Configuration.RepeatEnabled = !this.plugin.Configuration.RepeatEnabled;
            this.plugin.Save();
            this.controller.SetStatus(this.plugin.Configuration.RepeatEnabled ? "Repeat on." : "Repeat off.");
        }
    }

    private void DrawWindowButtons(Vector2 origin, float scale)
    {
        if (SkinButton.Draw(this.plugin.CurrentSkin, "options", "MAIN_OPTIONS_BUTTON", "MAIN_OPTIONS_BUTTON_DEPRESSED", origin + new Vector2(6, 3) * scale, new Vector2(9, 9) * scale))
            ImGui.OpenPopup("xivamp_setup");

        if (SkinButton.Draw(this.plugin.CurrentSkin, "shade", "MAIN_SHADE_BUTTON", "MAIN_SHADE_BUTTON_DEPRESSED", origin + new Vector2(254, 3) * scale, new Vector2(9, 9) * scale))
            this.ToggleShade();

        if (SkinButton.Draw(this.plugin.CurrentSkin, "close", "MAIN_CLOSE_BUTTON", "MAIN_CLOSE_BUTTON_DEPRESSED", origin + new Vector2(264, 3) * scale, new Vector2(9, 9) * scale))
        {
            this.IsOpen = false;
            this.plugin.PlaylistWindow.IsOpen = false;
        }
    }

    private void ToggleShade()
    {
        this.plugin.Configuration.MainWindowShade = !this.plugin.Configuration.MainWindowShade;
        this.plugin.Save();

        // Re-apply the (now smaller/larger) size immediately so the window does not
        // flash at the old dimensions for a frame.
        var scale = SkinHelper.SkinScale(this.plugin.Configuration);
        this.Size = this.CurrentSize * scale;
    }

    private void DrawVisualizer(Vector2 origin, float scale)
    {
        // Webamp-style spectrum analyzer: VISCOLOR.txt gradient bars (green→red) with
        // slowly falling peak dots, drawn in the 76x16 visualization area at (24,43).
        var colors = this.plugin.CurrentSkin.VisualizerColors;
        var drawList = ImGui.GetWindowDrawList();
        var min = origin + new Vector2(24, 43) * scale;
        var max = min + new Vector2(76, 16) * scale;
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(colors[0]));

        var active = !this.plugin.Configuration.IsPaused && !this.plugin.Configuration.IsStopped && !string.IsNullOrWhiteSpace(this.plugin.Configuration.LastAppliedOptionName);
        var time = active ? (float)ImGui.GetTime() : 0f;
        var dt = ImGui.GetIO().DeltaTime;

        var gap = MathF.Max(1, scale);
        var barWidth = MathF.Max(1, MathF.Floor((max.X - min.X - (VisualizerBars - 1) * gap) / VisualizerBars));
        var rowHeight = (max.Y - min.Y) / VisualizerRows;

        // Bar heights are simulated (no game-audio FFT) but keyed off the track metadata.
        var entry = this.controller.AppliedEntry() ?? this.controller.CurrentEntry();
        var bitrate = entry?.BitrateKbps > 0 ? entry.BitrateKbps : 192;
        var sampleRate = entry?.SampleRate > 0 ? entry.SampleRate : 44100;
        var speedFactor = 2.5f + Math.Clamp((bitrate - 96) / 256.0f, 0, 2.0f);
        var trebleBoost = Math.Clamp((sampleRate - 22050) / 22050.0f, 0, 1.0f);

        for (var i = 0; i < VisualizerBars; i++)
        {
            var wave = 0f;
            if (active)
            {
                var freqBand = (float)i / VisualizerBars; // 0=bass, 1=treble
                var f1 = MathF.Sin(time * (speedFactor + freqBand * 1.2f) + i * 0.7f);
                var f2 = MathF.Sin(time * (speedFactor * 1.7f + freqBand * 0.5f) + i * 1.3f);
                var f3 = MathF.Sin(time * (speedFactor * 0.6f) + i * 2.1f);
                var bassWeight = 1.0f - freqBand;
                var trebleWeight = freqBand * (1.0f + trebleBoost * 0.5f);
                wave = 0.3f + 0.25f * (f1 * (0.5f + trebleWeight) + f2 * 0.3f + f3 * bassWeight * 0.4f);
                wave = Math.Clamp(wave, 0.0f, 1.0f);
            }

            var level = wave * VisualizerRows; // 0..16
            var litRows = (int)MathF.Round(level);
            var x = min.X + i * (barWidth + gap);

            // Vertical gradient: VISCOLOR[17] is the bottom of the spectrum and [2] the
            // top, so row r from the bottom uses VISCOLOR[17 - r].
            for (var r = 0; r < litRows && r < VisualizerRows; r++)
            {
                var color = colors[Math.Max(2, 17 - r)];
                var rowTop = max.Y - (r + 1) * rowHeight;
                drawList.AddRectFilled(new Vector2(x, rowTop), new Vector2(x + barWidth, rowTop + rowHeight), ImGui.GetColorU32(color));
            }

            // Peak indicator: snaps up to the bar, then falls slowly. VISCOLOR[23].
            var peak = this.visualizerPeaks[i];
            peak = level >= peak ? level : MathF.Max(0f, peak - 14f * dt);
            this.visualizerPeaks[i] = peak;

            if (peak > 0.5f)
            {
                var peakRow = Math.Min(VisualizerRows - 1, (int)peak);
                var peakTop = max.Y - (peakRow + 1) * rowHeight;
                drawList.AddRectFilled(new Vector2(x, peakTop), new Vector2(x + barWidth, peakTop + MathF.Max(1, rowHeight)), ImGui.GetColorU32(colors[23]));
            }
        }
    }

    private void DrawMediaInfo(Vector2 origin, float scale)
    {
        var entry = this.controller.AppliedEntry() ?? this.controller.CurrentEntry();
        var active = !this.plugin.Configuration.IsPaused && !this.plugin.Configuration.IsStopped && !string.IsNullOrWhiteSpace(this.plugin.Configuration.LastAppliedOptionName);
        var bitrate = entry?.BitrateKbps > 0 ? entry.BitrateKbps : active ? 192 : 0;
        var sampleRate = entry?.SampleRate > 0 ? entry.SampleRate : active ? 44100 : 0;
        if (bitrate > 0)
            SkinTextRenderer.DrawText(this.plugin.CurrentSkin, bitrate.ToString(), origin + new Vector2(111, 43) * scale, 15 * scale, scale);

        if (sampleRate > 0)
            SkinTextRenderer.DrawText(this.plugin.CurrentSkin, Math.Max(1, sampleRate / 1000).ToString(), origin + new Vector2(156, 43) * scale, 10 * scale, scale);

        if (!SkinRenderer.DrawSprite(this.plugin.CurrentSkin, "MAIN_MONO", origin + new Vector2(212, 41) * scale, new Vector2(27, 12) * scale))
            SkinTextRenderer.DrawText(this.plugin.CurrentSkin, "mono", origin + new Vector2(212, 44) * scale, 27 * scale, scale);

        if (!SkinRenderer.DrawSprite(this.plugin.CurrentSkin, "MAIN_STEREO_SELECTED", origin + new Vector2(239, 41) * scale, new Vector2(29, 12) * scale))
            SkinTextRenderer.DrawText(this.plugin.CurrentSkin, "stereo", origin + new Vector2(239, 44) * scale, 29 * scale, scale);
    }

    private static void DrawDisplayText(Vector2 pos, Vector2 size, string text)
    {
        ImGui.PushClipRect(pos, pos + size, true);
        ImGui.GetWindowDrawList().AddText(pos, ImGui.GetColorU32(new Vector4(0.0f, 1.0f, 0.18f, 1.0f)), text);
        ImGui.PopClipRect();
    }

    private void DrawTimerLabel(Vector2 origin, float scale, string label)
    {
        if (!SkinNumberRenderer.DrawTime(this.plugin.CurrentSkin, label, origin + new Vector2(48, 26) * scale, scale))
            DrawDisplayText(origin + new Vector2(48, 24) * scale, new Vector2(48, 15) * scale, label);
    }

    private bool BlinkVisible()
        => ((int)(ImGui.GetTime() * 2.0)) % 2 == 0;

    private void DrawSliders(Vector2 origin, float scale)
    {
        var volume = this.ReadBgmVolume();

        // VOLUME.bmp stacks 28 slider backgrounds (one per level) at y = index * 15,
        // with index 0 green and 27 red. Like Webamp, show green at max volume:
        // index = 27 - round(volume/100 * 27).
        var volumeIndex = 27 - (int)MathF.Round(volume / 100.0f * 27.0f);
        var volumeBackground = new SkinSprite("VOLUME", 0, volumeIndex * 15, 68, 13);
        this.DrawHorizontalSlider(
            "volume",
            origin + new Vector2(107, 57) * scale,
            new Vector2(68, 13) * scale,
            "MAIN_VOLUME_BACKGROUND",
            "MAIN_VOLUME_THUMB",
            "MAIN_VOLUME_THUMB_SELECTED",
            volume / 100.0f,
            value => this.WriteBgmVolume((uint)Math.Clamp(MathF.Round(value * 100), 0, 100)),
            volumeBackground);

        this.DrawHorizontalSlider(
            "balance",
            origin + new Vector2(177, 57) * scale,
            new Vector2(38, 13) * scale,
            "MAIN_BALANCE_BACKGROUND",
            "MAIN_BALANCE_THUMB",
            "MAIN_BALANCE_THUMB_ACTIVE",
            0.5f,
            _ => this.controller.SetStatus("Balance is visual-only for now."));
    }

    private void DrawPositionBar(Vector2 origin, float scale)
    {
        var pos = origin + new Vector2(16, 72) * scale;
        var size = new Vector2(248, 10) * scale;
        if (!SkinRenderer.DrawSprite(this.plugin.CurrentSkin, "MAIN_POSITION_SLIDER_BACKGROUND", pos, size))
            ImGui.GetWindowDrawList().AddRectFilled(pos, pos + size, ImGui.GetColorU32(new Vector4(0.02f, 0.02f, 0.025f, 1)));

        var duration = this.TrackDurationSeconds();
        var progress = this.EstimatedProgress(duration);
        if (!this.plugin.Configuration.IsPaused && !this.plugin.Configuration.IsStopped && !string.IsNullOrWhiteSpace(this.plugin.Configuration.LastAppliedOptionName))
        {
            var thumbWidth = 29 * scale;
            var thumbX = pos.X + Math.Clamp(progress, 0, 1) * Math.Max(0, size.X - thumbWidth);
            var thumbSprite = ImGui.IsMouseDown(ImGuiMouseButton.Left) && ImGui.IsMouseHoveringRect(pos, pos + size)
                ? "MAIN_POSITION_SLIDER_THUMB_SELECTED"
                : "MAIN_POSITION_SLIDER_THUMB";
            SkinRenderer.DrawSprite(this.plugin.CurrentSkin, thumbSprite, new Vector2(thumbX, pos.Y), new Vector2(29, 10) * scale);
        }

        ImGui.SetCursorScreenPos(pos);
        if (ImGui.InvisibleButton("##position", size) && duration > 0)
        {
            var local = Math.Clamp((ImGui.GetIO().MousePos.X - pos.X) / size.X, 0, 1);
            this.controller.SetEstimatedSeek(local * duration);
        }
    }

    private void DrawHorizontalSlider(string id, Vector2 pos, Vector2 size, string backgroundSprite, string thumbSprite, string activeThumbSprite, float value, Action<float> onChange, SkinSprite? backgroundOverride = null)
    {
        var scale = SkinHelper.SkinScale(this.plugin.Configuration);
        var drewBackground = backgroundOverride is { } overrideSprite
            ? SkinRenderer.DrawSprite(this.plugin.CurrentSkin, overrideSprite, pos, size)
            : SkinRenderer.DrawSprite(this.plugin.CurrentSkin, backgroundSprite, pos, size);
        if (!drewBackground)
            ImGui.GetWindowDrawList().AddRectFilled(pos, pos + size, ImGui.GetColorU32(new Vector4(0.03f, 0.03f, 0.04f, 1)));

        var thumbSize = new Vector2(14, 11) * scale;
        var usable = Math.Max(0, size.X - thumbSize.X);
        var thumbPos = new Vector2(pos.X + Math.Clamp(value, 0, 1) * usable, pos.Y + scale);

        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton($"##{id}", size);
        var active = ImGui.IsItemActive();
        if (active || clicked)
        {
            var next = Math.Clamp((ImGui.GetIO().MousePos.X - pos.X) / size.X, 0, 1);
            onChange(next);
            thumbPos.X = pos.X + next * usable;
        }

        SkinRenderer.DrawSprite(this.plugin.CurrentSkin, active ? activeThumbSprite : thumbSprite, thumbPos, thumbSize);
    }

    private uint ReadBgmVolume()
        => Plugin.GameConfig.TryGet(SystemConfigOption.SoundBgm, out uint volume) ? Math.Clamp(volume, 0, 100) : 100;

    private void WriteBgmVolume(uint volume)
    {
        Plugin.GameConfig.Set(SystemConfigOption.SoundBgm, Math.Clamp(volume, 0u, 100u));
        this.controller.SetStatus($"BGM volume {volume}%.");
    }

    private float EstimatedProgress(double duration)
    {
        if (duration <= 0 || this.plugin.Configuration.LastAppliedAtUtc == default)
            return 0;

        return (float)Math.Clamp(this.EstimatedElapsedSeconds(duration) / duration, 0, 1);
    }

    private double EstimatedElapsedSeconds(double duration)
    {
        if (this.plugin.Configuration.LastAppliedAtUtc == default)
            return 0;

        var elapsed = this.plugin.Configuration.EstimatedSeekOffsetSeconds + (DateTime.UtcNow - this.plugin.Configuration.LastAppliedAtUtc).TotalSeconds;
        if (duration > 0 && this.plugin.Configuration.RepeatEnabled)
            elapsed %= duration;
        else if (duration > 0)
            elapsed = Math.Min(elapsed, duration);

        return Math.Max(0, elapsed);
    }

    private double TrackDurationSeconds()
    {
        // Use the applied (playing) track's duration; the selected row can differ.
        var entry = this.controller.AppliedEntry() ?? this.controller.CurrentEntry();
        var entryDuration = entry?.DurationSeconds ?? 0;
        if (entryDuration > 0)
            return entryDuration;

        return Math.Max(1, this.plugin.Configuration.FallbackTrackDurationSeconds <= 0 ? 180 : this.plugin.Configuration.FallbackTrackDurationSeconds);
    }

    private void HandleTitlebarDrag(Vector2 origin, float scale)
    {
        var mouse = ImGui.GetIO().MousePos;
        bool hovered;
        if (this.plugin.Configuration.MainWindowShade)
        {
            // In shade mode the bar is one row tall; grab it by the title/time display
            // area, away from the menu button, transport controls, seek bar and window buttons.
            hovered = IsInRect(mouse, origin + new Vector2(16, 0) * scale, origin + new Vector2(165, 14) * scale);
        }
        else
        {
            hovered = IsInRect(mouse, origin, origin + new Vector2(5, 14) * scale)
                || IsInRect(mouse, origin + new Vector2(16, 0) * scale, origin + new Vector2(263, 14) * scale);
        }

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);

        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            this.draggingTitlebar = true;
            this.dragOffset = mouse - origin;
        }

        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            this.draggingTitlebar = false;

        if (!this.draggingTitlebar)
            return;

        var newPosition = mouse - this.dragOffset;
        this.pendingPosition = newPosition;
        this.ThrottledSavePosition(newPosition);
        this.plugin.PlaylistWindow.DockTo(newPosition + new Vector2(0, this.CurrentSize.Y * scale));
    }

    private static bool IsInRect(Vector2 point, Vector2 min, Vector2 max)
        => point.X >= min.X && point.X <= max.X && point.Y >= min.Y && point.Y <= max.Y;

    private void DrawChrome(Vector2 origin, float scale)
    {
        if (SkinRenderer.DrawSprite(this.plugin.CurrentSkin, "MAIN_WINDOW_BACKGROUND", origin, BaseSize * scale))
        {
            SkinRenderer.DrawSprite(this.plugin.CurrentSkin, "MAIN_TITLE_BAR_SELECTED", origin, new Vector2(275, 14) * scale);
            return;
        }

        var drawList = ImGui.GetWindowDrawList();
        var size = BaseSize * scale;
        drawList.AddRectFilled(origin, origin + size, ImGui.GetColorU32(new Vector4(0.13f, 0.13f, 0.18f, 1.0f)));
        drawList.AddRectFilled(origin + new Vector2(4, 4) * scale, origin + new Vector2(271, 112) * scale, ImGui.GetColorU32(new Vector4(0.24f, 0.24f, 0.32f, 1.0f)));
        drawList.AddRectFilled(origin + new Vector2(24, 22) * scale, origin + new Vector2(266, 77) * scale, ImGui.GetColorU32(new Vector4(0.0f, 0.0f, 0.0f, 1.0f)));
        drawList.AddRect(origin, origin + size, ImGui.GetColorU32(new Vector4(0.72f, 0.72f, 0.82f, 1.0f)));
    }

    private void ThrottledSavePosition(Vector2 position)
    {
        if (this.plugin.Configuration.HasPlayerWindowPosition
            && Math.Abs(this.plugin.Configuration.PlayerWindowX - position.X) < 0.5f
            && Math.Abs(this.plugin.Configuration.PlayerWindowY - position.Y) < 0.5f)
            return;

        var now = DateTime.UtcNow;
        if ((now - this.lastPositionSave).TotalMilliseconds < 500 && ImGui.IsMouseDown(ImGuiMouseButton.Left))
            return;

        this.plugin.Configuration.HasPlayerWindowPosition = true;
        this.plugin.Configuration.PlayerWindowX = position.X;
        this.plugin.Configuration.PlayerWindowY = position.Y;
        this.plugin.Save();
        this.lastPositionSave = now;
    }

    private void CheckAutoAdvance()
    {
        // Only auto-advance when a track is actively playing (not paused or stopped).
        if (this.plugin.Configuration.IsPaused
            || this.plugin.Configuration.IsStopped
            || string.IsNullOrWhiteSpace(this.plugin.Configuration.LastAppliedOptionName)
            || this.plugin.Configuration.LastAppliedAtUtc == default)
            return;

        var duration = this.TrackDurationSeconds();

        // Hold the current track an extra "track gap" past its end before advancing, so the
        // next track's Penumbra/Mare sync lands after this one has finished for everyone.
        var threshold = duration + Math.Max(0, this.plugin.Configuration.TrackGapSeconds);
        var rawElapsed = this.plugin.Configuration.EstimatedSeekOffsetSeconds
            + (DateTime.UtcNow - this.plugin.Configuration.LastAppliedAtUtc).TotalSeconds;

        if (rawElapsed < threshold)
        {
            // Track is still playing (or within the gap) — clear the guard for next time.
            this.autoAdvancedOptionName = string.Empty;
            return;
        }

        // Past the track end + gap: advance, unless repeat is on (let the SCD loop).
        if (this.plugin.Configuration.RepeatEnabled)
            return;

        var currentOption = PlaylistFormat.EntryKey(
            this.plugin.Configuration.LastAppliedOptionGroup,
            this.plugin.Configuration.LastAppliedOptionName); // legacy: $"{group}\u001F{this.plugin.Configuration.LastAppliedOptionName}";

        // Don't re-trigger for the same track (prevent infinite loop each frame).
        if (string.Equals(this.autoAdvancedOptionName, currentOption, StringComparison.Ordinal))
            return;

        this.autoAdvancedOptionName = currentOption;
        this.controller.ApplyRelative(1);
    }
}
