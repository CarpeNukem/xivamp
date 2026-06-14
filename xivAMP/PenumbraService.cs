using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Penumbra.Api.Enums;
using Penumbra.Api.Helpers;
using Penumbra.Api.IpcSubscribers;

namespace xivAMP;

public sealed class PenumbraService : IDisposable
{
    // Arbitrary key to identify xivAMP's temporary settings in Penumbra.
    private const int TemporaryKey = -1487601;
    private const string TemporarySource = "xivAMP";

    private readonly IPluginLog log;
    private readonly EventSubscriber? penumbraInitialized;
    private readonly EventSubscriber? penumbraDisposed;
    private readonly GetAvailableModSettings? getAvailableModSettings;
    private readonly GetCollectionForObject? getCollectionForObject;
    private readonly GetCurrentModSettingsWithTemp? getCurrentModSettingsWithTemp;
    private readonly GetModDirectory? getModDirectory;
    private readonly GetModList? getModList;
    private readonly GetChangedItems? getChangedItems;
    private readonly RedrawObject? redrawObject;
    private readonly RemoveAllTemporaryModSettingsPlayer? removeAllTemporaryModSettingsPlayer;
    private readonly SetTemporaryModSettingsPlayer? setTemporaryModSettingsPlayer;
    private readonly TrySetModSetting? trySetModSetting;

