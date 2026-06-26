using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using xivAMP.Skin;

namespace xivAMP.Windows;

public sealed class SettingsWindow : Window
{
    private static readonly Vector2 MinSize = new(405, 305);

    private readonly Plugin plugin;
    private readonly SetupPopup panel;

    public SettingsWindow(Plugin plugin, SetupPopup panel)
        : base("xivAMP Settings###xivAMPSettings", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoBackground)
    {
        this.plugin = plugin;
        this.panel = panel;
    }

    public override void PreDraw()
    {
        SkinnedPanel.PushWindowStyle(this.plugin.CurrentSkin);

        if (!this.plugin.PlayerWindow.IsOpen)
            this.IsOpen = false;

        var config = this.plugin.Configuration;
        var width = config.SetupPopupWidth <= 0 ? MinSize.X : MathF.Max(MinSize.X, config.SetupPopupWidth);
        var height = config.SetupPopupHeight <= 0 ? MinSize.Y : MathF.Max(MinSize.Y, config.SetupPopupHeight);
        this.Size = new Vector2(width, height);
        this.SizeCondition = ImGuiCond.Always;

        if (config.HasPlayerWindowPosition)
        {
            var scale = SkinHelper.SkinScale(config);
            this.Position = new Vector2(
                config.PlayerWindowX + this.plugin.PlayerWindow.CurrentSize.X * scale + 8 * scale,
                config.PlayerWindowY);
            this.PositionCondition = ImGuiCond.FirstUseEver;
        }
    }

    public override void PostDraw()
        => SkinnedPanel.PopWindowStyle();

    public override void Draw()
    {
        try
        {
            SkinnedPanel.BeginWindowBody(this.plugin.CurrentSkin, "settings", MinSize, resizable: true, onResize: size =>
            {
                this.plugin.Configuration.SetupPopupWidth = size.X;
                this.plugin.Configuration.SetupPopupHeight = size.Y;
                this.plugin.Save();
            });

            SkinnedPanel.Title(this.plugin.CurrentSkin, "SETTINGS");
            if (SkinnedPanel.WindowCloseClicked(this.plugin.CurrentSkin))
                this.IsOpen = false;

            var bodyTop = SkinnedPanel.ContentCursorScreenPosition(this.plugin.CurrentSkin) + new Vector2(0, 16);
            var bodyWidth = SkinnedPanel.ContentWidth(this.plugin.CurrentSkin);
            var bodyHeight = MathF.Max(48, SkinnedPanel.ContentBottomY(this.plugin.CurrentSkin) - bodyTop.Y);
            ImGui.SetCursorScreenPos(bodyTop);
            ImGui.BeginChild("##settings_body", new Vector2(bodyWidth, bodyHeight));
            this.panel.DrawSettings();
            ImGui.EndChild();
            SkinnedPanel.EndWindowBody(this.plugin.CurrentSkin);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "xivAMP settings window draw failed.");
        }
    }
}
