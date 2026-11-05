using System.Collections.Generic;
using SimpleJSON;
using VaMMCP.Api;

namespace VaMMCP.Mcp {
	/// <summary>Builds the complete MCP tool surface.</summary>
	public static class ToolRegistry {
		public static List<Tool> CreateAll(VaMApi api, bool allowEval) {
			List<Tool> tools = new List<Tool>();

			// ---------- status / session ----------
			tools.Add(new Tool("status", "VaM runtime status: plugin version, VaM version, atom/person counts, endpoint info.")
				.Fn(a => api.Status()));

			// ---------- scene ----------
			tools.Add(new Tool("list_scenes", "List scene files under Saves/scene. Returns path + name for each scene.")
				.P("search", "string", "Optional substring filter", false)
				.Fn(api.ListScenes));
			tools.Add(new Tool("load_scene", "Load a scene. With merge=true the scene is merged into the current one. The load happens over the next few seconds.")
				.P("path", "string", "Scene path from list_scenes", true)
				.P("merge", "boolean", "Merge instead of replace (default false)", false)
				.Fn(api.LoadScene));
			tools.Add(new Tool("new_scene", "Start a new empty scene. Warning: discards the current scene.")
				.Fn(a => api.NewScene()));

			// Loader diagnostics: the private atoms Dictionary vs atomsList vs
			// startingAtoms counts and every entry that is dead / duplicated /
			// not resolvable. A List.Count > Dictionary.Count aborts VaM's load
			// with "Destination array was not long enough" and wedges it.
			tools.Add(new Tool("atom_info", "Loader diagnostics: atoms dict/list/startingAtoms counts and anomalies (dead, duplicate uid, missing from dict).")
				.Fn(a => api.AtomInfo(a)));
			tools.Add(new Tool("loader_heal", "Repair the loader-wedge state without restarting VaM: rebuild atomsList from the atoms dict, prune dead startingAtoms entries, clear stuck _isLoading.")
				.P("clearLoading", "boolean", "Clear the stuck _isLoading flag (default true)", false)
				.Fn(a => api.LoaderHeal(a)));
			tools.Add(new Tool("save_scene", "Save the current scene to Saves/scene.")
				.P("path", "string", "Optional save path (default Saves/scene/VaMMCP_<timestamp>.json)", false)
				.Fn(api.SaveScene));

			tools.Add(new Tool("press_key", "Send a real key press to VaM (Escape dismisses modal prompts such as the save-screenshot dialog). Never steals focus: the key is only sent while VaM already has keyboard focus, otherwise it refuses (focus=true opts in to raising the VaM window first).")
				.P("key", "string", "Key name: Escape, Return, Space, Tab, Backspace, Delete, Home, End, PageUp, PageDown, Up/Down/Left/Right, F1-F12, a-z, 0-9", true)
				.P("times", "number", "How many times to press (default 1)", false)
				.P("focus", "boolean", "Raise the VaM window before sending (default false - no focus stealing)", false)
				.Fn(api.PressKey));

			tools.Add(new Tool("ui_hud", "Hide or show VaM's main HUD (launcher panels: Hub, scene browser, Create). Use action=hide to look through to the scene view for captures while VaM stays unfocused; action=show to bring the panels back.")
				.P("action", "string", "hide or show", true)
				.Fn(api.UiHud));

			// ---------- atoms ----------
			tools.Add(new Tool("list_atom_types", "List all atom types VaM can create, grouped by category.")
				.Fn(a => api.ListAtomTypes()));
			tools.Add(new Tool("list_atoms", "List atoms in the current scene with uid, type, on/off, position, rotation.")
				.P("type", "string", "Optional atom type filter, e.g. Person", false)
				.Fn(api.ListAtoms));
			tools.Add(new Tool("list_objects", "List all objects (atoms) in the scene with uid, type, on/off, position, rotation, scale and bounding box (min/max/center/size in world space from renderers).")
				.P("type", "string", "Optional atom type filter, e.g. Person", false)
				.P("bbox", "boolean", "Include bounding box calculation (default true)", false)
				.Fn(api.ListObjects));
			tools.Add(new Tool("add_atom", "Create an atom of the given type (Person, Light, AnimationPattern, ...). Waits until it exists.")
				.P("type", "string", "Atom type (see list_atom_types)", true)
				.P("uid", "string", "Optional uid; auto-generated when omitted", false)
				.Timeout(60000).Poller()
				.Fn(api.AddAtom));
			tools.Add(new Tool("remove_atom", "Remove an atom from the scene.")
				.P("uid", "string", "Atom uid", true)
				.Fn(api.RemoveAtom));
			tools.Add(new Tool("set_atom_on", "Show (on=true) or hide (on=false) an atom.")
				.P("uid", "string", "Atom uid", true)
				.P("on", "boolean", "true to show, false to hide", true)
				.Fn(api.SetAtomOn));
			tools.Add(new Tool("get_atom_transform", "Get an atom's world position, rotation and scale.")
				.P("uid", "string", "Atom uid", true)
				.Fn(api.GetAtomTransform));
			tools.Add(new Tool("set_atom_transform", "Move/rotate/scale an atom. With relative=true the values are added to the current transform.")
				.P("uid", "string", "Atom uid", true)
				.P("position", "array", "[x, y, z] world position", false)
				.P("rotation", "array", "[x, y, z] euler angles", false)
				.P("scale", "array", "[x, y, z] local scale", false)
				.P("relative", "boolean", "Apply relative to current transform (default false)", false)
				.Fn(api.SetAtomTransform));

			// ---------- persons ----------
			tools.Add(new Tool("add_person", "Add a Person atom to the scene. Waits until it exists.")
				.P("uid", "string", "Optional uid (default auto)", false)
				.Timeout(90000).Poller()
				.Fn(api.AddPerson));
			tools.Add(new Tool("list_persons", "List Person atoms: uid, character, gender, on/off, position, worn clothing (worn/wornCount - include to spot a swapped person still wearing its source outfit).")
				.Fn(a => api.ListPersons()));
			tools.Add(new Tool("list_looks", "List appearance presets (looks) on disk.")
				.P("search", "string", "Optional substring filter", false)
				.Fn(api.ListLooks));
			tools.Add(new Tool("load_look", "Apply an appearance preset (look) to a person. Replaces their character, morphs and skin.")
				.P("path", "string", "Look path from list_looks", true)
				.P("person", "string", "Person uid (default: first Person)", false)
				.Fn(api.LoadLook));
			tools.Add(new Tool("save_look", "Save the current appearance of a person as a look preset.")
				.P("path", "string", "Optional save path (default Saves/Person/Appearance/<uid>_<timestamp>.json)", false)
				.P("person", "string", "Person uid (default: first Person)", false)
				.Fn(api.SaveLook));
			tools.Add(new Tool("set_character", "Switch the base character mesh of a person (e.g. 'Female 3'). List available characters with get_param uid=<person> storable=geometry param=characterSelection. Loads asynchronously.")
				.P("person", "string", "Person uid (default: first Person)", false)
				.P("name", "string", "Character name", true)
				.Fn(api.SetCharacter));

// ---------- morphs (捏人) ----------
			tools.Add(new Tool("morph_action", "Morphs: list/search, set, reset, get.")
				.P("person", "string", "Person uid (default: first Person)", false)
				.P("action", "string", "Action: list, set, reset, get", true)
				.P("name", "string", "Morph name/uid (for set/get)", false)
				.P("value", "number", "Morph value (for set)", false)
				.P("region", "string", "Region filter (for list/reset)", false)
				.P("search", "string", "Search filter (for list)", false)
				.P("group", "string", "Group filter (for list)", false)
				.P("limit", "number", "Max results (for list)", false)
				.Fn(api.MorphAction));
// ---------- clothing / hair ----------
			tools.Add(new Tool("clothing_action", "Clothing: list_presets, list_items, load_preset, add_item, remove_item, set_item_on, open_tab.")
				.P("person", "string", "Person uid (default: first Person)", false)
				.P("action", "string", "Action", true)
				.P("path", "string", "Preset/item path", false)
				.P("uid", "string", "Item uid", false)
				.P("on", "boolean", "On/off for set_item_on", false)
				.P("search", "string", "Search filter", false)
				.Fn(api.ClothingAction));
			tools.Add(new Tool("hair_action", "Hair: list_presets, list_items, load_preset, add_item, remove_item, set_item_on.")
				.P("person", "string", "Person uid (default: first Person)", false)
				.P("action", "string", "Action", true)
				.P("path", "string", "Preset/item path", false)
				.P("uid", "string", "Item uid", false)
				.P("on", "boolean", "On/off for set_item_on", false)
				.P("search", "string", "Search filter", false)
				.Fn(api.HairAction));
// ---------- poses / expressions ----------
			tools.Add(new Tool("pose_action", "Poses: list, load, save, save_full.")
				.P("person", "string", "Person uid (default: first Person)", false)
				.P("action", "string", "Action: list, load, save, savefull", true)
				.P("path", "string", "Pose path", false)
				.P("name", "string", "Pose name", false)
				.Fn(api.PoseAction));
			tools.Add(new Tool("expression_action", "Expressions: list, set.")
				.P("person", "string", "Person uid (default: first Person)", false)
				.P("action", "string", "Action: list, set", true)
				.P("name", "string", "Expression name", false)
				.P("value", "number", "Expression value", false)
				.Fn(api.ExpressionAction));
			// ---------- controls ----------
			tools.Add(new Tool("list_controls", "List FreeControllerV3 controls of a person (headControl, chestControl, lHandControl, ...).")
				.P("person", "string", "Person uid (default: first Person)", false)
				.Fn(api.ListControls));
			tools.Add(new Tool("get_control", "Read a person control's world position and rotation.")
				.P("person", "string", "Person uid (default: first Person)", false)
				.P("control", "string", "Control id from list_controls", true)
				.Fn(api.GetControl));
			tools.Add(new Tool("set_control", "Move/rotate a person control (e.g. position a hand, tilt the head).")
				.P("person", "string", "Person uid (default: first Person)", false)
				.P("control", "string", "Control id from list_controls", true)
				.P("position", "array", "[x, y, z] world position", false)
				.P("rotation", "array", "[x, y, z] euler angles", false)
				.Fn(api.SetControl));
			tools.Add(new Tool("set_control_state", "Set a control's positionState/rotationState (On/Off/Follow/Hold/Lock/ParentLink/PhysicsLink/Comply). State must be On to pose the control.")
				.P("person", "string", "Person uid (default: first Person)", false)
				.P("control", "string", "Control id from list_controls", true)
				.P("positionState", "string", "On/Off/Follow/Hold/Lock/ParentLink/PhysicsLink/Comply", false)
				.P("rotationState", "string", "On/Off/Follow/Hold/Lock/LookAt/ParentLink/PhysicsLink/Comply", false)
				.Fn(api.SetControlState));
			tools.Add(new Tool("control_pose", "Pose a control: sets positionState/rotationState to On, then sets position and rotation in one call.")
				.P("person", "string", "Person uid (default: first Person)", false)
				.P("control", "string", "Control id from list_controls", true)
				.P("position", "array", "[x, y, z] world position", false)
				.P("rotation", "array", "[x, y, z] euler angles", false)
				.Fn(api.ControlPose));
			tools.Add(new Tool("person_teleport", "Teleport a person's whole figure instantly: person collision params captured+off, hipControl written without force (no tweening), previous state choosers restored, collision values restored, optional core ResetSimulation so cloth/soft bodies re-derive from the new spot.")
				.P("person", "string", "Person uid (default: first Person)", false)
				.P("position", "array", "[x, y, z] world position for hipControl", true)
				.P("rotation", "array", "[x, y, z] euler angles (omit to keep current)", false)
				.P("reset", "boolean", "reset simulation after the jump (default true; needs ~10 s)", false)
				.Fn(api.PersonTeleport));
			tools.Add(new Tool("person_point_at", "Point a person's hand control(s) at a target: rotates lHandControl/rHandControl (positionState/rotationState On) so they aim at the target via LookRotation. Position is untouched - combine with set_control to extend the arm.")
				.P("person", "string", "Person uid (default: first Person)", false)
				.P("target", "array", "[x, y, z] world position to point at", false)
				.P("targetControl", "string", "Atom uid whose world position is the aim target", false)
				.P("hand", "string", "Which hand: Left, Right, or Both (default Both)", false)
				.P("up", "array", "[x, y, z] up vector for aim orientation (default world up)", false)
				.Fn(api.PersonPointAt));
			tools.Add(new Tool("set_gaze", "Adjust a person's gaze via the lookAt storable (best effort; inspect with list_storable_params storable=lookAt for the full param list).")
				.P("person", "string", "Person uid (default: first Person)", false)
				.P("amount", "number", "gazeAmount 0..1 (optional)", false)
				.P("targetControl", "string", "Target control/atom for the eyes (e.g. None, Camera)", false)
				.Fn(api.SetGaze));

			// ---------- generic param access (long tail) ----------
			tools.Add(new Tool("list_atom_storables", "List every storable (sub-component with params) of an atom.")
				.P("uid", "string", "Atom uid", true)
				.Fn(api.ListAtomStorables));
			tools.Add(new Tool("list_storable_params", "Introspect a storable: all float/bool/string/url/chooser/color params (with ranges, choices) and actions. Url params include custom texture slots (e.g. customTexture_MainTex, customTexture_AlphaTex). This is how an agent can control ANY VaM slider.")
				.P("uid", "string", "Atom uid", true)
				.P("storable", "string", "Storable id from list_atom_storables", true)
				.Fn(api.ListStorableParams));
			tools.Add(new Tool("get_param", "Read a single param value (float|bool|string|url|chooser|color).")
				.P("uid", "string", "Atom uid", true)
				.P("storable", "string", "Storable id", true)
				.P("param", "string", "Param name", true)
				.Fn(api.GetParam));
			tools.Add(new Tool("set_param", "Set a single param value. Type is auto-detected; pass type to force (float|bool|string|url|chooser|color|property). 'url' sets a file/image url and reloads it (pass reload=false to skip); 'property' writes a public property/field of the storable (covers custom params like gravityX). Colors accept #RRGGBB or r,g,b,a.")
				.P("uid", "string", "Atom uid", true)
				.P("storable", "string", "Storable id", true)
				.P("param", "string", "Param name", true)
				.P("value", "any", "Value to set", true)
				.P("type", "string", "Optional type override", false)
				.P("reload", "boolean", "For url params: reload the file after setting (default true)", false)
				.Fn(api.SetParam));
			tools.Add(new Tool("call_action", "Invoke an action button of a storable (e.g. Reset, Stop, Play).")
				.P("uid", "string", "Atom uid", true)
				.P("storable", "string", "Storable id", true)
				.P("action", "string", "Action name from list_storable_params", true)
				.Fn(api.CallAction));

// ---------- camera ----------
			tools.Add(new Tool("camera_action", "Camera: get, set, aim, capture. Uses VaMMCP's OWN camera by default so nothing ever moves the user's editor view; pass camera=\"editor\" (or editor=true) to drive the editor/monitor camera instead. capture modes: mcp (default), editor, window.")
				.P("action", "string", "Action", true)
				.P("camera", "string", "mcp (default, own camera) or editor (user's monitor camera)", false)
				.P("editor", "boolean", "Shorthand for camera=\"editor\"", false)
				.P("position", "array", "Position [x,y,z]", false)
				.P("rotation", "array", "Rotation [x,y,z]", false)
				.P("uid", "string", "Aim target atom uid (capture can aim too)", false)
				.P("target_uid", "string", "Alias of uid", false)
				.P("person", "string", "Aim target person uid", false)
				.P("control", "string", "Person control for the aim target (default hipControl)", false)
				.P("mode", "string", "Capture mode: mcp (default), editor, window", false)
				.P("fov", "number", "FOV", false)
				.P("width", "number", "Capture width", false)
				.P("height", "number", "Capture height", false)
				.P("path", "string", "Output path", false)
				.P("return_image", "boolean", "Inline the PNG as base64", false)
				.Fn(api.CameraAction));

// ---------- simulation ----------
			tools.Add(new Tool("simulation_action", "Simulation: set, reset.")
				.P("action", "string", "Action: set, reset", true)
				.P("paused", "boolean", "Paused state (for set)", false)
				.P("timeScale", "number", "Time scale (for set)", false)
				.Fn(api.SimulationAction));
// ---------- scene animation / timeline ----------
			tools.Add(new Tool("animation_action", "Animation: list_patterns, list_steps, create_pattern, create_step, record, pattern action, step action.")
				.P("action", "string", "Action", true)
				.P("pattern_uid", "string", "Pattern atom uid", false)
				.P("step_uid", "string", "Step uid", false)
				.P("name", "string", "Name", false)
				.P("time", "number", "Time", false)
				.Fn(api.AnimationAction));
// ---------- triggers ----------
			tools.Add(new Tool("trigger_action", "Triggers: list, action, create.")
				.P("action", "string", "Action: list, create, or trigger action name", true)
				.P("uid", "string", "Trigger uid", false)
				.P("name", "string", "Trigger name", false)
				.Fn(api.TriggerActionAll));
// ---------- physics ----------
			tools.Add(new Tool("physics_action", "Physics: list_objects, action, create_joint.")
				.P("action", "string", "Action", true)
				.P("uid", "string", "Physics object uid", false)
				.P("name", "string", "Name", false)
				.Fn(api.PhysicsAction));
// ---------- scene lighting / audio / misc ----------
			tools.Add(new Tool("light_action", "Lights: list, action (toggle/set color/intensity/range/angle/type).")
				.P("uid", "string", "Light atom uid", false)
				.P("action", "string", "Action", true)
				.P("color", "string", "Color", false)
				.P("value", "number", "Intensity/range/angle", false)
				.Fn(api.LightActionAll));
			tools.Add(new Tool("audio_action", "Audio: list_sources, list_clips, source action.")
				.P("uid", "string", "Audio source uid", false)
				.P("action", "string", "Action", true)
				.P("name", "string", "Clip name", false)
				.P("value", "number", "Volume/pitch", false)
				.Fn(api.AudioAction));
			// ---------- user preferences ----------
			tools.Add(new Tool("get_user_preferences", "Get current UserPreferences (graphics, performance, VR, navigation, security, etc.).")
				.P("category", "string", "Optional category filter", false)
				.Fn(api.GetUserPreferences));
			tools.Add(new Tool("set_user_preferences", "Set UserPreferences value.")
				.P("key", "string", "Preference key (e.g. 'renderScale', 'vrMirrorMode', 'autoSaveInterval')", true)
				.P("value", "any", "Value to set", true)
				.Fn(api.SetUserPreferences));

			// ---------- scene browser / template browser ----------
			tools.Add(new Tool("scene_browser", "Open/control the Scene Browser: List, Load, New, Save, Delete scenes.")
				.P("action", "string", "Action: List, Load, New, Save, Delete, Refresh", true)
				.P("path", "string", "Scene path for Load/Save/Delete", false)
				.Fn(api.SceneBrowser));
			tools.Add(new Tool("template_browser", "Open/control the Template Browser: List, Load templates.")
				.P("action", "string", "Action: List, Load", true)
				.P("category", "string", "Template category filter", false)
				.Fn(api.TemplateBrowser));

			// ---------- atom UI (control, move, geometry, physics) ----------
			tools.Add(new Tool("atom_action", "Atom control/move: Select, FreezePhysics, DisableRender, DisableCollision, EndIsolate, LoadUserDefaults, SetPosition, SetRotation, SetScale, SnapToGround, AlignTo, ResetTransform, ParentTo (value = parent atom uid, e.g. a SubScene), Unparent.")
				.P("uid", "string", "Atom uid", true)
				.P("action", "string", "Action", true)
				.P("target_uid", "string", "Target atom for AlignTo", false)
				.P("value", "any", "Optional value (ParentTo: the parent atom uid)", false)
				.Fn(api.AtomAction));
			tools.Add(new Tool("atom_geometry_choices", "Get/set atom geometry choices (for CustomUnityAsset, etc.).")
				.P("uid", "string", "Atom uid", true)
				.P("action", "string", "Action: List, Select", true)
				.P("choice", "string", "Choice name for Select", false)
				.Fn(api.AtomGeometryChoices));

			// ---------- person animation ----------
			tools.Add(new Tool("person_animation", "Control Person animation recording/playback: ArmRecord, PlaybackEnabled, RecordAnimation, PlayAnimation.")
				.P("person", "string", "Person uid", false)
				.P("action", "string", "Action: ArmRecord, PlaybackEnabled, RecordAnimation, PlayAnimation, StopAnimation", true)
				.P("value", "boolean", "true/false for ArmRecord/PlaybackEnabled", false)
				.Fn(api.PersonAnimation));

// ---------- custom unity assets ----------
			tools.Add(new Tool("cua_action", "CUA: list, action.")
				.P("uid", "string", "CUA uid", false)
				.P("action", "string", "Action", true)
				.P("name", "string", "Name", false)
				.Fn(api.CUAActionAll));
			// ---------- package manager ----------
			tools.Add(new Tool("package_manager", "Local package management: ScanPackages, RefreshPackages, EnablePackage, DisablePackage, DeletePackage.")
				.P("action", "string", "Action: ScanPackages, RefreshPackages, EnablePackage, DisablePackage, DeletePackage, ListEnabled", true)
				.P("package", "string", "Package uid for Enable/Disable/Delete", false)
				.Fn(api.PackageManager));

			// ---------- triggers (extended) ----------
			tools.Add(new Tool("trigger_ui", "Open/control Trigger UI panel for a trigger atom.")
				.P("trigger_uid", "string", "Trigger atom uid", true)
				.P("action", "string", "Action: Show, Hide, Refresh", true)
				.Fn(api.TriggerUI));

			// ---------- person hand control / skin / materials ----------
			tools.Add(new Tool("person_hand_control", "Control hand IK/targeting: SetTarget, SetWeight, SetRotation, AutoGrasp.")
				.P("person", "string", "Person uid", false)
				.P("hand", "string", "left or right", true)
				.P("action", "string", "Action: SetTarget, SetWeight, SetRotation, AutoGrasp, Release", true)
				.P("target_uid", "string", "Target atom uid for SetTarget", false)
				.P("value", "number", "Weight (0-1) or rotation value", false)
				.Fn(api.PersonHandControl));
			tools.Add(new Tool("person_material", "Get/set person material parameters (skin, genitals, eyes, etc.).")
				.P("person", "string", "Person uid", false)
				.P("material", "string", "Material name: skin, genitals, eyes, mouth, nails, etc.", true)
				.P("action", "string", "Action: GetParam, SetParam, ListParams", true)
				.P("param", "string", "Parameter name", false)
				.P("value", "any", "Value for SetParam", false)
				.Fn(api.PersonMaterial));

			// ---------- scene state inspection / debugging ----------
			tools.Add(new Tool("scene_state", "Get comprehensive current scene state: atoms, persons, plugins, physics, simulation, performance.")
				.P("detail", "string", "Detail level: summary, full, physics, plugins, persons", false)
				.Fn(api.SceneState));
			tools.Add(new Tool("atom_tree", "Get hierarchical atom tree with parent/child relationships and transforms.")
				.P("root_uid", "string", "Optional root atom uid (default: all)", false)
				.P("max_depth", "number", "Maximum depth to traverse", false)
				.Fn(api.AtomTree));
			tools.Add(new Tool("person_state", "Get detailed person state: morphs, clothing, hair, controls, physics, expressions.")
				.P("person", "string", "Person uid (default: first)", false)
				.P("include_hidden", "boolean", "Include zero/non-active items", false)
				.Fn(api.PersonState));
			tools.Add(new Tool("physics_state", "Get physics engine state: active rigidbodies, joints, collisions, performance.")
				.Fn(a => api.PhysicsState()));
			tools.Add(new Tool("simulation_performance", "Get simulation performance metrics: frame time, physics time, atom counts, memory.")
				.Fn(a => api.SimulationPerformance()));
			tools.Add(new Tool("find_atoms", "Find atoms by various criteria: name, type, component, property, tag.")
				.P("name", "string", "Name substring filter", false)
				.P("type", "string", "Atom type filter", false)
				.P("component", "string", "Component type name filter", false)
				.P("property", "string", "Property name to check", false)
				.P("value", "any", "Property value to match", false)
				.P("tag", "string", "Tag filter", false)
				.Fn(api.FindAtoms));
			tools.Add(new Tool("storable_diff", "Compare two storables or two atoms' storables and show differences.")
				.P("uid_a", "string", "First atom uid", true)
				.P("storable_a", "string", "First storable id", true)
				.P("uid_b", "string", "Second atom uid", true)
				.P("storable_b", "string", "Second storable id", true)
				.Fn(api.StorableDiff));
			tools.Add(new Tool("scene_dependencies", "Analyze scene dependencies: missing packages, broken references, missing assets.")
				.Fn(a => api.SceneDependencies()));
			tools.Add(new Tool("performance_profile", "Run a short performance profile: frame times, GC, physics, render, scripts. Runs on the HTTP worker thread - it polls Plugin.frameTick live instead of occupying the main-thread dispatcher, which would freeze the loop it observes.")
				.P("duration_ms", "number", "Profile duration in ms (default 5000)", false)
				.Worker()
				.Fn(api.PerformanceProfile));

// ---------- hub / community resources ----------
			tools.Add(new Tool("hub_action", "Hub: browse, detail, download.")
				.P("action", "string", "Action: browse, detail, download", true)
				.P("search", "string", "Search text (browse)", false)
				.P("package", "string", "Package name (detail/download)", false)
				.P("resource_id", "string", "Resource id (detail)", false)
				.P("page", "number", "Page (browse)", false)
				.P("perpage", "number", "Per page (browse)", false)
				.P("sort", "string", "Sort (browse)", false)
				.P("type", "string", "Type filter (browse)", false)
				.Timeout(300000).Poller()
				.Fn(api.HubAction));
			// ---------- skin ----------
			tools.Add(new Tool("set_skin_sss", "Set the skin subsurface-scattering (SSS) tint of a person via the skin material's 'Subsurface Color' (_SubdermisColor).")
				.P("person", "string", "Person uid (default: first Person)", false)
				.P("color", "string", "#RRGGBB color (e.g. #C06060 reddish subsurface tint)", true)
				.Fn(api.SetSkinSss));

// ---------- plugins (VaM native plugin system) ----------
			tools.Add(new Tool("plugin_action", "Plugins: list, add, remove.")
				.P("action", "string", "Action: list, add, remove", true)
				.P("path", "string", "Plugin path (for add)", false)
				.P("uid", "string", "Plugin uid (for remove)", false)
				.Fn(api.PluginAction));
			// ---------- eval escape hatch ----------
			if (allowEval) {
				int evalTimeoutMs = Plugin.cfgEvalTimeoutSec.Value > 0 ? Plugin.cfgEvalTimeoutSec.Value * 1000 : 30000;
				tools.Add(new Tool("eval_cs", "Execute arbitrary C# inside the VaM process. 'sc' is bound to SuperController.singleton. Without ';' the code is treated as an expression and its value is returned; with ';' it runs as statements (use an explicit return if you want a value). VaM's runtime sandbox prohibits System.IO, System.Reflection, System.AppDomain, UnityEditor and Mono.Cecil; avoid GetType().Name (use ToString()). USE AT YOUR OWN RISK.")
					.P("code", "string", "C# expression or statements to evaluate", true)
					.Timeout(evalTimeoutMs)
					.Fn(api.EvalCs));
			}

			return tools;
		}
	}
}
