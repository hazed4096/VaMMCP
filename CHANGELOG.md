# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **`list_persons` reports worn clothing.** Each person row now carries
  `wornCount` and `worn` (display names of active clothing items, excluding the
  runtime clothing creator) - built from `sel.clothingItems`/`active`, the same
  source as `list_clothing_items`. Purpose: catch a character-swapped person
  that still wears its source look's outfit. Verified live: girls stripped to
  0 while the male reports 2 and the Nurse Futa 8, values stable across a
  scene reload.
- **`list_audio_clips` reports URL-clip load state.** URL section rows now
  carry `ready` (bool) and `error` (when the queued load failed), so a silent
  trigger fired against a still-loading package WAV is diagnosable without
  playing into a refusal. Verified live across a reload: all 15 queued machine
  WAVs flip from ready:false to ready:true as the queue drains.
- **`list_audio_clips` (new tool).** Enumerates VaM's registered audio clips -
  `EmbeddedAudioClipManager.singleton` and `URLAudioClipManager.singleton`
  (public `GetCategories`/`GetCategoryClips`, decompile lines 263863+), grouped
  by category with `type` (all/embedded/url), case-insensitive `category` and
  `search` filters. The returned uids feed straight into `audio_source_action`
  Play/SetClip. Verified live: 332 embedded clips across 11 categories on a
  fresh boot; search/category filters; url section empty cleanly; bad type
  errors clearly.
- **`audio_source_action Play` resolves embedded clips.** `FindRegisteredClip`
  now falls back to `EmbeddedAudioClipManager` after the URL manager, so a clip
  name like `NightlyNewsOrigMix` plays without any Sound-tab visit. Verified
  live: `Play` -> `playing: NightlyNewsOrigMix` on a test AudioSource atom.
- **`atom_control LoadUserDefaults`.** Calls the atom's
  `mainPresetControl.LoadUserDefaults()` - the same call VaM's own add-flow
  (`AddAtomByType` userInvoked branch, decompile 318782) makes. Applies the
  user-saved defaults preset when one exists; otherwise a faithful no-op
  (`loaded:true`, unchanged values - verified on a stock cube, whose defaults
  store nothing).
- **Local WAV decode + play via `audio_source_action`.** `SetClip` decodes
  disk PCM WAVs (16-bit or float32) in-plugin (`FileManagerSecure.ReadAllBytes`
  + `AudioClip.Create`/`SetData`) and caches them, because VaM's
  `URLAudioClipManager` queue never advances in a fresh scene (its coroutine is
  dormant until the Sound tab is touched, so queued ogg/mp3 stay `ready=false`
  silently). `Play`/`PlayLoop` remember the last `SetClip` per atom; ogg/mp3
  still route to `QueueClip` with a "may stall until Sound tab opened" note.
  Verified: SetClip -> ready/Play/Pause/UnPause/Stop round trip on a test tone.
- **`set_gaze` drives VaM's native `eyeTargetControl`.** A Person has no
  `lookAt` storable (decompile: nothing named `lookAt` exists); native gaze is
  the `eyeTargetControl` FreeControllerV3. `"Camera"`/an atom uid moves it and
  forces position/rotation states On, `"None"` returns it to Off (pose-driven).
- **`person_hand_control SetTarget/SetRotation`.** `SetTarget` now sets the
  hand FreeControllerV3's transform `lookAt` to the target (or camera) and
  switches its `rotationState` chooser to `LookAt`; `SetRotation` uses
  `SetRotationNoForce`. A stock FreeControllerV3 has no IK-weight param, so
  `SetWeight`/`AutoGrasp` error with that explanation instead of a mystery.
- **`person_animation` touches every animation storable** (32 on a fresh
  Person: `abdomen2Animation`, `chestAnimation`, ...), not just the first
  (previously only `abdomen2Animation`). Playback/arm/stop report `touched` and
  `called` per action; per-storable failures are skipped, not fatal.
- **`trigger_action Fire` really fires.** VaM's button handler fires by
  pulsing the `Trigger` object's `active` flag; the tool now does the same for
  `UIButtonTrigger` (and `VariableTrigger`), which works even with no receiver
  actions configured. Also fixed trigger resolution: the exact-id `Trigger`
  result was being unconditionally overwritten by a fuzzy `contains("trigger")`
  pick, so `Fire` errored or hit the wrong storable.
