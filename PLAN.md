# xivAMP — Fixes & Improvements Plan

Based on a full read of the codebase (2026-06-11). All items implemented except where noted.

## P0 — core function (continuous playback)

1. **UI-bound playback (by design) — release control on close.** ✅ done
   Playback control is intentionally active only while the UI is drawn. `PlayerWindow.OnClose` and `Plugin.Dispose` call `XivAmpController.ReleaseControl()`, which restores the default option and clears xivAMP's temporary Penumbra settings. Auto-advance stays inside `PlayerWindow.Draw` on purpose.

2. **Applied-track duration for clock/progress/auto-advance.** ✅ done
   `XivAmpController.AppliedEntry()` resolves the actually-playing entry by `LastAppliedOptionGroup/Name`; PlayerWindow (clock, position bar, visualizer, media info, title) and the PlaylistWindow timer use it, falling back to the selected row.

3. **Shared `AudioMetadataService`.** ✅ done
   Exposed as `XivAmpController.AudioMetadata`; PlaylistWindow's private instance removed.

## P1 — UX correctness

4. **Pause mechanism — decided: keep current.** Pause stays as the Penumbra default-option swap (true per-track pause/seek isn't feasible; even Orchestrion only has play/stop). Blinking `00:00` is accurate since the track restarts on resume.

5. **Shuffle play-through.** ✅ done
   Identity-keyed `shuffleHistory` in the controller: every track plays once per round; Repeat off ends the playlist after a full round; Repeat on starts a new round. History clears on playlist rebuild/clear/preset load/mod change.

6. **Main clock total minutes.** ✅ done
   `DrawClock` now shows `mm:ss` with total minutes (e.g. `62:03`), capped at `99:59`.

## P2 — cleanup / robustness

7. **Penumbra lifecycle.** ✅ done
   `PenumbraService` probes `ApiVersion` at construction and subscribes to Penumbra's `Initialized`/`Disposed` events; `PenumbraReady` triggers `ReloadPenumbraData()` so the mod list recovers without a plugin reload. Service is now `IDisposable`.

8. **Deduplicate helpers.** ✅ done
   `PlaylistFormat` (EntryKey, IsEntryIdentity, TryParseDuration, FormatDuration) is the single implementation; controller/windows/metadata service delegate to it.

9. **Docking dead code.** ✅ done
   Removed `PlaylistDocked`, playlist position persistence, the per-frame force-sets, and the no-op titlebar-drag stub. The playlist is always docked below the player window.

10. **Reduce config disk writes.** ✅ done
    `Plugin.Save()` sets a dirty flag; flushed at most once per second in `DrawUi`, force-flushed on `Dispose`.

## Also fixed along the way

- Playlist-window elapsed timer matches the main clock (seek offset; wrap only on Repeat).
- Playlist bottom buttons hit areas aligned with the painted graphics (y −30, 22×18).
- REM menu: correct sidebar sprite (PLEDIT 100,111 3×54), Winamp-exact positioning (items on the button column, bar 3px left), and a new CROP item (`CropToEntry`) with confirm popup.

## Verification

Build on your machine (`dotnet build` — sandbox lacks the Dalamud SDK) and smoke-test in-game: close window → music control released; timer after clicking another row; shuffle full round then "End of playlist"; REM menu look and CROP; Penumbra restart recovery; long-track clock (62:03).
