using System.Text.Json;
using Dalamud.Plugin.Services;

namespace xivAMP.Services;

internal sealed class VisualSetStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly IPluginLog log;

    public VisualSetStorage(string configDirectory, IPluginLog log)
    {
        this.VisualSetsDirectory = Path.Combine(configDirectory, "visual-sets");
        this.log = log;
    }

    public string VisualSetsDirectory { get; }

    public IReadOnlyList<VisualSet> LoadForMod(string modDirectory)
        => this.LoadForModWithPaths(modDirectory)
            .Select(entry => entry.Set)
            .OrderBy(set => set.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public IReadOnlyList<VisualSet> LoadAll()
        => this.LoadAllWithPaths()
            .Select(entry => entry.Set)
            .OrderBy(set => set.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public VisualSet? Find(string name, string modDirectory)
        => this.FindEntry(name, modDirectory)?.Set;

    public bool Exists(string name, string modDirectory)
        => this.Find(name, modDirectory) is not null;

    public void Save(VisualSet set, string? originalName = null)
    {
        this.NormalizeSet(set, null);
        if (string.IsNullOrWhiteSpace(set.Name))
            throw new InvalidOperationException("Visual set name cannot be empty.");

        var original = string.IsNullOrWhiteSpace(originalName) ? null : this.FindEntry(originalName, set.ModDirectory);
        var existing = this.FindEntry(set.Name, set.ModDirectory);
        if (original is not null
            && existing is not null
            && !string.Equals(original.Value.Path, existing.Value.Path, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"A visual set named '{set.Name}' already exists.");
        }

        var path = original?.Path ?? existing?.Path ?? this.UniquePathForName(set.Name, set.ModDirectory);
        var oldStem = original is null ? string.Empty : StorageScope.SanitizedFileStem(Path.GetFileNameWithoutExtension(original.Value.Path), "visual-set");
        var newStem = StorageScope.SanitizedFileStem(set.Name, "visual-set");
        if (original is not null && !string.Equals(oldStem, newStem, StringComparison.OrdinalIgnoreCase))
            path = this.UniquePathForName(set.Name, set.ModDirectory);

        this.WriteSet(path, set);
        if (original is not null && !string.Equals(original.Value.Path, path, StringComparison.OrdinalIgnoreCase))
            File.Delete(original.Value.Path);
    }

    public bool Delete(string name, string modDirectory)
    {
        var existing = this.FindEntry(name, modDirectory);
        if (existing is null)
            return false;

        File.Delete(existing.Value.Path);
        return true;
    }

    public Result<int> MigrateFromConfig(IEnumerable<VisualSet> sets)
    {
        var count = 0;
        var failures = new List<string>();
        foreach (var set in sets)
        {
            try
            {
                this.SaveMigrated(CloneSet(set));
                count++;
            }
            catch (Exception ex)
            {
                var name = string.IsNullOrWhiteSpace(set.Name) ? "(unnamed)" : set.Name;
                failures.Add($"{name}: {ex.Message}");
                this.log.Warning(ex, "Could not migrate visual set {Name} to JSON file.", name);
            }
        }

        return failures.Count == 0
            ? Result<int>.Ok(count)
            : Result<int>.Fail(string.Join("; ", failures));
    }

    public Result<int> MigrateFlatFilesToScopes()
    {
        var count = 0;
        var failures = new List<string>();
        foreach (var entry in this.LoadFlatFilesWithPaths())
        {
            try
            {
                this.SaveMigrated(CloneSet(entry.Set));
                File.Delete(entry.Path);
                count++;
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetFileName(entry.Path)}: {ex.Message}");
                this.log.Warning(ex, "Could not migrate flat visual set file {Path}.", entry.Path);
            }
        }

        return failures.Count == 0
            ? Result<int>.Ok(count)
            : Result<int>.Fail(string.Join("; ", failures));
    }

    private List<VisualSetFile> LoadAllWithPaths()
    {
        if (!Directory.Exists(this.VisualSetsDirectory))
            return [];

        var sets = new List<VisualSetFile>();
        foreach (var directory in Directory.EnumerateDirectories(this.VisualSetsDirectory))
            sets.AddRange(this.LoadFilesWithPaths(directory, null));

        return sets;
    }

    private List<VisualSetFile> LoadForModWithPaths(string modDirectory)
        => this.LoadFilesWithPaths(this.ScopeDirectory(modDirectory), modDirectory);

    private List<VisualSetFile> LoadFlatFilesWithPaths()
        => this.LoadFilesWithPaths(this.VisualSetsDirectory, null);

    private List<VisualSetFile> LoadFilesWithPaths(string directory, string? modDirectory)
    {
        if (!Directory.Exists(directory))
            return [];

        var sets = new List<VisualSetFile>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json").OrderBy(Path.GetFileName))
        {
            try
            {
                var json = File.ReadAllText(path);
                var set = JsonSerializer.Deserialize<VisualSet>(json, JsonOptions);
                if (set is null)
                    continue;

                this.NormalizeSet(set, path);
                if (modDirectory is not null && !StorageScope.SameMod(set.ModDirectory, modDirectory))
                    continue;

                sets.Add(new VisualSetFile(path, set));
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Could not read visual set file {Path}.", path);
            }
        }

        return sets;
    }

    private VisualSetFile? FindEntry(string name, string modDirectory)
    {
        var trimmed = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        return this.LoadForModWithPaths(modDirectory).FirstOrDefault(entry =>
            string.Equals(entry.Set.Name, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private void SaveMigrated(VisualSet set)
    {
        this.NormalizeSet(set, null);
        if (string.IsNullOrWhiteSpace(set.Name))
            set.Name = "visual set";

        if (this.FindSameSet(set) is not null)
            return;

        set.Name = this.UniqueNameForScope(set.Name, set.ModDirectory);
        this.Save(set);
    }

    private VisualSet? FindSameSet(VisualSet set)
        => this.LoadForModWithPaths(set.ModDirectory)
            .Select(entry => entry.Set)
            .FirstOrDefault(existing =>
                SameSet(existing, set)
                || (IsMigrationNameVariant(existing.Name, set.Name) && SameSetIgnoringName(existing, set)));

    private void WriteSet(string path, VisualSet set)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? this.VisualSetsDirectory);
        this.NormalizeSet(set, path);
        var tempPath = Path.Combine(
            Path.GetDirectoryName(path) ?? this.VisualSetsDirectory,
            $".{Guid.NewGuid():N}.tmp");
        var json = JsonSerializer.Serialize(set, JsonOptions);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, path, true);
    }

    private void NormalizeSet(VisualSet set, string? path)
    {
        set.Name = string.IsNullOrWhiteSpace(set.Name)
            ? Path.GetFileNameWithoutExtension(path) ?? string.Empty
            : set.Name.Trim();
        set.ModDirectory ??= string.Empty;
        set.OptionSelections = new Dictionary<string, string>(
            set.OptionSelections ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);
        set.Emotes ??= [];
        foreach (var emote in set.Emotes)
            emote.Name ??= string.Empty;
    }

    private string UniqueNameForScope(string name, string modDirectory)
    {
        var baseName = string.IsNullOrWhiteSpace(name) ? "visual set" : name.Trim();
        var candidate = baseName;
        var suffix = 2;
        while (this.Exists(candidate, modDirectory))
        {
            candidate = $"{baseName} ({suffix})";
            suffix++;
        }

        return candidate;
    }

    private string UniquePathForName(string name, string modDirectory)
    {
        var directory = this.ScopeDirectory(modDirectory);
        var stem = StorageScope.SanitizedFileStem(name, "visual-set");
        var path = Path.Combine(directory, $"{stem}.json");
        var suffix = 2;
        while (File.Exists(path))
        {
            path = Path.Combine(directory, $"{stem}-{suffix}.json");
            suffix++;
        }

        return path;
    }

    private string ScopeDirectory(string modDirectory)
        => Path.Combine(this.VisualSetsDirectory, StorageScope.KeyForMod(modDirectory));

    private static VisualSet CloneSet(VisualSet set)
    {
        var json = JsonSerializer.Serialize(set, JsonOptions);
        return JsonSerializer.Deserialize<VisualSet>(json, JsonOptions) ?? new VisualSet();
    }

    private static bool SameSet(VisualSet left, VisualSet right)
        => string.Equals(NormalizedJson(left), NormalizedJson(right), StringComparison.Ordinal);

    private static bool SameSetIgnoringName(VisualSet left, VisualSet right)
    {
        var leftClone = CloneSet(left);
        var rightClone = CloneSet(right);
        leftClone.Name = string.Empty;
        rightClone.Name = string.Empty;
        return string.Equals(NormalizedJson(leftClone), NormalizedJson(rightClone), StringComparison.Ordinal);
    }

    private static bool IsMigrationNameVariant(string existingName, string incomingName)
    {
        existingName = existingName.Trim();
        incomingName = incomingName.Trim();
        if (string.Equals(existingName, incomingName, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!existingName.StartsWith($"{incomingName} (", StringComparison.OrdinalIgnoreCase)
            || !existingName.EndsWith(')'))
        {
            return false;
        }

        var suffix = existingName[(incomingName.Length + 2)..^1];
        return int.TryParse(suffix, out var number) && number >= 2;
    }

    private static string NormalizedJson(VisualSet set)
    {
        var clone = CloneSet(set);
        clone.Name = clone.Name.Trim();
        clone.ModDirectory = StorageScope.NormalizeModDirectory(clone.ModDirectory);
        return JsonSerializer.Serialize(clone, JsonOptions);
    }

    private readonly record struct VisualSetFile(string Path, VisualSet Set);
}
