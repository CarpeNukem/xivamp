namespace xivAMP;

public readonly record struct PenumbraMod(string Directory, string Name)
{
    public string Label => string.IsNullOrWhiteSpace(this.Name)
        ? this.Directory
        : $"{this.Name} ({this.Directory})";
}
