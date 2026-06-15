using Dalamud.Plugin.Services;

namespace xivAMP.Services;

public sealed class XivAmpController
{
    private readonly Plugin plugin;
    private readonly AudioMetadataService audioMetadata;
    private readonly IPluginLog log;
    private readonly IObjectTable objectTable;
    private readonly Random random = new();
    private readonly HashSet<string> shuffleHistory = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<PenumbraMod> mods = [];
    private IReadOnlyDictionary<string, string[]> groups = new Dictionary<string, string[]>();
    private bool dataLoaded;

    // True while the mod's "on play" emote has already been fired for the current playback
    // session, so auto-advancing between tracks doesn't restart it. Reset on stop/pause/etc.
    private bool emoteActive;

    public XivAmpController(Plugin plugin, IPluginLog log, IObjectTable objectTable)
    {
        this.plugin = plugin;
        this.log = log;
        this.objectTable = objectTable;
        this.audioMetadata = new AudioMetadataService();
        this.NormalizePlaylistEntries();
    }

    public string Status { get; private set; } = "Ready";

    public IReadOnlyList<PenumbraMod> Mods
    {
        get
        {
            this.EnsureDataLoaded();
            return this.mods;
        }
    }

    public IReadOnlyDictionary<string, string[]> Groups
    {
        get
        {
            this.EnsureDataLoaded();
            return this.groups;
        }
    }

    public AudioMetadataService AudioMetadata => this.audioMetadata;

    public PlaylistEntry? CurrentEntry()
    {
        var index = this.plugin.Configuration.CurrentIndex;
        return index >= 0 && index < this.plugin.Configuration.Playlist.Count
            ? this.plugin.Configuration.Playlist[index]
            : null;
    }

    /// <summary>
    /// The playlist entry that is actually applied/playing (which can differ from
    /// <see cref="CurrentEntry"/> — a single click changes the selection without applying).
    /// </summary>
    public PlaylistEntry? AppliedEntry()
    {
        if (string.IsNullOrWhiteSpace(this.plugin.Configuration.LastAppliedOptionName))
            return null;

        return this.plugin.Configuration.Playlist.FirstOrDefault(entry => this.IsEntryIdentity(
            entry,
            this.plugin.Configuration.LastAppliedOptionGroup,
            this.plugin.Configuration.LastAppliedOptionName));
    }

    public bool PresetExists(string name)
        => this.FindPreset(name) is not null;

    public void ReloadPenumbraData()
    {
        var modsResult = this.plugin.Penumbra.GetMods();
        if (!modsResult.Success || modsResult.Value is null)
        {
            this.Status = modsResult.Error;
            this.mods = [];
            this.groups = new Dictionary<string, string[]>();
            this.dataLoaded = true;
            return;
        }

        this.mods = modsResult.Value;
        this.ReloadGroups();
        this.dataLoaded = true;
    }

    /// <summary>
    /// Re-read the selected mod's option groups from Penumbra so options added to the mod
    /// since it was last loaded show up in the Add panel. Keeps the current selection.
    /// </summary>
    public void RefreshGroups()
    {
        if (string.IsNullOrWhiteSpace(this.plugin.Configuration.SelectedModDirectory))
        {
            this.Status = "Choose a Penumbra mod first.";
            return;
        }

        this.ReloadGroups();
        var count = this.groups.TryGetValue(this.plugin.Configuration.SelectedOptionGroup, out var options) ? options.Length : 0;
        this.Status = count > 0 ? $"Refreshed - {count} options in group." : "Refreshed options.";
    }

    public void SelectMod(string directory)
    {
        this.ResetActiveTrack("Reset previous mod option.", false);
        this.shuffleHistory.Clear();
        this.plugin.Configuration.SelectedModDirectory = directory;
        this.plugin.Configuration.SelectedOptionGroup = string.Empty;
        this.plugin.Configuration.Playlist.Clear();
        this.plugin.Configuration.CurrentIndex = -1;
        this.plugin.Save();
        this.audioMetadata.InvalidateCache();
        this.ReloadGroups();
    }

    public void SelectGroup(string group)
    {
        if (!string.Equals(this.plugin.Configuration.SelectedOptionGroup, group, StringComparison.OrdinalIgnoreCase))
            this.ResetActiveTrack("Reset previous option group.", false);

        this.plugin.Configuration.SelectedOptionGroup = group;
        this.BuildPlaylistFromGroup(group);
        this.plugin.Save();
    }