- **`add_atom` poller waits by uid with a 90 s deadline** (was type-match +
  45 s): a cold-boot Person can take longer than the old deadline to stream in,
  and the atom then appeared *after* the timeout - with the uid poll the tool
  either returns the real created atom or admits it may still be streaming.
- **Keep-alive for an unfocused, maximised VaM window on a second virtual
  desktop.** Exclusive fullscreen pauses Unity's player loop on focus loss
  (verified: no `Update()` ticks while unfocused), which stopped every
  main-thread tool. The launch script now starts VaM windowed
  (`-screen-fullscreen 0`) and the plugin re-asserts `runInBackground` every
  frame, so the loop keeps ticking; IdleSaver then correctly caps the unfocused
  VaM to its idle/minimised FPS instead of the GPU burning at full speed.
- **`performance_profile` is now a worker tool** (new `Tool.RunOnWorker` flag):
  it previously ran through the main-thread dispatcher and blocked exactly the
  loop it was sampling, so it always measured 0 frames. It now samples from the
  worker thread; verified 20 frames / 4 s (5 fps idle) with VaM unfocused.
- **`create_animation_step` reports the real step uid.** VaM renames the
  AnimationStep atom when it is attached to a pattern (e.g. `AnimationStep` ->
  `MCP Test PatternStep1`); the tool re-reads the uid after attach instead of
  returning the pre-attach name.
- **`physics_object_action AddForce` semantics**: `value` is the impulse
  magnitude (numeric), default direction +Y; a vector string parses as 0, so a
  length is the only safe input. Confirmed live on a Cube (`mass` 2.5, `drag`
  0.1, `"[0, 5, 0]"`).
- URL parameter support across `list_storable_params` / `get_param` /
  `set_param`

### Fixed
- **A nondeterministic "blurry screen + upside-down triangle + dead camera" was
  a real (OS-level) minimised VaM.** `GetWindowPlacement` showed `showCmd: 2`
  (SW_SHOWMINIMIZED); IdleSaver's `(minimized)` status string was the giveaway.
  The white triangle is the empty Unity render of the minimised window. Restoring
  the window and focusing VaM brings input back.
- **`capture_view` camera mode could crash VaM natively.** It created the
  `RenderTexture` and ran `cam.Render()`/`ReadPixels` on the HTTP worker
  thread; RenderTexture construction from a non-main thread aborts the player
  (crash stack: `RenderTexture:.ctor` under `CaptureViewCamera`). All camera
  work now runs inside one `Mt.Run` block.
- **`atom_control Select` now selects for real.** VaM's selection entry point
  is `SelectController(FreeControllerV3)` on SuperController - the previously
  probed `SelectAtom`/`SetSelectedAtom` names do not exist. Likewise `EndIsolate`
  maps to `SuperController.EndIsolateEditAtom()`; `IsolateEdit` errors with an
  explanation (entering isolation is an editor-UI action).
- **`set_param` falls back to float for numeric values it cannot classify**,
  and its error names the storable and points at `list_storable_params`
  (previously "cannot determine param type" told no more).
- **`animation_step_action SetTransition` accepts a numeric value as
  `transitionToTime`** (VaM's AnimationStep stores the transition length as
  JSONStorableFloat `transitionToTime`; the old code only matched a
  transition-type chooser and errored otherwise). String values still try the
  choosers.
- **Five broken em-dashes (`ΓÇö` mojibake) removed from tool help/results**;
  the bytes broke cp1252 console clients mid-JSON (scripts with
  `errors='strict'` printing crashed on tool output).
- **`atom_control FreezePhysics` semantic fix**: `value=false` now unfreezes
  (symmetric with `UnfreezePhysics`).
- **`physics_object_action` fixed: it hit the wrong component.** The old
  storable-search path landed on `CollisionTrigger` first ("no such param on
  CollisionTrigger"). `SetMass`/`SetDrag`/`AddForce` now operate on a Unity
  Rigidbody attached to the atom's mainController GameObject (auto-added when
  missing; ForceMode.VelocityChange; `AddForce` returns the resulting velocity).
  `FreezePhysics` also honours `value=false` as unfreeze.
