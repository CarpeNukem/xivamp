using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace xivAMP.Services;

/// <summary>
/// Fires in-game emotes by id through the game's emote agent. The id is an Emote sheet row
/// id - the same value Penumbra reports for an emote it changes (see <see cref="ChangedItem"/>).
/// </summary>
public sealed class EmoteService
{
    private readonly IPluginLog log;

    public EmoteService(IPluginLog log)
        => this.log = log;

    /// <summary>
    /// Try to play the emote. Must be called from the game's main thread (xivAMP's playback
    /// actions run during the UI draw, which already satisfies this).
    /// </summary>
    public unsafe bool TryExecute(ushort emoteId, out string error)
    {
        error = string.Empty;
        if (emoteId == 0)
        {
            error = "No emote selected.";
            return false;
        }

        try
        {
            var agent = AgentEmote.Instance();
            if (agent is null)
            {
                error = "Emote agent is not available.";
                return false;
            }

            if (!agent->CanUseEmote(emoteId))
            {
                error = "That emote can't be used right now.";
                return false;
            }

            agent->ExecuteEmote(emoteId);
            return true;
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "Could not execute emote {EmoteId}.", emoteId);
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// True when the character is currently performing the given emote. Reads the live emote
    /// id from the character's EmoteController, so idle / sit / doze (which are loop emotes
    /// with different ids) don't count - only the same emote does. Used to avoid restarting
    /// an emote that is already playing.
    /// </summary>
    public unsafe bool IsPerformingEmote(nint characterAddress, ushort emoteId)
    {
        if (characterAddress == nint.Zero || emoteId == 0)
            return false;

        try
        {
            var character = (Character*)characterAddress;
            return character->EmoteController.EmoteId == emoteId;
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "Could not read character emote state.");
            return false;
        }
    }
}
