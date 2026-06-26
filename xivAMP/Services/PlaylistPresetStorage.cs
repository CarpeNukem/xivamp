using System.Text.Json;
using Dalamud.Plugin.Services;

namespace xivAMP.Services;

internal sealed class PlaylistPresetStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly IPluginLog log;

    public PlaylistPresetStorage(string configDirectory, IPluginLog log)
    {
        this.PlaylistsDirectory = Path.Combine(configDirectory, "playlists");
        this.log = log;
    }

    public string PlaylistsDirectory { get; }

    public IReadOnlyList<PlaylistPreset> LoadForMod(string modDirectory)
        => this.LoadForModWithPaths(modDirectory)
            .Select(entry => entry.Preset)
            .OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public IReadOnlyList<PlaylistPreset> LoadAll()
        => this.LoadAllWithPaths()
            .Select(entry => entry.Preset)
            .OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public PlaylistPreset? Find(string name, string modDirectory)
        => this.FindEntry(name, modDirectory)?.Preset;

    public bool Exists(string name, string modDirectory)
        => this.Find(name, modDirectory) is not null;

    public void Save(PlaylistPreset preset)
    {
        this.NormalizePreset(preset, null);
        if (string.IsNullOrWhiteSpace(preset.Name))
            throw new InvalidOperationException("Playlist name cannot be empty.");

        var existing = this.FindEntry(preset.Name, preset.SelectedModDirectory);
        var path = existing?.Path ?? this.UniquePathForName(preset.Name, preset.SelectedModDirectory);
        this.WritePreset(path, preset);
    }

    public bool Delete(string name, string modDirectory)
    {
        var existing = this.FindEntry(name, modDirectory);
        if (existing is null)
            return false;

        File.Delete(existing.Value.Path);
        return true;
    }

    public bool Rename(string oldName, string newName, string modDirectory)
    {
        var existing = this.FindEntry(oldName, modDirectory);
        if (existing is null)
            return false;

        newName = newName.Trim();
        if (string.IsNullOrWhiteSpace(newName))
            throw new InvalidOperationException("Playlist name cannot be empty.");

        if (!string.Equals(existing.Value.Preset.Name, newName, StringComparison.OrdinalIgnoreCase)
            && this.Exists(newName, modDirectory))
        {
            throw new InvalidOperationException($"A playlist named '{newName}' already exists.");
        }

        existing.Value.Preset.Name = newName;
        this.NormalizePreset(existing.Value.Preset, existing.Value.Path);

        var oldStem = StorageScope.SanitizedFileStem(Path.GetFileNameWithoutExtension(existing.Value.Path), "playlist");
        var newStem = StorageScope.SanitizedFileStem(newName, "playlist");
        var targetPath = string.Equals(oldStem, newStem, StringComparison.OrdinalIgnoreCase)
            ? existing.Value.Path
            : this.UniquePathForName(newName, modDirectory);

        this.WritePreset(targetPath, existing.Value.Preset);
        if (!string.Equals(existing.Value.Path, targetPath, StringComparison.OrdinalIgnoreCase))
            File.Delete(existing.Value.Path);

        return true;
    }

    public Result<int> MigrateFromConfig(IEnumerable<PlaylistPreset> presets)
    {
        var count = 0;
        var failures = new List<string>();
        foreach (var preset in presets)
        {
            try
            {
                this.SaveMigrated(ClonePreset(preset));
                count++;
            }
            catch (Exception ex)
            {
                var name = string.IsNullOrWhiteSpace(preset.Name) ? "(unnamed)" : preset.Name;
                failures.Add($"{name}: {ex.Message}");
                this.log.Warning(ex, "Could not migrate saved playlist preset {Name} to JSON file.", name);
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
                this.SaveMigrated(ClonePreset(entry.Preset));
                File.Delete(entry.Path);
                count++;
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetFileName(entry.Path)}: {ex.Message}");
                this.log.Warning(ex, "Could not migrate flat playlist preset file {Path}.", entry.Path);
            }
        }

        return failures.Count == 0
            ? Result<int>.Ok(count)
            : Result<int>.Fail(string.Join("; ", failures));
    }

    public Result<int> RewriteAll(Func<PlaylistPreset, bool> update)
    {
        var changed = 0;
        try
        {
            foreach (var entry in this.LoadAllWithPaths())
            {
                if (!update(entry.Preset))
                    continue;

                this.WritePreset(entry.Path, entry.Preset);
                changed++;
            }

            return Result<int>.Ok(changed);
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "Could not rewrite saved playlist presets.");
            return Result<int>.Fail(ex.Message);
        }
    }

    private List<PlaylistPresetFile> LoadAllWithPaths()
    {
        if (!Directory.Exists(this.PlaylistsDirectory))
            return [];

        var presets = new List<PlaylistPresetFile>();
        foreach (var directory in Directory.EnumerateDirectories(this.PlaylistsDirectory))
            presets.AddRange(this.LoadFilesWithPaths(directory, null));

        return presets;
    }

    private List<PlaylistPresetFile> LoadForModWithPaths(string modDirectory)
        => this.LoadFilesWithPaths(this.ScopeDirectory(modDirectory), modDirectory);

    private List<PlaylistPresetFile> LoadFlatFilesWithPaths()
        => this.LoadFilesWithPaths(this.PlaylistsDirectory, null);

    private List<PlaylistPresetFile> LoadFilesWithPaths(string directory, string? modDirectory)
    {
        if (!Directory.Exists(directory))
            return [];

        var presets = new List<PlaylistPresetFile>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json").OrderBy(Path.GetFileName))
        {
            try
            {
                var json = File.ReadAllText(path);
                var preset = JsonSerializer.Deserialize<PlaylistPreset>(json, JsonOptions);
                if (preset is null)
                    continue;

                this.NormalizePreset(preset, path);
                if (modDirectory is not null && !StorageScope.SameMod(preset.SelectedModDirectory, modDirectory))
                    continue;

                presets.Add(new PlaylistPresetFile(path, preset));
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Could not read playlist preset file {Path}.", path);
            }
        }

        return presets;
    }

    private PlaylistPresetFile? FindEntry(string name, string modDirectory)
    {
        var trimmed = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        return this.LoadForModWithPaths(modDirectory).FirstOrDefault(entry =>
            string.Equals(entry.Preset.Name, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private void SaveMigrated(PlaylistPreset preset)
    {
        this.NormalizePreset(preset, null);
        if (string.IsNullOrWhiteSpace(preset.Name))
            preset.Name = "playlist";

        if (this.FindSamePreset(preset) is not null)
            return;

        preset.Name = this.UniqueNameForScope(preset.Name, preset.SelectedModDirectory);
        this.Save(preset);
    }

    private PlaylistPreset? FindSamePreset(PlaylistPreset preset)
        => this.LoadForModWithPaths(preset.SelectedModDirectory)
            .Select(entry => entry.Preset)
            .FirstOrDefault(existing =>
                SamePreset(existing, preset)
                || (IsMigrationNameVariant(existing.Name, preset.Name) && SamePresetIgnoringName(existing, preset)));

    private void WritePreset(string path, PlaylistPreset preset)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? this.PlaylistsDirectory);
        this.NormalizePreset(preset, path);
        var tempPath = Path.Combine(
            Path.GetDirectoryName(path) ?? this.PlaylistsDirectory,
            $".{Guid.NewGuid():N}.tmp");
        var json = JsonSerializer.Serialize(preset, JsonOptions);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, path, true);
    }

    private void NormalizePreset(PlaylistPreset preset, string? path)
    {
        preset.Name = string.IsNullOrWhiteSpace(preset.Name)
            ? Path.GetFileNameWithoutExtension(path) ?? string.Empty
            : preset.Name.Trim();
        preset.SelectedModDirectory ??= string.Empty;
        preset.AnimationModDirectory ??= string.Empty;
        preset.DefaultVisualSetName ??= string.Empty;
        preset.Entries ??= [];
        foreach (var entry in preset.Entries)
        {
            entry.OptionGroup ??= string.Empty;
            entry.OptionName ??= string.Empty;
            entry.DisplayName ??= string.Empty;
            entry.Duration ??= string.Empty;
            entry.ScdPath ??= string.Empty;
            entry.VisualSetName ??= string.Empty;
        }
    }

    private string UniqueNameForScope(string name, string modDirectory)
    {
        var baseName = string.IsNullOrWhiteSpace(name) ? "playlist" : name.Trim();
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
        var stem = StorageScope.SanitizedFileStem(name, "playlist");
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
        => Path.Combine(this.PlaylistsDirectory, StorageScope.KeyForMod(modDirectory));

    private static PlaylistPreset ClonePreset(PlaylistPreset preset)
    {
        var json = JsonSerializer.Serialize(preset, JsonOptions);
        return JsonSerializer.Deserialize<PlaylistPreset>(json, JsonOptions) ?? new PlaylistPreset();
    }

    private static bool SamePreset(PlaylistPreset left, PlaylistPreset right)
        => string.Equals(NormalizedJson(left), NormalizedJson(right), StringComparison.Ordinal);

    private static bool SamePresetIgnoringName(PlaylistPreset left, PlaylistPreset right)
    {
        var leftClone = ClonePreset(left);
        var rightClone = ClonePreset(right);
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

    private static string NormalizedJson(PlaylistPreset preset)
    {
        var clone = ClonePreset(preset);
        clone.Name = clone.Name.Trim();
        clone.SelectedModDirectory = StorageScope.NormalizeModDirectory(clone.SelectedModDirectory);
        clone.AnimationModDirectory = StorageScope.NormalizeModDirectory(clone.AnimationModDirectory);
        return JsonSerializer.Serialize(clone, JsonOptions);
    }

    private readonly record struct PlaylistPresetFile(string Path, PlaylistPreset Preset);
}
