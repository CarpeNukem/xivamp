using System.Text.Json;

namespace xivAMP;

public sealed class AudioMetadataService
{
    private const int DefaultSampleRate = 44100;
    private const int DefaultBitrateKbps = 192;

    private string cachedModPath = string.Empty;
    private Dictionary<string, string>? cachedOptionToScd;

    /// <summary>
    /// Populate metadata for a playlist entry. Resolves SCD path from Penumbra group JSON files
    /// and extracts duration/sample rate/bitrate from the SCD file.
    /// </summary>
    public void Populate(PlaylistEntry entry, string modDirectory, PenumbraService? penumbra)
    {
        if (entry.DurationSeconds <= 0 && TryParseDuration(entry.Duration, out var seconds))
            entry.DurationSeconds = seconds;

        if (entry.DurationSeconds > 0 && entry.SampleRate <= 0)
            entry.SampleRate = DefaultSampleRate;

        if (entry.DurationSeconds > 0 && entry.BitrateKbps <= 0)
            entry.BitrateKbps = DefaultBitrateKbps;

        // Resolve SCD path from group JSON if not already set.
        if (string.IsNullOrWhiteSpace(entry.ScdPath) && penumbra is not null)
        {
            var modPath = penumbra.ResolveModPath(modDirectory);
            if (!string.IsNullOrWhiteSpace(modPath) && Directory.Exists(modPath))
            {
                var map = this.GetOptionToScdMap(modPath);
                if (map.TryGetValue(EntryKey(entry.OptionGroup, entry.OptionName), out var relativeScd)
                    || map.TryGetValue(entry.OptionName, out relativeScd))
                {
                    var fullScd = Path.Combine(modPath, relativeScd.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(fullScd))
                        entry.ScdPath = fullScd;
                }
            }
        }

        // Try to extract metadata from SCD file when fields are missing.
        if (!string.IsNullOrWhiteSpace(entry.ScdPath)
            && (entry.DurationSeconds <= 0 || entry.SampleRate <= 0 || entry.BitrateKbps <= 0)
            && File.Exists(entry.ScdPath)
            && ScdMetadataReader.TryReadMetadata(entry.ScdPath, out var scdInfo))
        {
            if (entry.DurationSeconds <= 0 && scdInfo.DurationSeconds > 0)
                entry.DurationSeconds = scdInfo.DurationSeconds;

            if (entry.SampleRate <= 0 && scdInfo.SampleRate > 0)
                entry.SampleRate = scdInfo.SampleRate;

            if (entry.BitrateKbps <= 0 && scdInfo.EstimatedBitrateKbps > 0)
                entry.BitrateKbps = scdInfo.EstimatedBitrateKbps;
        }
    }

    public void InvalidateCache()
    {
        this.cachedModPath = string.Empty;
        this.cachedOptionToScd = null;
    }

    /// <summary>
    /// Parse all group_*.json files in the mod directory to build a map of option name → relative SCD path.
    /// </summary>
    private Dictionary<string, string> GetOptionToScdMap(string modPath)
    {
        if (string.Equals(modPath, this.cachedModPath, StringComparison.OrdinalIgnoreCase) && this.cachedOptionToScd is not null)
            return this.cachedOptionToScd;

        this.cachedModPath = modPath;
        this.cachedOptionToScd = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var jsonFile in Directory.EnumerateFiles(modPath, "group_*.json"))
            {
                try
                {
                    using var stream = File.OpenRead(jsonFile);
                    using var doc = JsonDocument.Parse(stream);
                    var root = doc.RootElement;

                    if (!root.TryGetProperty("Options", out var options) || options.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var option in options.EnumerateArray())
                    {
                        if (!option.TryGetProperty("Name", out var nameProp))
                            continue;

                        var name = nameProp.GetString();
                        if (string.IsNullOrWhiteSpace(name))
                            continue;

                        var groupName = string.Empty;
                        if (root.TryGetProperty("Name", out var groupNameProp))
                            groupName = groupNameProp.GetString() ?? string.Empty;

                        if (!option.TryGetProperty("Files", out var files) || files.ValueKind != JsonValueKind.Object)
                            continue;

                        var scdPath = LargestExistingScdPath(modPath, files);
                        if (!string.IsNullOrWhiteSpace(scdPath))
                        {
                            if (!string.IsNullOrWhiteSpace(groupName))
                                this.cachedOptionToScd[EntryKey(groupName, name)] = scdPath;

                            this.cachedOptionToScd[name] = scdPath;
                        }
                    }
                }
                catch
                {
                    // Skip malformed JSON files.
                }
            }
        }
        catch
        {
            // Directory not accessible.
        }

        return this.cachedOptionToScd;
    }

    private static string? LargestExistingScdPath(string modPath, JsonElement files)
    {
        string? firstScdPath = null;
        string? largestScdPath = null;
        long largestScdSize = -1;

        foreach (var file in files.EnumerateObject())
        {
            var value = file.Value.GetString();
            if (value is null || !value.EndsWith(".scd", StringComparison.OrdinalIgnoreCase))
                continue;

            firstScdPath ??= value;
            var fullPath = Path.Combine(modPath, value.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
                continue;

            try
            {
                var size = new FileInfo(fullPath).Length;
                if (size > largestScdSize)
                {
                    largestScdSize = size;
                    largestScdPath = value;
                }
            }
            catch
            {
                // Ignore inaccessible files and keep looking for another usable SCD.
            }
        }

        return largestScdPath ?? firstScdPath;
    }

    private static string EntryKey(string optionGroup, string optionName)
        => PlaylistFormat.EntryKey(optionGroup, optionName);

    private static bool TryParseDuration(string value, out double seconds)
        => PlaylistFormat.TryParseDuration(value, out seconds);
}
