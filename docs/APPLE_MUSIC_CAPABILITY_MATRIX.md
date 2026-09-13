# Apple Music Capability Matrix — Phase 0 results

検証日: 2026-09-11
環境: Windows 11, Apple Music (Store) `AppleInc.AppleMusicWin_1.1540.23042.0_x64__nzyj5cx40ttqa`, .NET SDK 10.0.401
検証アプリ: `src/AppleMusicWidget.Probe` (`AmwProbe.exe`, `net10.0-windows10.0.19041.0`)

## Feature matrix

| Feature                        | Supported | Method / Notes |
|--------------------------------|-----------|----------------|
| Process name                   | Yes       | `AppleMusic.exe` (exact match; companion `AMPLibraryAgent.exe` must be excluded) |
| Start detection                | Yes       | WMI `__InstanceCreationEvent` on `Win32_Process` — **works non-elevated** (fired ~0.4s before the 2s poll) |
| Exit detection                 | Yes       | `Process.Exited`, `WaitForExitAsync`, and WMI `__InstanceDeletionEvent` all fire non-elevated |
| GSMTC session identity         | Yes       | `SourceAppUserModelId` = `AppleInc.AppleMusicWin_nzyj5cx40ttqa!App` (prefix `AppleInc.AppleMusic`) |
| SessionsChanged event          | Yes       | Fires on session appear/disappear; usable as reconnection trigger |
| Track title                    | Yes       | `MediaProperties.Title` |
| Artist                         | Partial   | `MediaProperties.Artist` contains **`"{artist} — {album}"` combined** (e.g. `BlueVeil — Spring Spring - Single`, `n-buna • アイラ - Single`). Separator varies per track. `AlbumArtist` holds the same combined value. `AlbumTitle` is always empty. |
| Album title                    | No        | `MediaProperties.AlbumTitle` always `""` — parse out of `Artist` field instead |
| Track number / genres          | No        | `TrackNumber=0`, `AlbumTrackCount=0`, `Genres` empty |
| Artwork                        | Yes       | `MediaProperties.Thumbnail` → `OpenReadAsync()` (~120KB JPEG observed) |
| Playback status                | Yes       | `GetPlaybackInfo().PlaybackStatus` — observed `Playing`, `Paused`, `Opened` (fresh launch, never played) |
| Play / Pause / Prev / Next     | Yes       | `TryPlayAsync`/`TryPauseAsync`/`TrySkipPreviousAsync`/`TrySkipNextAsync`; availability is dynamic (`IsPreviousEnabled=False` at queue start) |
| Timeline (pos/end/seek range)  | Yes       | `GetTimelineProperties()` gives `Position`, `EndTime`, `MinSeekTime`, `MaxSeekTime`, `LastUpdatedTime` → supports local interpolation |
| Seek                           | **No**    | `TryChangePlaybackPositionAsync` returns `True` but Apple Music ignores it (Phase 2: tested playing and paused, ticks and ms — position never moved). `IsPlaybackPositionEnabled=False` is honest. Phase 0's "Yes" was a false positive (seeked to the *current* position). Seeks made in Apple Music's own scrubber DO arrive via `TimelinePropertiesChanged`. Widget shows a read-only progress bar; plumbing kept in case a future version enables it. |
| Track change latency           | Yes       | `MediaPropertiesChanged` → widget title+artwork updated in ~360 ms after `TrySkipNextAsync` (Phase 2) |
| Minimized / other v-desktop    | Assumed   | Session is process-global, not window-bound; expected to persist (confirm in Phase 7) |
| Launched-but-never-played      | Yes       | Session exists with `PlaybackStatus=Opened`, empty props, zeroed timeline → show idle UI |
| `…` menu                       | Yes       | Not exposed via GSMTC → UI Automation on user action only: `AutomationId="ActionButton"` + `InvokePattern.Invoke()` opens the menu (Phase 5) |
| Play Next queue                | Yes       | Not exposed via GSMTC → on user action, temporarily toggle `PlayQueueToggleButton`, enumerate `PlayQueueListView` through `ItemContainerPattern`, and render title / artist-album / duration in the widget. The Apple Music queue state and previous foreground window are restored after reading (Phase 5) |

## Lifecycle behavior (observed)

