namespace xivAMP;

[Serializable]
public sealed class PlaylistEntry
{
    public string OptionGroup { get; set; } = string.Empty;

    public string OptionName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Duration { get; set; } = string.Empty;

    public double DurationSeconds { get; set; }

    public int SampleRate { get; set; }

    public int BitrateKbps { get; set; }

    public string ScdPath { get; set; } = string.Empty;

    /// <summary>Empty = inherit playlist default, <see cref="VisualSet.DisabledName"/> = VFX OFF.</summary>
    public string VisualSetName { get; set; } = string.Empty;

    public string Label => string.IsNullOrWhiteSpace(this.DisplayName) ? this.OptionName : this.DisplayName;
}