    public void LoadSelectedGroup()
    {
        if (string.IsNullOrWhiteSpace(this.plugin.Configuration.SelectedOptionGroup))
        {
            this.Status = "Choose an option group first.";
            return;
        }

        this.BuildPlaylistFromGroup(this.plugin.Configuration.SelectedOptionGroup);
        this.plugin.Save();
    }

    public void ApplyRelative(int delta)
    {
        var playlist = this.plugin.Configuration.Playlist;
        if (playlist.Count == 0)
        {
            this.Status = "Playlist is empty.";
            return;
        }

        var baseIndex = -1;
        if (!string.IsNullOrWhiteSpace(this.plugin.Configuration.LastAppliedOptionName))
        {
            baseIndex = playlist.FindIndex(entry =>
                this.IsEntryIdentity(
                    entry,
                    this.plugin.Configuration.LastAppliedOptionGroup,
                    this.plugin.Configuration.LastAppliedOptionName));
        }

        if (baseIndex < 0)
            baseIndex = this.plugin.Configuration.CurrentIndex;

        int next;
        if (this.plugin.Configuration.ShuffleEnabled)
        {
            // Play every track once before any repeats (Winamp-style play-through).
            var unplayed = new List<int>();
            for (var i = 0; i < playlist.Count; i++)
            {
                if (i != baseIndex && !this.shuffleHistory.Contains(EntryKey(playlist[i].OptionGroup, playlist[i].OptionName)))
                    unplayed.Add(i);
            }

            if (unplayed.Count == 0)
            {
                if (!this.plugin.Configuration.RepeatEnabled)
                {
                    this.Status = "End of playlist.";
                    this.shuffleHistory.Clear();
                    this.plugin.Configuration.LastAppliedOptionGroup = string.Empty;
                    this.plugin.Configuration.LastAppliedOptionName = string.Empty;
                    this.plugin.Save();
                    return;
                }

                // Repeat on: start a new shuffle round.
                this.shuffleHistory.Clear();
                for (var i = 0; i < playlist.Count; i++)
                {
                    if (i != baseIndex)
                        unplayed.Add(i);
                }
            }

            next = unplayed.Count == 0
                ? Math.Max(0, baseIndex)
                : unplayed[this.random.Next(unplayed.Count)];
        }
        else
        {
            var raw = baseIndex < 0 ? 0 : baseIndex + delta;
            if (!this.plugin.Configuration.RepeatEnabled && (raw < 0 || raw >= playlist.Count))
            {
                this.Status = "End of playlist.";
                this.plugin.Configuration.LastAppliedOptionGroup = string.Empty;
                this.plugin.Configuration.LastAppliedOptionName = string.Empty;
                this.plugin.Save();
                return;
            }

            next = (raw + playlist.Count) % playlist.Count;
        }

        this.plugin.Configuration.CurrentIndex = next;
        this.plugin.Save();
        this.ApplyCurrent();
    }

    public void ApplyCurrent()
    {
        var entry = this.CurrentEntry();
        if (entry is null)
        {
            this.Status = "No playlist entry selected.";
            return;
        }

        var player = this.objectTable.LocalPlayer;
        if (player is null)
        {
            this.Status = "Local player is not available.";
            return;
        }

        // Populate metadata from SCD file if not yet loaded.
        this.audioMetadata.Populate(entry, this.plugin.Configuration.SelectedModDirectory, this.plugin.Penumbra);
        if (!this.ResetPreviousGroupIfNeeded(entry.OptionGroup))
            return;

        var result = this.plugin.Penumbra.ApplyTrack(
            (int)player.ObjectIndex,
            this.plugin.Configuration.SelectedModDirectory,
            entry.OptionGroup,
            entry.OptionName,
            this.plugin.Configuration.UseTemporarySettings,
            this.plugin.Configuration.RedrawAfterApply);

        if (result.Success)
        {
            this.shuffleHistory.Add(EntryKey(entry.OptionGroup, entry.OptionName));
            this.plugin.Configuration.LastAppliedOptionGroup = entry.OptionGroup;
            this.plugin.Configuration.LastAppliedOptionName = entry.OptionName;
            this.plugin.Configuration.LastAppliedAtUtc = DateTime.UtcNow;
            this.plugin.Configuration.EstimatedSeekOffsetSeconds = 0;
            this.plugin.Configuration.IsPaused = false;
            this.plugin.Configuration.IsStopped = false;
            this.plugin.Save();
            this.MaybeFireModEmote();
            this.Status = $"Applied {entry.Label}";
            return;
        }

        this.Status = result.Error;
    }

