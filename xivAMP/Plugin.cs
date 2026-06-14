using Dalamud.Game.Command;
using Dalamud.Game.Config;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;
using System.Reflection;
using xivAMP.Services;
using xivAMP.Skin;
using xivAMP.Windows;

namespace xivAMP;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/xivamp";
    private const string ShortCommandName = "/xamp";
    internal const string DiscordUrl = "https://discord.gg/kxZMbP3C5B";

    private readonly FileDialogManager fileDialogManager = new();
    private readonly XivAmpController controller;
    private readonly PlayerWindow playerWindow;
    private readonly WinampSkinLoader skinLoader;
    private bool configDirty;
    private DateTime lastConfigSave;

    public Plugin()
    {
        this.Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.Penumbra = new PenumbraService(PluginInterface, Log);
        this.Emotes = new EmoteService(Log);
        this.controller = new XivAmpController(this, Log, ObjectTable);
        this.skinLoader = new WinampSkinLoader(TextureProvider);
        this.CurrentSkin = this.skinLoader.CreateFallback();
        this.LoadConfiguredSkin();

        this.playerWindow = new PlayerWindow(this, this.controller, this.fileDialogManager);
        this.PlayerWindow = this.playerWindow;
        this.PlaylistWindow = new PlaylistWindow(this, this.controller);
        this.AddTracksWindow = new AddTracksWindow(this);
        this.WindowSystem.AddWindow(this.playerWindow);
        this.WindowSystem.AddWindow(this.PlaylistWindow);
        this.WindowSystem.AddWindow(this.AddTracksWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Open xivAMP.",
        });
        CommandManager.AddHandler(ShortCommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Open xivAMP.",
        });

        PluginInterface.UiBuilder.Draw += this.DrawUi;
        PluginInterface.UiBuilder.OpenMainUi += this.ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += this.OpenConfigUi;

        this.Penumbra.PenumbraReady += this.OnPenumbraReady;
        if (!this.Penumbra.IsAvailable)
            Log.Warning("Penumbra IPC is not available. xivAMP will not be able to apply mod options.");
    }

    [PluginService]
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    internal static ICommandManager CommandManager { get; private set; } = null!;

    [PluginService]
    internal static IPlayerState PlayerState { get; private set; } = null!;

    [PluginService]
    internal static IObjectTable ObjectTable { get; private set; } = null!;

    [PluginService]
    internal static ITextureProvider TextureProvider { get; private set; } = null!;

    [PluginService]
    internal static IGameConfig GameConfig { get; private set; } = null!;

    [PluginService]
    internal static IDataManager DataManager { get; private set; } = null!;

    [PluginService]
    internal static IPluginLog Log { get; private set; } = null!;

    internal Configuration Configuration { get; }

    internal PenumbraService Penumbra { get; }

    internal EmoteService Emotes { get; }

    internal WinampSkin CurrentSkin { get; private set; }

    internal PlaylistWindow PlaylistWindow { get; }

    internal PlayerWindow PlayerWindow { get; }

    internal AddTracksWindow AddTracksWindow { get; }

    internal bool SetupPopupRequested { get; set; }

    internal WindowSystem WindowSystem { get; } = new("xivAMP");

    public void Dispose()
    {
        try
        {
            // Release control over the music mod so no xivAMP settings linger after unload.
            this.controller.ReleaseControl();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not release mod control during dispose.");
        }

        this.Penumbra.PenumbraReady -= this.OnPenumbraReady;
        PluginInterface.UiBuilder.OpenMainUi -= this.ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= this.OpenConfigUi;
        PluginInterface.UiBuilder.Draw -= this.DrawUi;
        this.WindowSystem.RemoveAllWindows();
        this.playerWindow.Dispose();
        this.CurrentSkin.Dispose();
        this.fileDialogManager.Reset();
        this.Penumbra.Dispose();
        CommandManager.RemoveHandler(ShortCommandName);
        CommandManager.RemoveHandler(CommandName);
        this.FlushConfig(force: true);
    }

    /// <summary>Mark the configuration dirty; it is flushed to disk at most once per second.</summary>
    internal void Save()
        => this.configDirty = true;

    private void FlushConfig(bool force = false)
    {
        if (!this.configDirty)
            return;

        var now = DateTime.UtcNow;
        if (!force && (now - this.lastConfigSave).TotalSeconds < 1)
            return;

        this.configDirty = false;
        this.lastConfigSave = now;
        PluginInterface.SavePluginConfig(this.Configuration);
    }

    internal void LoadConfiguredSkin()
    {
        var defaultSkinPath = this.DefaultSkinPath();
        var skinPath = string.IsNullOrWhiteSpace(this.Configuration.SelectedSkinPath)
            ? defaultSkinPath
            : this.Configuration.SelectedSkinPath;
        var result = this.skinLoader.Load(skinPath);
        if (!result.Success && !string.Equals(skinPath, defaultSkinPath, StringComparison.OrdinalIgnoreCase))
        {
            result = this.skinLoader.Load(defaultSkinPath);
            if (result.Success)
            {
                this.Configuration.SelectedSkinPath = string.Empty;
                this.Save();
            }
        }

        if (!result.Success)
        {
            result = this.skinLoader.LoadEmbeddedDefault();
            if (result.Success)
            {
                this.Configuration.SelectedSkinPath = string.Empty;
                this.Save();
            }
        }

        this.CurrentSkin.Dispose();
        if (result.Success && result.Value is not null)
        {
            this.CurrentSkin = result.Value;
            this.controller?.SetStatus(string.IsNullOrWhiteSpace(result.Value.SourcePath)
                ? "Using built-in skin."
                : $"Loaded skin {result.Value.Name}.");
            return;
        }

        this.CurrentSkin = this.skinLoader.CreateFallback("Fallback");
        this.controller?.SetStatus(result.Error);
    }

    private string DefaultSkinPath()
    {
        var pluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
        return Path.Combine(pluginDirectory, "Skins", "base-2.91.wsz");
    }

    private void OnPenumbraReady()
    {
        try
        {
            this.controller.ReloadPenumbraData();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not reload Penumbra data after it initialized.");
        }
    }

    private void OnCommand(string command, string args)
        => this.ToggleMainUi();

    private void OpenConfigUi()
    {
        this.playerWindow.IsOpen = true;
        if (this.Configuration.PlaylistWindowVisible)
            this.PlaylistWindow.IsOpen = true;
    }

    private void DrawUi()
    {
        this.WindowSystem.Draw();
        this.fileDialogManager.Draw();
        this.FlushConfig();
    }

    private void ToggleMainUi()
    {
        if (this.playerWindow.IsOpen)
        {
            this.playerWindow.IsOpen = false;
            this.PlaylistWindow.IsOpen = false;
            return;
        }

        this.playerWindow.IsOpen = true;
        if (this.Configuration.PlaylistWindowVisible)
            this.PlaylistWindow.IsOpen = true;
    }
}
