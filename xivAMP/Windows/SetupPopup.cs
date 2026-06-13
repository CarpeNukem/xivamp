using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using xivAMP.Services;
using xivAMP.Skin;

namespace xivAMP.Windows;

public sealed class SetupPopup
{
    private const float ButtonHeight = 15;
    private const float RowGap = 5;
    private const float SetupColumnTargetWidth = 310;
    private static readonly Vector2 PresetConfirmSize = new(330, 130);

    private readonly Plugin plugin;
    private readonly XivAmpController controller;
    private readonly FileDialogManager fileDialogManager;
    private string modFilter = string.Empty;
    private string selectedPresetName = string.Empty;
    private string presetNameBuffer = string.Empty;
    private string renamePresetBuffer = string.Empty;
    private string pendingPresetName = string.Empty;

    public SetupPopup(Plugin plugin, XivAmpController controller, FileDialogManager fileDialogManager)
    {
        this.plugin = plugin;
        this.controller = controller;
        this.fileDialogManager = fileDialogManager;
    }

    public void Draw()
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

        // Scale is locked at 1.0 for now; the skin-museum link takes its place.
        if (this.plugin.Configuration.SkinScale != 1.0f)
        {
            this.plugin.Configuration.SkinScale = 1.0f;
            this.plugin.Save();
        }

        SkinnedPanel.SameRow(RowGap);
        LinkText("Browse skins", "https://skins.webamp.org/");

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

        this.DrawPresetControls(columnWidth);
        this.BeginSetupColumn();
        this.Section("mod", columnWidth);
        this.DrawModCombo(columnWidth);
    }

    private float BeginSetupColumn()
        => SkinnedPanel.CenterContentColumn(this.plugin.CurrentSkin, SetupColumnTargetWidth);

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
        this.BeginSetupColumn();
        SkinnedPanel.Section(this.plugin.CurrentSkin, label, columnWidth);
    }

    private void DrawPresetControls(float columnWidth)
    {
        this.Section("playlists", columnWidth);
        this.EnsureSelectedPreset();

        var selectedPreset = this.SelectedPreset();
        var comboLabel = selectedPreset is null
            ? "No saved playlists"
            : $"{selectedPreset.Name} ({selectedPreset.Entries.Count})";

        var presetFieldWidth = MathF.Max(145, columnWidth - 56 - 68 - RowGap * 2);
        var editFieldWidth = MathF.Max(145, columnWidth - 72 - RowGap);

        this.BeginSetupColumn();
        ImGui.SetNextItemWidth(presetFieldWidth);
        if (ImGui.BeginCombo("##preset", comboLabel))
        {
            foreach (var preset in this.plugin.Configuration.SavedPlaylists.OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase))
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
                    this.EnsureSelectedPreset();
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

    private void EnsureSelectedPreset()
    {
        if (!string.IsNullOrWhiteSpace(this.selectedPresetName) && this.SelectedPreset() is not null)
            return;

        var first = this.plugin.Configuration.SavedPlaylists
            .OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
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

    private PlaylistPreset? SelectedPreset()
        => this.plugin.Configuration.SavedPlaylists.FirstOrDefault(preset =>
            string.Equals(preset.Name, this.selectedPresetName, StringComparison.OrdinalIgnoreCase));

    private void DrawModCombo(float columnWidth)
    {
        var selectedMod = this.controller.Mods.FirstOrDefault(mod => mod.Directory == this.plugin.Configuration.SelectedModDirectory);
        var selectedLabel = string.IsNullOrWhiteSpace(selectedMod.Directory) ? "Choose Penumbra mod" : selectedMod.Label;
        this.BeginSetupColumn();
        ImGui.SetNextItemWidth(columnWidth);
        if (!ImGui.BeginCombo("##mod", selectedLabel))
            return;

        ImGui.SetNextItemWidth(-1);
        if (ImGui.IsWindowAppearing())
            ImGui.SetKeyboardFocusHere();

        ImGui.InputTextWithHint("##modfilter", "filter mods", ref this.modFilter, 128);
        ImGui.Separator();

        foreach (var mod in this.FilteredMods())
        {
            var selected = mod.Directory == this.plugin.Configuration.SelectedModDirectory;
            if (ImGui.Selectable(mod.Label, selected))
            {
                this.modFilter = string.Empty;
                this.controller.SelectMod(mod.Directory);
            }

            if (selected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
    }

    private IEnumerable<PenumbraMod> FilteredMods()
    {
        if (string.IsNullOrWhiteSpace(this.modFilter))
            return this.controller.Mods;

        return this.controller.Mods.Where(mod =>
            mod.Directory.Contains(this.modFilter, StringComparison.OrdinalIgnoreCase)
            || mod.Name.Contains(this.modFilter, StringComparison.OrdinalIgnoreCase));
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
}