    /// <summary>
    /// Fire the selected mod's "on play" emote once per playback session. Skipped if one is
    /// already considered active (so auto-advancing tracks doesn't restart the emote).
    /// </summary>
    private void MaybeFireModEmote()
    {
        if (this.emoteActive)
            return;

        var modDir = this.plugin.Configuration.SelectedModDirectory;
        if (string.IsNullOrWhiteSpace(modDir)
            || !this.plugin.Configuration.ModEmoteSets.TryGetValue(modDir, out var emotes)
            || emotes is null
            || emotes.Count == 0)
            return;

        // Don't restart if one of this mod's selected emotes is already playing (matched by
        // the live emote id, so idle/sit/doze don't count). Treat it as satisfied this session.
        var player = this.objectTable.LocalPlayer;
        if (player is not null
            && emotes.Any(emote => this.plugin.Emotes.IsPerformingEmote(player.Address, (ushort)emote.EmoteId)))
        {
            this.emoteActive = true;
            return;
        }

        // A mod can have several selected emotes; pick one at random for this session.
        var trigger = emotes[this.random.Next(emotes.Count)];
        if (trigger.EmoteId == 0)
            return;

        if (this.plugin.Emotes.TryExecute((ushort)trigger.EmoteId, out var error))
            this.emoteActive = true;
        else
            this.log.Warning("Did not play emote {Name}: {Error}", trigger.Name, error);
    }

    public void PauseCurrent()
    {
        // Toggle pause/resume. Resuming re-applies the selected track (true per-track
        // seek isn't possible with Penumbra option-swapping, so it restarts the track).
        if (this.plugin.Configuration.IsPaused)
        {
            this.ApplyCurrent();
            return;
        }

        this.ApplyDefaultOption(preserveCurrentIndex: true, paused: true, status: "Paused.");
    }

    public void StopCurrent()
    {
        // Stop = switch to the group's default/"off" option (applied directly through
        // Penumbra), or clear xivAMP's temporary settings if the group has no off option.
        // This silences the music whether or not an "off" entry exists in the playlist.
        // The current selection is kept, so Play resumes the same track.
        this.ApplyDefaultOption(preserveCurrentIndex: true, paused: false, status: "Stopped.");

        // Show a stopped state (no running clock / visualizer) and don't auto-advance.
        this.plugin.Configuration.IsStopped = true;
        this.plugin.Save();
    }

    public void MovePlaylistEntry(int sourceIndex, int targetIndex)
    {
        var playlist = this.plugin.Configuration.Playlist;
        if (sourceIndex == targetIndex || sourceIndex < 0 || sourceIndex >= playlist.Count || targetIndex < 0 || targetIndex >= playlist.Count)
            return;

        var currentEntry = this.CurrentEntry();
        var entry = playlist[sourceIndex];
        playlist.RemoveAt(sourceIndex);
        playlist.Insert(targetIndex, entry);
        this.plugin.Configuration.CurrentIndex = currentEntry is null ? -1 : playlist.IndexOf(currentEntry);
        this.plugin.Save();
    }

    public void AddPlaylistEntry(string optionGroup, string optionName)
    {
        if (string.IsNullOrWhiteSpace(optionGroup) || string.IsNullOrWhiteSpace(optionName))
            return;

        var playlist = this.plugin.Configuration.Playlist;
        if (playlist.Any(entry => this.IsEntryIdentity(entry, optionGroup, optionName)))
        {
            this.Status = "Playlist already contains that option.";
            return;
        }

        var entry = new PlaylistEntry { OptionGroup = optionGroup, OptionName = optionName };
        this.audioMetadata.Populate(entry, this.plugin.Configuration.SelectedModDirectory, this.plugin.Penumbra);
        playlist.Add(entry);
        if (this.plugin.Configuration.CurrentIndex < 0)
            this.plugin.Configuration.CurrentIndex = 0;

        this.plugin.Save();
        this.Status = $"Added {entry.Label}.";
    }

