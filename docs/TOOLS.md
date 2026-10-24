# Tool reference (64 tools)

*[中文版](TOOLS.zh-CN.md)*

Every tool is invoked through `tools/call`: `{ "name": "...", "arguments": { ... } }`.
A `*` marks a required argument, `?` an optional one. When `person` is omitted the first Person
in the scene is used. Vectors are `[x, y, z]`.

`eval_cs` is only registered when `Security.AllowEval=true`, so a default install exposes 62 tools.

## Status

### status
VaM runtime status: plugin version, VaM version, atom/person counts, endpoint, eval switch.

## Scene

| Tool | Arguments | Notes |
| --- | --- | --- |
| `list_scenes` | `search?` | Scenes under `Saves/scene` |
| `load_scene` | `path`*, `merge?` | Load a scene (`merge=true` merges into the current one); .var paths work (`Package.1:/Saves/scene/x.json`) |
| `new_scene` | — | New empty scene (**discards the current one**; an empty scene has no lights — add one or raise ambient light) |
| `save_scene` | `path?` | Save the scene (default `Saves/scene/VaMMCP_<timestamp>.json`; an existing file is removed first so the save is not swallowed by VaM's confirm dialog) |
| `press_key` | `key`*, `times?`, `focus?` | Send a real key press to VaM (Escape dismisses modal prompts such as the save-screenshot dialog); never steals focus - sends only while VaM already has focus, refuses otherwise (`focus=true` opts in to raising it) |

## Atom

| Tool | Arguments | Notes |
| --- | --- | --- |
| `list_atom_types` | — | Creatable atom types, grouped by category (from VaM's runtime data) |
| `list_atoms` | `type?` | Atoms in the scene (uid/type/on/position/rotation) |
| `add_atom` | `type`*, `uid?` | Create an atom and wait until it is ready (up to 90 s; polls by uid) |
| `remove_atom` | `uid`* | Delete an atom |
| `set_atom_on` | `uid`*, `on`* | Show/hide |
| `get_atom_transform` | `uid`* | World position / euler angles / scale |
| `set_atom_transform` | `uid`*, `position?`, `rotation?`, `scale?`, `relative?` | Transform (`relative=true` offsets instead of setting) |

## Person

| Tool | Arguments | Notes |
| --- | --- | --- |
| `add_person` | `uid?` | Add a Person (VaM may rename the uid — trust the return value) |
| `list_persons` | — | Persons in the scene (uid/character/gender/position) |
| `list_looks` | `search?` | Appearance presets (`Saves/Person/Appearance`) |
| `load_look` | `path`*, `person?` | Apply an appearance preset (character/clothing/hair/morph deltas; asynchronous, verify a moment later) |
| `save_look` | `path?`, `person?` | Export an appearance preset (**note**: VaM presets do not carry the full material parameter set — re-apply skin parameters after moving a look between scenes) |
| `set_character` | `person?`, `name`* | Switch the base character mesh (names come from the choices of `get_param storable=geometry param=characterSelection`) |

## Preset export

| Tool | Notes |
| --- | --- |
| `save_look` | Appearance (character/clothing/hair/morphs) → `Saves/Person/Appearance/` |
| `save_pose` | Pose + physics → `Saves/Person/Pose/` |
| `save_full_preset` | Everything (appearance + pose + physics) → `Saves/Person/full/` |

## Morphs

| Tool | Arguments | Notes |
| --- | --- | --- |
| `list_morphs` | `person?`, `search?`, `region?`, `group?`, `limit?` | Search morphs (name/uid/region/group/current value) |
| `set_morph` | `person?`, `name`*, `value`* | Set a morph value (typically -1..1) |
| `get_morph` | `person?`, `name`* | Read a morph value |
| `reset_morphs` | `person?`, `region?` | Zero morphs, optionally only one region |

## Clothing / hair

| Tool | Arguments | Notes |
| --- | --- | --- |
| `list_packages` | — | Installed .var packages |
| `list_clothing_presets` | `search?` | Clothing presets and items: disk **plus a recursive scan of every package**'s `Custom/Clothing` (.vam/.json) |
| `load_clothing_preset` | `path`*, `person?` | Apply a clothing preset |
| `list_clothing_items` | `person?`, `all?` | Clothing actually worn (uid/name/active) plus a `worn` summary; `all=true` also returns the unworn library |
| `add_clothing_item` | `path`*, `person?`, `wait_seconds?` | Add a clothing item (`.vam` package path); activates an existing library item when possible, otherwise blocks until VaM confirms it is worn, and returns `ok:false` + hint when nothing could be added |
| `remove_clothing_item` | `id`*, `person?` | Remove an item (deletion is deferred to the end of the frame; confirm with `list_clothing_items`) |
| `set_clothing_item_on` | `id`*, `on`*, `person?` | Wear / take off |
| `list_hair_presets` / `load_hair_preset` / `list_hair_items` / `add_hair_item` / `remove_hair_item` / `set_hair_item_on` | `add_hair_item`: `path`*, `person?` | Hair (`Custom/Hair`). `add_hair_item` matches an installed hair item by uid == path, then `dynamicRuntimeLoadPath`, then unique display name, waits for the person's hair state to settle, and toggles it with `SetActiveHairItem` (the hair *creator* flow is the hair-author editor and never adds anything) |

## Pose / expression

| Tool | Arguments | Notes |
| --- | --- | --- |
| `list_poses` | `search?` | Pose presets (`Saves/Person/Pose`) |
| `load_pose` | `path`*, `person?`, `all?` | Apply a pose (`all=true` applies it to every person) |
| `save_pose` | `path?`, `person?` | Export a pose |
| `list_expressions` | `person?` | Expression morphs (heuristic filter) |
| `set_expression` | `person?`, `name`*, `value?`, `reset?` | Set an expression (clears the other expressions first by default; `value` defaults to 1) |

## Bone control

| Tool | Arguments | Notes |
| --- | --- | --- |
| `list_controls` | `person?` | FreeControllerV3 control points (headControl / chestControl / …) |
| `get_control` | `person?`, `control`* | Control point world position / euler angles |
| `set_control` | `person?`, `control`*, `position?`, `rotation?` | Move / rotate a control point |
| `set_gaze` | `person?`, `targetControl?` | Drive the native `eyeTargetControl`: `"Camera"`/atom uid = look at it (position/rotation states forced On), `"None"` = Off (pose-driven). There is no `lookAt` storable on a Person |
| `person_hand_control` | `person?`, `hand?` (left/right), `action`*, `target_uid?`/`value?` | Hand control point: `SetTarget` (atom uid or `"Camera"`) sets the hand control's transform `lookAt` + switches its rotationState chooser to `LookAt`; `SetRotation` (euler array) uses `SetRotationNoForce`; `Release` returns rotationState to Off. A stock FreeControllerV3 has no IK-weight param, so `SetWeight`/`AutoGrasp` error with that explanation |

## Generic parameters (the long tail)

| Tool | Arguments | Notes |
| --- | --- | --- |
| `list_atom_storables` | `uid`* | Every storable on an atom, with its type |
 | `list_storable_params` | `uid`*, `storable`* | Parameter introspection: floats (with min/max/default) / bools / strings / urls / choosers / colors / actions / customParams / properties |
| `get_param` | `uid`*, `storable`*, `param`* | Read one value (float/bool/string/url/chooser/color, falls back to public properties) |
| `set_param` | `uid`*, `storable`*, `param`*, `value`*, `type?`, `reload?` | Write a parameter (colors accept `#RRGGBB` or `r,g,b,a`; url params set file/image urls and reload them unless `reload=false`; unknown param + numeric value falls back to float, otherwise a clear error) |
| `call_action` | `uid`*, `storable`*, `action`* | Trigger a storable action (a button in the UI) |

`type` can force the interpretation: `float|bool|string|url|chooser|color|property`
(`property` reads/writes a public property through reflection, covering customParamNames such as `gravityX`).

## Camera

| Tool | Arguments | Notes |
| --- | --- | --- |
| `get_camera` | — | Monitor camera position / rotation / FOV |
| `set_camera` | `position?`, `rotation?`, `fov?` | Move the camera |
| `capture_view` | `width?`, `height?`, `path?`, `return_image?`, `mode?`, `timeout_ms?` | Screenshot (default `Saves/PluginData/vam-mcp/preview.png`). `mode=window` (default) grabs the real back buffer so person skin/clothing render correctly (polls until the next frame; TailSaver-style idle can make it slow). `mode=camera` does the fast RenderTexture re-render of the monitor camera, but person meshes may come out black in it. With `return_image=true` the PNG also comes back as an MCP image block — keep the resolution modest (e.g. 640×360); anything above 4 MB stays on disk only. Note: applying skin/clothing colours is still verified best in the VaM window, captures are indicative |

## Simulation

| Tool | Arguments | Notes |
| --- | --- | --- |
| `set_simulation` | `paused?`, `timeScale?` | Pause / resume / time scale |
| `reset_simulation` | — | Reset the physics simulation (settles soft-body and cloth jitter) |

## Triggers / animation / audio / physics objects

| Tool | Arguments | Notes |
| --- | --- | --- |
| `create_trigger` / `list_triggers` | `create_trigger`: `type?` (default UIButton), `uid?` | VaM trigger atoms; the created atom's `Trigger` storable is the target (see `trigger_action`) |
| `trigger_action` | `trigger_uid`*, `action`*, `storable?`, `param?`, `value?` | `Fire` pulses the trigger exactly like VaM's button handler does (`trigger.active` true→false on `UIButtonTrigger`/`VariableTrigger`; works even with no receiver actions configured). `SetFloatParam`/`SetBoolParam` write `param`; any other name calls that action. Resolves `atom/Trigger` uids; default storable is the exact-id `Trigger` |
| `create_animation_pattern` / `list_animation_patterns` | `uid?` / `search?` | AnimationPattern atoms (+ their step children, subject to VaM's auto-linking) |
| `create_animation_step` | `pattern_uid`*, `uid?`, `duration?` | AnimationStep with `transitionToTime`; linkage to the pattern happens in the VaM UI when no auto-link action exists |
| `list_animation_steps` | `pattern_uid`? | Steps of a pattern (some steps report no linkage param — see note in response) |
| `animation_pattern_action` / `animation_step_action` | `pattern_uid`/`step_uid`*, `action`*, `value?` | Play/Stop/… actions; `Step SetTransition` takes seconds → `transitionToTime` (numeric) or a transition-type chooser word |
| `record_scene_animation` | `pattern_uid`*, `action`* (start/stop) | Errors helpfully: VaM's built-in controller recording is UI-only (no Record action on the pattern) |
| `create_audio_source` / `list_audio_sources` / `audio_source_action` | `audio_source_action`: `uid`*, `action`*, `clip?`/`value?` | Actions: SetClip, Play, PlayLoop, Pause, UnPause, Stop, SetVolume, SetLoop, … . `SetClip` decodes local PCM WAVs in-plugin (16-bit/float32); ogg/mp3 fall back to VaM's URL clip queue, which may stay `ready=false` until the Sound tab is opened. `Play`/`PlayLoop` reuse the last `SetClip`; `list_audio_sources` shows `playingClip` |
| `physics_object_action` | `uid`*, `action`*, `value`/`direction?` | Rigidbody on the atom's mainController GameObject (auto-added when missing): `SetMass`, `SetDrag`, `AddForce` (impulse magnitude, default +Y; the sim must be awake — IdleSaver freezing stops motion) |

## Atom UI helpers

| Tool | Arguments | Notes |
| --- | --- | --- |
| `atom_control` | `uid`*, `action`*, `value?` | `Select` (SuperController.SelectController on the main controller), `FreezePhysics`/`UnfreezePhysics` (controller Position/Rotation state Freeze↔Off; `value=false` on FreezePhysics unfreezes), `DisableCollision`/`EnableCollision`, `DisableRender`/`EnableRender` (mapped to the atom power toggle), `EndIsolate` (EndIsolateEditAtom; entering isolation is UI-only) |
| `atom_move` | `uid`*, `action`*, `value?` | SetPosition/SetRotation/SetScale/SnapToGround/AlignTo/ResetTransform |
| `atom_geometry_choices` | `uid`*, `action`, `choice?` | Atom geometry choices on atoms that have them (CustomUnityAsset) |
| `storable_diff` | `uid_a`*, `storable_a`*, `uid_b`*, `storable_b`* | Diff two storables (sameness counts + changed params) |


## Hub (community content)

| Tool | Arguments | Notes |
| --- | --- | --- |
| `hub_browse` | `search?`, `page?`, `perpage?`, `sort?`, `type?` | Search the VaM Hub (id/title/author/version/type/downloads/rating/tags) |
| `hub_detail` | `package?`, `resource_id?` | Resource detail: package name + every version's .var + dependencies |
| `hub_download` | `package`* | Download and install through VaM's own downloader (handles auth and dependencies; free content works anonymously, paid content needs you signed in inside VaM) |

## Skin

| Tool | Arguments | Notes |
| --- | --- | --- |
| `set_skin_sss` | `person?`, `color`* | Subdermis (subsurface) colour `_SubdermisColor`; a pink tint such as `#FFC9C9` reads as fairer skin |

## Plugin management (native VaM plugins)

| Tool | Arguments | Notes |
| --- | --- | --- |
| `list_plugins` | `uid?` | Loaded plugins (empty uid = CoreControl session plugins; pass a person uid for that person's plugins) |
| `add_plugin` | `path`*, `uid?` | Load a .cs/.cslist/.dll plugin and wait for compilation. Once loaded the plugin is a storable, so `list_storable_params` / `set_param` configure it (storable ids look like `plugin#0_MVRPlugin.ClassName`) |
| `remove_plugin` | `plugin_uid`*, `uid?` | Unload a plugin |

## Escape hatch

### eval_cs (disabled by default, enable with `Security.AllowEval=true`)
- No `;` → expression mode, the value of the expression is returned
- Contains `;` → statement mode (use an explicit `return` when you want a value)
- `sc` is bound to `SuperController.singleton`
- Restricted by VaM's runtime sandbox: System.IO / System.Reflection / System.AppDomain /
  UnityEditor / Mono.Cecil are blocked (note that `GetType().Name` pulls in System.Reflection —
  use `ToString()` instead)
