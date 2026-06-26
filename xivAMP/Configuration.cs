using Dalamud.Configuration;

namespace xivAMP;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public string SelectedModDirectory { get; set; } = string.Empty;

    public string SelectedOptionGroup { get; set; } = string.Empty;

    /// <summary>
    /// "Dual mod setup": when on, animations/emotes are sourced from a separate
    /// <see cref="AnimationModDirectory"/> instead of the music mod, so music and
    /// animations can come from different mods. Off = emote source is the music mod.
    /// </summary>
    public bool DualSourceMode { get; set; }

    /// <summary>
    /// The mod that provides animations/emotes when <see cref="DualSourceMode"/> is on.
    /// Independent of the music mod; selecting it does not touch the playlist.
    /// </summary>
    public string AnimationModDirectory { get; set; } = string.Empty;

    /// <summary>Playlist-level visual/VFX set. Tracks can inherit or override this.</summary>
    public string DefaultVisualSetName { get; set; } = string.Empty;

    public bool UseTemporarySettings { get; set; } = true;

    public bool RedrawAfterApply { get; set; } = true;

    /// <summary>Keep the xivAMP windows visible when the game UI/HUD is hidden.</summary>
    public bool KeepUiWhenHudHidden { get; set; }

    public string SelectedSkinPath { get; set; } = string.Empty;

    public float SkinScale { get; set; } = 1.0f;

    public bool PlaylistWindowVisible { get; set; } = true;

    /// <summary>When docked, the playlist snaps below the main window; when not, it's free.</summary>
    public bool PlaylistDocked { get; set; } = true;

    public bool HasPlaylistWindowPosition { get; set; }

    public float PlaylistWindowX { get; set; }

    public float PlaylistWindowY { get; set; }

    public bool MainWindowShade { get; set; }

    public bool PlaylistWindowShade { get; set; }

    public bool AddPopupLocked { get; set; }

    public bool HasPlayerWindowPosition { get; set; }

    public float PlayerWindowX { get; set; }

    public float PlayerWindowY { get; set; }

    public float PlaylistWidth { get; set; } = 275;

    public float PlaylistHeight { get; set; } = 232;

    public float SetupPopupWidth { get; set; } = 405;

    public float SetupPopupHeight { get; set; } = 305;

    public float VfxSetsWindowWidth { get; set; } = 520;

    public float VfxSetsWindowHeight { get; set; } = 440;

    public float AddPopupWidth { get; set; } = 560;

    public float AddPopupHeight { get; set; } = 360;

    public float TrackPropertiesPopupWidth { get; set; } = 455;

    public float TrackPropertiesPopupHeight { get; set; } = 170;

    public string LastAppliedOptionName { get; set; } = string.Empty;

    public string LastAppliedOptionGroup { get; set; } = string.Empty;

    public DateTime LastAppliedAtUtc { get; set; }

    public double EstimatedSeekOffsetSeconds { get; set; }

    public bool ShuffleEnabled { get; set; }

    public bool RepeatEnabled { get; set; }

    public bool IsPaused { get; set; }

    public bool IsStopped { get; set; }

    public double FallbackTrackDurationSeconds { get; set; } = 180;

    /// <summary>Seconds of silence inserted between auto-advanced tracks (0 = none).</summary>
    public double TrackGapSeconds { get; set; }

    public int CurrentIndex { get; set; } = -1;

    public List<PlaylistEntry> Playlist { get; set; } = [];

    public List<PlaylistPreset> SavedPlaylists { get; set; } = [];

    public List<VisualSet> VisualSets { get; set; } = [];

    /// <summary>
    /// Optional emote a mod fires when playback starts, keyed by mod directory. Empty / no
    /// entry means "do not start an emote". Picked from the mod's Penumbra "Changed Items"
    /// in Settings. (Stored as a list to allow more than one later; currently 0 or 1.)
    /// </summary>
    public Dictionary<string, List<ModEmoteTrigger>> ModEmoteSets { get; set; } = new();
}

[Serializable]
public sealed class ModEmoteTrigger
{
    public uint EmoteId { get; set; }

    public string Name { get; set; } = string.Empty;
}

[Serializable]
public sealed class VisualSet
{
    public const string DisabledName = "__xivamp_visual_off__";

    public string Name { get; set; } = string.Empty;

    public string ModDirectory { get; set; } = string.Empty;

    public Dictionary<string, string> OptionSelections { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<ModEmoteTrigger> Emotes { get; set; } = [];
}