    public void AddPlaylistEntries(string optionGroup, IEnumerable<string> optionNames)
    {
        if (string.IsNullOrWhiteSpace(optionGroup))
            return;

        var added = 0;
        foreach (var optionName in optionNames.Where(option => !string.IsNullOrWhiteSpace(option)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (this.plugin.Configuration.Playlist.Any(entry => this.IsEntryIdentity(entry, optionGroup, optionName)))
                continue;

            var entry = new PlaylistEntry { OptionGroup = optionGroup, OptionName = optionName };
            this.audioMetadata.Populate(entry, this.plugin.Configuration.SelectedModDirectory, this.plugin.Penumbra);
            this.plugin.Configuration.Playlist.Add(entry);
            added++;
        }

        if (this.plugin.Configuration.CurrentIndex < 0 && this.plugin.Configuration.Playlist.Count > 0)
            this.plugin.Configuration.CurrentIndex = 0;

        this.plugin.Save();
        this.Status = added == 0 ? "Selected options are already in the playlist." : $"Added {added} tracks.";
    }

    public void AddPlaylistGroup(string optionGroup)
    {
        if (string.IsNullOrWhiteSpace(optionGroup) || !this.groups.TryGetValue(optionGroup, out var options))
        {
            this.Status = "Choose an option group first.";
            return;
        }

        // The group's first option is the default / "off" value; don't add it when adding the
        // whole group (it can still be added individually via its checkbox).
        var defaultOption = this.DefaultOptionForGroup(optionGroup);

        var added = 0;
        foreach (var option in options)
        {
            if (string.Equals(option, defaultOption, StringComparison.OrdinalIgnoreCase))
                continue;

            if (this.plugin.Configuration.Playlist.Any(entry => this.IsEntryIdentity(entry, optionGroup, option)))
                continue;

            var entry = new PlaylistEntry { OptionGroup = optionGroup, OptionName = option };
            this.audioMetadata.Populate(entry, this.plugin.Configuration.SelectedModDirectory, this.plugin.Penumbra);
            this.plugin.Configuration.Playlist.Add(entry);
            added++;
        }

        if (this.plugin.Configuration.CurrentIndex < 0 && this.plugin.Configuration.Playlist.Count > 0)
            this.plugin.Configuration.CurrentIndex = 0;

        this.plugin.Save();
        this.Status = added == 0 ? "Group is already in the playlist." : $"Added {added} tracks from {optionGroup}.";
    }

    public void RemovePlaylistEntry(int index)
    {
        var playlist = this.plugin.Configuration.Playlist;
        if (index < 0 || index >= playlist.Count)
            return;

        var removed = playlist[index];
        var currentEntry = this.CurrentEntry();
        playlist.RemoveAt(index);
        this.plugin.Configuration.CurrentIndex = currentEntry is null ? Math.Min(index, playlist.Count - 1) : playlist.IndexOf(currentEntry);
        if (this.plugin.Configuration.CurrentIndex < 0 && playlist.Count > 0)
            this.plugin.Configuration.CurrentIndex = Math.Min(index, playlist.Count - 1);

        if (this.IsEntryIdentity(removed, this.plugin.Configuration.LastAppliedOptionGroup, this.plugin.Configuration.LastAppliedOptionName))
        {
            this.plugin.Configuration.LastAppliedOptionGroup = string.Empty;
            this.plugin.Configuration.LastAppliedOptionName = string.Empty;
        }

        this.plugin.Save();
        this.Status = $"Removed {removed.Label}.";
    }

    public void CropToEntry(int index)
    {
        var playlist = this.plugin.Configuration.Playlist;
        if (index < 0 || index >= playlist.Count)
            return;

        if (playlist.Count == 1)
        {
            this.Status = "Playlist already contains only that track.";
            return;
        }

        var keep = playlist[index];

        // Clear applied state if the applied track is among the removed entries.
        if (!string.IsNullOrWhiteSpace(this.plugin.Configuration.LastAppliedOptionName)
            && !this.IsEntryIdentity(keep, this.plugin.Configuration.LastAppliedOptionGroup, this.plugin.Configuration.LastAppliedOptionName))
        {
            this.plugin.Configuration.LastAppliedOptionGroup = string.Empty;
            this.plugin.Configuration.LastAppliedOptionName = string.Empty;
        }

        playlist.Clear();
        playlist.Add(keep);
        this.plugin.Configuration.CurrentIndex = 0;
        this.plugin.Save();
        this.Status = $"Cropped playlist to {keep.Label}.";
    }

    public void ClearPlaylist()
    {
        this.ResetActiveTrack("Stopped current playlist.", false);
        this.shuffleHistory.Clear();
        this.plugin.Configuration.Playlist.Clear();
        this.plugin.Configuration.CurrentIndex = -1;
        this.ClearAppliedState();
        this.plugin.Save();
        this.Status = "Playlist cleared.";
    }

    public bool SaveCurrentPlaylistAs(string name)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            this.Status = "Enter a playlist name first.";
            return false;
        }

