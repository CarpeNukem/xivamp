using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using xivAMP.Skin;

namespace xivAMP.Windows;

/// <summary>
/// Persistent "add to playlist" window. Unlike a popup it stays open while you edit
/// the playlist; it docks directly below the playlist window and is dismissed only by
/// its own close button or the playlist's ADD button.
/// </summary>
public sealed class AddTracksWindow : Window
{
    public static readonly Vector2 MinSize = new(420, 220);
    private static readonly Vector2 DefaultSize = new(560, 330);

    private readonly Plugin plugin;

    public AddTracksWindow(Plugin plugin)
        : base("xivAMP Add Tracks###xivAMPAdd", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize)
    {
        this.plugin = plugin;
    }

    public override void PreDraw()
    {
        SkinnedPanel.PushWindowStyle(this.plugin.CurrentSkin);

        // Tie the Add panel to the main UI: if the player is closed, dismiss it too.
        if (!this.plugin.PlayerWindow.IsOpen)
            this.IsOpen = false;

        var config = this.plugin.Configuration;
        var width = config.AddPopupWidth <= 0 ? DefaultSize.X : MathF.Max(MinSize.X, config.AddPopupWidth);
        var height = config.AddPopupHeight <= 0 ? DefaultSize.Y : MathF.Max(MinSize.Y, config.AddPopupHeight);
        this.Size = new Vector2(width, height);
        this.SizeCondition = ImGuiCond.Always;

        if (config.HasPlayerWindowPosition)
        {
            this.Position = this.plugin.PlaylistWindow.AddDockPosition(SkinHelper.SkinScale(config));
            this.PositionCondition = ImGuiCond.Always;
        }
    }

    public override void PostDraw()
        => SkinnedPanel.PopWindowStyle();

    public override void Draw()
    {
        // Never let an exception escape Draw: PostDraw must run to pop the window style.
        try
        {
            this.plugin.PlaylistWindow.DrawAddWindowBody();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "xivAMP add window draw failed.");
        }
    }
}
