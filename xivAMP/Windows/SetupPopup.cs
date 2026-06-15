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
    private static readonly Vector2 PresetConfirmSize = new(330, 130);

    private readonly Plugin plugin;
    private readonly XivAmpController controller;
    private readonly FileDialogManager fileDialogManager;
    private ContactLogo? discordLogo;
    private ContactLogo? nRootLogo;
    private bool contactLogosLoaded;
    private string audioModFilter = string.Empty;
    private string animationModFilter = string.Empty;
    private string selectedPresetName = string.Empty;
    private string presetNameBuffer = string.Empty;
    private string renamePresetBuffer = string.Empty;
    private string pendingPresetName = string.Empty;
    private string changedItemsModDir = string.Empty;
    private IReadOnlyList<ChangedItem> changedItems = Array.Empty<ChangedItem>();
    private string changedItemsError = string.Empty;

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

        // Scale is locked at 1.0 for now... Maybe will make x2 option later
        if (this.plugin.Configuration.SkinScale != 1.0f)
        {
            this.plugin.Configuration.SkinScale = 1.0f;
            this.plugin.Save();
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
            ImGui.SetTooltip("Redraw your character after applying a track.\nRequired for the replacement SCD to be requested and start playing.");

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
        this.Section("audio track mod", columnWidth);
        this.DrawAudioModCombo(columnWidth);
        this.Section("animation mod", columnWidth);
        this.DrawAnimationModCombo(columnWidth);
        this.DrawChangedItems(columnWidth);
        this.DrawContacts(columnWidth);
    }

    public void Dispose()
    {
        this.discordLogo?.Dispose();
        this.nRootLogo?.Dispose();
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

    private void DrawAudioModCombo(float columnWidth)
    {
        var selectedMod = this.controller.Mods.FirstOrDefault(mod => mod.Directory == this.plugin.Configuration.SelectedModDirectory);
        var selectedLabel = string.IsNullOrWhiteSpace(selectedMod.Directory) ? "Choose audio track mod" : selectedMod.Label;
        this.BeginSetupColumn();
        ImGui.SetNextItemWidth(columnWidth);
        if (!ImGui.BeginCombo("##audio_mod", selectedLabel))
            return;

        ImGui.SetNextItemWidth(-1);
        if (ImGui.IsWindowAppearing())
            ImGui.SetKeyboardFocusHere();

        ImGui.InputTextWithHint("##audio_mod_filter", "filter mods", ref this.audioModFilter, 128);
        ImGui.Separator();

        foreach (var mod in this.FilteredMods(this.audioModFilter))
        {
            var selected = mod.Directory == this.plugin.Configuration.SelectedModDirectory;
            if (ImGui.Selectable(mod.Label, selected))
            {
                this.audioModFilter = string.Empty;
                this.controller.SelectMod(mod.Directory);
            }

            if (selected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
    }

    private void DrawAnimationModCombo(float columnWidth)
    {
        var selectedDirectory = this.plugin.Configuration.SelectedAnimationModDirectory;
        var selectedMod = this.controller.Mods.FirstOrDefault(mod => mod.Directory == selectedDirectory);
        var selectedLabel = string.IsNullOrWhiteSpace(selectedDirectory)
            ? "(use audio mod)"
            : selectedMod.Label;

        this.BeginSetupColumn();
        ImGui.SetNextItemWidth(columnWidth);
        if (!ImGui.BeginCombo("##animation_mod", selectedLabel))
            return;

        ImGui.SetNextItemWidth(-1);
        if (ImGui.IsWindowAppearing())
            ImGui.SetKeyboardFocusHere();

        ImGui.InputTextWithHint("##animation_mod_filter", "filter mods", ref this.animationModFilter, 128);
        ImGui.Separator();

        if (ImGui.Selectable("(use audio mod)", string.IsNullOrWhiteSpace(selectedDirectory)))
            this.controller.SelectAnimationMod(string.Empty);

        foreach (var mod in this.FilteredMods(this.animationModFilter))
        {
            var selected = mod.Directory == selectedDirectory;
            if (ImGui.Selectable(mod.Label, selected))
            {
                this.animationModFilter = string.Empty;
                this.controller.SelectAnimationMod(mod.Directory);
            }

            if (selected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
    }

    private void DrawChangedItems(float columnWidth)
    {
        // Refresh the cached list only when the selected mod changes (the IPC call is not
        // free, so we don't run it every frame).
        var modDir = string.IsNullOrWhiteSpace(this.plugin.Configuration.SelectedAnimationModDirectory)
            ? this.plugin.Configuration.SelectedModDirectory
            : this.plugin.Configuration.SelectedAnimationModDirectory;
        if (!string.Equals(modDir, this.changedItemsModDir, StringComparison.Ordinal))
        {
            this.changedItemsModDir = modDir;
            var result = this.plugin.Penumbra.GetChangedItemsList(modDir);
            this.changedItems = result.Success && result.Value is not null ? result.Value : Array.Empty<ChangedItem>();
            this.changedItemsError = result.Success ? string.Empty : result.Error;
        }

        if (string.IsNullOrWhiteSpace(modDir))
            return;

        this.Section("Select emote to play", columnWidth);
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