        var preset = this.FindPreset(name);
        if (preset is null)
        {
            preset = new PlaylistPreset();
            this.plugin.Configuration.SavedPlaylists.Add(preset);
        }

        preset.Name = name;
        preset.SelectedModDirectory = this.plugin.Configuration.SelectedModDirectory;
        preset.CurrentIndex = this.plugin.Configuration.CurrentIndex;
        preset.Entries = this.CloneEntries(this.plugin.Configuration.Playlist);

        this.plugin.Configuration.SavedPlaylists = this.plugin.Configuration.SavedPlaylists
            .OrderBy(saved => saved.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        this.plugin.Save();
        this.Status = $"Saved playlist '{name}' ({preset.Entries.Count} tracks).";
        return true;
    }

    public bool LoadPlaylistPreset(string name)
    {
        var preset = this.FindPreset(name);
        if (preset is null)
        {
            this.Status = "Saved playlist no longer exists.";
            return false;
        }

        this.ResetActiveTrack("Stopped current playlist.", false);
        this.shuffleHistory.Clear();
        this.plugin.Configuration.SelectedModDirectory = preset.SelectedModDirectory;
        this.plugin.Configuration.Playlist = this.CloneEntries(preset.Entries);
        this.plugin.Configuration.CurrentIndex = this.NormalizePresetIndex(preset.CurrentIndex);
        var firstPresetGroup = this.plugin.Configuration.Playlist
            .Select(entry => entry.OptionGroup)
            .FirstOrDefault(group => !string.IsNullOrWhiteSpace(group)) ?? string.Empty;
        this.plugin.Configuration.SelectedOptionGroup = string.Empty;
        this.ClearAppliedState();
        this.audioMetadata.InvalidateCache();
        this.ReloadGroups();
        this.plugin.Configuration.SelectedOptionGroup = this.groups.ContainsKey(firstPresetGroup) ? firstPresetGroup : string.Empty;
        this.NormalizePlaylistEntries();

        var missing = this.CountMissingPresetEntries();
        this.plugin.Save();
        this.Status = missing == 0
            ? $"Loaded playlist '{preset.Name}' ({this.plugin.Configuration.Playlist.Count} tracks)."
            : $"Loaded playlist '{preset.Name}' ({this.plugin.Configuration.Playlist.Count} tracks, {missing} missing options).";
        return true;
    }

    public bool DeletePlaylistPreset(string name)
    {
        var preset = this.FindPreset(name);
        if (preset is null)
        {
            this.Status = "Saved playlist no longer exists.";
            return false;
        }

        this.plugin.Configuration.SavedPlaylists.Remove(preset);
        this.plugin.Save();
        this.Status = $"Deleted playlist '{preset.Name}'.";
        return true;
    }

    public bool RenamePlaylistPreset(string oldName, string newName)
    {
        newName = newName.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            this.Status = "Enter a new playlist name first.";
            return false;
        }

        var preset = this.FindPreset(oldName);
        if (preset is null)
        {
            this.Status = "Saved playlist no longer exists.";
            return false;
        }

        if (!string.Equals(preset.Name, newName, StringComparison.OrdinalIgnoreCase) && this.PresetExists(newName))
        {
            this.Status = $"A playlist named '{newName}' already exists.";
            return false;
        }

        preset.Name = newName;
        this.plugin.Configuration.SavedPlaylists = this.plugin.Configuration.SavedPlaylists
            .OrderBy(saved => saved.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        this.plugin.Save();
        this.Status = $"Renamed playlist to '{newName}'.";
        return true;
    }

    public void SetEstimatedSeek(double seconds)
    {
        this.plugin.Configuration.EstimatedSeekOffsetSeconds = Math.Max(0, seconds);
        this.plugin.Configuration.LastAppliedAtUtc = DateTime.UtcNow;
        this.plugin.Save();
        this.Status = "Seek is visual-only until audio hook support.";
    }

