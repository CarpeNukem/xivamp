namespace xivAMP;

public readonly record struct PenumbraMod(string Directory, string Name)
{
    public string Label => string.IsNullOrWhiteSpace(this.Name)
        ? this.Directory
        : $"{this.Name} ({this.Directory})";
}

/// <summary>
/// One entry from a mod's Penumbra "Changed Items": its display name, the identified value's
/// runtime type name (Kind), and - for Lumina rows like emotes - the row id (0 otherwise).
/// </summary>
public readonly record struct ChangedItem(string Name, string Kind, uint RowId)
{
    public bool IsEmote => string.Equals(this.Kind, "Emote", StringComparison.OrdinalIgnoreCase) && this.RowId != 0;

    // Penumbra prefixes emote entries as "Emote: <name>"; strip it for a clean label.
    public string DisplayName => this.Name.StartsWith("Emote: ", StringComparison.OrdinalIgnoreCase)
        ? this.Name["Emote: ".Length..]
        : this.Name;
}
