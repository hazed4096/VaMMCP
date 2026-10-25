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
			tools.Add(new Tool("list_persons", "List Person atoms: uid, character, gender, on/off, position.")
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
			tools.Add(new Tool("list_morphs", "Search body/face morphs of a person. Returns name, uid, region, group and current value. The morphs ARE the character sliders.")
				.P("person", "string", "Person uid (default: first Person)", false)
				.P("search", "string", "Substring filter on morph name or uid", false)
				.P("region", "string", "Exact region filter (e.g. Face, Body)", false)
				.P("group", "string", "Exact group filter", false)
				.P("limit", "number", "Max results (default 500)", false)
				.Fn(api.ListMorphs));
			tools.Add(new Tool("set_morph", "Set a morph value on a person (e.g. breast size, face shape). Typical range -1..1.")
				.P("person", "string", "Person uid (default: first Person)", false)
				.P("name", "string", "Morph display name or uid from list_morphs", true)
				.P("value", "number", "Morph value, typically -1..1", true)
				.Fn(api.SetMorph));
			tools.Add(new Tool("get_morph", "Read a morph value on a person.")
				.P("person", "string", "Person uid (default: first Person)", false)
				.P("name", "string", "Morph display name or uid", true)
				.Fn(api.GetMorph));
			tools.Add(new Tool("reset_morphs", "Reset morphs of a person to 0 (optionally only one region).")
				.P("person", "string", "Person uid (default: first Person)", false)
				.P("region", "string", "Optional region to reset only (e.g. Face)", false)
				.Fn(api.ResetMorphs));

			// ---------- clothing / hair ----------
			tools.Add(new Tool("list_packages", "List installed .var packages (their asset paths are usable in load_clothing_preset / load_hair_preset / add_clothing_item / add_hair_item).")
				.Fn(a => api.ListPackages(a)));
			tools.Add(new Tool("list_clothing_presets", "List clothing presets/items on disk AND inside all .var packages (path may be a package path like 'Package.1:/Custom/Clothing/...').")
				.P("search", "string", "Optional substring filter", false)
				.Fn(api.ListClothingPresets));
			tools.Add(new Tool("load_clothing_preset", "Apply a clothing preset to a person.")
				.P("path", "string", "Preset path from list_clothing_presets", true)
				.P("person", "string", "Person uid (default: first Person)", false)
				.Fn(api.LoadClothingPreset));
			tools.Add(new Tool("list_clothing_items", "List the clothing a person is actually wearing (uid/name/active), plus a 'worn' summary and libraryCount. Pass all=true to dump the whole clothing library, including items that are not worn.")
				.P("person", "string", "Person uid (default: first Person)", false)
				.P("all", "boolean", "true = include unworn library items (default false: worn only)", false)
				.Fn(api.ListClothingItems));
			tools.Add(new Tool("add_clothing_item", "Add a clothing item to a person from its .vam/.json path (package paths work, e.g. from list_clothing_presets). Blocks until VaM confirms the item exists and is worn, and returns ok=false with a hint when the runtime clothing creator is not initialised (open Person > Clothing once in VaM).")
				.P("path", "string", "Clothing item .vam/.json path", true)
				.P("person", "string", "Person uid (default: first Person)", false)
				.P("wait_seconds", "number", "How long to wait for the async load (default 20)", false)
				.Timeout(60000).Poller()
				.Fn(api.AddClothingItem));
			tools.Add(new Tool("open_clothing_tab", "Initialise a person's runtime clothing creator (the equivalent of opening that person's Person > Clothing tab once in VaM). Without this, add_clothing_item silently no-ops because VaM never builds the DAZRuntimeCreator on the creator item. Must be re-run after every scene load.")
				.P("person", "string", "Person uid (default: first Person)", false)
				.P("wait_seconds", "number", "How long to wait for the creator to initialise (default 15)", false)
				.Timeout(60000).Poller()
				.Fn(api.OpenClothingTab));
			tools.Add(new Tool("remove_clothing_item", "Remove a clothing item from a person.")
				.P("id", "string", "Clothing item uid from list_clothing_items", true)
				.P("person", "string", "Person uid (default: first Person)", false)
				.Fn(api.RemoveClothingItem));
			tools.Add(new Tool("set_clothing_item_on", "Show or hide a clothing item.")
				.P("id", "string", "Clothing item uid", true)
				.P("on", "boolean", "true to wear, false to hide", true)
				.P("person", "string", "Person uid (default: first Person)", false)
				.Fn(api.SetClothingItemOn));
			tools.Add(new Tool("list_hair_presets", "List hair presets/items on disk AND inside all .var packages (path may be a package path).")
				.P("search", "string", "Optional substring filter", false)
				.Fn(api.ListHairPresets));
			tools.Add(new Tool("load_hair_preset", "Apply a hair preset to a person.")
				.P("path", "string", "Preset path from list_hair_presets", true)
				.P("person", "string", "Person uid (default: first Person)", false)
				.Fn(api.LoadHairPreset));
			tools.Add(new Tool("list_hair_items", "List hair items currently on a person.")
				.P("person", "string", "Person uid (default: first Person)", false)
				.Fn(api.ListHairItems));
			tools.Add(new Tool("add_hair_item", "Add a hair item to a person from its .vam/.json path (package paths work, e.g. from list_hair_presets). Waits for the character to be ready; loads asynchronously.")
				.P("path", "string", "Hair item .vam/.json path", true)
				.P("person", "string", "Person uid (default: first Person)", false)
				.Timeout(60000).Poller()
				.Fn(api.AddHairItem));
			tools.Add(new Tool("remove_hair_item", "Remove a hair item from a person.")
				.P("id", "string", "Hair item uid from list_hair_items", true)
				.P("person", "string", "Person uid (default: first Person)", false)
				.Fn(api.RemoveHairItem));
			tools.Add(new Tool("set_hair_item_on", "Show or hide a hair item.")
				.P("id", "string", "Hair item uid", true)
				.P("on", "boolean", "true to show, false to hide", true)
				.P("person", "string", "Person uid (default: first Person)", false)
				.Fn(api.SetHairItemOn));

			// ---------- poses / expressions ----------
			tools.Add(new Tool("list_poses", "List pose presets on disk.")
				.P("search", "string", "Optional substring filter", false)
				.Fn(api.ListPoses));
			tools.Add(new Tool("load_pose", "Apply a pose preset. With all=true applies to every Person in the scene.")
				.P("path", "string", "Pose path from list_poses", true)
				.P("person", "string", "Person uid (default: first Person; ignored when all=true)", false)
				.P("all", "boolean", "Apply to all persons (default false)", false)
				.Fn(api.LoadPose));
			tools.Add(new Tool("save_pose", "Save a person's current pose as a pose preset.")
				.P("path", "string", "Optional save path (default Saves/Person/Pose/<uid>_<timestamp>.json)", false)
				.P("person", "string", "Person uid (default: first Person)", false)
				.Fn(api.SavePose));
			tools.Add(new Tool("save_full_preset", "Export a person as a full preset (appearance + pose + physics) — equivalent to VaM's 'Save Full'.")
				.P("path", "string", "Optional save path (default Saves/Person/full/<uid>_<timestamp>.json)", false)
				.P("person", "string", "Person uid (default: first Person)", false)
				.Fn(api.SaveFullPreset));
			tools.Add(new Tool("list_expressions", "List expression-like face morphs of a person with current values.")
				.P("person", "string", "Person uid (default: first Person)", false)
				.Fn(api.ListExpressions));
			tools.Add(new Tool("set_expression", "Set a facial expression: optionally resets all expression morphs first, then sets the named one (default value 1).")
				.P("person", "string", "Person uid (default: first Person)", false)
				.P("name", "string", "Expression morph name from list_expressions", true)
				.P("value", "number", "Morph value (default 1)", false)
				.P("reset", "boolean", "Reset other expression morphs first (default true)", false)
				.Fn(api.SetExpression));

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
				.P("reload", "bool", "For url params: reload the file after setting (default true)", false)
				.Fn(api.SetParam));
			tools.Add(new Tool("call_action", "Invoke an action button of a storable (e.g. Reset, Stop, Play).")
				.P("uid", "string", "Atom uid", true)
				.P("storable", "string", "Storable id", true)
				.P("action", "string", "Action name from list_storable_params", true)
				.Fn(api.CallAction));

			// ---------- camera ----------
			tools.Add(new Tool("get_camera", "Read the monitor camera position, rotation and FOV.")
				.Fn(a => api.GetCamera()));
			tools.Add(new Tool("set_camera", "Move the monitor camera: position, rotation (euler) and/or FOV.")
				.P("position", "array", "[x, y, z] world position", false)
				.P("rotation", "array", "[x, y, z] euler angles", false)
				.P("fov", "number", "Field of view in degrees", false)
				.Fn(api.SetCamera));
			tools.Add(new Tool("capture_view", "Capture what VaM actually renders on screen to a PNG (default Saves/PluginData/vam-mcp/preview.png) and return its path. mode=window (default) grabs the real back buffer, so person skin/clothing render; it completes at end of the next frame, so the tool polls (up to timeout_ms - idle VaM frames can take seconds). mode=camera does the old RenderTexture re-render of the monitor camera only (fast, but person meshes may be missing). Set return_image=true to also get the picture back inline.")
				.P("width", "number", "Image width (default 1280, max 4096; window mode scales the full buffer down to this)", false)
				.P("height", "number", "Image height (default 720, max 4096)", false)
				.P("path", "string", "Optional output path", false)
				.P("mode", "string", "'window' (default, full fidelity) or 'camera' (legacy fast re-render)", false)
				.P("return_image", "boolean", "Return the PNG inline as an MCP image (default false; keep the resolution modest, e.g. 640x360, and note that images above 4 MB stay on disk only)", false)
				.P("timeout_ms", "number", "How long to wait for the next rendered frame in window mode (default 20000)", false)
				.Timeout(60000).Poller()
				.Fn(api.CaptureView));

			// ---------- simulation ----------
			tools.Add(new Tool("set_simulation", "Pause/resume the simulation or set the global time scale.")
				.P("paused", "boolean", "true pauses, false resumes", false)
				.P("timeScale", "number", "Global time scale (1 = normal)", false)
				.Fn(api.SetSimulation));
			tools.Add(new Tool("reset_simulation", "Reset the physics simulation (collapses soft-body / cloth jitter).")
				.Fn(a => api.ResetSimulation()));

			// ---------- scene animation / timeline ----------
			tools.Add(new Tool("list_animation_patterns", "List AnimationPattern atoms in the scene (each manages a timeline of AnimationSteps).")
				.P("search", "string", "Optional uid/name filter", false)
				.Fn(api.ListAnimationPatterns));
			tools.Add(new Tool("list_animation_steps", "List AnimationStep atoms for a given AnimationPattern.")
				.P("pattern_uid", "string", "AnimationPattern atom uid", true)
				.Fn(api.ListAnimationSteps));
			tools.Add(new Tool("create_animation_pattern", "Create a new AnimationPattern atom (timeline container).")
				.P("uid", "string", "Optional uid", false)
				.Timeout(30000).Poller()
				.Fn(api.CreateAnimationPattern));
			tools.Add(new Tool("create_animation_step", "Create an AnimationStep atom and attach it to an AnimationPattern.")
				.P("pattern_uid", "string", "AnimationPattern atom uid", true)
				.P("uid", "string", "Optional uid", false)
				.P("transition_type", "string", "Transition type: Linear, Smooth, etc.", false)
				.P("duration", "number", "Step duration in seconds", false)
				.Timeout(30000).Poller()
				.Fn(api.CreateAnimationStep));
			tools.Add(new Tool("animation_pattern_action", "Control an AnimationPattern: Play, Stop, Pause, Reset, Scrub.")
				.P("pattern_uid", "string", "AnimationPattern atom uid", true)
				.P("action", "string", "Action: Play, Stop, Pause, Reset, ScrubToTime, SetTime", true)
				.P("time", "number", "Time value for ScrubToTime/SetTime", false)
				.Fn(api.AnimationPatternAction));
			tools.Add(new Tool("animation_step_action", "Control an AnimationStep: Play, Stop, SetTransition, SetDuration.")
				.P("step_uid", "string", "AnimationStep atom uid", true)
				.P("action", "string", "Action: Play, Stop, SetTransition, SetDuration", true)
				.P("value", "number", "Value for SetTransition (index) or SetDuration (seconds)", false)
				.Fn(api.AnimationStepAction));
			tools.Add(new Tool("record_scene_animation", "Start/stop recording a scene animation (records atom transforms to AnimationSteps).")
				.P("pattern_uid", "string", "AnimationPattern to record into", true)
				.P("action", "string", "Start or Stop", true)
				.P("record_physics", "boolean", "Record physics-driven atoms too", false)
				.Fn(api.RecordSceneAnimation));

			// ---------- triggers ----------
			tools.Add(new Tool("list_triggers", "List trigger-carrying atoms in the scene (Trigger storables on sliders/buttons/steps).")
				.P("search", "string", "Optional uid/storable filter", false)
				.Fn(api.ListTriggers));
			tools.Add(new Tool("trigger_action", "Fire or configure a trigger: Fire, Reset, SetParams.")
				.P("trigger_uid", "string", "Trigger atom uid", true)
				.P("action", "string", "Action: Fire, Reset, SetFloatParam, SetStringParam", true)
				.P("param", "string", "Parameter name", false)
				.P("value", "any", "Value to set", false)
				.Fn(api.TriggerAction));
			tools.Add(new Tool("create_trigger", "Create a new Trigger atom.")
				.P("uid", "string", "Optional uid", false)
				.Timeout(30000).Poller()
				.Fn(api.CreateTrigger));

			// ---------- physics ----------
			tools.Add(new Tool("list_physics_objects", "List physics-relevant atoms (their physics-ish storables by name).")
				.P("search", "string", "Optional uid filter", false)
				.Fn(api.ListPhysicsObjects));
			tools.Add(new Tool("physics_object_action", "Control a physics object: Enable/Disable, SetCollision, AddForce, SetMass.")
				.P("uid", "string", "Physics atom uid", true)
				.P("action", "string", "Action: EnablePhysics, DisablePhysics, SetCollision, AddForce, SetMass, SetDrag", true)
				.P("value", "any", "Value for the action", false)
				.Fn(api.PhysicsObjectAction));
			tools.Add(new Tool("create_physics_joint", "Create a physics joint between two atoms (ConfigurableJoint, FixedJoint, SpringJoint).")
				.P("uid", "string", "Optional uid", false)
				.P("atom_a", "string", "First atom uid", true)
				.P("atom_b", "string", "Second atom uid", true)
				.P("joint_type", "string", "ConfigurableJoint, FixedJoint, SpringJoint, HingeJoint", true)
				.P("anchor", "array", "[x,y,z] local anchor on atom A", false)
				.P("connected_anchor", "array", "[x,y,z] local anchor on atom B", false)
				.Timeout(30000).Poller()
				.Fn(api.CreatePhysicsJoint));

			// ---------- scene lighting / audio / misc ----------
			tools.Add(new Tool("list_lights", "List Light atoms in the scene with their current settings.")
				.Fn(api.ListLights));
			tools.Add(new Tool("light_action", "Control a Light: On/Off, SetColor, SetIntensity, SetRange, SetType.")
				.P("uid", "string", "Light atom uid", true)
				.P("action", "string", "Action: Toggle, SetColor, SetIntensity, SetRange, SetType, SetSpotAngle", true)
				.P("value", "any", "Value (color hex, float, or string for type)", false)
				.Fn(api.LightAction));
			tools.Add(new Tool("list_audio_sources", "List AudioSource atoms.")
				.Fn(api.ListAudioSources));
			tools.Add(new Tool("list_audio_clips", "List VaM's registered audio clips (embedded package audio + URL clips) with categories; uids work directly as audio_source_action Play clip.")
				.P("type", "string", "all|embedded|url (default all)", false)
				.P("category", "string", "Case-insensitive category name filter", false)
				.P("search", "string", "Substring filter on clip uid/name", false)
				.Fn(api.ListAudioClips));
			tools.Add(new Tool("audio_source_action", "Control AudioSource: Play, Stop, Pause, SetClip (also decodes local PCM WAV in-plugin), SetLoop, SetVolume.")
				.P("uid", "string", "AudioSource atom uid", true)
				.P("action", "string", "Action: Play, Stop, Pause, SetVolume, SetClip, SetLoop", true)
				.P("value", "any", "Value for the action", false)
				.Fn(api.AudioSourceAction));

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
			tools.Add(new Tool("atom_control", "Control atom state: Select, FreezePhysics, DisableRender, DisableCollision, EndIsolate, LoadUserDefaults (apply the atom's user-default preset).")
				.P("uid", "string", "Atom uid", true)
				.P("action", "string", "Action: Select, FreezePhysics, DisableCollision, EndIsolate, LoadUserDefaults, ...", true)
				.P("value", "any", "Optional value (e.g. value=false on FreezePhysics to unfreeze)", false)
				.Fn(api.AtomControl));
			tools.Add(new Tool("atom_move", "Move atom with gizmo: SetPosition, SetRotation, SetScale, SnapToGround, AlignTo.")
				.P("uid", "string", "Atom uid", true)
				.P("action", "string", "Action: SetPosition, SetRotation, SetScale, SnapToGround, AlignTo, ResetTransform", true)
				.P("target_uid", "string", "Target atom for AlignTo", false)
				.P("value", "array", "[x,y,z] for position/rotation/scale", false)
				.Fn(api.AtomMove));
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
			tools.Add(new Tool("list_cua", "List CustomUnityAsset atoms with their assetUrl, assetName, and load state.")
				.Fn(api.ListCUA));
			tools.Add(new Tool("cua_action", "Control CustomUnityAsset: LoadAsset, UnloadAsset, ReloadAsset, SetAssetUrl, SetAssetName.")
				.P("uid", "string", "CUA atom uid", true)
				.P("action", "string", "Action: LoadAsset, UnloadAsset, ReloadAsset, SetAssetUrl, SetAssetName", true)
				.P("value", "string", "Asset URL or asset name", false)
				.Fn(api.CUAAction));

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
			tools.Add(new Tool("hub_browse", "Browse the VaM Hub (community content: looks, clothing, scenes, plugins...). Queries the game's own Hub API.")
				.P("search", "string", "Search text", false)
				.P("page", "number", "Page number (default 1)", false)
				.P("perpage", "number", "Results per page (default 20)", false)
				.P("sort", "string", "Sort order (e.g. resource_update_date, downloads, rating)", false)
				.P("type", "string", "Content type filter", false)
				.Timeout(90000).Poller()
				.Fn(api.HubBrowse));
			tools.Add(new Tool("hub_detail", "Get full detail of one Hub resource: package name, author, version and all downloadable .var files + dependencies. Use the package name from here with hub_download.")
				.P("package", "string", "Package name (e.g. AcidBubbles.Timeline)", false)
				.P("resource_id", "string", "Hub resource id from hub_browse (alternative to package)", false)
				.Timeout(90000).Poller()
				.Fn(api.HubDetail));
			tools.Add(new Tool("hub_download", "Download a package from the VaM Hub using the game's own downloader (auth + install into AddonPackages). Package name from hub_browse/hub_detail results.")
				.P("package", "string", "Package name, e.g. AcidBubbles.Timeline", true)
				.Timeout(300000).Poller()
				.Fn(api.HubDownload));

			// ---------- skin ----------
			tools.Add(new Tool("set_skin_sss", "Set the skin subsurface-scattering (SSS) tint of a person via the skin material's 'Subsurface Color' (_SubdermisColor).")
				.P("person", "string", "Person uid (default: first Person)", false)
				.P("color", "string", "#RRGGBB color (e.g. #C06060 reddish subsurface tint)", true)
				.Fn(api.SetSkinSss));

			// ---------- plugins (VaM native plugin system) ----------
			tools.Add(new Tool("list_plugins", "List plugins on an atom's PluginManager. Empty uid = CoreControl (session plugins); pass uid=<person> for a person's plugins. A loaded plugin also registers as a storable of that atom — configure it via list_storable_params/set_param with storable=<plugin_uid>.")
				.P("uid", "string", "Atom uid (default CoreControl = session plugins)", false)
				.Fn(api.ListPlugins));
			tools.Add(new Tool("add_plugin", "Add a VaM plugin (.cs/.cslist/.dll) to an atom's PluginManager and wait until it compiles/loads. Default target = CoreControl (session plugins); pass uid=<person> to attach it to a person.")
				.P("path", "string", "Plugin path, e.g. Custom/Scripts/MyPlugin.cs or a package path", true)
				.P("uid", "string", "Atom uid (default CoreControl = session plugins)", false)
				.Timeout(90000).Poller()
				.Fn(api.AddPlugin));
			tools.Add(new Tool("remove_plugin", "Remove a plugin from an atom's PluginManager.")
				.P("plugin_uid", "string", "Plugin uid from list_plugins", true)
				.P("uid", "string", "Atom uid (default CoreControl = session plugins)", false)
				.Fn(api.RemovePlugin));

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