    public void ScanAllMetadata()
    {
        var modDir = this.plugin.Configuration.SelectedModDirectory;
        if (string.IsNullOrWhiteSpace(modDir))
        {
            this.Status = "Choose a Penumbra mod first.";
            return;
        }

        var modPath = this.plugin.Penumbra.ResolveModPath(modDir);
        if (string.IsNullOrWhiteSpace(modPath) || !Directory.Exists(modPath))
        {
            this.Status = $"Could not resolve mod path for '{modDir}'.";
            return;
        }

        this.audioMetadata.InvalidateCache();
        var populated = 0;
        foreach (var entry in this.plugin.Configuration.Playlist)
        {
            // Clear existing metadata so Populate always re-reads from SCD.
            entry.ScdPath = string.Empty;
            entry.DurationSeconds = 0;
            entry.SampleRate = 0;
            entry.BitrateKbps = 0;

            this.audioMetadata.Populate(entry, modDir, this.plugin.Penumbra);
            if (entry.DurationSeconds > 0)
                populated++;
        }

        this.plugin.Save();
        var total = this.plugin.Configuration.Playlist.Count;
        var withScd = this.plugin.Configuration.Playlist.Count(e => !string.IsNullOrWhiteSpace(e.ScdPath));
        var withDuration = this.plugin.Configuration.Playlist.Count(e => e.DurationSeconds > 0);
        this.Status = $"Scan done: {withScd}/{total} SCD found, {withDuration}/{total} with duration (+{populated} new).";
    }

    public void SetStatus(string status)
        => this.Status = status;

    /// <summary>
    /// Fully release control over the music mod: restore the default option for the
    /// active group and clear all of xivAMP's temporary Penumbra settings.
    /// Called when the UI closes or the plugin unloads.
    /// </summary>
    public void ReleaseControl()
    {
        this.ResetActiveTrack("Stopped — released mod control.", true);
        this.plugin.Save();
    }

    private void EnsureDataLoaded()
    {
        if (this.dataLoaded)
            return;

        this.ReloadPenumbraData();
    }

    private void ReloadGroups()
    {
        var groupsResult = this.plugin.Penumbra.GetAvailableSettings(this.plugin.Configuration.SelectedModDirectory);
        if (!groupsResult.Success || groupsResult.Value is null)
        {
            this.Status = groupsResult.Error;
            this.groups = new Dictionary<string, string[]>();
            return;
        }

        this.groups = groupsResult.Value;
        this.Status = this.groups.Count == 0 ? "Choose a Penumbra mod." : "Penumbra options loaded.";

        if (!string.IsNullOrWhiteSpace(this.plugin.Configuration.SelectedOptionGroup)
            && !this.groups.ContainsKey(this.plugin.Configuration.SelectedOptionGroup))
        {
            this.plugin.Configuration.SelectedOptionGroup = string.Empty;
            this.plugin.Configuration.Playlist.Clear();
            this.plugin.Configuration.CurrentIndex = -1;
            this.ClearAppliedState();
            this.plugin.Save();
            this.Status = "Selected option group no longer exists.";
        }
    }

    private void ResetActiveTrack(string successStatus, bool clearTemporaryAfterDefault)
    {
        // Releasing/clearing playback - the emote may fire again on the next play.
        this.emoteActive = false;

        var previousMod = this.plugin.Configuration.SelectedModDirectory;
        var previousGroup = !string.IsNullOrWhiteSpace(this.plugin.Configuration.LastAppliedOptionGroup)
            ? this.plugin.Configuration.LastAppliedOptionGroup
            : this.plugin.Configuration.SelectedOptionGroup;
        var defaultOption = this.DefaultOptionForGroup(previousGroup);
        this.ClearAppliedState();

        var player = this.objectTable.LocalPlayer;
        if (player is null)
        {
            this.Status = successStatus;
            return;
        }

        var appliedDefault = false;
        if (!string.IsNullOrWhiteSpace(previousMod)
            && !string.IsNullOrWhiteSpace(previousGroup)
            && !string.IsNullOrWhiteSpace(defaultOption))
        {
            var defaultResult = this.plugin.Penumbra.ApplyTrack(
                (int)player.ObjectIndex,
                previousMod,
                previousGroup,
                defaultOption,
                this.plugin.Configuration.UseTemporarySettings,
                this.plugin.Configuration.RedrawAfterApply);
            if (!defaultResult.Success)
            {
                this.Status = defaultResult.Error;
                return;
            }

            appliedDefault = true;
        }

        if (appliedDefault && !clearTemporaryAfterDefault)
        {
            this.Status = successStatus;
            return;
        }

        var result = this.plugin.Penumbra.ClearTemporaryPlayerSettings(
            (int)player.ObjectIndex,
            this.plugin.Configuration.RedrawAfterApply);
        this.Status = result.Success ? successStatus : result.Error;
    }