- Apple Music exit sequence: GSMTC `SessionsChanged` (session gone) → ~1.2s later `Process.Exited`. Widget should hide on **process** exit, not session loss (session can drop earlier).
- Cold start sequence: `__InstanceCreationEvent` → process found → `SessionsChanged` ~80ms later → session connectable within ~2s.
- Release on exit: detach handlers, drop refs → working set back to baseline (~60MB probe baseline).
- Idle CPU (`WaitingForAppleMusic`): **0.01–0.04%** measured over minutes. Playing-idle similar.
- Memory: stable ~61MB in probe (no WPF yet); no growth observed across start/stop cycle.

## PowerShell 5.1 note

`ManagementEventWatcher` + `WaitForNextEvent()` timed out in PS5.1 probe (non-elevated), but the **same API works in .NET 10**. WMI events are usable as primary detection; keep 2s polling as fallback since plan §5.3 allows it.

## Phase 2 measurements (Debug build, WPF widget)

- CPU while Playing (500 ms interpolation tick): 0.044 % — Paused (tick stopped): 0.020 %
- Working set: ~183 MB in Debug — **over the 100 MB target**; Phase 3 must measure Release + investigate (WinForms load for NotifyIcon, WPF baseline, UIA).
- Position tick observed advancing 1 s/s while Playing and frozen while Paused; resumes correctly.

## Phase 3 measurements (Release build, taskbar strip)

Method: `tools/measure-mem.ps1` (WS / Private Bytes / Private WS = Task Manager "Memory" / CPU over 10 s), `tools/soak.ps1`.

| State | Before (Release) | After Phase 3 | Notes |
|---|---|---|---|
| Waiting (AM closed) WS | 141 MB | **34 MB** | WinForms removed (−25 MB), working-set trim on entering `WaitingForAppleMusic` |
| Playing WS | 147 MB | 58 MB (after a trim) / 139 MB (fresh start, mostly shared framework pages) | |
| Playing Private WS (Task Manager) | — | **40.7 MB** | InvariantGlobalization −7 MB |
| Playing Private Bytes | 74.6 MB | 72.4 MB | |
| CPU playing / paused / waiting | 0.04 % / 0.02 % / 0.01 % | same | 500 ms tick only while playing |
| 100 track skips | WS +20 MB then flat | peak then settles | LOH copy removed in ArtworkProvider |
| 20 AM start/stop cycles | handles +21 | **756→751** | `Process` disposal fix |
| 20 min continuous play | Private +3 MB | — | no growth trend |

Attribution (module image sizes, Release): `Microsoft.Windows.SDK.NET.dll` 23.7 MB (WinRT projection; only touched pages count), `PresentationFramework` 15 MB, CoreLib 15 MB. `PresentationFramework.Fluent` is loaded (0.9 MB image) because of `ThemeMode="System"`.

Remaining ideas if needed later: drop `ThemeMode="System"` (strip uses its own brushes), lower TFM to 17763 to shrink the WinRT projection, ReadyToRun publish to cut JIT memory. Not pursued — targets met.

## Phase 7 measurements (Release build, 2026-09-13)

Method: `tools/measure-mem.ps1`, `tools/soak.ps1`, real Apple Music playback, and physical mouse input for shell integration checks.

| Test | Result | Notes |
|---|---|---|
| Paused, 10 s | Private WS 37.5 MB, CPU 0.0146% | One visible 340×48 strip; flyout not created until clicked |
| 100 track skips | Private WS 38.6→47.8 MB | Rose during initial artwork/cache loading, then stayed around 45–48 MB |
| 50 start/stop cycles | Private Bytes 83.4→84.6 MB | No growth trend; waiting-state samples had trimmed Private WS 5.5–9.1 MB |
| 60 min real playback | Private WS 39.3→39.1 MB, CPU max 0.0488% | Tracks advanced throughout; Private Bytes 73.6→93.2 MB |

Shell integration passed for Explorer restart, real tray-icon right click and re-registration, taskbar auto-hide/reveal, foreground fullscreen hide/restore, light/dark theme switching, compact 136 DIP layout, minimum-width hiding, and Apple Music minimization. Phase 7 exposed and fixed three defects: the message-only tray HWND missing `TaskbarCreated`, unreliable fullscreen detection without a shell-hook fallback, and first-show collision leaking the strip at WPF's default position.

## Open questions for later phases

- Whether `Artist` field separator is consistently ` — ` (em-dash) vs ` • ` — observed both; treat `Artist` string as display-ready "artist — album" line rather than parsing.
- Behavior during sleep/resume and session re-creation after Apple Music update (Phase 7).
