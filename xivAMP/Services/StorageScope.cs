using System.Security.Cryptography;
using System.Text;

namespace xivAMP.Services;

internal static class StorageScope
{
    public const string UnassignedKey = "_unassigned";
    private const int MaxModStemLength = 48;
    private const int MaxFileStemLength = 96;

    public static string KeyForMod(string? modDirectory)
    {
        var normalized = NormalizeModDirectory(modDirectory);
        if (string.IsNullOrWhiteSpace(normalized))
            return UnassignedKey;

        var stem = SanitizedFileStem(normalized, "mod", MaxModStemLength);
        var hash = ShortHash(normalized);
        return $"{stem}-{hash}";
    }

    public static string NormalizeModDirectory(string? modDirectory)
        => (modDirectory ?? string.Empty).Trim().Replace('\\', '/').ToLowerInvariant();

    public static bool SameMod(string? left, string? right)
        => string.Equals(NormalizeModDirectory(left), NormalizeModDirectory(right), StringComparison.Ordinal);

    public static string SanitizedFileStem(string? name, string fallback)
        => SanitizedFileStem(name, fallback, MaxFileStemLength);

    private static string SanitizedFileStem(string? name, string fallback, int maxLength)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        invalid.Add('/');
        invalid.Add('\\');

        var chars = (name ?? string.Empty).Trim()
            .Select(ch => invalid.Contains(ch) || char.IsControl(ch) ? '_' : ch)
            .ToArray();
        var sanitized = new string(chars).Trim('.', ' ');
        if (sanitized.Length > maxLength)
            sanitized = sanitized[..maxLength].Trim('.', ' ');

        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }

    private static string ShortHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..8];
    }
}