    public PenumbraService(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.log = log;
        try
        {
            this.getAvailableModSettings = new GetAvailableModSettings(pluginInterface);
            this.getCollectionForObject = new GetCollectionForObject(pluginInterface);
            this.getCurrentModSettingsWithTemp = new GetCurrentModSettingsWithTemp(pluginInterface);
            this.getModDirectory = new GetModDirectory(pluginInterface);
            this.getModList = new GetModList(pluginInterface);
            this.getChangedItems = new GetChangedItems(pluginInterface);
            this.redrawObject = new RedrawObject(pluginInterface);
            this.removeAllTemporaryModSettingsPlayer = new RemoveAllTemporaryModSettingsPlayer(pluginInterface);
            this.setTemporaryModSettingsPlayer = new SetTemporaryModSettingsPlayer(pluginInterface);
            this.trySetModSetting = new TrySetModSetting(pluginInterface);

            // Probe whether Penumbra is actually loaded right now.
            try
            {
                _ = new ApiVersion(pluginInterface).Invoke();
                this.IsAvailable = true;
            }
            catch
            {
                this.IsAvailable = false;
                this.log.Warning("Penumbra is not loaded yet; waiting for its Initialized event.");
            }

            // Track Penumbra load/unload so availability recovers without a plugin reload.
            this.penumbraInitialized = Initialized.Subscriber(pluginInterface, this.OnPenumbraInitialized);
            this.penumbraDisposed = Disposed.Subscriber(pluginInterface, this.OnPenumbraDisposed);
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "Penumbra IPC is not available.");
            this.IsAvailable = false;
        }
    }

    public bool IsAvailable { get; private set; }

    /// <summary>Raised when Penumbra (re)initializes after this plugin loaded.</summary>
    public event Action? PenumbraReady;

    public void Dispose()
    {
        this.penumbraInitialized?.Dispose();
        this.penumbraDisposed?.Dispose();
    }

    /// <summary>
    /// Resolve a Penumbra mod directory name to its full filesystem path.
    /// Combines the Penumbra root mods folder with the mod directory name.
    /// </summary>
    public string? ResolveModPath(string modDirectory)
    {
        if (!this.IsAvailable || this.getModDirectory is null || string.IsNullOrWhiteSpace(modDirectory))
            return null;

        try
        {
            var rootPath = this.getModDirectory.Invoke();
            if (string.IsNullOrWhiteSpace(rootPath))
                return null;

            var fullPath = Path.Combine(rootPath, modDirectory);
            return Directory.Exists(fullPath) ? fullPath : null;
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "Could not resolve mod path for {ModDirectory}.", modDirectory);
            return null;
        }
    }

    public Result<IReadOnlyList<PenumbraMod>> GetMods()
    {
        if (!this.IsAvailable || this.getModList is null)
            return Result<IReadOnlyList<PenumbraMod>>.Fail("Penumbra is not available.");

        try
        {
            var mods = this.getModList.Invoke()
                .Select(pair => new PenumbraMod(pair.Key, pair.Value))
                .OrderBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(mod => mod.Directory, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Result<IReadOnlyList<PenumbraMod>>.Ok(mods);
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "Could not read Penumbra mods.");
            return Result<IReadOnlyList<PenumbraMod>>.Fail($"Could not read Penumbra mods: {ex.Message}");
        }
    }

    public Result<IReadOnlyDictionary<string, string[]>> GetAvailableSettings(string modDirectory)
    {
        if (!this.IsAvailable || this.getAvailableModSettings is null)
            return Result<IReadOnlyDictionary<string, string[]>>.Fail("Penumbra is not available.");

        if (string.IsNullOrWhiteSpace(modDirectory))
            return Result<IReadOnlyDictionary<string, string[]>>.Ok(new Dictionary<string, string[]>());

        try
        {
            var settings = this.getAvailableModSettings.Invoke(modDirectory, string.Empty);
            if (settings is null)
                return Result<IReadOnlyDictionary<string, string[]>>.Fail("Penumbra could not find the selected mod.");

            var groups = settings.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Item1,
                StringComparer.OrdinalIgnoreCase);

            return Result<IReadOnlyDictionary<string, string[]>>.Ok(groups);
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "Could not read Penumbra options for {ModDirectory}.", modDirectory);
            return Result<IReadOnlyDictionary<string, string[]>>.Fail($"Could not read Penumbra options: {ex.Message}");
        }
    }

    /// <summary>
    /// The mod's "Changed Items" as Penumbra identifies them: each entry is the changed
    /// object's display name, the runtime type name of its identified value (so emotes /
    /// actions can be told apart from gear), and - when the value is a Lumina row (e.g. an
    /// Emote) - that row's id, which doubles as the in-game emote id for ExecuteEmote.
    /// Mirrors the Changed Items tab (whole mod).
    /// </summary>
    public Result<IReadOnlyList<ChangedItem>> GetChangedItemsList(string modDirectory)
    {
        if (!this.IsAvailable || this.getChangedItems is null)
            return Result<IReadOnlyList<ChangedItem>>.Fail("Penumbra is not available.");

        if (string.IsNullOrWhiteSpace(modDirectory))
            return Result<IReadOnlyList<ChangedItem>>.Ok(Array.Empty<ChangedItem>());

        try
        {
            var changed = this.getChangedItems.Invoke(modDirectory, string.Empty) ?? new Dictionary<string, object?>();
            var list = changed
                .Select(pair => new ChangedItem(
                    pair.Key,
                    pair.Value?.GetType().Name ?? "Unknown",
                    ExtractRowId(pair.Value)))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return Result<IReadOnlyList<ChangedItem>>.Ok(list);
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "Could not read Penumbra changed items for {ModDirectory}.", modDirectory);
            return Result<IReadOnlyList<ChangedItem>>.Fail($"Could not read changed items: {ex.Message}");
        }
    }

    // Penumbra hands back the identified value as a boxed Lumina row (e.g. an Emote struct).
    // Lumina rows expose a uint RowId; for an Emote that id is the same id ExecuteEmote wants.
    private static uint ExtractRowId(object? value)
    {
        if (value is null)
            return 0;

        try
        {
            var prop = value.GetType().GetProperty("RowId");
            if (prop?.GetValue(value) is { } raw)
                return Convert.ToUInt32(raw);
        }
        catch
        {
            // Non-row value (e.g. a boxed int count) - no usable id.
        }

        return 0;
    }

    public Result ApplyTrack(int objectIndex, string modDirectory, string optionGroup, string optionName, bool temporary, bool redraw)
    {
        if (!this.IsAvailable)
            return Result.Fail("Penumbra is not available.");

        if (string.IsNullOrWhiteSpace(modDirectory))
            return Result.Fail("Choose a Penumbra mod first.");

        if (string.IsNullOrWhiteSpace(optionGroup))
            return Result.Fail("Choose an option group first.");

        if (string.IsNullOrWhiteSpace(optionName))
            return Result.Fail("Playlist entry has no option name.");

        try
        {
            var code = temporary
                ? this.ApplyTemporary(objectIndex, modDirectory, optionGroup, optionName)
                : this.ApplyPersistent(objectIndex, modDirectory, optionGroup, optionName);

            if (!IsSuccess(code))
                return Result.Fail($"Penumbra rejected the option change: {code}");

            if (redraw && this.redrawObject is not null)
                this.redrawObject.Invoke(objectIndex, RedrawType.Redraw);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "Could not apply track {OptionName} from {ModDirectory}.", optionName, modDirectory);
            return Result.Fail($"Could not apply playlist entry: {ex.Message}");
        }
    }

    public Result ClearTemporaryPlayerSettings(int objectIndex, bool redraw)
    {
        if (!this.IsAvailable || this.removeAllTemporaryModSettingsPlayer is null)
            return Result.Ok();

        try
        {
            var code = this.removeAllTemporaryModSettingsPlayer.Invoke(objectIndex, TemporaryKey);
            if (!IsSuccess(code))
                return Result.Fail($"Penumbra rejected temporary reset: {code}");

            if (redraw && this.redrawObject is not null)
                this.redrawObject.Invoke(objectIndex, RedrawType.Redraw);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "Could not clear xivAMP temporary settings.");
            return Result.Fail($"Could not reset temporary Penumbra settings: {ex.Message}");
        }
    }

    private PenumbraApiEc ApplyTemporary(int objectIndex, string modDirectory, string optionGroup, string optionName)
    {
        if (this.getCollectionForObject is null || this.getCurrentModSettingsWithTemp is null || this.setTemporaryModSettingsPlayer is null)
            return PenumbraApiEc.NothingChanged;

        var (valid, objectValid, (collectionId, _)) = this.getCollectionForObject.Invoke(objectIndex);
        if (!valid || !objectValid)
            return PenumbraApiEc.CollectionMissing;

        var (resultCode, currentSettings) = this.getCurrentModSettingsWithTemp.Invoke(collectionId, modDirectory, string.Empty, false, false, TemporaryKey);
        if (!IsSuccess(resultCode) || currentSettings is not { } settings)
            return resultCode;

        var (enabled, priority, settingsDict, _, _) = settings;
        var merged = settingsDict.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.ToList(),
            StringComparer.OrdinalIgnoreCase);
        merged[optionGroup] = [optionName];

        return this.setTemporaryModSettingsPlayer.Invoke(
            objectIndex,
            modDirectory,
            false,
            enabled,
            priority,
            merged,
            TemporarySource,
            TemporaryKey,
            string.Empty);
    }

    private PenumbraApiEc ApplyPersistent(int objectIndex, string modDirectory, string optionGroup, string optionName)
    {
        if (this.getCollectionForObject is null || this.trySetModSetting is null)
            return PenumbraApiEc.NothingChanged;

        var (valid, objectValid, (collectionId, _)) = this.getCollectionForObject.Invoke(objectIndex);
        if (!valid || !objectValid)
            return PenumbraApiEc.CollectionMissing;

        return this.trySetModSetting.Invoke(collectionId, modDirectory, optionGroup, optionName, string.Empty);
    }

    private void OnPenumbraInitialized()
    {
        this.IsAvailable = true;
        this.log.Information("Penumbra initialized; IPC is now available.");
        this.PenumbraReady?.Invoke();
    }

    private void OnPenumbraDisposed()
    {
        this.IsAvailable = false;
        this.log.Information("Penumbra disposed; IPC is no longer available.");
    }

    private static bool IsSuccess(PenumbraApiEc code)
        => code is PenumbraApiEc.Success or PenumbraApiEc.NothingChanged;
}