    private string DefaultOptionForGroup(string group)
        => !string.IsNullOrWhiteSpace(group) && this.groups.TryGetValue(group, out var options) && options.Length > 0
            ? options[0]
            : string.Empty;

    private void ApplyDefaultOption(bool preserveCurrentIndex, bool paused, string status)
    {
        // Playback is stopping/pausing - allow the emote to fire again on the next play.
        this.emoteActive = false;

        var currentEntry = this.CurrentEntry();
        var optionGroup = !string.IsNullOrWhiteSpace(currentEntry?.OptionGroup)
            ? currentEntry.OptionGroup
            : this.plugin.Configuration.SelectedOptionGroup;
        var player = this.objectTable.LocalPlayer;
        if (player is null)
        {
            this.Status = "Local player is not available.";
            return;
        }

        var defaultOption = this.DefaultOptionForGroup(optionGroup);
        var originalIndex = this.plugin.Configuration.CurrentIndex;

        // Swap to the group's default/"off" option to silence the music. If no default
        // option can be resolved (e.g. the mod's option groups aren't loaded), fall back
        // to clearing xivAMP's temporary Penumbra settings so the music still stops.
        var result = string.IsNullOrWhiteSpace(defaultOption)
            ? this.plugin.Penumbra.ClearTemporaryPlayerSettings((int)player.ObjectIndex, this.plugin.Configuration.RedrawAfterApply)
            : this.plugin.Penumbra.ApplyTrack(
                (int)player.ObjectIndex,
                this.plugin.Configuration.SelectedModDirectory,
                optionGroup,
                defaultOption,
                this.plugin.Configuration.UseTemporarySettings,
                this.plugin.Configuration.RedrawAfterApply);
        if (!result.Success)
        {
            this.Status = result.Error;
            return;
        }

        if (preserveCurrentIndex)
        {
            this.plugin.Configuration.CurrentIndex = originalIndex;
        }
        else
        {
            var defaultIndex = this.plugin.Configuration.Playlist.FindIndex(entry =>
                this.IsEntryIdentity(entry, optionGroup, defaultOption));
            this.plugin.Configuration.CurrentIndex = defaultIndex >= 0 ? defaultIndex : this.plugin.Configuration.CurrentIndex;
        }

        this.plugin.Configuration.LastAppliedOptionGroup = string.Empty;
        this.plugin.Configuration.LastAppliedOptionName = string.Empty;
        this.plugin.Configuration.LastAppliedAtUtc = default;
        this.plugin.Configuration.EstimatedSeekOffsetSeconds = 0;
        this.plugin.Configuration.IsPaused = paused;
        this.plugin.Configuration.IsStopped = false;
        this.plugin.Save();
        this.Status = status;
    }

    private void ClearAppliedState()
    {
        this.plugin.Configuration.LastAppliedOptionGroup = string.Empty;
        this.plugin.Configuration.LastAppliedOptionName = string.Empty;
        this.plugin.Configuration.LastAppliedAtUtc = default;
        this.plugin.Configuration.EstimatedSeekOffsetSeconds = 0;
        this.plugin.Configuration.IsPaused = false;
    }

    private void BuildPlaylistFromGroup(string group)
    {
        if (!this.groups.TryGetValue(group, out var options))
        {
            this.Status = "Selected option group no longer exists.";
            return;
        }

        this.shuffleHistory.Clear();

        var previousEntries = new Dictionary<string, PlaylistEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in this.plugin.Configuration.Playlist)
        {
            var key = EntryKey(entry.OptionGroup, entry.OptionName);
            if (string.IsNullOrWhiteSpace(entry.OptionName) || previousEntries.ContainsKey(key))
                continue;

            previousEntries[key] = entry;
        }

        this.plugin.Configuration.Playlist = options
            .Select(option =>
            {
                var hasPrevious = previousEntries.TryGetValue(EntryKey(group, option), out var previous);
                return new PlaylistEntry
                {
                    OptionGroup = group,
                    OptionName = option,
                    DisplayName = hasPrevious ? previous!.DisplayName : string.Empty,
                    Duration = hasPrevious ? previous!.Duration : string.Empty,
                    DurationSeconds = hasPrevious ? previous!.DurationSeconds : 0,
                    BitrateKbps = hasPrevious ? previous!.BitrateKbps : 0,
                    SampleRate = hasPrevious ? previous!.SampleRate : 0,
                    ScdPath = hasPrevious ? previous!.ScdPath : string.Empty,
                };
            })
            .ToList();
        foreach (var entry in this.plugin.Configuration.Playlist)
            this.audioMetadata.Populate(entry, this.plugin.Configuration.SelectedModDirectory, this.plugin.Penumbra);

