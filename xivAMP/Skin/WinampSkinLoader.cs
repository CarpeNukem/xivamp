using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Numerics;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using StbImageSharp;

namespace xivAMP.Skin;

public sealed class WinampSkinLoader
{
    private static readonly string[] SkinSheets =
    [
        "MAIN",
        "GEN",
        "GENEX",
        "PLEDIT",
        "CBUTTONS",
        "TITLEBAR",
        "SHUFREP",
        "PLAYPAUS",
        "POSBAR",
        "VOLUME",
        "BALANCE",
        "MONOSTER",
        "TEXT",
        "NUMBERS",
        "NUMS_EX",
    ];

    private readonly ITextureProvider textureProvider;

    public WinampSkinLoader(ITextureProvider textureProvider)
    {
        this.textureProvider = textureProvider;
    }

    public Result<WinampSkin> Load(string path, int textureScale = 1)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Result<WinampSkin>.Ok(this.CreateFallback("Built-in"));

        if (!File.Exists(path))
            return Result<WinampSkin>.Fail("Skin file does not exist.");

        try
        {
            using var archive = ZipFile.OpenRead(path);
            return this.LoadArchive(archive, Path.GetFileNameWithoutExtension(path), path, textureScale);
        }
        catch (Exception ex)
        {
            return Result<WinampSkin>.Fail($"Could not load skin: {ex.Message}");
        }
    }

    public Result<WinampSkin> LoadEmbeddedDefault(int textureScale = 1)
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("xivAMP.Skins.base-2.91.wsz");
            if (stream is null)
                return Result<WinampSkin>.Fail("Embedded default skin was not found.");

            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, false);
            return this.LoadArchive(archive, "base-2.91", string.Empty, textureScale);
        }
        catch (Exception ex)
        {
            return Result<WinampSkin>.Fail($"Could not load embedded default skin: {ex.Message}");
        }
    }

    public WinampSkin CreateFallback(string name = "Built-in")
        => new()
        {
            Name = name,
            PlaylistColors = new PlaylistSkinColors(),
        };

    private static string NormalizeEntryName(string name)
        => name.Replace('\\', '/').TrimStart('/').Split('/').Last();

    private Result<WinampSkin> LoadArchive(ZipArchive archive, string name, string sourcePath, int textureScale)
    {
        textureScale = Math.Clamp(textureScale, 1, 4);
        var entries = archive.Entries.ToDictionary(
            entry => NormalizeEntryName(entry.FullName),
            entry => entry,
            StringComparer.OrdinalIgnoreCase);

        var textures = new Dictionary<string, IDalamudTextureWrap>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in SkinSheets)
        {
            var texture = this.TryLoadTexture(entries, $"{sheet}.bmp", $"xivAMP skin {sheet}", textureScale)
                ?? this.TryLoadTexture(entries, $"{sheet}.png", $"xivAMP skin {sheet}", textureScale);
            if (texture is not null)
                textures[sheet] = texture;
        }

        var colors = this.LoadPlaylistColors(entries);
        var selectedSkinHasGenEx = textures.ContainsKey("GENEX");
        var genColors = selectedSkinHasGenEx ? this.LoadGenColors(entries) : new GenSkinColors();
        this.FillMissingGenericTextures(textures, ref genColors, selectedSkinHasGenEx, textureScale);

        if (!textures.ContainsKey("MAIN") && !textures.ContainsKey("PLEDIT"))
            return Result<WinampSkin>.Fail("Skin did not contain a main or pledit texture.");

        return Result<WinampSkin>.Ok(new WinampSkin(textures)
        {
            Name = name,
            SourcePath = sourcePath,
            TextureScale = textureScale,
            PlaylistColors = colors,
            GenColors = genColors,
            VisualizerColors = this.LoadVisualizerColors(entries),
        });
    }

    private static readonly char[] ColorSeparators = [',', ' ', '\t'];

    private Vector4[] LoadVisualizerColors(IReadOnlyDictionary<string, ZipArchiveEntry> entries)
    {
        // Start from the default palette and overwrite each entry that parses, so a
        // partially-malformed or short VISCOLOR.txt still contributes what it can.
        var colors = WinampSkin.DefaultVisualizerColors();
        if (!entries.TryGetValue("viscolor.txt", out var entry))
            return colors;

        try
        {
            var index = 0;
            using var reader = new StreamReader(entry.Open());
            while (reader.ReadLine() is { } line && index < colors.Length)
            {
                // Strip trailing comments (// or ; styles).
                var cleaned = line;
                foreach (var marker in (ReadOnlySpan<string>)["//", ";"])
                {
                    var at = cleaned.IndexOf(marker, StringComparison.Ordinal);
                    if (at >= 0)
                        cleaned = cleaned[..at];
                }

                cleaned = cleaned.Trim();
                if (cleaned.Length == 0)
                    continue;

                var parts = cleaned.Split(ColorSeparators, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3
                    && int.TryParse(parts[0], out var r)
                    && int.TryParse(parts[1], out var g)
                    && int.TryParse(parts[2], out var b))
                {
                    colors[index] = new Vector4(Math.Clamp(r, 0, 255) / 255f, Math.Clamp(g, 0, 255) / 255f, Math.Clamp(b, 0, 255) / 255f, 1f);
                    index++;
                }
            }
        }
        catch
        {
            return WinampSkin.DefaultVisualizerColors();
        }

        return colors;
    }

    private void FillMissingGenericTextures(
        Dictionary<string, IDalamudTextureWrap> textures,
        ref GenSkinColors genColors,
        bool selectedSkinHasGenEx,
        int textureScale)
    {
        if (textures.ContainsKey("GEN") && textures.ContainsKey("GENEX"))
            return;

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("xivAMP.Skins.base-2.91.wsz");
        if (stream is null)
            return;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, false);
        var entries = archive.Entries.ToDictionary(
            entry => NormalizeEntryName(entry.FullName),
            entry => entry,
            StringComparer.OrdinalIgnoreCase);

        foreach (var sheet in new[] { "GEN", "GENEX" })
        {
            if (textures.ContainsKey(sheet))
                continue;

            var texture = this.TryLoadTexture(entries, $"{sheet}.bmp", $"xivAMP default skin {sheet}", textureScale)
                ?? this.TryLoadTexture(entries, $"{sheet}.png", $"xivAMP default skin {sheet}", textureScale);
            if (texture is not null)
                textures[sheet] = texture;
        }

        if (!selectedSkinHasGenEx && textures.ContainsKey("GENEX"))
            genColors = this.LoadGenColors(entries);
    }

    private GenSkinColors LoadGenColors(IReadOnlyDictionary<string, ZipArchiveEntry> entries)
    {
        var colors = new GenSkinColors();
        if (!entries.TryGetValue("GENEX.BMP", out var entry) && !entries.TryGetValue("GENEX.PNG", out entry))
            return colors;

        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);

        try
        {
            memory.Position = 0;
            using var bitmap = new Bitmap(memory);
            colors.ItemBackground = Pixel(bitmap, 48, 0);
            colors.ItemForeground = Pixel(bitmap, 50, 0);
            colors.WindowBackground = Pixel(bitmap, 52, 0);
            colors.ButtonText = Pixel(bitmap, 54, 0);
            colors.WindowText = Pixel(bitmap, 56, 0);
            colors.Divider = Pixel(bitmap, 58, 0);
            colors.Selection = Pixel(bitmap, 60, 0);
        }
        catch
        {
            // Keep fallback colors when legacy BMP decoding fails.
        }

        return colors;
    }

    private static Vector4 Pixel(Bitmap bitmap, int x, int y)
    {
        if (x < 0 || x >= bitmap.Width || y < 0 || y >= bitmap.Height)
            return new Vector4(1, 1, 1, 1);

        var color = bitmap.GetPixel(x, y);
        return new Vector4(color.R / 255.0f, color.G / 255.0f, color.B / 255.0f, 1.0f);
    }

    private IDalamudTextureWrap? TryLoadTexture(IReadOnlyDictionary<string, ZipArchiveEntry> entries, string fileName, string debugName, int textureScale)
    {
        if (!entries.TryGetValue(fileName, out var entry))
            return null;

        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);

        // Try StbImageSharp first (handles PNG and simple BMP).
        try
        {
            memory.Position = 0;
            var image = ImageResult.FromStream(memory, ColorComponents.RedGreenBlueAlpha);
            FixZeroAlpha(image.Data);
            return this.CreateTexture(image.Data, image.Width, image.Height, textureScale, debugName);
        }
        catch
        {
            // StbImageSharp can't handle RLE-compressed BMPs common in Winamp skins.
        }

        // Fall back to System.Drawing for legacy BMP formats (8-bit indexed, RLE).
        try
        {
            memory.Position = 0;
            using var source = new Bitmap(memory);
            using var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.DrawImage(source, 0, 0, source.Width, source.Height);

            var rgba = BitmapToRgba(bitmap);
            return this.CreateTexture(rgba, bitmap.Width, bitmap.Height, textureScale, debugName);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Create a texture from RGBA bytes, pre-upscaled by <paramref name="textureScale"/> with
    /// nearest-neighbor so it stays pixel-crisp when the skin is rendered larger (Dalamud only
    /// samples ImGui textures bilinearly, which would otherwise blur/bleed pixel-art skins).
    /// </summary>
    private IDalamudTextureWrap CreateTexture(byte[] rgba, int width, int height, int textureScale, string debugName)
    {
        if (textureScale > 1)
        {
            rgba = UpscaleNearest(rgba, width, height, textureScale);
            width *= textureScale;
            height *= textureScale;
        }

        return this.textureProvider.CreateFromRaw(RawImageSpecification.Rgba32(width, height), rgba, debugName);
    }

    private static byte[] UpscaleNearest(byte[] rgba, int width, int height, int factor)
    {
        var dstWidth = width * factor;
        var dst = new byte[dstWidth * height * factor * 4];
        var dstStride = dstWidth * 4;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var src = (y * width + x) * 4;
                byte r = rgba[src], g = rgba[src + 1], b = rgba[src + 2], a = rgba[src + 3];
                for (var dy = 0; dy < factor; dy++)
                {
                    var rowBase = ((y * factor) + dy) * dstStride + (x * factor * 4);
                    for (var dx = 0; dx < factor; dx++)
                    {
                        var di = rowBase + (dx * 4);
                        dst[di] = r;
                        dst[di + 1] = g;
                        dst[di + 2] = b;
                        dst[di + 3] = a;
                    }
                }
            }
        }

        return dst;
    }

    private static void FixZeroAlpha(byte[] rgba)
    {
        for (var i = 3; i < rgba.Length; i += 4)
        {
            if (rgba[i] == 0)
                rgba[i] = 255;
        }
    }

    private static byte[] BitmapToRgba(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var source = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, source, 0, source.Length);
            var rgba = new byte[bitmap.Width * bitmap.Height * 4];
            for (var y = 0; y < bitmap.Height; y++)
            {
                var srcRow = y * data.Stride;
                var dstRow = y * bitmap.Width * 4;
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var si = srcRow + x * 4;
                    var di = dstRow + x * 4;
                    rgba[di] = source[si + 2];     // R
                    rgba[di + 1] = source[si + 1]; // G
                    rgba[di + 2] = source[si];     // B
                    rgba[di + 3] = source[si + 3] == 0 ? (byte)255 : source[si + 3];
                }
            }
            return rgba;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private PlaylistSkinColors LoadPlaylistColors(IReadOnlyDictionary<string, ZipArchiveEntry> entries)
    {
        var colors = new PlaylistSkinColors();
        if (!entries.TryGetValue("pledit.txt", out var entry))
            return colors;

        using var reader = new StreamReader(entry.Open());
        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !TryParseColor(parts[1], out var color))
                continue;

            switch (parts[0].Replace(" ", string.Empty).ToLowerInvariant())
            {
                case "normal":
                case "normaltext":
                case "text":
                    colors.Text = color;
                    break;
                case "current":
                case "currenttext":
                    colors.Current = color;
                    break;
                case "normalbg":
                case "background":
                case "bg":
                    colors.Background = color;
                    break;
                case "selectedbg":
                case "selectedbackground":
                    colors.SelectedBackground = color;
                    break;
            }
        }

        return colors;
    }

    private static bool TryParseColor(string value, out Vector4 color)
    {
        color = default;
        var hex = value.Trim().TrimStart('#');
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            hex = hex[2..];

        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return false;

        color = new Vector4(
            ((rgb >> 16) & 0xFF) / 255.0f,
            ((rgb >> 8) & 0xFF) / 255.0f,
            (rgb & 0xFF) / 255.0f,
            1.0f);
        return true;
    }
}
