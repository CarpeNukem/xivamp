using System.Numerics;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using StbImageSharp;
using xivAMP.Services;
using xivAMP.Skin;

namespace xivAMP.Windows;

public sealed class SetupPopup : IDisposable
{
    private const string SkinMuseumUrl = "https://skins.webamp.org/";
    private const float ButtonHeight = 15;
    private const float RowGap = 5;
    private const float SetupColumnTargetWidth = 310;
    private const float VfxColumnTargetWidth = 460;
    private static readonly Vector2 PresetConfirmSize = new(330, 130);

    private readonly Plugin plugin;
    private readonly XivAmpController controller;
    private readonly FileDialogManager fileDialogManager;
    private ContactLogo? discordLogo;
    private ContactLogo? nRootLogo;
    private bool contactLogosLoaded;
    private string modFilter = string.Empty;
    private string animationModFilter = string.Empty;
    private string selectedPresetName = string.Empty;
    private string presetNameBuffer = string.Empty;
    private string renamePresetBuffer = string.Empty;
    private string pendingPresetName = string.Empty;
    private string changedItemsModDir = string.Empty;
    private IReadOnlyList<ChangedItem> changedItems = Array.Empty<ChangedItem>();
    private string changedItemsError = string.Empty;
    private string selectedVisualSetName = string.Empty;
    private string visualSetNameBuffer = string.Empty;
    private bool creatingVisualSet;
    private bool focusVisualSetName;
    private string visualGroupsModDir = string.Empty;
    private IReadOnlyDictionary<string, string[]> visualGroups = new Dictionary<string, string[]>();
    private string visualGroupsError = string.Empty;

    public SetupPopup(Plugin plugin, XivAmpController controller, FileDialogManager fileDialogManager)
    {
        this.plugin = plugin;
        this.controller = controller;
        this.fileDialogManager = fileDialogManager;
    }