- **`list_audio_sources` reports `playingClip`/`playingClipReady`** so a
  playback can be checked without a separate get_param.
- **`add_hair_item` now works.** It used to drive the runtime hair *creator*
  (`HairCreator` -> `DAZRuntimeCreator.LoadFromPath`), which silently loaded
  nothing (that flow is the hair AUTHOR editor; the creator item's `ready` never
  flips without a `Person > Hair` tab open + bundled meta). The tool now finds
  the matching item in the person's registered hair library (`uid` == path, then
  `dynamicRuntimeLoadPath`, then unique display name) and calls
  `SetActiveHairItem(target, true)` - exactly what the Person > Hair UI does.
  Verified: `{"added":"ddaamm.hair_long12.3:/.../long12 base.vam","active":true}`
  and `list_hair_items` shows it active.
- **A still-building person no longer swallows the hair add.** A fresh Person
  runs a final async restore pass after creation that rewrites hair toggles
  (default hair on, everything else off), so anything toggled earlier is
  silently reverted. `add_hair_item` first waits for the hair-item set to
  settle (`WaitForHairStable`: snapshot of uid/active/ready unchanged for 3 s,
  minimum 8 s, no active item still loading).
- **`add_hair_item` no longer stalls VaM.** It used to poll/sleep inside the
  main-thread dispatch, freezing every `Update()` while it waited; the polling
  now happens on the worker thread and only hops to the main thread for the
  atom reads/writes.
- **Nested `Mt.Run` deadlock fixed.** A main-thread tool handler that nested
  `Mt.Run` (e.g. `TriggerAction` Fire -> `GetComponentInChildren<Button>`)
  enqueued a token that could only drain on the NEXT main-thread frame - which
  never came while the outer drain ran - and hung until the 10 s timeout.
  `MainThreadDispatcher.Run` now executes inline when the caller is already on
  the main thread (`Plugin.mainThreadId`, captured in `Awake`). Verified:
  `Fire` (`UI.Button.onClick`) returns instantly.

- URL parameter support across `list_storable_params` / `get_param` / `set_param`.
  Url params (VaM's `JSONStorableUrl`, e.g. the `customTexture_MainTex` /
  `customTexture_AlphaTex` texture slots on clothing materials) were invisible and could
  not be written, which blocked any texture-based customisation. `list_storable_params`
  now reports a `urls` section, `get_param` reads them, `set_param` sets them with
  `type:"url"` (auto-detected; pass `reload=false` to skip reloading an unchanged path).
- `press_key` tool: sends a real key press to VaM (Escape dismisses modal prompts such as the
  save-screenshot dialog). It refuses to send if it cannot confirm VaM holds focus, so a
  stray key cannot land in another application; `focus=true` opts in to raising the window.

### Fixed
- **`press_key` no longer steals focus.** The default was to raise the VaM window
  (`focus=true` via `SetForegroundWindow`) before every send, which yanked the user's
  active window away. Now no window is raised automatically: the key is synthesised only
  when VaM already holds the foreground focus (checked live, so a key can never land in
  another application), and the tool refuses with a hint when VaM does not have focus.
  `focus=true` remains as an explicit opt-in to raising the VaM window.
- **`press_key` waits long enough for a resumed-but-slow player loop.** Restoring from the
  1 FPS minimised idle cap takes up to a second to run an `Update()`, so the previous
  350 ms settle (and the short synth key hold) could still lose the keypress; the settle
  is now 1.2 s and the key is held/released longer, so Escape reliably dismisses the
  save-screenshot prompt right after an un-minimise.
- **`press_key` now restores a minimised VaM window and waits for the player loop to
  resume before sending the key.** A minimised VaM does not run `Update()`, so the key
  was delivered but never read and the save-screenshot prompt stayed up. It also fixes
  the `GetCurrentThreadId` entry-point lookup (the function lives in kernel32, not user32)
  that could fail while taking focus.
- **`add_clothing_item` now works even when VaM's runtime clothing creator is
  not initialised.** Clothing paths VaM already has in the person's library are activated
  directly (`"activated":true`) instead of being silently dropped, which is why adds used to
  "succeed" without any garment appearing.
