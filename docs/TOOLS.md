# Tool reference (73 tools)

*[中文版](TOOLS.zh-CN.md)*

Every tool is invoked through `tools/call`: `{ "name": "...", "arguments": { ... } }`.
A `*` marks a required argument, `?` an optional one. When `person` is omitted the first Person
in the scene is used. Vectors are `[x, y, z]`.

`eval_cs` is only registered when `Security.AllowEval=true`, so a default install exposes 72 tools.

## Status / session

| Tool | Arguments | Notes |
| --- | --- | --- |
| `status` | — | VaM runtime status: plugin version, VaM version, atom/person counts, endpoint, eval switch |

## Scene

| Tool | Arguments | Notes |
| --- | --- | --- |
| `list_scenes` | `search?` | Scenes under `Saves/scene` |
| `load_scene` | `path`*, `merge?` | Load a scene (`merge=true` merges into the current one); .var paths work (`Package.1:/Saves/scene/x.json`). The load runs over the next few seconds — poll `atom_info` until `loading` is false before loading again |
| `new_scene` | — | New empty scene (**discards the current one**; an empty scene has no lights — add one or raise ambient light) |
| `atom_info` | — | Loader diagnostics: `atoms` dict / `atomsList` / `startingAtoms` counts, the `loading` flag and every entry that is dead, duplicated, missing from the dict or not in the list. `list` > `dict` is the fatal pre-state of VaM's "Destination array was not long enough" abort |
| `loader_heal` | `clearLoading?` | Rebuild `atomsList` from the `atoms` dict, prune stale `startingAtoms` and clear a stuck `_isLoading` — without restarting VaM. The heal does not cancel an already-aborted load coroutine: restart VaM before the next load unless you accept the risk |
| `save_scene` | `path?` | Save the scene (default `Saves/scene/VaMMCP_<timestamp>.json`; an existing file is removed first so the save is not swallowed by VaM's confirm dialog) |
| `press_key` | `key`*, `times?`, `focus?` | Send a real key press to VaM (Escape dismisses modal prompts such as the save-screenshot dialog); never steals focus — sends only while VaM already has focus, refuses otherwise (`focus=true` opts in to raising it) |
| `ui_hud` | `action`* | `hide`/`show` VaM's main HUD (launcher panels: Hub, scene browser, Create). `hide` lets a capture look through to the scene view while VaM stays unfocused |

## Atoms

| Tool | Arguments | Notes |
| --- | --- | --- |
| `list_atom_types` | — | Creatable atom types, grouped by category (from VaM's runtime data) |
| `list_atoms` | `type?`, `query?`, `detail?`, `fields?`, `exclude?`, `include_off?`, `limit?`, `offset?`, `sort?`, `precision?` | Compact atom listing. `detail` picks a column preset (`ids`, `placement` — the default: uid/type/name/on/pos, `contents`, `health`, `full`); `fields` overrides it with explicit columns (`uid,type,name,on,pos,rot,scale,bbox,size,children,renderers,colliders,rigidbodies,storables,subScene,asset,sims,controls,worn,hair,torn`); `exclude` subtracts. `query` matches uid/name/storable id. Vectors are rounded to `precision` decimals (3 = mm). Returns `total`/`returned`/`hasMore`, so paging with `offset` is unambiguous |
| `list_objects` | `type?`, `query?`, `detail?`, `fields?`, `exclude?`, `bbox?`, `include_off?`, `limit?`, `offset?`, `sort?`, `precision?` | Same projection/paging as `list_atoms`, plus `bbox` — a six-number `[center.xyz, size.xyz]` array. **Bounding boxes are no longer computed by default** (four arrays per atom made every listing expensive); ask for `fields=...,bbox` or `bbox=true` when you actually need extents |
| `add_atom` | `type`*, `uid?` | Create an atom (Person, Light, AnimationPattern, …) and wait until it exists (polls by uid, 60 s timeout) |
| `remove_atom` | `uid`* | Delete an atom |
| `set_atom_on` | `uid`*, `on`* | Show/hide |
| `get_atom_transform` | `uid`* | World position / euler angles / scale |
| `set_atom_transform` | `uid`*, `position?`, `rotation?`, `scale?`, `relative?` | Transform (`relative=true` offsets instead of setting) |

## Persons

| Tool | Arguments | Notes |
| --- | --- | --- |
| `add_person` | `uid?` | Add a Person (VaM may rename the uid — trust the return value). Waits up to 90 s |
| `list_persons` | — | Persons in the scene (uid/character/gender/position) plus worn clothing (`worn` display names + `wornCount`) — spot a swapped person still wearing its source look's outfit |
| `list_looks` | `search?` | Appearance presets (`Saves/Person/Appearance`) |
| `load_look` | `path`*, `person?` | Apply an appearance preset (character/clothing/hair/morph deltas; asynchronous, verify a moment later) |
| `save_look` | `path?`, `person?` | Export an appearance preset (**note**: VaM presets do not carry the full material parameter set — re-apply skin parameters after moving a look between scenes) |
| `set_character` | `person?`, `name`* | Switch the base character mesh (names come from the choices of `get_param storable=geometry param=characterSelection`); loads asynchronously |

## Morphs (character sculpting)

| Tool | Arguments | Notes |
| --- | --- | --- |
| `morph_action` | `person?`, `action`*, `name?`, `value?`, `region?`, `search?`, `group?`, `limit?` | `list` (name/uid/region/group/current value; filter with `search`/`region`/`group`/`limit`), `set` (`name`, `value`, typically -1..1), `get` (`name`), `reset` (zero all, or only `region`) |

## Clothing / hair

| Tool | Arguments | Notes |
| --- | --- | --- |
| `clothing_action` | `person?`, `action`*, `path?`, `uid?`, `on?`, `search?` | `list_presets` (disk **plus a recursive scan of every package**'s `Custom/Clothing`), `list_items` (worn items, uid/name/active), `load_preset` (`path`), `add_item` (`path`: activates an existing library item when possible, otherwise waits for VaM to confirm it is worn and returns `ok:false` + hint when nothing could be added), `remove_item`, `set_item_on` (`on`), `open_tab`. Also `list_item_morphs` / `set_item_morph` for garment morphs. The item actions match by an item uid passed as `id` (take it from `list_items`; the schema's `uid` field is not read), and removal is deferred to the end of the frame |
| `hair_action` | `person?`, `action`*, `path?`, `uid?`, `on?`, `search?` | Hair (`Custom/Hair`): `list_presets`, `list_items`, `load_preset`, `add_item` (matches an installed hair item by uid == path, then `dynamicRuntimeLoadPath`, then unique display name, waits for the hair state to settle and toggles it with `SetActiveHairItem`), `remove_item`, `set_item_on` (`on`, same `id` matching as clothing) |

## Pose / expression

| Tool | Arguments | Notes |
| --- | --- | --- |
| `pose_action` | `person?`, `action`*, `path?`, `name?` | `list` (pose presets under `Saves/Person/Pose`), `load` (`path`; applies to the given person), `save` (`path?`, pose + physics), `savefull` (everything: appearance + pose + physics → `Saves/Person/full/`) |
| `expression_action` | `person?`, `action`*, `name?`, `value?` | `list` (expression morphs, heuristic filter), `set` (`name`, `value` defaults to 1; clears the other expressions first) |

## Controls

| Tool | Arguments | Notes |
| --- | --- | --- |
| `list_controls` | `person?` | FreeControllerV3 control points (headControl / chestControl / lHandControl / …) |
| `get_control` | `person?`, `control`* | Control point world position / euler angles |
| `set_control` | `person?`, `control`*, `position?`, `rotation?` | Move / rotate a control point (the state must be `On` — see `control_pose`) |
| `set_control_state` | `person?`, `control`*, `positionState?`, `rotationState?` | Set a control's `positionState`/`rotationState`: On/Off/Follow/Hold/Lock/ParentLink/PhysicsLink/Comply (rotationState also LookAt) |
| `control_pose` | `person?`, `control`*, `position?`, `rotation?` | One call: forces both states to `On`, then sets position and rotation |
| `person_teleport` | `person?`, `position`*, `rotation?`, `reset?` | Move the whole figure instantly: collision params captured and switched off, hipControl written without force (no tweening), choosers and collision values restored, optional core ResetSimulation (`reset`, default true, ~10 s) so cloth/soft bodies re-derive |
| `person_point_at` | `person?`, `target?`, `targetControl?`, `hand?`, `up?` | Rotate `lHandControl`/`rHandControl` (states forced On) so they aim at `target` `[x,y,z]` or at the atom named by `targetControl`, via LookRotation. Position is untouched — combine with `set_control` to extend the arm. `hand` is Left, Right or Both (default) |
| `set_gaze` | `person?`, `amount?`, `targetControl?` | Drive the native `eyeTargetControl`: `"Camera"`/atom uid = look at it (position state forced On), `"None"` = Off (pose-driven). `targetControl` is required in practice even though the schema marks it optional. VaM's eyes follow the target fully, so `amount` values below 1 are ignored (a note says so in the response); a stock Person has no `lookAt` storable |

## Generic parameters (the long tail)

| Tool | Arguments | Notes |
| --- | --- | --- |
| `list_atom_storables` | `uid`* | Every storable on an atom, with its type |
| `list_storable_params` | `uid`*, `storable`* | Parameter introspection: floats (with min/max/default) / bools / strings / urls / choosers / colors / actions / customParams / properties. Url params include the custom texture slots (`customTexture_MainTex`, `customTexture_AlphaTex`, …) |
| `get_param` | `uid`*, `storable`*, `param`* | Read one value (float/bool/string/url/chooser/color, falls back to public properties) |
| `set_param` | `uid`*, `storable`*, `param`*, `value`*, `type?`, `reload?` | Write a parameter (colors accept `#RRGGBB` or `r,g,b,a`; url params set file/image urls and reload them unless `reload=false`; unknown param + numeric value falls back to float, otherwise a clear error) |
| `call_action` | `uid`*, `storable`*, `action`* | Trigger a storable action (a button in the UI) |

`type` can force the interpretation: `float|bool|string|url|chooser|color|property`
(`property` reads/writes a public property through reflection, covering customParamNames such as `gravityX`).

## Camera

| Tool | Arguments | Notes |
| --- | --- | --- |
| `camera_action` | `action`*, `camera?`, `editor?`, `position?`, `rotation?`, `uid?`, `target_uid?`, `person?`, `control?`, `mode?`, `fov?`, `width?`, `height?`, `path?`, `return_image?` | Camera `get` / `set` / `aim` / `capture` / `mcp_create` / `mcp_get` / `mcp_set` / `mcp_capture` |

Camera details:

- By default every action drives VaMMCP's **own** camera (uid `mcp_camera`), so nothing ever moves
  the user's editor view. Pass `camera="editor"` (or `editor=true`) to read/move/aim the
  editor/monitor camera instead; `mcp_create`/`mcp_get`/`mcp_set`/`mcp_capture` are explicit
  aliases for the own camera.
- `aim` looks at an atom `uid` (or `target_uid`), or a `person` plus its `control` (default
  `hipControl`), and can move the camera first with `position`. Prefer it to hand-computed yaw:
  Unity yaw-180 views are mirrored (screen-right = -X).
- Capture modes: `mcp` (default — the own camera rendered through the normal frame loop),
  `editor` (the monitor camera rendered through the frame loop, honours `width`/`height`) and
  `window` (back-buffer grab of the presented frame, which automatically falls back to a screen
  grab; `width`/`height` are then the window size). Default output path
  `Saves/PluginData/vam-mcp/preview.png`.
- Every capture path restores the camera state afterwards (target texture, enabled flag, temporary
  RenderTexture) and reports which path produced the image in a `via` field: `mcp-render`,
  `monitor-render`, `backbuffer`, `screen` or `none`.
- With `return_image=true` the PNG also comes back as an MCP image block — keep the resolution
  modest (e.g. 640×360); anything above 4 MB stays on disk only.

## Simulation

| Tool | Arguments | Notes |
| --- | --- | --- |
| `simulation_action` | `action`*, `paused?`, `timeScale?` | `set` (`paused` and/or `timeScale`), `reset` (core ResetSimulation; settles soft-body and cloth jitter) |

## Animation

| Tool | Arguments | Notes |
| --- | --- | --- |
| `animation_action` | `action`*, `pattern_uid?`, `step_uid?`, `name?`, `time?` | `list_patterns`, `list_steps` (`pattern_uid`), `create_pattern` (AnimationPattern atom), `create_step` (`pattern_uid`, AnimationStep with `transitionToTime`; linking to the pattern happens via VaM's auto-link action when it exists), `record` (start/stop scene recording; errors helpfully because VaM's built-in controller recording is UI-only), `pattern` / `step` (pattern or step action, e.g. Play/Stop) |

## Triggers

| Tool | Arguments | Notes |
| --- | --- | --- |
| `trigger_action` | `action`*, `uid?`, `name?` | `list` (trigger-named storables with their params/actions), `create` (a UIButton trigger atom; pass `uid` to name it — `name` is not used by create), or any trigger action: `Fire` pulses `trigger.active` true→false exactly like VaM's button handler (works with no receiver actions configured), `SetFloatParam`/`SetBoolParam` write `param`, any other name calls that action. `uid` accepts an atom uid or `atom/Trigger`; the default storable is the exact-id `Trigger` |

## Physics

| Tool | Arguments | Notes |
| --- | --- | --- |
| `physics_action` | `action`*, `uid?`, `name?` | `list_objects` (rigidbodies on atoms), `create_joint` (legacy Unity joint between two atoms), or a physics-object action: `SetMass`, `SetDrag`, `AddForce` (impulse magnitude, default +Y; the sim must be awake — IdleSaver freezing stops motion). A rigidbody is added to the atom's mainController when missing |

## Lights / audio

| Tool | Arguments | Notes |
| --- | --- | --- |
| `light_action` | `uid?`, `action`*, `color?`, `value?` | `list` (lights with a parameter snapshot) or a light action: `Toggle`, `SetColor` (`#RRGGBB`), `SetIntensity`, `SetRange`, `SetSpotAngle`, `SetType` (`value` carries the number or the type word) |
| `audio_action` | `uid?`, `action`*, `name?`, `value?` | `list_sources` (`playingClip`, `playingClipReady`), `list_clips` (embedded + URL clips with categories and `ready`), or a source action: `SetClip`, `Play`, `PlayLoop`, `Pause`, `UnPause`, `TogglePause`, `Stop`, `StopLoop`, `SetVolume`, `SetLoop`, `ClearQueue`. `SetClip` decodes local PCM WAVs in-plugin; ogg/mp3 go through VaM's URL clip queue |

## User preferences

| Tool | Arguments | Notes |
| --- | --- | --- |
| `get_user_preferences` | `category?` | Current UserPreferences (graphics, performance, VR, navigation, security, …) |
| `set_user_preferences` | `key`*, `value`* | Write one preference (e.g. `renderScale`, `vrMirrorMode`, `autoSaveInterval`) and save it |

## Scene browser / templates

| Tool | Arguments | Notes |
| --- | --- | --- |
| `scene_browser` | `action`*, `path?` | `List`, `Load`, `New`, `Save`, `Delete` (`path`; **delete** needs `confirm: true` — scene files are not recoverable) |
| `template_browser` | `action`*, `category?` | `List` (counts per template category), `Load` (`category`: look, pose, scene, clothing or hair) |

## Atom UI

| Tool | Arguments | Notes |
| --- | --- | --- |
| `atom_action` | `uid`*, `action`*, `target_uid?`, `value?` | Control: `Select` (SuperController.SelectController on the main controller), `FreezePhysics`/`UnfreezePhysics` (controller Position/Rotation state Freeze↔Off; `value=false` on FreezePhysics unfreezes), `DisableCollision`/`EnableCollision`, `DisableRender`/`EnableRender` (mapped to the atom power toggle), `EndIsolate`, `LoadUserDefaults` (mainPresetControl.LoadUserDefaults — applies only if a *user-saved defaults preset* exists, otherwise a no-op with `loaded:true`). Move: `SetPosition`/`SetRotation`/`SetScale` (`value` is the vector), `SnapToGround`, `AlignTo` (`target_uid`), `ResetTransform`, `ParentTo` (`value` = parent atom uid, e.g. a SubScene), `Unparent`, plus `WheelCreate` (`value` = `pivotUid|atomUid;atomUid;…`), `WheelAngle` and `WheelStatus` for rigid group rotation |
| `atom_geometry_choices` | `uid`*, `action`*, `choice?` | Geometry choices on atoms that have them (CustomUnityAsset): `List` (choosers + choices), `Select` (`choice`) |

## Person animation

| Tool | Arguments | Notes |
| --- | --- | --- |
| `person_animation` | `person?`, `action`*, `value?` | Applies to every `…Animation` storable on the person: `ArmRecord`/`RecordAnimation` (record action or bool param), `PlaybackEnabled` (`value` true/false), `PlayAnimation`, `StopAnimation` |

## Custom Unity assets

| Tool | Arguments | Notes |
| --- | --- | --- |
| `cua_action` | `uid?`, `action`*, `name?` | `list` (CUA atoms with assetUrl/assetName/loaded) or `SetAssetUrl`, `SetAssetName`, `LoadAsset`, `UnloadAsset`, `ReloadAsset` on the atom's asset storable |

## Package manager

| Tool | Arguments | Notes |
| --- | --- | --- |
| `package_manager` | `action`*, `package?` | `ScanPackages`/`RefreshPackages` (first scanning API VaM exposes), `ListEnabled`/`list`, `DeletePackage` (`package`; needs `confirm: true` — .var files are not recoverable). Per-package `EnablePackage`/`DisablePackage` is UI-only in VaM and returns an explanation |

## Trigger UI

| Tool | Arguments | Notes |
| --- | --- | --- |
| `trigger_ui` | `trigger_uid`*, `action`* | `Show`, `Hide` or `Refresh` the Trigger panel by calling the matching Trigger-storable action |

## Hand control / material

| Tool | Arguments | Notes |
| --- | --- | --- |
| `person_hand_control` | `person?`, `hand`*, `action`*, `target_uid?`, `value?` | Hand control point (`hand` is left/right): `SetTarget` (atom uid or `"Camera"`) sets the hand's `lookAt` and switches rotationState to `LookAt`; `SetRotation` (euler array in `value`) uses `SetRotationNoForce`; `Release` returns rotationState to Off. A stock FreeControllerV3 has no IK-weight param, so `SetWeight`/`AutoGrasp` error with that explanation |
| `person_material` | `person?`, `material`*, `action`*, `param?`, `value?` | Material storables by name or substring (skin, genitals, eyes, mouth, nails, …): `ListParams`, `GetParam`, `SetParam` (`param`, `value`) — a thin wrapper over the generic param tools |

## Scene state / inspection

| Tool | Arguments | Notes |
| --- | --- | --- |
| `scene_state` | `sections?`, `uid?`, `detail?`, `query?`, `type?`, `fields?`, `limit?`, `offset?`, `precision?` | Scene overview: counts, per-type census, per-person state (`controls` = `[On, Off, other]`, `sims` = `on/total`, worn/hair counts, torn flag), and `warnings`. `sections` adds detail without paying for it elsewhere: `atoms`, `health`, `plugins`, `physics`, `cameras`, `performance`, `animation`, `controls`, or `all`. Pass `uid=<uid>` for a **dossier** of one atom instead: its storables with parameter counts (`f`/`b`/`c`/`s`), control ids with states and positions, sims, plugin instances and asset `urls`. `detail=full` turns the heavier sections on; the old section names (`persons`, `plugins`, `physics`, `diagnostics`) still work |
| `atom_tree` | `root_uid?`, `max_depth?`, `query?`, `components?`, `limit?`, `atoms_limit?`, `detail?`, `precision?` | Hierarchy as **nested `children` arrays** (`name`, plus `pos` at the root), not a flat list. Components are opt-in (`components=true`) because a per-node component string was the single biggest source of useless output. `query` keeps only subtrees containing a name; `limit` caps children per node, `atoms_limit` caps atoms, `max_depth` 1..8 (default 2); `detail=summary` = depth 1 without components |
| `person_state` | `person?`, `sections?`, `detail?`, `morph_limit?`, `item_limit?`, `include_hidden?`, `precision?` | One person by section: `character`, `morphs`, `clothing`, `hair`, `controls`, `control_rows`, `sims`, `expressions` (or `all`). Lists are capped (`morph_limit` default 10 / full 40, `item_limit` default 20 / full 200) and the response always carries the true counts plus a `morphsTruncated`/`truncated` remainder, so a capped list never looks complete. Control rows (id, position state, position) are opt-in |
| `physics_state` | `detail?`, `limit?` | Simulation timing (time scale, fixed delta, fps) plus a counted summary: physics objects, physics storables, sims on. The per-atom list is opt-in with `detail=objects` (or a `limit`) because it dominated the response |
| `simulation_performance` | — | Frame counters and deltas, timeScale, vSync, GC heap and atom count |
| `find_atoms` | `name?`, `type?`, `component?`, `property?`, `value?`, `tag?`, `detail?`, `fields?`, `exclude?`, `limit?`, `offset?`, `sort?`, `precision?` | Conjunctive search by uid/name substring, atom type, component type name, or a parameter name (with optional `value`). The result is a projection (`detail`/`fields`) with `total`/`returned`/`hasMore`; `limit` defaults to 50. `tag` is unsupported by VaM's runtime and errors with a hint |
| `storable_diff` | `uid_a`*, `storable_a`*, `uid_b`*, `storable_b`* , `param?`, `limit?` | Diff two storables: same/different counts, changed params and params only present on one side. `param` keeps only matching parameter names and `limit` caps the rows (default 100) with a `truncated` count |
| `scene_dependencies` | — | Scan url params for package paths and flag references whose .var is not installed and whose file does not exist |
| `performance_profile` | `duration_ms?` | Short profile (clamped 250–30000 ms): frames, avg/min/max frame time, FPS estimate, stall time, GC heap. Runs on the HTTP worker thread and polls the live frame tick instead of blocking the main-thread dispatcher |

## Hub (community content)

| Tool | Arguments | Notes |
| --- | --- | --- |
| `hub_action` | `action`*, `search?`, `package?`, `resource_id?`, `page?`, `perpage?`, `sort?`, `type?` | `browse` (`search`/`page`/`perpage`/`sort`/`type`; id/title/author/version/type/downloads/rating/tags), `detail` (`package` or `resource_id`; every version's .var + dependencies), `download` (`package`) through VaM's own downloader (handles auth and dependencies; free content works anonymously, paid content needs you signed in inside VaM). Long timeout, polls the frame loop |

## Skin

| Tool | Arguments | Notes |
| --- | --- | --- |
| `set_skin_sss` | `person?`, `color`* | Set the skin subsurface-scattering tint (the skin material's `Subsurface Color` / `_SubdermisColor`); a pink tint such as `#FFC9C9` reads as fairer skin |

## Plugin management (native VaM plugins)

| Tool | Arguments | Notes |
| --- | --- | --- |
| `plugin_action` | `action`*, `path?`, `uid?` | `list` (`uid?` — empty means CoreControl session plugins; pass a person uid for that person's plugins), `add` (`path`: load a .cs/.cslist/.dll plugin and wait for compilation; once loaded it is a storable, so `list_storable_params` / `set_param` configure it — storable ids look like `plugin#0_MVRPlugin.ClassName`), `remove` (`uid`: unload a plugin) |

## Escape hatch

### eval_cs (disabled by default, enable with `Security.AllowEval=true`)
- No `;` → expression mode, the value of the expression is returned
- Contains `;` → statement mode (use an explicit `return` when you want a value)
- `sc` is bound to `SuperController.singleton`
- Restricted by VaM's runtime sandbox: System.IO / System.Reflection / System.AppDomain /
  UnityEditor / Mono.Cecil are blocked (note that `GetType().Name` pulls in System.Reflection —
  use `ToString()` instead)