    public void DrawSettings()
    {
        ImGui.Dummy(new Vector2(1, 10));

        var columnWidth = this.BeginSetupColumn();
        this.Section("skin", columnWidth);

        if (SkinnedPanel.Button(this.plugin.CurrentSkin, "##load_skin", "LOAD", new Vector2(54, ButtonHeight)))
            this.fileDialogManager.OpenFileDialog("Choose Winamp skin", "Winamp skin{.wsz},.*", this.OnSkinSelected);

        SkinnedPanel.SameRow(RowGap);
        if (SkinnedPanel.Button(this.plugin.CurrentSkin, "##clear_skin", "CLEAR", new Vector2(62, ButtonHeight)))
        {
            this.plugin.Configuration.SelectedSkinPath = string.Empty;
            this.plugin.Save();
            this.plugin.LoadConfiguredSkin();
        }

        SkinnedPanel.SameRow(RowGap);
        if (SkinnedPanel.Button(this.plugin.CurrentSkin, "##browse_skins", "BROWSE SKINS", new Vector2(104, ButtonHeight)))
            Dalamud.Utility.Util.OpenLink(SkinMuseumUrl);

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(SkinMuseumUrl);

        this.BeginSetupColumn();
        this.Section("options", columnWidth);
        if (this.ToggleButton("TEMP", this.plugin.Configuration.UseTemporarySettings, new Vector2(56, ButtonHeight)))
        {
            this.plugin.Configuration.UseTemporarySettings = !this.plugin.Configuration.UseTemporarySettings;
            this.plugin.Save();
        }

        SkinnedPanel.SameRow(RowGap);
        if (this.ToggleButton("REDRAW", this.plugin.Configuration.RedrawAfterApply, new Vector2(70, ButtonHeight)))
        {
            this.plugin.Configuration.RedrawAfterApply = !this.plugin.Configuration.RedrawAfterApply;
            this.plugin.Save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Redraw character after applying a track.\nRequired for visual changes to appear.");

        SkinnedPanel.SameRow(RowGap);
        if (this.ToggleButton("KEEP UI", this.plugin.Configuration.KeepUiWhenHudHidden, new Vector2(78, ButtonHeight)))
        {
            this.plugin.Configuration.KeepUiWhenHudHidden = !this.plugin.Configuration.KeepUiWhenHudHidden;
            this.plugin.Save();
            this.plugin.ApplyUiHidePreference();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Keep the xivAMP windows visible when the game UI/HUD is hidden\n(e.g. the hide-HUD hotkey or screenshot mode).");

        // Double-size (2x) toggle: render the player/playlist at 2x with pixel-crisp,
        // nearest-neighbor pre-scaled skin sheets (reloads the skin to rebuild the textures).
        SkinnedPanel.SameRow(RowGap);
        var doubleSize = this.plugin.Configuration.SkinScale >= 1.5f;
        if (this.ToggleButton("DOUBLE", doubleSize, new Vector2(74, ButtonHeight)))
        {
            this.plugin.Configuration.SkinScale = doubleSize ? 1.0f : 2.0f;
            this.plugin.Save();
            this.plugin.LoadConfiguredSkin();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Render xivAMP at double size (2x), kept pixel-crisp.");

        // Extra time a track is held past its end before auto-advancing, so the next
        // track's Penumbra/Mare (or analog lol... no Mare except original Mare!.. I miss it :c..) sync lands without cutting this one short.
        this.BeginSetupColumn();
        var gap = (float)this.plugin.Configuration.TrackGapSeconds;
        ImGui.SetNextItemWidth(120);
        if (ImGui.SliderFloat("##trackgap", ref gap, 0f, 10f, "%.1fs"))
        {
            this.plugin.Configuration.TrackGapSeconds = Math.Clamp(gap, 0f, 10f);
            this.plugin.Save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Extra time to hold a track past its end before auto-advancing,\nso the next track has time to sync before it plays.");

        SkinnedPanel.SameRow(RowGap);
        ImGui.TextDisabled("Pause between tracks for sync");

        this.DrawPresetControls(columnWidth);
        this.BeginSetupColumn();
        this.Section("mod", columnWidth);

        var dual = this.plugin.Configuration.DualSourceMode;
        this.BeginSetupColumn();
        if (this.ToggleButton("DUAL MOD SETUP", dual, new Vector2(columnWidth, ButtonHeight)))
        {
            this.controller.SetDualSourceMode(!dual);
            this.ResetVisualSetEditor();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Take music from one mod and animations from another.\nAdds a second mod selector for the animation source.");

        if (dual)
        {
            this.BeginSetupColumn();
            ImGui.TextDisabled("music mod");
        }

        this.DrawModCombo(columnWidth);

        if (dual)
        {
            this.BeginSetupColumn();
            ImGui.TextDisabled("animation mod");
            this.DrawAnimationModCombo(columnWidth);
        }

        this.DrawVfxSettings(columnWidth);
        this.DrawChangedItems(columnWidth);
        this.DrawContacts(columnWidth);
    }

    public void DrawVfxSets()
    {
        ImGui.Dummy(new Vector2(1, 10));

        var columnWidth = this.BeginVfxColumn();
        this.DrawVisualSetLibrary(columnWidth);
    }

    public void Dispose()
    {
        this.discordLogo?.Dispose();
        this.nRootLogo?.Dispose();
    }

    private float BeginSetupColumn()
        => SkinnedPanel.CenterContentColumn(this.plugin.CurrentSkin, SetupColumnTargetWidth);

    private float BeginVfxColumn()
    {
        var columnWidth = MathF.Min(VfxColumnTargetWidth, MathF.Max(1, ImGui.GetContentRegionAvail().X));
        CenterCurrentContent(columnWidth);
        return columnWidth;
    }

    private void BeginVfxColumn(float columnWidth)
    {
        var width = MathF.Min(columnWidth, MathF.Max(1, ImGui.GetContentRegionAvail().X));
        CenterCurrentContent(width);
    }

    private static void CenterCurrentContent(float width)
    {
        var cursor = ImGui.GetCursorScreenPos();
        var available = ImGui.GetContentRegionAvail().X;
        ImGui.SetCursorScreenPos(new Vector2(cursor.X + MathF.Max(0, (available - width) * 0.5f), cursor.Y));
    }

    private static void LinkText(string text, string url)
    {
        var color = new Vector4(0.45f, 0.62f, 1.0f, 1.0f);
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();

        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            ImGui.GetWindowDrawList().AddLine(new Vector2(min.X, max.Y), new Vector2(max.X, max.Y), ImGui.GetColorU32(color));
            ImGui.SetTooltip(url);
        }

        if (ImGui.IsItemClicked())
            Dalamud.Utility.Util.OpenLink(url);
    }

    private void Section(string label, float columnWidth)
    {
        SkinnedPanel.Section(this.plugin.CurrentSkin, label, columnWidth);
    }

    private void DrawPresetControls(float columnWidth)
    {
        this.Section("playlists", columnWidth);
        var presets = this.plugin.PlaylistPresets.LoadForMod(this.plugin.Configuration.SelectedModDirectory);
        this.EnsureSelectedPreset(presets);

        var selectedPreset = this.SelectedPreset(presets);
        var comboLabel = selectedPreset is null
            ? "No saved playlists"
            : $"{selectedPreset.Name} ({selectedPreset.Entries.Count})";

        var presetFieldWidth = MathF.Max(145, columnWidth - 56 - 68 - RowGap * 2);
        var editFieldWidth = MathF.Max(145, columnWidth - 72 - RowGap);

        this.BeginSetupColumn();
        ImGui.SetNextItemWidth(presetFieldWidth);
        if (ImGui.BeginCombo("##preset", comboLabel))
        {
            foreach (var preset in presets)
            {
                var selected = string.Equals(preset.Name, this.selectedPresetName, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable($"{preset.Name} ({preset.Entries.Count})", selected))
                {
                    this.selectedPresetName = preset.Name;
                    this.presetNameBuffer = preset.Name;
                    this.renamePresetBuffer = preset.Name;
                }

                if (selected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        SkinnedPanel.SameRow(RowGap);
        if (SkinnedPanel.Button(this.plugin.CurrentSkin, "##load_preset", "LOAD", new Vector2(56, ButtonHeight)))
        {
            if (!string.IsNullOrWhiteSpace(this.selectedPresetName))
                this.controller.LoadPlaylistPreset(this.selectedPresetName);
            else
                this.controller.SetStatus("Choose a saved playlist first.");
        }

        SkinnedPanel.SameRow(RowGap);
        if (SkinnedPanel.Button(this.plugin.CurrentSkin, "##delete_preset", "DELETE", new Vector2(68, ButtonHeight)))
        {
            if (selectedPreset is null)
            {
                this.controller.SetStatus("Choose a saved playlist first.");
            }
            else
            {
                this.pendingPresetName = selectedPreset.Name;
                ImGui.OpenPopup("delete_playlist_preset");
            }
        }

        this.BeginSetupColumn();
        ImGui.SetNextItemWidth(editFieldWidth);
        ImGui.InputTextWithHint("##presetname", "playlist name", ref this.presetNameBuffer, 64);
        SkinnedPanel.SameRow(RowGap);
        if (SkinnedPanel.Button(this.plugin.CurrentSkin, "##save_preset", "SAVE", new Vector2(56, ButtonHeight)))
        {
            var name = this.presetNameBuffer.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                this.controller.SetStatus("Enter a playlist name first.");
            }
            else if (this.controller.PresetExists(name))
            {
                this.pendingPresetName = name;
                ImGui.OpenPopup("overwrite_playlist_preset");
            }
            else if (this.controller.SaveCurrentPlaylistAs(name))
            {
                this.selectedPresetName = name;
                this.renamePresetBuffer = name;
            }
        }

        this.BeginSetupColumn();
        ImGui.SetNextItemWidth(editFieldWidth);
        ImGui.InputTextWithHint("##presetrename", "new name", ref this.renamePresetBuffer, 64);
        SkinnedPanel.SameRow(RowGap);
        if (SkinnedPanel.Button(this.plugin.CurrentSkin, "##rename_preset", "RENAME", new Vector2(72, ButtonHeight)))
        {
            var oldName = this.selectedPresetName;
            var newName = this.renamePresetBuffer.Trim();
            if (this.controller.RenamePlaylistPreset(oldName, newName))
            {
                this.selectedPresetName = newName;
                this.presetNameBuffer = newName;
            }
        }

        this.DrawPresetConfirmPopups();
    }

    private void DrawPresetConfirmPopups()
    {
        if (SkinnedPanel.BeginPopup(this.plugin.CurrentSkin, "overwrite_playlist_preset", PresetConfirmSize, PresetConfirmSize, false, null))
        {
            SkinnedPanel.Title(this.plugin.CurrentSkin, $"Overwrite {this.pendingPresetName}");
            SkinnedPanel.BodyTopCursor(this.plugin.CurrentSkin);
            SkinnedPanel.TextCentered(this.plugin.CurrentSkin, $"{this.plugin.Configuration.Playlist.Count} active tracks will be saved.", true);
            SkinnedPanel.BottomButtonRow(this.plugin.CurrentSkin, 96 + RowGap + 80);
            if (SkinnedPanel.Button(this.plugin.CurrentSkin, "##confirm_overwrite_preset", "OVERWRITE", new Vector2(96, ButtonHeight)))
            {
                if (this.controller.SaveCurrentPlaylistAs(this.pendingPresetName))
                {
                    this.selectedPresetName = this.pendingPresetName;
                    this.presetNameBuffer = this.pendingPresetName;
                    this.renamePresetBuffer = this.pendingPresetName;
                }

                this.pendingPresetName = string.Empty;
                ImGui.CloseCurrentPopup();
            }

            SkinnedPanel.SameRow(RowGap);
            if (SkinnedPanel.Button(this.plugin.CurrentSkin, "##cancel_overwrite_preset", "CANCEL", new Vector2(80, ButtonHeight)))
            {
                this.pendingPresetName = string.Empty;
                ImGui.CloseCurrentPopup();
            }

            SkinnedPanel.EndPopup(this.plugin.CurrentSkin);
        }

        if (SkinnedPanel.BeginPopup(this.plugin.CurrentSkin, "delete_playlist_preset", PresetConfirmSize, PresetConfirmSize, false, null))
        {
            SkinnedPanel.Title(this.plugin.CurrentSkin, $"Delete {this.pendingPresetName}");
            SkinnedPanel.BodyTopCursor(this.plugin.CurrentSkin);
            SkinnedPanel.TextCentered(this.plugin.CurrentSkin, "The active playlist is not changed.", true);
            SkinnedPanel.BottomButtonRow(this.plugin.CurrentSkin, 80 + RowGap + 80);
            if (SkinnedPanel.Button(this.plugin.CurrentSkin, "##confirm_delete_preset", "DELETE", new Vector2(80, ButtonHeight)))
            {
                if (this.controller.DeletePlaylistPreset(this.pendingPresetName)
                    && string.Equals(this.selectedPresetName, this.pendingPresetName, StringComparison.OrdinalIgnoreCase))
                {
                    this.selectedPresetName = string.Empty;
                    this.presetNameBuffer = string.Empty;
                    this.renamePresetBuffer = string.Empty;
                    this.EnsureSelectedPreset(this.plugin.PlaylistPresets.LoadForMod(this.plugin.Configuration.SelectedModDirectory));
                }

                this.pendingPresetName = string.Empty;
                ImGui.CloseCurrentPopup();
            }

            SkinnedPanel.SameRow(RowGap);
            if (SkinnedPanel.Button(this.plugin.CurrentSkin, "##cancel_delete_preset", "CANCEL", new Vector2(80, ButtonHeight)))
            {
                this.pendingPresetName = string.Empty;
                ImGui.CloseCurrentPopup();
            }

            SkinnedPanel.EndPopup(this.plugin.CurrentSkin);
        }
    }

    private void EnsureSelectedPreset(IReadOnlyList<PlaylistPreset> presets)
    {
        if (!string.IsNullOrWhiteSpace(this.selectedPresetName) && this.SelectedPreset(presets) is not null)
            return;

        var first = presets.FirstOrDefault();
        if (first is null)
        {
            this.selectedPresetName = string.Empty;
            return;
        }

        this.selectedPresetName = first.Name;
        if (string.IsNullOrWhiteSpace(this.presetNameBuffer))
            this.presetNameBuffer = first.Name;
        if (string.IsNullOrWhiteSpace(this.renamePresetBuffer))
            this.renamePresetBuffer = first.Name;
    }

    private PlaylistPreset? SelectedPreset(IReadOnlyList<PlaylistPreset> presets)
        => presets.FirstOrDefault(preset =>
            string.Equals(preset.Name, this.selectedPresetName, StringComparison.OrdinalIgnoreCase));

    private void DrawModCombo(float columnWidth)
    {
        var selectedMod = this.controller.Mods.FirstOrDefault(mod => mod.Directory == this.plugin.Configuration.SelectedModDirectory);
        var selectedLabel = string.IsNullOrWhiteSpace(selectedMod.Directory) ? "Choose Penumbra mod" : selectedMod.Label;
        this.BeginSetupColumn();
        ImGui.SetNextItemWidth(columnWidth);

        // Pin the dropdown to the combo's width so it doesn't keep reflowing (shrinking its
        // right edge) as the filter narrows the longest visible mod name. Long labels clip
        // instead of widening the popup. Height is left to auto-fit.
        ImGui.SetNextWindowSizeConstraints(new Vector2(columnWidth, 0f), new Vector2(columnWidth, float.MaxValue));
        if (!ImGui.BeginCombo("##mod", selectedLabel))
            return;

        ImGui.SetNextItemWidth(-1);
        if (ImGui.IsWindowAppearing())
        {
            // Refresh the mod list from Penumbra each time the dropdown opens, so newly
            // installed/renamed mods appear without reloading the plugin.
            this.controller.RefreshMods();
            ImGui.SetKeyboardFocusHere();
        }

        ImGui.InputTextWithHint("##modfilter", "filter mods", ref this.modFilter, 128);
        ImGui.Separator();

        foreach (var mod in this.FilteredMods())
        {
            var selected = mod.Directory == this.plugin.Configuration.SelectedModDirectory;
            if (ImGui.Selectable(mod.Label, selected))
            {
                this.modFilter = string.Empty;
                this.controller.SelectMod(mod.Directory);
                this.ResetVisualSetEditor();
            }

            if (selected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
    }

    private void DrawAnimationModCombo(float columnWidth)
    {
        var selectedMod = this.controller.Mods.FirstOrDefault(mod => mod.Directory == this.plugin.Configuration.AnimationModDirectory);
        var selectedLabel = string.IsNullOrWhiteSpace(selectedMod.Directory) ? "Choose animation mod" : selectedMod.Label;
        this.BeginSetupColumn();
        ImGui.SetNextItemWidth(columnWidth);

        // Pin the dropdown width like the music combo so long labels clip instead of reflowing.
        ImGui.SetNextWindowSizeConstraints(new Vector2(columnWidth, 0f), new Vector2(columnWidth, float.MaxValue));
        if (!ImGui.BeginCombo("##animmod", selectedLabel))
            return;

        ImGui.SetNextItemWidth(-1);
        if (ImGui.IsWindowAppearing())
        {
            this.controller.RefreshMods();
            ImGui.SetKeyboardFocusHere();
        }

        ImGui.InputTextWithHint("##animmodfilter", "filter mods", ref this.animationModFilter, 128);
        ImGui.Separator();

        foreach (var mod in this.FilteredMods(this.animationModFilter))
        {
            var selected = mod.Directory == this.plugin.Configuration.AnimationModDirectory;
            if (ImGui.Selectable(mod.Label, selected))
            {
                this.animationModFilter = string.Empty;
                this.controller.SelectAnimationMod(mod.Directory);
                this.ResetVisualSetEditor();
            }

            if (selected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
    }

    private void DrawVfxSettings(float columnWidth)
    {
        var modDir = this.controller.EmoteSourceModDirectory;
        if (string.IsNullOrWhiteSpace(modDir))
            return;

        this.Section("vfx", columnWidth);
        this.DrawDefaultVisualSetCombo(columnWidth, modDir);

        this.BeginSetupColumn();
        if (SkinnedPanel.Button(this.plugin.CurrentSkin, "##open_vfx_sets", "VFX SETS", new Vector2(86, ButtonHeight)))
            this.plugin.VfxSetsWindow.IsOpen = true;
    }

    private void DrawVisualSetLibrary(float columnWidth)
    {
        var modDir = this.controller.EmoteSourceModDirectory;
        if (string.IsNullOrWhiteSpace(modDir))
        {
            this.DrawVfxSection("vfx set library", columnWidth);
            this.BeginVfxColumn(columnWidth);
            ImGui.TextDisabled("Choose an animation mod first.");
            return;
        }

        this.EnsureVisualGroups(modDir);
        this.DrawVfxSection("vfx set library", columnWidth);
        this.DrawVisualSetSelector(columnWidth, modDir);

        var set = this.SelectedVisualSet(modDir);
        if (set is null)
        {
            this.BeginVfxColumn(columnWidth);
            ImGui.TextDisabled(this.creatingVisualSet
                ? "Enter a set name, then SAVE SET."
                : "No VFX set selected.");
            return;
        }

        set.ModDirectory = modDir;
        if (!string.IsNullOrWhiteSpace(this.visualGroupsError))
        {
            this.BeginVfxColumn(columnWidth);
            ImGui.TextDisabled(this.visualGroupsError);
            return;
        }

        if (this.visualGroups.Count == 0)
        {
            this.BeginVfxColumn(columnWidth);
            ImGui.TextDisabled("No option groups found for this mod.");
            return;
        }

        this.BeginVfxColumn(columnWidth);
        ImGui.TextDisabled("mod options");
        foreach (var (group, options) in this.visualGroups.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            this.BeginVfxColumn(columnWidth);
            ImGui.PushID(group);
            ImGui.TextDisabled(group);
            this.BeginVfxColumn(columnWidth);
            ImGui.SetNextItemWidth(columnWidth);

            set.OptionSelections.TryGetValue(group, out var selectedOption);
            var hasSelected = !string.IsNullOrWhiteSpace(selectedOption);
            var selectionExists = hasSelected && options.Contains(selectedOption, StringComparer.OrdinalIgnoreCase);
            var label = !hasSelected
                ? "(unchanged)"
                : selectionExists
                    ? selectedOption
                    : $"{selectedOption} (missing)";

            if (ImGui.BeginCombo("##vfx_option", label))
            {
                if (ImGui.Selectable("(unchanged)", !hasSelected))
                {
                    set.OptionSelections.Remove(group);
                    this.SaveVisualSetFile(set);
                }

                foreach (var option in options)
                {
                    var selected = string.Equals(option, selectedOption, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable(option, selected))
                    {
                        set.OptionSelections[group] = option;
                        this.SaveVisualSetFile(set);
                    }

                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }

            ImGui.PopID();
        }

        this.DrawVisualSetEmoteCombo(columnWidth, set);

        this.BeginVfxColumn(columnWidth);
        if (SkinnedPanel.Button(this.plugin.CurrentSkin, "##clear_vfx_options", "CLEAR SET", new Vector2(86, ButtonHeight)))
        {
            set.OptionSelections.Clear();
            if (this.SaveVisualSetFile(set))
                this.controller.SetStatus($"Cleared visual set '{set.Name}'.");
        }
    }

    private void DrawVfxSection(string label, float columnWidth)
    {
        ImGui.Dummy(new Vector2(1, 7));
        this.BeginVfxColumn(columnWidth);

        var start = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var dividerColor = ImGui.GetColorU32(this.plugin.CurrentSkin.GenColors.Divider);
        var text = label.ToUpperInvariant();
        if (this.plugin.CurrentSkin.HasGenTexture && GenTextRenderer.CanRender(text))
        {
            GenTextRenderer.DrawText(this.plugin.CurrentSkin, text, start + new Vector2(1, 1), 1f, active: true);
            drawList.AddLine(new Vector2(start.X, start.Y + 11), new Vector2(start.X + columnWidth, start.Y + 11), dividerColor);
            ImGui.Dummy(new Vector2(columnWidth, 13));
        }
        else
        {
            var size = ImGui.CalcTextSize(text);
            drawList.AddText(start, ImGui.GetColorU32(this.plugin.CurrentSkin.GenColors.WindowText), text);
            drawList.AddLine(new Vector2(start.X, start.Y + size.Y + 2), new Vector2(start.X + columnWidth, start.Y + size.Y + 2), dividerColor);
            ImGui.Dummy(new Vector2(columnWidth, size.Y + 2));
        }

        this.BeginVfxColumn(columnWidth);
    }

    private void DrawDefaultVisualSetCombo(float columnWidth, string modDir)
    {
        var current = this.controller.VisualSetsForMod(modDir).FirstOrDefault(set =>
            string.Equals(set.Name, this.plugin.Configuration.DefaultVisualSetName, StringComparison.OrdinalIgnoreCase));
        if (current is null && !string.IsNullOrWhiteSpace(this.plugin.Configuration.DefaultVisualSetName))
        {
            this.plugin.Configuration.DefaultVisualSetName = string.Empty;
            this.plugin.Save();
        }

        var currentLabel = current is null ? "(no default)" : current.Name;
        this.BeginSetupColumn();
        ImGui.TextDisabled("playlist vfx");
        SkinnedPanel.SameRow(RowGap);
        ImGui.SetNextItemWidth(MathF.Max(145, columnWidth - 92));

        if (!ImGui.BeginCombo("##default_visual_set", currentLabel))
            return;

        if (ImGui.Selectable("(no default)", current is null))
        {
            this.plugin.Configuration.DefaultVisualSetName = string.Empty;
            this.plugin.Save();
        }

        foreach (var set in this.controller.VisualSetsForMod(modDir))
        {
            var selected = current is not null && string.Equals(current.Name, set.Name, StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable(set.Name, selected))
            {
                this.plugin.Configuration.DefaultVisualSetName = set.Name;
                this.plugin.Save();
            }

            if (selected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
    }

    private void DrawVisualSetSelector(float columnWidth, string modDir)
    {
        this.EnsureSelectedVisualSet(modDir);
        var selectedSet = this.SelectedVisualSet(modDir);
        var comboLabel = selectedSet is null ? "(new set)" : selectedSet.Name;

        this.BeginVfxColumn(columnWidth);
        ImGui.TextDisabled("edit set");
        this.BeginVfxColumn(columnWidth);
        ImGui.SetNextItemWidth(MathF.Max(160, columnWidth - 44 - 68 - RowGap * 2));
        if (ImGui.BeginCombo("##visual_set", comboLabel))
        {
            if (ImGui.Selectable("(new set)", selectedSet is null))
                this.StartNewVisualSet();

            foreach (var set in this.controller.VisualSetsForMod(modDir))
            {
                var selected = string.Equals(set.Name, this.selectedVisualSetName, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable(set.Name, selected))
                {
                    this.creatingVisualSet = false;
                    this.selectedVisualSetName = set.Name;
                    this.visualSetNameBuffer = set.Name;
                }

                if (selected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        SkinnedPanel.SameRow(RowGap);
        if (SkinnedPanel.Button(this.plugin.CurrentSkin, "##new_visual_set", "NEW", new Vector2(44, ButtonHeight)))
            this.StartNewVisualSet();

        SkinnedPanel.SameRow(RowGap);
        if (SkinnedPanel.Button(this.plugin.CurrentSkin, "##delete_visual_set", "DELETE", new Vector2(68, ButtonHeight)))
            this.DeleteSelectedVisualSet(modDir);

        this.BeginVfxColumn(columnWidth);
        ImGui.TextDisabled("set name");
        this.BeginVfxColumn(columnWidth);
        ImGui.SetNextItemWidth(MathF.Max(160, columnWidth - 76 - RowGap));
        if (this.focusVisualSetName)
        {
            ImGui.SetKeyboardFocusHere();
            this.focusVisualSetName = false;
        }

        ImGui.InputTextWithHint("##visual_set_name", "name", ref this.visualSetNameBuffer, 64);
        SkinnedPanel.SameRow(RowGap);
        if (SkinnedPanel.Button(this.plugin.CurrentSkin, "##save_visual_set", "SAVE SET", new Vector2(76, ButtonHeight)))
            this.SaveVisualSet(modDir);
    }

    private void StartNewVisualSet()
    {
        this.creatingVisualSet = true;
        this.focusVisualSetName = true;
        this.selectedVisualSetName = string.Empty;
        this.visualSetNameBuffer = string.Empty;
    }

    private void DrawVisualSetEmoteCombo(float columnWidth, VisualSet set)
    {
        var modDir = VisualSetModDirectory(set, this.controller.EmoteSourceModDirectory);
        this.EnsureChangedItems(modDir);
        if (!string.IsNullOrWhiteSpace(this.changedItemsError))
            return;

        var emoteItems = this.changedItems.Where(item => item.IsEmote).ToList();
        if (emoteItems.Count == 0)
            return;

        var current = set.Emotes.Count > 0 ? set.Emotes[0] : null;
        var comboLabel = current is null ? "(no emote)" : current.Name;
        this.BeginVfxColumn(columnWidth);
        ImGui.TextDisabled("set emote");
        this.BeginVfxColumn(columnWidth);
        ImGui.SetNextItemWidth(columnWidth);
        if (ImGui.BeginCombo("##visual_set_emote", comboLabel))
        {
            if (ImGui.Selectable("(no emote)", current is null))
            {
                set.Emotes.Clear();
                this.SaveVisualSetFile(set);
            }

            foreach (var item in emoteItems)
            {
                var selected = current is not null && current.EmoteId == item.RowId;
                if (ImGui.Selectable(item.DisplayName, selected))
                {
                    set.Emotes =
                    [
                        new ModEmoteTrigger { EmoteId = item.RowId, Name = item.DisplayName },
                    ];
                    this.SaveVisualSetFile(set);
                }

                if (selected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }
    }

    private void EnsureVisualGroups(string modDir)
    {
        if (string.Equals(this.visualGroupsModDir, modDir, StringComparison.Ordinal))
            return;

        this.visualGroupsModDir = modDir;
        var result = this.plugin.Penumbra.GetAvailableSettings(modDir);
        this.visualGroups = result.Success && result.Value is not null
            ? result.Value
            : new Dictionary<string, string[]>();
        this.visualGroupsError = result.Success ? string.Empty : result.Error;
    }

    private void ResetVisualSetEditor()
    {
        this.creatingVisualSet = false;
        this.focusVisualSetName = false;
        this.selectedVisualSetName = string.Empty;
        this.visualSetNameBuffer = string.Empty;
        this.visualGroupsModDir = string.Empty;
        this.visualGroups = new Dictionary<string, string[]>();
        this.visualGroupsError = string.Empty;
    }

    private void EnsureSelectedVisualSet(string modDir)
    {
        if (this.creatingVisualSet)
            return;

        if (!string.IsNullOrWhiteSpace(this.selectedVisualSetName)
            && this.SelectedVisualSet(modDir) is not null)
            return;

        var first = this.controller.VisualSetsForMod(modDir).FirstOrDefault();
        this.selectedVisualSetName = first?.Name ?? string.Empty;
        if (string.IsNullOrWhiteSpace(this.visualSetNameBuffer))
            this.visualSetNameBuffer = this.selectedVisualSetName;
    }

    private VisualSet? SelectedVisualSet(string modDir)
        => this.creatingVisualSet
            ? null
            : this.controller.VisualSetsForMod(modDir).FirstOrDefault(set =>
                string.Equals(set.Name, this.selectedVisualSetName, StringComparison.OrdinalIgnoreCase));

    private void SaveVisualSet(string modDir)
    {
        var name = this.visualSetNameBuffer.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            this.controller.SetStatus("Enter a visual set name first.");
            return;
        }

        var selected = this.SelectedVisualSet(modDir);
        var oldName = selected?.Name ?? string.Empty;
        var isRename = !string.IsNullOrWhiteSpace(oldName)
            && !string.Equals(oldName, name, StringComparison.OrdinalIgnoreCase);
        if (this.creatingVisualSet && this.plugin.VisualSets.Exists(name, modDir))
        {
            this.controller.SetStatus($"A visual set named '{name}' already exists.");
            return;
        }

        if (!this.creatingVisualSet && isRename && this.plugin.VisualSets.Exists(name, modDir))
        {
            this.controller.SetStatus($"A visual set named '{name}' already exists.");
            return;
        }

        var set = selected ?? new VisualSet();
        set.Name = name;
        set.ModDirectory = modDir;
        set.OptionSelections ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        set.Emotes ??= [];
        if (!this.SaveVisualSetFile(set, oldName))
            return;

        var savedPlaylists = Result<int>.Ok(0);
        if (isRename)
            savedPlaylists = this.RenameVisualSetReferences(oldName, name, modDir);

        this.selectedVisualSetName = name;
        this.visualSetNameBuffer = name;
        this.creatingVisualSet = false;
        this.focusVisualSetName = false;
        this.plugin.Save();
        this.controller.SetStatus(savedPlaylists.Success
            ? $"Saved visual set '{name}'."
            : $"Saved visual set '{name}', but saved playlists may need cleanup.");
    }

    private void DeleteSelectedVisualSet(string modDir)
    {
        var set = this.SelectedVisualSet(modDir);
        if (set is null)
        {
            this.controller.SetStatus("Choose a visual set first.");
            return;
        }

        try
        {
            if (!this.plugin.VisualSets.Delete(set.Name, modDir))
            {
                this.controller.SetStatus("Visual set no longer exists.");
                return;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Could not delete visual set {Name}.", set.Name);
            this.controller.SetStatus($"Could not delete visual set '{set.Name}': {ex.Message}");
            return;
        }

        if (this.ActivePlaylistUsesVisualMod(modDir)
            && string.Equals(this.plugin.Configuration.DefaultVisualSetName, set.Name, StringComparison.OrdinalIgnoreCase))
        {
            this.plugin.Configuration.DefaultVisualSetName = string.Empty;
        }

        if (this.ActivePlaylistUsesVisualMod(modDir))
        {
            foreach (var entry in this.plugin.Configuration.Playlist)
            {
                if (string.Equals(entry.VisualSetName, set.Name, StringComparison.OrdinalIgnoreCase))
                    entry.VisualSetName = string.Empty;
            }
        }

        var savedPlaylists = this.plugin.PlaylistPresets.RewriteAll(preset =>
        {
            if (!PlaylistUsesVisualMod(preset, modDir))
                return false;

            var changed = false;
            if (string.Equals(preset.DefaultVisualSetName, set.Name, StringComparison.OrdinalIgnoreCase))
            {
                preset.DefaultVisualSetName = string.Empty;
                changed = true;
            }

            foreach (var entry in preset.Entries)
            {
                if (!string.Equals(entry.VisualSetName, set.Name, StringComparison.OrdinalIgnoreCase))
                    continue;

                entry.VisualSetName = string.Empty;
                changed = true;
            }

            return changed;
        });

        this.selectedVisualSetName = string.Empty;
        this.visualSetNameBuffer = string.Empty;
        this.creatingVisualSet = false;
        this.focusVisualSetName = false;
        this.EnsureSelectedVisualSet(modDir);
        this.plugin.Save();
        this.controller.SetStatus(savedPlaylists.Success
            ? $"Deleted visual set '{set.Name}'."
            : $"Deleted visual set '{set.Name}', but saved playlists may need cleanup.");
    }

    private Result<int> RenameVisualSetReferences(string oldName, string newName, string modDir)
    {
        if (this.ActivePlaylistUsesVisualMod(modDir)
            && string.Equals(this.plugin.Configuration.DefaultVisualSetName, oldName, StringComparison.OrdinalIgnoreCase))
        {
            this.plugin.Configuration.DefaultVisualSetName = newName;
        }

        if (this.ActivePlaylistUsesVisualMod(modDir))
        {
            foreach (var entry in this.plugin.Configuration.Playlist)
            {
                if (string.Equals(entry.VisualSetName, oldName, StringComparison.OrdinalIgnoreCase))
                    entry.VisualSetName = newName;
            }
        }

        return this.plugin.PlaylistPresets.RewriteAll(preset =>
        {
            if (!PlaylistUsesVisualMod(preset, modDir))
                return false;

            var changed = false;
            if (string.Equals(preset.DefaultVisualSetName, oldName, StringComparison.OrdinalIgnoreCase))
            {
                preset.DefaultVisualSetName = newName;
                changed = true;
            }

            foreach (var entry in preset.Entries)
            {
                if (!string.Equals(entry.VisualSetName, oldName, StringComparison.OrdinalIgnoreCase))
                    continue;

                entry.VisualSetName = newName;
                changed = true;
            }

            return changed;
        });
    }

    private bool ActivePlaylistUsesVisualMod(string modDir)
        => StorageScope.SameMod(this.controller.EmoteSourceModDirectory, modDir);

    private static bool PlaylistUsesVisualMod(PlaylistPreset preset, string modDir)
    {
        var source = preset.DualSourceMode && !string.IsNullOrWhiteSpace(preset.AnimationModDirectory)
            ? preset.AnimationModDirectory
            : preset.SelectedModDirectory;
        return StorageScope.SameMod(source, modDir);
    }

    private bool SaveVisualSetFile(VisualSet set, string? originalName = null)
    {
        try
        {
            this.plugin.VisualSets.Save(set, originalName);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Could not save visual set {Name}.", set.Name);
            this.controller.SetStatus($"Could not save visual set '{set.Name}': {ex.Message}");
            return false;
        }
    }

    private void DrawChangedItems(float columnWidth)
    {
        // Refresh the cached list only when the emote-source mod changes (the IPC call is not
        // free, so we don't run it every frame). In dual mod setup this is the animation mod.
        var modDir = this.controller.EmoteSourceModDirectory;
        this.EnsureChangedItems(modDir);

        if (string.IsNullOrWhiteSpace(modDir))
            return;

        this.Section("fallback emote", columnWidth);
        this.BeginSetupColumn();

        if (!string.IsNullOrWhiteSpace(this.changedItemsError))
        {
            ImGui.TextDisabled(this.changedItemsError);
            return;
        }

        if (this.changedItems.Count == 0)
        {
            ImGui.TextDisabled("Penumbra reports no changed items for this mod.");
            return;
        }

        var modDirKey = modDir;
        var emoteItems = this.changedItems.Where(item => item.IsEmote).ToList();

        this.BeginSetupColumn();
        if (emoteItems.Count == 0)
        {
            ImGui.TextDisabled("No identifiable emote among this mod's changed items.");
            return;
        }

        // Single dropdown: "(do not start)" or one of the mod's emotes, fired on play.
        const string noneLabel = "(do not start an emote)";
        var current = this.SelectedEmote(modDirKey);
        var comboLabel = current is null ? noneLabel : current.Name;

        ImGui.TextDisabled("play emote");
        SkinnedPanel.SameRow(RowGap);
        ImGui.SetNextItemWidth(MathF.Max(150, columnWidth - 80));
        if (ImGui.BeginCombo("##playemote", comboLabel))
        {
            if (ImGui.Selectable(noneLabel, current is null))
                this.ClearModEmote(modDirKey);

            foreach (var item in emoteItems)
            {
                var isOn = current is not null && current.EmoteId == item.RowId;
                if (ImGui.Selectable(item.DisplayName, isOn))
                    this.SetModEmote(modDirKey, item);

                if (isOn)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }
    }

    private ModEmoteTrigger? SelectedEmote(string modDir)
        => this.plugin.Configuration.ModEmoteSets.TryGetValue(modDir, out var list) && list.Count > 0
            ? list[0]
            : null;

    private void SetModEmote(string modDir, ChangedItem item)
    {
        if (string.IsNullOrWhiteSpace(modDir) || !item.IsEmote)
            return;

        this.plugin.Configuration.ModEmoteSets[modDir] =
        [
            new ModEmoteTrigger { EmoteId = item.RowId, Name = item.DisplayName },
        ];
        this.plugin.Save();
        this.controller.SetStatus($"Play emote: {item.DisplayName} (id {item.RowId}).");
    }

    private void ClearModEmote(string modDir)
    {
        if (this.plugin.Configuration.ModEmoteSets.Remove(modDir))
        {
            this.plugin.Save();
            this.controller.SetStatus("Play emote: none.");
        }
    }

    private void EnsureChangedItems(string modDir)
    {
        if (string.Equals(modDir, this.changedItemsModDir, StringComparison.Ordinal))
            return;

        this.changedItemsModDir = modDir;
        var result = this.plugin.Penumbra.GetChangedItemsList(modDir);
        this.changedItems = result.Success && result.Value is not null ? result.Value : Array.Empty<ChangedItem>();
        this.changedItemsError = result.Success ? string.Empty : result.Error;
    }

    private static string VisualSetModDirectory(VisualSet set, string fallback)
        => !string.IsNullOrWhiteSpace(set.ModDirectory) ? set.ModDirectory : fallback;

    private void DrawContacts(float columnWidth)
    {
        this.Section("contacts", columnWidth);
        this.EnsureContactLogosLoaded();

        var discordSize = this.discordLogo?.Size ?? Vector2.Zero;
        var nRootSize = this.nRootLogo?.Size ?? Vector2.Zero;
        var totalWidth = discordSize.X + nRootSize.X + (this.discordLogo is not null && this.nRootLogo is not null ? RowGap : 0);
        if (totalWidth <= 0)
            return;

        SkinnedPanel.ButtonRow(this.plugin.CurrentSkin, totalWidth);
        if (this.DrawContactLogo("##discord_contact", this.discordLogo, null) && this.nRootLogo is not null)
            SkinnedPanel.SameRow(RowGap);

        this.DrawContactLogo("##nroot_contact", this.nRootLogo, "Join the //n_root Discord");
    }

    private bool DrawContactLogo(string id, ContactLogo? logo, string? tooltip)
    {
        if (logo is null)
            return false;

        var pos = ImGui.GetCursorScreenPos();
        var clickable = tooltip is not null;
        if (ImGui.InvisibleButton(id, logo.Size) && clickable)
            Dalamud.Utility.Util.OpenLink(Plugin.DiscordUrl);

        var hovered = ImGui.IsItemHovered();
        if (hovered && clickable)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var tint = hovered && clickable ? Vector4.One : new Vector4(0.86f, 0.86f, 0.9f, 1);
        ImGui.GetWindowDrawList().AddImage(
            logo.Texture.Handle,
            pos,
            pos + logo.Size,
            Vector2.Zero,
            Vector2.One,
            ImGui.GetColorU32(tint));

        if (hovered && tooltip is not null)
            ImGui.SetTooltip(tooltip);

        return true;
    }

    // Don't ask why I don't just load these at startup like normal human being...
    private void EnsureContactLogosLoaded()
    {
        if (this.contactLogosLoaded)
            return;

        this.contactLogosLoaded = true;
        this.discordLogo = LoadContactLogo("discord.png");
        this.nRootLogo = LoadContactLogo("n_root.png");
    }

    private static ContactLogo? LoadContactLogo(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var pluginDirectory = Path.GetDirectoryName(assembly.Location) ?? string.Empty;
        var path = Path.Combine(pluginDirectory, fileName);
        if (File.Exists(path))
        {
            try
            {
                using var stream = File.OpenRead(path);
                return LoadContactLogo(stream, fileName);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "Could not load contact logo {FileName} from {Path}.", fileName, path);
            }
        }

        var resourceName = $"xivAMP.{fileName}";
        using var resource = assembly.GetManifestResourceStream(resourceName);
        if (resource is null)
            return null;

        try
        {
            return LoadContactLogo(resource, fileName);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Could not load embedded contact logo {FileName}.", fileName);
            return null;
        }
    }

    private static ContactLogo LoadContactLogo(Stream stream, string fileName)
    {
        var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        var texture = Plugin.TextureProvider.CreateFromRaw(
            RawImageSpecification.Rgba32(image.Width, image.Height),
            image.Data,
            $"xivAMP contact {fileName}");
        return new ContactLogo(texture, new Vector2(image.Width, image.Height));
    }

    private IEnumerable<PenumbraMod> FilteredMods()
        => this.FilteredMods(this.modFilter);

    private IEnumerable<PenumbraMod> FilteredMods(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return this.controller.Mods;

        return this.controller.Mods.Where(mod =>
            mod.Directory.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || mod.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    private void OnSkinSelected(bool success, string path)
    {
        if (!success || string.IsNullOrWhiteSpace(path))
            return;

        this.plugin.Configuration.SelectedSkinPath = path;
        this.plugin.Save();
        this.plugin.LoadConfiguredSkin();
    }

    private bool ToggleButton(string label, bool enabled, Vector2 size)
    {
        return SkinnedPanel.Button(
            this.plugin.CurrentSkin,
            $"##toggle_{label}",
            label,
            size,
            enabled);
    }

    private sealed class ContactLogo(IDalamudTextureWrap texture, Vector2 size) : IDisposable
    {
        public IDalamudTextureWrap Texture { get; } = texture;

        public Vector2 Size { get; } = size;

        public void Dispose()
            => this.Texture.Dispose();
    }
}