- **`add_clothing_item` no longer lies about success.** It used to return
  `{"loading":true}` right after calling `LoadFromPath`, so an add that VaM silently dropped
  (its runtime clothing creator was never initialised because the person's Clothing tab had
  never been opened in that scene) looked identical to a working add. The tool now waits for a
  new worn item to appear and returns `ok:true` with its uid, or `ok:false` plus a
  `hint`/`libraryBefore`/`libraryAfter` explaining what to do.
- `list_clothing_items` only returned the person's whole clothing *library* (hundreds of
  unworn entries with an `active` flag). It now returns the worn items, a `worn` uid list and
  `activeCount`/`libraryCount`; `all=true` restores the full dump.
- `set_clothing_item_on` and `remove_clothing_item` accept a uid, a display name or the tail of
  a package path, and report the resulting state (`on`/`changed`, `removed` + `on` + `libraryCount`) instead of
  echoing the request. `remove_clothing_item` refuses to delete the clothing creator item.
- The clothing "creator" helper item is no longer listed, counted or treated as a garment by
  `list_clothing_items` / `add_clothing_item` (it was reported as "worn" and even as a
  successfully added item).
- `Invoke` now tolerates overloaded VaM methods instead of throwing
  `AmbiguousMatchException`, which fixes `set_hair_item_on`.

## [1.0.1] - 2026-08-21

### Changed
- Tool payloads carry real JSON types too: counts, coordinates, morph values, parameter
  min/max/default and `on`/`active` flags are numbers and booleans instead of quoted strings, and
  a null value is `null` rather than `"null"`. Timestamps and enum names stay strings, as do the
  parameters sent to the Hub API, which expects them that way.

### Fixed
- **Protocol values are now real JSON types.** VaM's SimpleJSON quotes everything it serialises,
  so `listChanged`, `subscribe`, `isError`, JSON-RPC error codes and — most importantly — the
  JSON-RPC `id` all went out as strings. Clients built on the official MCP SDK validate against
  the spec schema and match responses by id, so they rejected the handshake and fell back to the
  legacy SSE transport until they timed out. opencode could not connect at all; it can now.
- `status` reported `vaMVersion` from `Application.version`, which is the Unity player version
  ("1.0"). It now uses `SuperController.GetVersion()` and reports the actual VaM build.
- `scripts/deploy.sh` and `scripts/install-bepinex.sh` resolved `VAM_ROOT` one level too high
  (the scripts already `cd` to the repo root), so with the repo inside the VaM folder they wrote
  to the *parent* of the VaM install. Both now resolve it correctly and refuse to run against a
  directory with no `VaM_Data` in it.

## [1.0.0] - 2026-08-20

First public release: an MCP server that lives inside Virt-A-Mate as a BepInEx plugin.

### Added
- Streamable HTTP MCP transport (spec 2025-06-18) served from inside the VaM process, targeting net35.
- 63 tools covering scenes, atoms, persons, morphs, clothing, hair, poses, expressions, bone
  controls, camera, simulation, Hub browsing/downloads, skin subsurface colour and VaM plugin
  management (see [docs/TOOLS.md](docs/TOOLS.md)).
- Generic JSONStorable introspection (`list_atom_storables`, `list_storable_params`, `get_param`,
  `set_param`, `call_action`) so any VaM UI slider is reachable without a dedicated tool.
- Preset export/import, `.var` package asset scanning, and native plugin management.
- `eval_cs` escape hatch, disabled by default.
- MCP resources: `vam://status` and `vam://atoms`.
- `capture_view` can return the rendered PNG inline as an MCP image with `return_image=true`.

### Security
- The HTTP listener binds to loopback only and validates the `Origin` header (DNS-rebinding
  protection); CORS echoes only an already-validated loopback origin instead of `*`.
- Concurrent connections are capped and request sizes are bounded.

[Unreleased]: https://github.com/Rorical/VaMMCP/compare/v1.0.1...HEAD
[1.0.1]: https://github.com/Rorical/VaMMCP/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/Rorical/VaMMCP/releases/tag/v1.0.0
