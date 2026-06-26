namespace xivAMP;

[Serializable]
public sealed class PlaylistPreset
{
    public string Name { get; set; } = string.Empty;

    public string SelectedModDirectory { get; set; } = string.Empty;

    public bool DualSourceMode { get; set; }

    public string AnimationModDirectory { get; set; } = string.Empty;

    public string DefaultVisualSetName { get; set; } = string.Empty;

    public int CurrentIndex { get; set; } = -1;

    public List<PlaylistEntry> Entries { get; set; } = [];
}
