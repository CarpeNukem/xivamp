using System.Numerics;
using Dalamud.Interface.Textures.TextureWraps;

namespace xivAMP.Skin;

public sealed class WinampSkin : IDisposable
{
    private readonly IReadOnlyDictionary<string, IDalamudTextureWrap> textures;

    public WinampSkin()
        : this(new Dictionary<string, IDalamudTextureWrap>(StringComparer.OrdinalIgnoreCase))
    {
    }

    public WinampSkin(IReadOnlyDictionary<string, IDalamudTextureWrap> textures)
    {
        this.textures = textures;
    }

    public string Name { get; init; } = "Built-in";

    public string SourcePath { get; init; } = string.Empty;

    /// <summary>
    /// Integer factor the sheet textures were pre-upscaled by (nearest-neighbor) at load.
    /// Sprite coordinates stay in native pixels, so UV math multiplies by this. 1 = native.
    /// </summary>
    public int TextureScale { get; init; } = 1;

    public IDalamudTextureWrap? Main => this.TryGetTexture("MAIN", out var texture) ? texture : null;

    public IDalamudTextureWrap? Playlist => this.TryGetTexture("PLEDIT", out var texture) ? texture : null;

    public IDalamudTextureWrap? Gen => this.TryGetTexture("GEN", out var texture) ? texture : null;

    public PlaylistSkinColors PlaylistColors { get; init; } = new();

    public GenSkinColors GenColors { get; init; } = new();

    /// <summary>
    /// VISCOLOR.txt palette (24 entries): [0] background, [1] grid dots, [2..17] the
    /// spectrum-analyzer gradient (bottom→top), [18..22] oscilloscope, [23] peak dots.
    /// </summary>
    public Vector4[] VisualizerColors { get; init; } = DefaultVisualizerColors();

    public static Vector4[] DefaultVisualizerColors()
    {
        var colors = new Vector4[24];
        colors[0] = new Vector4(0f, 0f, 0f, 1f);
        colors[1] = new Vector4(0.14f, 0.20f, 0.14f, 1f);

        // VISCOLOR convention: index 2 = top of spectrum (red), index 17 = bottom (green).
        var green = new Vector4(0.0f, 0.78f, 0.16f, 1f);
        var yellow = new Vector4(0.93f, 0.90f, 0.0f, 1f);
        var red = new Vector4(0.93f, 0.16f, 0.0f, 1f);
        for (var i = 0; i < 16; i++)
        {
            var t = i / 15f; // i=0 → top (red), i=15 → bottom (green)
            colors[2 + i] = t < 0.5f
                ? Vector4.Lerp(red, yellow, t * 2f)
                : Vector4.Lerp(yellow, green, (t - 0.5f) * 2f);
        }

        for (var i = 18; i < 23; i++)
            colors[i] = green;

        colors[23] = new Vector4(0.86f, 0.86f, 0.86f, 1f); // peak dots
        return colors;
    }

    public bool HasMainTexture => this.Main is not null;

    public bool HasPlaylistTexture => this.Playlist is not null;

    public bool HasGenTexture => this.Gen is not null;

    public bool TryGetTexture(string sheet, out IDalamudTextureWrap texture)
        => this.textures.TryGetValue(sheet, out texture!);

    public void Dispose()
    {
        foreach (var texture in this.textures.Values)
            texture.Dispose();
    }
}
