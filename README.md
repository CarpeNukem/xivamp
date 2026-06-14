# xivAMP

A Winamp-style **controller** for **FINAL FANTASY XIV** background music, built as a [Dalamud](https://github.com/goatcorp/Dalamud) plugin. xivAMP does **not** play or decode any audio itself - it's a front-end for [Penumbra](https://github.com/xivdev/Penumbra). It switches the options of a music-replacement mod, and *Penumbra* does the actual BGM swapping in-game. All of that is wrapped in a skinnable, nostalgia-accurate Winamp UI - main player, playlist, window-shade modes, spectrum analyzer, the works.

Open or close it with `/xivamp` (or the short form `/xamp`).

---

## How it works

xivAMP is just a remote control for **Penumbra** - it never touches audio itself. It works entirely by toggling Penumbra mod options:

- You select a Penumbra **mod** in Settings.
- In the Add panel you browse that mod's **option groups** and add their **options** to your playlist - each option becomes a **track**. A playlist can mix options from more than one group.
- "Playing" a track tells Penumbra to enable that option. **Penumbra** then swaps the corresponding `.scd` BGM file in-game, and your character is (optionally) redrawn so the change takes effect.

So xivAMP provides the playlist, the controls, and the UI; Penumbra provides the actual music files and does the swapping.

Because of this design, a few things follow naturally - see [Limitations](#limitations).

---

## Requirements

1. **Dalamud** (you're running this as a Dalamud plugin).
2. **Penumbra**, installed and enabled. xivAMP talks to Penumbra over IPC; if Penumbra isn't available, playback control won't work.
3. **A music-replacement mod** in Penumbra structured as an **option group** (see below).

---

## Supported mods

xivAMP works with any Penumbra mod that exposes a **single-selection option group whose options replace BGM (`.scd`) files**. In practice that means:

- One **option group** (a "radio button" / single-select group works best, so only one track plays at a time).
- Several **options** inside it, each one redirecting a game BGM `.scd` to a different song. Each option shows up as a track.
- Options may live across multiple groups; xivAMP lets you switch groups and build the playlist from whichever group you choose.

Mods that bundle many songs as selectable options (commonly called "DJ mods" or "music mods") are exactly the shape xivAMP expects. A mod that only ever force-replaces one fixed file with no options won't give you a playlist to choose from.

### Track metadata (duration / bitrate / kHz)

xivAMP reads your mod's `group_*.json` files to find each option's `.scd`, then parses the `.scd` to estimate **duration**, **sample rate**, and **bitrate**. This is what drives the clock, the position bar, and auto-advance. If an option has no readable `.scd`, xivAMP falls back to a default track length (so the clock still runs, just approximately).

---

## Getting started

1. **Install the prerequisites** - Penumbra, and a music mod set up as an option group (see [Supported mods](#supported-mods)).
2. **Open xivAMP** with `/xivamp`.
3. **Open Settings** - click the **eject** button on the main window (or the options button in the title bar).
4. **In Settings:**
   - Under **MOD**, choose the Penumbra mod / option group that holds your songs.
   - Turn on **REDRAW** (so your character is redrawn after each change - required for a BGM swap to actually start) and **TEMP** (use Penumbra *temporary* settings so the mod isn't permanently altered).
   - Optionally **LOAD** a Winamp skin.
5. **Build your playlist** - open the **Add** panel (the **ADD** button at the bottom-left of the playlist, or eject in the playlist footer). Pick a group on the left, then check/click options (tracks) on the right and use **ADD CHECKED** / **ADD GROUP**. The panel stays docked and open so you can keep adding; use the **filter** boxes to search.
6. **Play** - double-click a track in the playlist, or press play on the main window. Use previous/next, shuffle, and repeat as usual. **Stop** returns to the silent "off" option (below).
7. **Save your work** - in **Settings → PLAYLISTS** you can **SAVE** the current playlist as a named preset and **LOAD** it later.

> **Tip:** If a newly selected track doesn't start, make sure **REDRAW** is enabled - the game only applies the BGM swap when your character is redrawn.

---

## The "off" / silent option (important)

The **first option in your chosen group is treated as the default / "off" option.**

- **Stop** switches to this first option and parks there (no auto-advance).
- **Pause** switches to it as well (and toggles back to the track on the next press).

For Stop and Pause to actually **silence** the music, the **first option in the group should be a no-audio / silent option** - for example an option that doesn't replace the BGM, or replaces it with a silent `.scd`.

It does **not** have to literally be named "Off". It just needs to be:
- **First** in the option group's order, and
- **Silent** (produces no music).

If your first option is itself a song, Stop/Pause will switch *to that song* instead of going quiet. The simplest fix is to add a silent/"None"/"Off" option and make it the first entry in the group.

> On window close (and plugin unload), xivAMP releases control entirely and restores the game's normal BGM, so nothing lingers after you're done.

---

## Winamp skins

xivAMP renders through **classic Winamp 2.x skins** (`.wsz` files - they're just renamed ZIP archives).

- Load one in **Settings → SKIN → LOAD**, or **CLEAR** to return to the built-in skin.
- Thousands of skins are at the **[Winamp Skin Museum](https://skins.webamp.org/)** (there's a "Browse skins" link in Settings).
- If a skin fails to load or is missing pieces, xivAMP falls back to a bundled default skin (`base-2.91`) and fills in any missing generic-window art.

### What gets read from a skin

| File | Used for |
|------|----------|
| `MAIN.bmp` | Main window background |
| `TITLEBAR.bmp` | Title bar, window buttons, **window-shade (minimized) mode** |
| `CBUTTONS.bmp` | Transport buttons (prev/play/pause/stop/next) |
| `SHUFREP.bmp` | Shuffle / repeat / EQ / playlist toggles |
| `POSBAR.bmp` | Position (seek) bar |
| `VOLUME.bmp`, `BALANCE.bmp` | Volume / balance sliders |
| `MONOSTER.bmp` | Mono / stereo indicators |
| `PLAYPAUS.bmp` | Play / pause / stop indicators |
| `NUMBERS.bmp` *(or `NUMS_EX.bmp`)* | Time-display digits |
| `TEXT.bmp` | Bitmap font (track title, etc.) |
| `PLEDIT.bmp` + `PLEDIT.txt` | Playlist window art + playlist text colors |
| `GEN.bmp`, `GENEX.bmp` | Generic windows (Settings, Add panel) + their colors |
| `VISCOLOR.txt` | **Spectrum-analyzer color palette** (24 colors) |

Both standard and RLE-compressed BMPs are supported, as are PNG versions of the sheets. Skins without a `VISCOLOR.txt` get a default green→red analyzer gradient; skins without number sheets fall back gracefully.

---

## Features

- **Main player** - play / pause / stop / previous / next, eject (opens Settings), shuffle, repeat, a volume slider (controls the in-game **BGM volume**), an estimated position/seek bar, a VISCOLOR-driven **spectrum analyzer** with falling peaks, and the time display.
- **Window-shade (minimized) modes** for both the main window and the playlist - collapse to a single title bar, Winamp-style.
- **Playlist** - add / remove / crop, drag-and-drop reordering, a footer with its own transport controls, and per-skin colors. Save and load named **playlist presets**.
- **Add panel** - a persistent, dockable window (sits under the playlist) with separate filters for groups and options, so you can keep editing the playlist without it closing.
- **Settings** - load/clear skins, toggle Penumbra temporary-settings and character redraw, set a **track gap** (extra hold time before auto-advancing), manage playlist presets, and choose the source mod/group.

---

## Controls & commands

- `/xivamp` or `/xamp` - toggle the player (and playlist).
- Title bar: options menu, **shade** (minimize), close.
- The Winamp logo on the main window links to the //n_root discord community that authored the mod **[Discord](https://discord.gg/kxZMbP3C5B)**.

---

## Limitations

- **Playback control is UI-bound.** xivAMP only controls the BGM while its window is open; closing the window releases control and restores the game's default music. (This is by design.)
- **Pause/Stop use option-swapping, not true transport.** There's no real per-track seek or position-hold - resuming restarts the current track, and Pause/Stop rely on the silent "off" option described above.
- **The volume slider sets the game's BGM volume**, not a separate output level.
- **The spectrum analyzer is animated, not a real FFT** of the audio (the game doesn't expose BGM audio data to plugins). Its motion is keyed off the playing track's metadata.

---

## Credits

Skins are the work of their original Winamp skin authors. Winamp is a trademark of its respective owners; xivAMP is a fan project and is not affiliated with Winamp, Webamp, Square Enix, or Penumbra.