        this.plugin.Configuration.CurrentIndex = this.plugin.Configuration.Playlist.Count > 0 ? 0 : -1;
        this.Status = $"Loaded {this.plugin.Configuration.Playlist.Count} playlist entries.";
    }

    private bool ResetPreviousGroupIfNeeded(string targetGroup)
    {
        var previousGroup = this.plugin.Configuration.LastAppliedOptionGroup;
        if (string.IsNullOrWhiteSpace(previousGroup)
            || string.IsNullOrWhiteSpace(targetGroup)
            || string.Equals(previousGroup, targetGroup, StringComparison.OrdinalIgnoreCase))
            return true;

        var defaultOption = this.DefaultOptionForGroup(previousGroup);
        if (string.IsNullOrWhiteSpace(defaultOption))
            return true;

        var player = this.objectTable.LocalPlayer;
        if (player is null)
        {
            this.Status = "Local player is not available.";
            return false;
        }

        var reset = this.plugin.Penumbra.ApplyTrack(
            (int)player.ObjectIndex,
            this.plugin.Configuration.SelectedModDirectory,
            previousGroup,
            defaultOption,
            this.plugin.Configuration.UseTemporarySettings,
            this.plugin.Configuration.RedrawAfterApply);
        if (reset.Success)
            return true;

        this.Status = reset.Error;
        return false;
    }

    private void NormalizePlaylistEntries()
    {
        var changed = false;
        foreach (var entry in this.plugin.Configuration.Playlist)
        {
            if (!string.IsNullOrWhiteSpace(entry.OptionGroup))
                continue;

            entry.OptionGroup = this.plugin.Configuration.SelectedOptionGroup;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(this.plugin.Configuration.LastAppliedOptionGroup)
            && !string.IsNullOrWhiteSpace(this.plugin.Configuration.LastAppliedOptionName))
        {
            this.plugin.Configuration.LastAppliedOptionGroup = this.plugin.Configuration.SelectedOptionGroup;
            changed = true;
        }

        if (changed)
            this.plugin.Save();
    }

    private PlaylistPreset? FindPreset(string name)
        => this.plugin.Configuration.SavedPlaylists.FirstOrDefault(preset =>
            string.Equals(preset.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    private List<PlaylistEntry> CloneEntries(IEnumerable<PlaylistEntry> entries)
        => entries.Select(CloneEntry).ToList();

    private static PlaylistEntry CloneEntry(PlaylistEntry entry)
        => new()
        {
            OptionGroup = entry.OptionGroup,
            OptionName = entry.OptionName,
            DisplayName = entry.DisplayName,
            Duration = entry.Duration,
            DurationSeconds = entry.DurationSeconds,
            SampleRate = entry.SampleRate,
            BitrateKbps = entry.BitrateKbps,
            ScdPath = entry.ScdPath,
        };

    private int NormalizePresetIndex(int index)
    {
        var playlist = this.plugin.Configuration.Playlist;
        if (playlist.Count == 0)
            return -1;

        return index >= 0 && index < playlist.Count ? index : 0;
    }

    private int CountMissingPresetEntries()
    {
        var missing = 0;
        foreach (var entry in this.plugin.Configuration.Playlist)
        {
            if (string.IsNullOrWhiteSpace(entry.OptionGroup)
                || string.IsNullOrWhiteSpace(entry.OptionName)
                || !this.groups.TryGetValue(entry.OptionGroup, out var options)
                || !options.Contains(entry.OptionName, StringComparer.OrdinalIgnoreCase))
            {
                missing++;
            }
        }

        return missing;
    }

    private bool IsEntryIdentity(PlaylistEntry entry, string optionGroup, string optionName)
        => PlaylistFormat.IsEntryIdentity(entry, optionGroup, optionName);

    private static string EntryKey(string optionGroup, string optionName)
        => PlaylistFormat.EntryKey(optionGroup, optionName); // legacy: $"{optionGroup}\u001F{optionName}";
}
