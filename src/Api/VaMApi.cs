using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UnityEngine;
using SimpleJSON;
using MVR.FileManagementSecure;

namespace VaMMCP.Api {
	/// <summary>
	/// The VaM control layer. Every method must be called on the Unity main thread
	/// (wrap with MainThreadDispatcher.Run), except AddAtom-style pollers.
	/// </summary>
	public class VaMApi {
		public readonly MainThreadDispatcher Mt;

		public VaMApi(MainThreadDispatcher mt) {
			Mt = mt;
		}

		// ===================== helpers =====================

		private static SuperController SC { get { return SuperController.singleton; } }

		/// <summary>A float as a JSON number (see JSONRaw: SimpleJSON would quote it).</summary>
		public static JSONNode F(float v) { return JSONRaw.Num(v); }

		public static JSONArray Vec(Vector3 v) {
			JSONArray a = new JSONArray();
			a.Add(F(v.x));
			a.Add(F(v.y));
			a.Add(F(v.z));
			return a;
		}

		public static Vector3 ParseVec3(JSONNode n) {
			JSONArray a = n.AsArray;
			if (a == null || a.Count < 3) throw new ApiError("expected [x, y, z] array");
			return new Vector3(a[0].AsFloat, a[1].AsFloat, a[2].AsFloat);
		}

		public static bool Has(JSONNode o, string k) { return o != null && o[k] != null; }
		public static string S(JSONNode o, string k) { return Has(o, k) ? o[k].Value : ""; }
		public static bool Bool(JSONNode o, string k, bool def) { return Has(o, k) ? o[k].AsBool : def; }
		public static float Num(JSONNode o, string k, float def) { return Has(o, k) ? o[k].AsFloat : def; }

		public static Atom FindAtom(string uid) {
			if (string.IsNullOrEmpty(uid)) throw new ApiError("atom uid required");
			Atom a = SC.GetAtomByUid(uid);
			if (a == null) throw new ApiError("atom not found: " + uid);
			return a;
		}

		public static Atom RequirePerson(string uid) {
			if (!string.IsNullOrEmpty(uid)) {
				Atom a = SC.GetAtomByUid(uid);
				if (a == null) throw new ApiError("person not found: " + uid);
				if (a.type != "Person") throw new ApiError("atom is not a Person: " + uid);
				return a;
			}
			foreach (Atom a in SC.GetAtoms()) {
				if (a != null && a.type == "Person") return a;
			}
			throw new ApiError("no Person atom in the current scene");
		}

		public static DAZCharacterSelector Selector(Atom p) {
			JSONStorable g = p.GetStorableByID("geometry");
			if (g == null) throw new ApiError("no geometry storable on " + p.uid);
			DAZCharacterSelector sel = g as DAZCharacterSelector;
			if (sel == null) throw new ApiError("geometry is not a DAZCharacterSelector on " + p.uid);
			return sel;
		}

		public static JSONClass AtomInfo(Atom a) {
			JSONClass r = new JSONClass();
			r["uid"] = a.uid;
			r["type"] = a.type;
			r["name"] = a.name != null ? a.name : "";
			r["on"] = JSONRaw.Bool(a.on);
			r["position"] = Vec(a.transform.position);
			r["rotation"] = Vec(a.transform.eulerAngles);
			return r;
		}

		public static List<string> ListJsonFiles(string dir) {
			List<string> list = new List<string>();
			try {
				string[] files = FileManagerSecure.GetFiles(dir, "*.json");
				if (files != null) {
					foreach (string f in files) {
						if (f != null && f.ToLowerInvariant().EndsWith(".json")) list.Add(f);
					}
				}
			} catch (Exception e) {
				Log.Debug("ListJsonFiles(" + dir + "): " + e.Message);
			}
			list.Sort();
			return list;
		}

		public static string FileName(string path) {
			string p = path.Replace('\\', '/');
			int i = p.LastIndexOf('/');
			string name = i >= 0 ? p.Substring(i + 1) : p;
			if (name.EndsWith(".json")) name = name.Substring(0, name.Length - 5);
			return name;
		}

		public static string Timestamp() {
			return DateTime.Now.ToString("yyyyMMdd_HHmmss");
		}

		/// <summary>Apply a VaM preset file (look / clothing / hair / pose / full) by restoring its storables.</summary>
		public static void ApplyPresetStorables(Atom atom, string path) {
			JSONNode node = SC.LoadJSON(path);
			if (node == null) throw new ApiError("could not load JSON: " + path);
			JSONClass jc = node.AsObject;
			if (jc == null) throw new ApiError("preset is not a JSON object: " + path);
			JSONArray storables = jc["storables"] != null ? jc["storables"].AsArray : null;
			if (storables == null && jc["atoms"] != null) {
				// VaM 1.22+ presets wrap storables under atoms[0].storables
				JSONArray atoms = jc["atoms"].AsArray;
				if (atoms != null && atoms.Count > 0) {
					JSONClass atom0 = atoms[0].AsObject;
					if (atom0 != null) storables = atom0["storables"] != null ? atom0["storables"].AsArray : null;
				}
			}
			if (storables == null) throw new ApiError("preset has no storables array: " + path);
			bool setUnlisted = true;
			if (jc["setUnlistedParamsToDefault"] != null && jc["setUnlistedParamsToDefault"].Value == "false") setUnlisted = false;
			int restored = 0;
			for (int i = 0; i < storables.Count; i++) {
				JSONClass sj = storables[i].AsObject;
				if (sj == null) continue;
				string sid = sj["id"] != null ? sj["id"].Value : "";
				if (sid == "") continue;
				JSONStorable st = atom.GetStorableByID(sid);
				if (st == null) continue;
				st.RestoreFromJSON(sj, true, true, null, setUnlisted);
				restored++;
			}
			if (restored == 0) throw new ApiError("no matching storables on " + atom.uid + " for " + path);
		}

		private static IEnumerable<DAZMorph> AllMorphs(DAZCharacterSelector sel) {
			HashSet<string> seen = new HashSet<string>();
			DAZMorphBank[] banks = { sel.morphBank1, sel.morphBank2, sel.morphBank3 };
			foreach (DAZMorphBank bank in banks) {
				if (bank == null || bank.morphs == null) continue;
				foreach (DAZMorph m in bank.morphs) {
					if (m == null) continue;
					if (seen.Add(m.uid)) yield return m;
				}
			}
		}

		private static DAZMorph FindMorph(DAZCharacterSelector sel, string name) {
			foreach (DAZMorph m in AllMorphs(sel)) {
				if (m.displayName != null && string.Equals(m.displayName, name, StringComparison.OrdinalIgnoreCase)) return m;
				if (m.uid != null && string.Equals(m.uid, name, StringComparison.OrdinalIgnoreCase)) return m;
			}
			return null;
		}

		// ---------- reflection helpers (protected/internal VaM internals) ----------

		private static FieldInfo floatParamsField;
		private static Dictionary<string, JSONStorableFloat> FloatParams(JSONStorable st) {
			try {
				if (floatParamsField == null) {
					floatParamsField = typeof(JSONStorable).GetField("floatParams", BindingFlags.NonPublic | BindingFlags.Instance);
				}
				return floatParamsField != null ? (Dictionary<string, JSONStorableFloat>)floatParamsField.GetValue(st) : null;
			} catch {
				return null;
			}
		}

		private static FieldInfo chooserParamsField;
		private static Dictionary<string, JSONStorableStringChooser> ChooserParams(JSONStorable st) {
			try {
				if (chooserParamsField == null) {
					chooserParamsField = typeof(JSONStorable).GetField("stringChooserParams", BindingFlags.NonPublic | BindingFlags.Instance);
				}
				return chooserParamsField != null ? (Dictionary<string, JSONStorableStringChooser>)chooserParamsField.GetValue(st) : null;
			} catch {
				return null;
			}
		}

		/// <summary>Invoke a method by name, tolerating overloads (GetMethod(name) throws
		/// AmbiguousMatchException when VaM exposes several overloads, e.g. SetActiveHairItem).</summary>
		public static object Invoke(object target, string method, params object[] args) {
			int argc = args == null ? 0 : args.Length;
			MethodInfo best = null;
			foreach (MethodInfo m in target.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) {
				if (m.Name != method) continue;
				ParameterInfo[] ps = m.GetParameters();
				if (ps.Length != argc) continue;
				bool fits = true;
				for (int i = 0; i < ps.Length; i++) {
					object a = args[i];
					if (a == null) continue;
					Type at = a.GetType();
					if (ps[i].ParameterType.IsAssignableFrom(at)) continue;
					// numeric literals may be int where the method wants float
					if (ps[i].ParameterType.IsPrimitive && at.IsPrimitive) continue;
					fits = false;
					break;
				}
				if (fits) { best = m; break; }
				if (best == null) best = m;
			}
			if (best == null) throw new ApiError("method not found: " + method + " on " + target.GetType().Name);
			return best.Invoke(target, args);
		}

		/// <summary>Find a component by type name without a compile-time reference (tries plain name, MeshVR.* and full scan fallback).</summary>
		private static Type runtimeCreatorType;
		public static Component FindComponent(Component root, string typeName) {
			if (root == null) return null;
			Type t = Type.GetType(typeName + ", Assembly-CSharp");
			if (t == null) t = Type.GetType("MeshVR." + typeName + ", Assembly-CSharp");
			if (t == null) {
				// fallback: scan the game assembly for the type by simple name (cached)
				try {
					if (runtimeCreatorType == null) {
						Type[] all = typeof(SuperController).Assembly.GetTypes();
						foreach (Type tt in all) {
							if (tt.Name == typeName) { runtimeCreatorType = tt; break; }
						}
					}
					t = runtimeCreatorType;
				} catch { }
			}
			if (t == null) return null;
			return root.GetComponentInChildren(t, true);
		}

		// ===================== scene =====================

		public JSONNode Status() {
			JSONClass r = new JSONClass();
			r["plugin"] = "VaMMCP";
			r["version"] = Mcp.McpServer.ServerVersion;
			// Application.version is the Unity player version ("1.0"); the VaM build number is
			// what SuperController reports (and what it stamps into saved scenes).
			try { r["vaMVersion"] = SC.GetVersion(); } catch { r["vaMVersion"] = "unknown"; }
			try { r["vaMRoot"] = Application.dataPath + "/.."; } catch { }
			int atoms = 0;
			int persons = 0;
			foreach (Atom a in SC.GetAtoms()) {
				if (a == null) continue;
				atoms++;
				if (a.type == "Person") persons++;
			}
			r["atoms"] = JSONRaw.Num(atoms);
			r["persons"] = JSONRaw.Num(persons);
			r["endpoint"] = "http://127.0.0.1:" + Plugin.cfgPort.Value + "/mcp";
			r["allowEval"] = JSONRaw.Bool(Plugin.cfgAllowEval.Value);
			r["time"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
			return r;
		}

		public JSONNode ListScenes(JSONNode args) {
			string search = S(args, "search").ToLowerInvariant();
			List<string> files = ListJsonFiles("Saves/scene");
			JSONArray arr = new JSONArray();
			foreach (string f in files) {
				string name = FileName(f);
				if (search != "" && name.ToLowerInvariant().IndexOf(search) < 0 && f.ToLowerInvariant().IndexOf(search) < 0) continue;
				JSONClass row = new JSONClass();
				row["path"] = f;
				row["name"] = name;
				arr.Add(row);
			}
			JSONClass r = new JSONClass();
			r["count"] = JSONRaw.Num(arr.Count);
			r["scenes"] = arr;
			return r;
		}

		public JSONNode LoadScene(JSONNode args) {
			string path = S(args, "path");
			if (path == "") throw new ApiError("path required (see list_scenes)");
			bool merge = Bool(args, "merge", false);
			if (!merge) {
				if (!FileManagerSecure.FileExists(path)) throw new ApiError("scene file not found: " + path);
				SC.Load(path);
			} else {
				SC.LoadMerge(path);
			}
			JSONClass r = new JSONClass();
			r["path"] = path;
			r["merge"] = JSONRaw.Bool(merge);
			r["loading"] = JSONRaw.Bool(true);
			return r;
		}

		public JSONNode NewScene() {
			SC.NewScene();
			JSONClass r = new JSONClass();
			r["ok"] = JSONRaw.Bool(true);
			return r;
		}

		/// <summary>Normalize a save path to VaM style (backslashes), ensure its directory exists, and remove an existing file
		/// (VaM asks for user confirmation when overwriting an existing file from a plugin context — we delete via System.IO first so Save writes fresh).</summary>
		public static string PrepareSavePath(string path) {
			path = path.Replace('/', '\\');
			int idx = path.LastIndexOf('\\');
			if (idx > 0) {
				string dir = path.Substring(0, idx);
				try { FileManagerSecure.CreateDirectory(dir); } catch (Exception e) { Log.Debug("PrepareSavePath mkdir: " + e.Message); }
			}
			try {
				if (FileManagerSecure.FileExists(path)) {
					string abs = Application.dataPath + "/.." + (path.StartsWith("\\") ? "" : "\\") + path;
					if (System.IO.File.Exists(abs)) {
						System.IO.File.Delete(abs);
						Log.Info("PrepareSavePath: deleted existing " + path);
					}
				}
			} catch (Exception e) {
				Log.Info("PrepareSavePath delete failed: " + e.Message);
			}
			return path;
		}

		public JSONNode SaveScene(JSONNode args) {
			string path = S(args, "path");
			if (path == "") path = "Saves/scene/VaMMCP_" + Timestamp() + ".json";
			if (!path.EndsWith(".json")) path += ".json";
			SC.Save(PrepareSavePath(path));
			JSONClass r = new JSONClass();
			r["saved"] = path;
			return r;
		}

		// ===================== input =====================

		[DllImport("user32.dll")]
		private static extern IntPtr GetForegroundWindow();
		[DllImport("user32.dll")]
		private static extern bool SetForegroundWindow(IntPtr hWnd);
		[DllImport("user32.dll")]
		private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

		private const uint KeyUp = 0x0002;

		private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
		[DllImport("user32.dll")]
		private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
		[DllImport("user32.dll")]
		private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
		[DllImport("user32.dll")]
		private static extern bool IsWindowVisible(IntPtr hWnd);
		[DllImport("user32.dll")]
		private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
		[DllImport("kernel32.dll")]
		private static extern uint GetCurrentThreadId();
		[DllImport("user32.dll")]
		private static extern bool IsIconic(IntPtr hWnd);
		[DllImport("user32.dll")]
		private static extern bool ShowWindow(IntPtr hWnd, int cmd);

		/// <summary>Raise our own window. SetForegroundWindow is refused while another
		/// process owns the foreground, so attach to that thread first and retry.</summary>
		private static bool FocusOwnWindow(IntPtr hwnd) {
			if (hwnd == IntPtr.Zero) return false;
			// A minimised player keeps Update() frozen, so keys are never consumed.
			if (IsIconic(hwnd)) ShowWindow(hwnd, 9 /* SW_RESTORE */);
			SetForegroundWindow(hwnd);
			Thread.Sleep(120);
			if (GetForegroundWindow() == hwnd) return true;
			IntPtr fg = GetForegroundWindow();
			uint fgPid;
			uint fgThread = fg != IntPtr.Zero ? GetWindowThreadProcessId(fg, out fgPid) : 0;
			uint self = GetCurrentThreadId();
			bool attached = fgThread != 0 && fgThread != self && AttachThreadInput(fgThread, self, true);
			try {
				SetForegroundWindow(hwnd);
				Thread.Sleep(150);
			} finally {
				if (attached) AttachThreadInput(fgThread, self, false);
			}
			return GetForegroundWindow() == hwnd;
		}

		/// <summary>Unity does not always expose a Process.MainWindowHandle, so find the
		/// biggest visible top-level window that belongs to this process instead.</summary>
		private static IntPtr OwnWindow() {
			IntPtr found = IntPtr.Zero;
			uint self = (uint)Process.GetCurrentProcess().Id;
			EnumWindows(delegate(IntPtr h, IntPtr l) {
				uint pid;
				GetWindowThreadProcessId(h, out pid);
				if (pid == self && IsWindowVisible(h)) { found = h; return false; }
				return true;
			}, IntPtr.Zero);
			if (found == IntPtr.Zero) found = Process.GetCurrentProcess().MainWindowHandle;
			return found;
		}

		/// <summary>Show or hide VaM's main HUD (the launcher panels over the scene view)
		/// so captures can see the scene while VaM is unfocused. Equivalent to the
		/// scene-view <-> main-screen switch; uses SuperController's own methods,
		/// no window focus changes.</summary>
		public JSONNode UiHud(JSONNode args) {
			string action = S(args, "action");
			if (action != "hide" && action != "show") throw new ApiError("action required: hide or show");
			JSONClass r = new JSONClass();
			Mt.Run(delegate {
				if (action == "hide") SC.HideMainHUD();
				else SC.ShowMainHUD();
			}, 10000);
			r["action"] = action;
			r["ok"] = JSONRaw.Bool(true);
			return r;
		}

		/// <summary>Synthesise a key press in VaM (e.g. Escape to skip the save-screenshot
		/// prompt). Never steals focus: by default the key is only sent while the user
		/// already has VaM in the foreground (verified with GetForegroundWindow); otherwise
		/// it refuses so the key cannot land in another application. focus=true is an
		/// explicit opt-in to raising the VaM window first.</summary>
		public JSONNode PressKey(JSONNode args) {
			string key = S(args, "key");
			if (key == "") throw new ApiError("key required, e.g. Escape, Return, Space, Tab, F5, a, Up");
			int times = (int)Num(args, "times", 1);
			if (times < 1) times = 1;
			if (times > 50) times = 50;
			bool focus = Bool(args, "focus", false);
			byte vk = VirtualKey(key);
			IntPtr hwnd = OwnWindow();
			bool focused = false;
			if (GetForegroundWindow() == hwnd) focused = true;
			if (focus) {
				focused = FocusOwnWindow(hwnd);
				if (!focused) {
					JSONClass f = new JSONClass();
					f["ok"] = JSONRaw.Bool(false);
					f["reason"] = "VaM window is not in the foreground; refusing to send keys that could reach another application";
					f["hint"] = "Click the VaM window once, or set focus=false if you accept that risk.";
					return f;
				}
				// A minimised/paused player does not run Update until focus has been back
				// for a moment. IdleSaver drops to 1 FPS while minimised, so wait longer
				// than one frozen frame or the key is pressed and released between Updates.
				Thread.Sleep(1200);
			}
			if (!focused) {
				JSONClass f = new JSONClass();
				f["ok"] = JSONRaw.Bool(false);
				f["reason"] = "VaM does not have keyboard focus; keys would land in another application (keybd_event targets the foreground window)";
				f["hint"] = "Click the VaM window once so it keeps focus (no window is raised automatically), then retry.";
				return f;
			}
			for (int i = 0; i < times; i++) {
				keybd_event(vk, 0, 0, UIntPtr.Zero);
				Thread.Sleep(120);
				keybd_event(vk, 0, KeyUp, UIntPtr.Zero);
				Thread.Sleep(150);
			}
			JSONClass r = new JSONClass();
			r["ok"] = JSONRaw.Bool(true);
			r["key"] = key;
			r["vk"] = JSONRaw.Num(vk);
			r["times"] = JSONRaw.Num(times);
			r["focused"] = JSONRaw.Bool(focused);
			return r;
		}

		private static byte VirtualKey(string key) {
			string k = key.Trim().ToLowerInvariant();
			switch (k) {
				case "escape": case "esc": return 0x1B;
				case "return": case "enter": return 0x0D;
				case "space": case "spacebar": return 0x20;
				case "tab": return 0x09;
				case "backspace": return 0x08;
				case "delete": case "del": return 0x2E;
				case "insert": return 0x2D;
				case "home": return 0x24;
				case "end": return 0x23;
				case "pageup": case "pgup": return 0x21;
				case "pagedown": case "pgdn": return 0x22;
				case "up": return 0x26;
				case "down": return 0x28;
				case "left": return 0x25;
				case "right": return 0x27;
				case "shift": return 0x10;
				case "ctrl": case "control": return 0x11;
				case "alt": return 0x12;
				case "menu": return 0x5D;
			}
			if (k.Length == 1) {
				char c = k[0];
				if (c >= 'a' && c <= 'z') return (byte)(0x41 + (c - 'a'));
				if (c >= '0' && c <= '9') return (byte)(0x30 + (c - '0'));
				throw new ApiError("unsupported key character: " + key);
			}
			if (k.StartsWith("f") && k.Length <= 3) {
				int n;
				if (int.TryParse(k.Substring(1), out n) && n >= 1 && n <= 12) return (byte)(0x70 + n - 1);
			}
			throw new ApiError("unsupported key name: " + key + " (try Escape, Return, Space, Tab, F1-F12, a-z, 0-9, arrows)");
		}

		// ===================== atoms =====================

		public JSONNode ListAtomTypes() {
			JSONClass r = new JSONClass();
			JSONArray cats = new JSONArray();
			try {
				FieldInfo f = typeof(SuperController).GetField("atomCategoryToAtomTypes", BindingFlags.NonPublic | BindingFlags.Instance);
				if (f != null) {
					object val = f.GetValue(SC);
					Dictionary<string, List<string>> d = val as Dictionary<string, List<string>>;
					if (d != null) {
						foreach (KeyValuePair<string, List<string>> kv in d) {
							JSONClass c = new JSONClass();
							c["category"] = kv.Key;
							JSONArray types = new JSONArray();
							foreach (string t in kv.Value) types.Add(t);
							c["types"] = types;
							cats.Add(c);
						}
					}
				}
			} catch (Exception e) {
				Log.Debug("atom types reflection failed: " + e.Message);
			}
			if (cats.Count == 0) {
				string[] common = {
					"Person", "Clothing", "Hair", "Light", "Spotlight", "AudioSource", "ImagePanel", "Canvas",
					"UIButton", "UIText", "AnimationPattern", "AnimationStep", "CollisionTrigger", "ForceProducer",
					"LiquidCollider", "ParticleSystem", "Screen", "Microphone", "WindowCamera", "TV", "PostProcessing"
				};
				foreach (string t in common) {
					JSONClass c = new JSONClass();
					c["category"] = "Common";
					JSONArray types = new JSONArray();
					types.Add(t);
					c["types"] = types;
					cats.Add(c);
				}
			}
			r["categories"] = cats;
			return r;
		}

		public JSONNode ListAtoms(JSONNode args) {
			string type = S(args, "type");
			JSONArray arr = new JSONArray();
			foreach (Atom a in SC.GetAtoms()) {
				if (a == null) continue;
				if (type != "" && !string.Equals(a.type, type, StringComparison.OrdinalIgnoreCase)) continue;
				arr.Add(AtomInfo(a));
			}
			JSONClass r = new JSONClass();
			r["count"] = JSONRaw.Num(arr.Count);
			r["atoms"] = arr;
			return r;
		}

		/// <summary>Poller tool: starts AddAtomByType on the main thread, then polls for the new atom.</summary>
		public JSONNode AddAtom(JSONNode args) {
			string type = S(args, "type");
			if (type == "") throw new ApiError("type required (see list_atom_types)");
			string uid = S(args, "uid");

			List<string> before = Mt.Run(delegate {
				if (uid != "" && SC.GetAtomByUid(uid) != null) throw new ApiError("atom already exists: " + uid);
				List<string> b = new List<string>();
				foreach (Atom a in SC.GetAtoms()) if (a != null) b.Add(a.uid);
				string useUid = uid == "" ? null : uid;
				SC.StartCoroutine(SC.AddAtomByType(type, useUid, true));
				return b;
			}, 15000);

			string created = null;
			DateTime deadline = DateTime.UtcNow.AddSeconds(45);
			while (DateTime.UtcNow < deadline) {
				Thread.Sleep(150);
				created = Mt.Run(delegate {
					foreach (Atom a in SC.GetAtoms()) {
						if (a == null) continue;
						if (a.type == type && !before.Contains(a.uid)) return a.uid;
					}
					return null;
				}, 10000);
				if (created != null) break;
			}
			if (created == null) throw new ApiError("atom creation timed out for type " + type + " (check list_atom_types for valid types)");

			JSONClass r = new JSONClass();
			r["uid"] = created;
			r["type"] = type;
			return r;
		}

		public JSONNode RemoveAtom(JSONNode args) {
			Atom a = FindAtom(S(args, "uid"));
			string uid = a.uid;
			SC.RemoveAtom(a);
			JSONClass r = new JSONClass();
			r["removed"] = uid;
			return r;
		}

		public JSONNode SetAtomOn(JSONNode args) {
			Atom a = FindAtom(S(args, "uid"));
			bool on = Bool(args, "on", true);
			if (a.on != on) a.ToggleOn();
			JSONClass r = new JSONClass();
			r["uid"] = a.uid;
			r["on"] = JSONRaw.Bool(a.on);
			return r;
		}

		public JSONNode GetAtomTransform(JSONNode args) {
			Atom a = FindAtom(S(args, "uid"));
			JSONClass r = new JSONClass();
			r["uid"] = a.uid;
			r["position"] = Vec(a.transform.position);
			r["rotation"] = Vec(a.transform.eulerAngles);
			r["scale"] = Vec(a.transform.localScale);
			return r;
		}

		public JSONNode SetAtomTransform(JSONNode args) {
			Atom a = FindAtom(S(args, "uid"));
			bool relative = Bool(args, "relative", false);
			if (Has(args, "position")) {
				Vector3 p = ParseVec3(args["position"]);
				a.transform.position = relative ? a.transform.position + p : p;
			}
			if (Has(args, "rotation")) {
				Quaternion q = Quaternion.Euler(ParseVec3(args["rotation"]));
				a.transform.rotation = relative ? a.transform.rotation * q : q;
			}
			if (Has(args, "scale")) {
				Vector3 s = ParseVec3(args["scale"]);
				a.transform.localScale = relative ? a.transform.localScale + s : s;
			}
			return GetAtomTransform(args);
		}

		// ===================== persons =====================

		public JSONNode AddPerson(JSONNode args) {
			// add_person = add_atom with type forced to Person
			if (!Has(args, "type")) args["type"] = "Person";
			return AddAtom(args);
		}

		public JSONNode ListPersons() {
			JSONArray arr = new JSONArray();
			foreach (Atom a in SC.GetAtoms()) {
				if (a == null || a.type != "Person") continue;
				JSONClass row = AtomInfo(a);
				try {
					DAZCharacterSelector sel = Selector(a);
					string character = "";
					try { character = sel.GetStringChooserParamValue("characterSelection"); } catch { }
					if (character == "") {
						try { character = sel.GetStringParamValue("character"); } catch { }
					}
					row["character"] = character;
					row["gender"] = sel.gender.ToString();
				} catch { }
				arr.Add(row);
			}
			JSONClass r = new JSONClass();
			r["count"] = JSONRaw.Num(arr.Count);
			r["persons"] = arr;
			return r;
		}

		public JSONNode SetCharacter(JSONNode args) {
			Atom p = RequirePerson(S(args, "person"));
			DAZCharacterSelector sel = Selector(p);
			string name = S(args, "name");
			if (name == "") throw new ApiError("name required (characters are listed via get_param uid=<person> storable=geometry param=characterSelection)");
			sel.SelectCharacterByName(name, false);
			JSONClass r = new JSONClass();
			r["person"] = p.uid;
			r["name"] = name;
			r["loading"] = JSONRaw.Bool(true);
			return r;
		}

		public JSONNode ListLooks(JSONNode args) {
			return ListPresets("Saves/Person/Appearance", S(args, "search"), "looks");
		}

		public JSONNode ListClothingPresets(JSONNode args) {
			JSONClass r = new JSONClass();
			JSONArray arr = new JSONArray();
			AddPresetFiles(arr, "Saves/Person/Clothing", S(args, "search"));
			AddPresetFiles(arr, "Custom/Clothing", S(args, "search"));
			AddPackageItemFiles(arr, "Custom/Clothing", S(args, "search"), 300);
			r["count"] = JSONRaw.Num(arr.Count);
			r["presets"] = arr;
			return r;
		}

		public JSONNode ListHairPresets(JSONNode args) {
			JSONClass r = new JSONClass();
			JSONArray arr = new JSONArray();
			AddPresetFiles(arr, "Saves/Person/Hair", S(args, "search"));
			AddPresetFiles(arr, "Custom/Hair", S(args, "search"));
			AddPackageItemFiles(arr, "Custom/Hair", S(args, "search"), 300);
			r["count"] = JSONRaw.Num(arr.Count);
			r["presets"] = arr;
			return r;
		}

		// ---------- .var package assets ----------

		public static List<string> ListPackageUids() {
			List<string> list = new List<string>();
			string[] files = null;
			try {
				files = FileManagerSecure.GetFiles("AddonPackages", "*.var");
			} catch { }
			if (files != null) {
				foreach (string f in files) {
					if (f == null) continue;
					string p = f.Replace('\\', '/');
					int i = p.LastIndexOf('/');
					string name = i >= 0 ? p.Substring(i + 1) : p;
					if (name.EndsWith(".var")) name = name.Substring(0, name.Length - 4);
					if (name != "") list.Add(name);
				}
			}
			list.Sort();
			return list;
		}

		public JSONNode ListPackages() {
			List<string> pkgs = ListPackageUids();
			JSONArray arr = new JSONArray();
			foreach (string p in pkgs) {
				JSONClass row = new JSONClass();
				row["uid"] = p;
				arr.Add(row);
			}
			JSONClass r = new JSONClass();
			r["count"] = JSONRaw.Num(arr.Count);
			r["packages"] = arr;
			return r;
		}

		/// <summary>Scan every .var package for item jsons under the given relative dir (e.g. Custom/Clothing), recursively.</summary>
		private static void AddPackageItemFiles(JSONArray arr, string relativeDir, string search, int cap) {
			string s = search.ToLowerInvariant();
			foreach (string pkg in ListPackageUids()) {
				if (arr.Count >= cap) break;
				string root = pkg + ":/" + relativeDir;
				ScanPackageDir(arr, root, s, cap, pkg, 0);
			}
		}

		private static void ScanPackageDir(JSONArray arr, string dir, string search, int cap, string pkg, int depth) {
			if (depth > 5 || arr.Count >= cap) return;
			AddItemFilesInDir(arr, dir, search, cap, pkg);
			string[] dirs = null;
			try { dirs = FileManagerSecure.GetDirectories(dir); } catch { }
			if (dirs == null) return;
			foreach (string d in dirs) {
				if (arr.Count >= cap) break;
				ScanPackageDir(arr, d, search, cap, pkg, depth + 1);
			}
		}

		private static void AddItemFilesInDir(JSONArray arr, string dir, string search, int cap, string pkg) {
			string[] files = null;
			try { files = FileManagerSecure.GetFiles(dir); } catch { }
			if (files == null) return;
			foreach (string f in files) {
				if (arr.Count >= cap) break;
				if (f == null) continue;
				string low = f.ToLowerInvariant();
				// item files are .vam (assets); presets are .json. Skip texture/variant/meta files.
				if (!low.EndsWith(".json") && !low.EndsWith(".vam")) continue;
				if (low.EndsWith("meta.json") || low.EndsWith("package.json")) continue;
				if (low.EndsWith(".vam") && (low.EndsWith("_main.vam") || low.EndsWith("_sim.vam"))) continue;
				string name = FileName(f);
				if (search != "" && name.ToLowerInvariant().IndexOf(search) < 0 && f.ToLowerInvariant().IndexOf(search) < 0) continue;
				JSONClass row = new JSONClass();
				row["path"] = f;
				row["name"] = name;
				row["package"] = pkg;
				arr.Add(row);
			}
		}

		public JSONNode ListPoses(JSONNode args) {
			return ListPresets("Saves/Person/Pose", S(args, "search"), "poses");
		}

		private JSONNode ListPresets(string dir, string search, string key) {
			string s = search.ToLowerInvariant();
			List<string> files = ListJsonFiles(dir);
			JSONArray arr = new JSONArray();
			foreach (string f in files) {
				string name = FileName(f);
				if (s != "" && name.ToLowerInvariant().IndexOf(s) < 0 && f.ToLowerInvariant().IndexOf(s) < 0) continue;
				JSONClass row = new JSONClass();
				row["path"] = f;
				row["name"] = name;
				arr.Add(row);
			}
			JSONClass r = new JSONClass();
			r["count"] = JSONRaw.Num(arr.Count);
			r[key] = arr;
			return r;
		}

		private static void AddPresetFiles(JSONArray arr, string dir, string search) {
			string s = search.ToLowerInvariant();
			foreach (string f in ListJsonFiles(dir)) {
				string name = FileName(f);
				if (s != "" && name.ToLowerInvariant().IndexOf(s) < 0 && f.ToLowerInvariant().IndexOf(s) < 0) continue;
				JSONClass row = new JSONClass();
				row["path"] = f;
				row["name"] = name;
				arr.Add(row);
			}
		}

		public JSONNode LoadLook(JSONNode args) {
			return ApplyPreset(args, "look");
		}

		public JSONNode LoadClothingPreset(JSONNode args) {
			return ApplyPreset(args, "clothing");
		}

		public JSONNode LoadHairPreset(JSONNode args) {
			return ApplyPreset(args, "hair");
		}

		public JSONNode LoadPose(JSONNode args) {
			Atom p = null;
			bool all = Bool(args, "all", false);
			string path = S(args, "path");
			if (path == "") throw new ApiError("path required (see list_poses)");
			JSONArray applied = new JSONArray();
			if (all) {
				foreach (Atom a in SC.GetAtoms()) {
					if (a == null || a.type != "Person") continue;
					ApplyPresetStorables(a, path);
					applied.Add(a.uid);
				}
				if (applied.Count == 0) throw new ApiError("no Person atom in the current scene");
			} else {
				p = RequirePerson(S(args, "person"));
				ApplyPresetStorables(p, path);
				applied.Add(p.uid);
			}
			JSONClass r = new JSONClass();
			r["path"] = path;
			r["kind"] = "pose";
			r["applied"] = applied;
			return r;
		}

		private JSONNode ApplyPreset(JSONNode args, string kind) {
			Atom p = RequirePerson(S(args, "person"));
			string path = S(args, "path");
			if (path == "") throw new ApiError("path required");
			ApplyPresetStorables(p, path);
			JSONClass r = new JSONClass();
			r["person"] = p.uid;
			r["path"] = path;
			r["kind"] = kind;
			return r;
		}

		public JSONNode SaveLook(JSONNode args) {
			Atom p = RequirePerson(S(args, "person"));
			string path = S(args, "path");
			if (path == "") path = "Saves/Person/Appearance/" + p.uid + "_" + Timestamp() + ".json";
			if (!path.EndsWith(".json")) path += ".json";
			SC.SaveFromAtom(PrepareSavePath(path), p, false, true);
			JSONClass r = new JSONClass();
			r["person"] = p.uid;
			r["saved"] = path;
			r["kind"] = "look";
			return r;
		}

		public JSONNode SavePose(JSONNode args) {
			Atom p = RequirePerson(S(args, "person"));
			string path = S(args, "path");
			if (path == "") path = "Saves/Person/Pose/" + p.uid + "_" + Timestamp() + ".json";
			if (!path.EndsWith(".json")) path += ".json";
			SC.SaveFromAtom(PrepareSavePath(path), p, true, false);
			JSONClass r = new JSONClass();
			r["person"] = p.uid;
			r["saved"] = path;
			r["kind"] = "pose";
			return r;
		}

		public JSONNode SaveFullPreset(JSONNode args) {
			Atom p = RequirePerson(S(args, "person"));
			string path = S(args, "path");
			if (path == "") path = "Saves/Person/full/" + p.uid + "_" + Timestamp() + ".json";
			if (!path.EndsWith(".json")) path += ".json";
			SC.SaveFromAtom(PrepareSavePath(path), p, true, true);
			JSONClass r = new JSONClass();
			r["person"] = p.uid;
			r["saved"] = path;
			r["kind"] = "full";
			return r;
		}

		// ===================== morphs (捏人) =====================

		public JSONNode ListMorphs(JSONNode args) {
			Atom p = RequirePerson(S(args, "person"));
			DAZCharacterSelector sel = Selector(p);
			string search = S(args, "search").ToLowerInvariant();
			string region = S(args, "region").ToLowerInvariant();
			string group = S(args, "group").ToLowerInvariant();
			int limit = (int)Num(args, "limit", 500);
			if (limit <= 0) limit = 500;
			JSONArray arr = new JSONArray();
			int count = 0;
			foreach (DAZMorph m in AllMorphs(sel)) {
				string dn = m.displayName != null ? m.displayName : "";
				string uid = m.uid != null ? m.uid : "";
				if (search != "" && dn.ToLowerInvariant().IndexOf(search) < 0 && uid.ToLowerInvariant().IndexOf(search) < 0) continue;
				if (region != "" && (m.region == null || m.region.ToLowerInvariant() != region)) continue;
				if (group != "" && (m.group == null || m.group.ToLowerInvariant() != group)) continue;
				JSONClass row = new JSONClass();
				row["name"] = dn;
				row["uid"] = uid;
				row["region"] = m.region != null ? m.region : "";
				row["group"] = m.group != null ? m.group : "";
				row["value"] = F(m.morphValue);
				arr.Add(row);
				count++;
				if (count >= limit) break;
			}
			JSONClass r = new JSONClass();
			r["person"] = p.uid;
			r["count"] = JSONRaw.Num(count);
			r["morphs"] = arr;
			return r;
		}

		public JSONNode SetMorph(JSONNode args) {
			Atom p = RequirePerson(S(args, "person"));
			DAZCharacterSelector sel = Selector(p);
			string name = S(args, "name");
			if (name == "") throw new ApiError("name required (see list_morphs)");
			float value = Num(args, "value", 0f);
			DAZMorph m = FindMorph(sel, name);
			if (m == null) throw new ApiError("morph not found: " + name + " (see list_morphs)");
			m.morphValue = value;
			JSONClass r = new JSONClass();
			r["person"] = p.uid;
			r["name"] = m.displayName != null ? m.displayName : name;
			r["value"] = F(m.morphValue);
			return r;
		}

		public JSONNode GetMorph(JSONNode args) {
			Atom p = RequirePerson(S(args, "person"));
			DAZCharacterSelector sel = Selector(p);
			string name = S(args, "name");
			DAZMorph m = FindMorph(sel, name);
			if (m == null) throw new ApiError("morph not found: " + name + " (see list_morphs)");
			JSONClass r = new JSONClass();
			r["person"] = p.uid;
			r["name"] = m.displayName != null ? m.displayName : name;
			r["region"] = m.region != null ? m.region : "";
			r["group"] = m.group != null ? m.group : "";
			r["value"] = F(m.morphValue);
			return r;
		}

		public JSONNode ResetMorphs(JSONNode args) {
			Atom p = RequirePerson(S(args, "person"));
			DAZCharacterSelector sel = Selector(p);
			string region = S(args, "region").ToLowerInvariant();
			int reset = 0;
			foreach (DAZMorph m in AllMorphs(sel)) {
				if (region != "" && (m.region == null || m.region.ToLowerInvariant() != region)) continue;
				if (m.morphValue != 0f) {
					m.morphValue = 0f;
					reset++;
				}
			}
			JSONClass r = new JSONClass();
			r["person"] = p.uid;
			r["reset"] = JSONRaw.Num(reset);
			if (region != "") r["region"] = region;
			return r;
		}

		// ===================== clothing / hair =====================

		/// <summary>Clothing items on a person. <c>sel.clothingItems</c> is the person's whole
		/// clothing *library* (every item VaM has instantiated, worn or not), so by default we
		/// return only what is actually worn and summarise the rest. Pass <c>all=true</c> for the
		/// full library.</summary>
		public JSONNode ListClothingItems(JSONNode args) {
			Atom p = RequirePerson(S(args, "person"));
			DAZCharacterSelector sel = Selector(p);
			bool all = Bool(args, "all", false);
			DAZClothingItem creator = CreatorFor(sel);
			JSONArray arr = new JSONArray();
			JSONArray worn = new JSONArray();
			DAZClothingItem[] items = sel.clothingItems;
			int total = 0;
			int active = 0;
			if (items != null) {
				foreach (DAZClothingItem it in items) {
					if (it == null) continue;
					bool isCreator = creator != null && it == creator;
					if (!isCreator) total++;
					bool on = it.active;
					if (on && !isCreator) {
						active++;
						worn.Add(it.uid);
					}
					if (!all && (!on || isCreator)) continue;
					JSONClass row = new JSONClass();
					row["uid"] = it.uid;
					row["name"] = it.displayName != null ? it.displayName : "";
					row["active"] = JSONRaw.Bool(on);
					if (isCreator) row["creator"] = JSONRaw.Bool(true);
					arr.Add(row);
				}
			}
			JSONClass r = new JSONClass();
			r["person"] = p.uid;
			r["count"] = JSONRaw.Num(arr.Count);
			r["activeCount"] = JSONRaw.Num(active);
			r["libraryCount"] = JSONRaw.Num(total);
			r["worn"] = worn;
			r["items"] = arr;
			return r;
		}

		public JSONNode ListHairItems(JSONNode args) {
			Atom p = RequirePerson(S(args, "person"));
			DAZCharacterSelector sel = Selector(p);
			JSONArray arr = new JSONArray();
			DAZHairGroup[] items = sel.hairItems;
			if (items != null) {
				foreach (DAZHairGroup it in items) {
					if (it == null) continue;
					JSONClass row = new JSONClass();
					row["uid"] = it.uid;
					row["name"] = it.displayName != null ? it.displayName : "";
					row["active"] = JSONRaw.Bool(it.active);
					arr.Add(row);
				}
			}
			JSONClass r = new JSONClass();
			r["person"] = p.uid;
			r["count"] = JSONRaw.Num(arr.Count);
			r["items"] = arr;
			return r;
		}

		private static DAZClothingItem CreatorFor(DAZCharacterSelector sel) {
			if (sel == null) return null;
			return sel.gender == DAZCharacterSelector.Gender.Male ? sel.maleClothingCreatorItem : sel.femaleClothingCreatorItem;
		}

		/// <summary>Resolve a clothing item by uid, display name or the tail of its path.
		/// <c>GetClothingItem</c> only matches uids, which are package paths in practice.</summary>
		private static DAZClothingItem FindClothingItem(DAZCharacterSelector sel, string id) {
			DAZClothingItem[] items = sel.clothingItems;
			if (items == null || id == null) return null;
			string needle = id.Trim();
			if (needle == "") return null;
			string low = needle.ToLowerInvariant();
			DAZClothingItem nameHit = null;
			DAZClothingItem tailHit = null;
			foreach (DAZClothingItem it in items) {
				if (it == null) continue;
				if (it.uid == needle) return it;
				string name = it.displayName != null ? it.displayName : "";
				if (name == needle) return it;
				string uidLow = it.uid != null ? it.uid.ToLowerInvariant() : "";
				if (nameHit == null && name.ToLowerInvariant() == low) nameHit = it;
				if (tailHit == null && uidLow.Length > 0) {
					string tail = uidLow.Substring(uidLow.LastIndexOf('/') + 1);
					if (tail == low) tailHit = it;
				}
			}
			return nameHit != null ? nameHit : tailHit;
		}

		/// <summary>uids of worn garments, excluding the clothing *creator* helper item
		/// (activating the creator is an implementation detail, not a garment).</summary>
		private static HashSet<string> ActiveClothingUids(DAZCharacterSelector sel) {
			HashSet<string> set = new HashSet<string>();
			DAZClothingItem[] items = sel.clothingItems;
			if (items == null) return set;
			DAZClothingItem creator = CreatorFor(sel);
			foreach (DAZClothingItem it in items) {
				if (it == null || it == creator) continue;
				if (it.active && it.uid != null) set.Add(it.uid);
			}
			return set;
		}

		/// <summary>Poller tool. Adds a clothing item and *waits until VaM confirms it exists*.
		/// A previous version returned <c>loading:true</c> unconditionally: VaM silently no-ops
		/// LoadFromPath when the person's runtime clothing creator was never initialised (its
		/// Person > Clothing tab has not been opened in this scene), so callers were told an add
		/// succeeded while the scene stayed unchanged.</summary>
		public JSONNode AddClothingItem(JSONNode args) {
			Atom p = Mt.Run(() => RequirePerson(S(args, "person")), 15000);
			string path = S(args, "path");
			if (path == "") throw new ApiError("path required (clothing item .vam/.json file, e.g. from list_clothing_presets)");
			int waitSeconds = Has(args, "wait_seconds") ? args["wait_seconds"].AsInt : 20;
			if (waitSeconds < 1) waitSeconds = 1;
			string wanted = path.Substring(path.LastIndexOf('/') + 1);
			if (wanted.EndsWith(".vam", StringComparison.OrdinalIgnoreCase)) wanted = wanted.Substring(0, wanted.Length - 4);
			else if (wanted.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) wanted = wanted.Substring(0, wanted.Length - 5);

			DAZClothingItem creator = Mt.Run(() => CreatorFor(Selector(p)), 15000);
			if (creator == null) throw new ApiError("no clothing creator item available on " + p.uid);

			// Already worn? Then this is a no-op and should say so instead of "loading".
			DAZClothingItem existing = Mt.Run(() => FindClothingItem(Selector(p), wanted), 15000);
			if (existing != null && existing.active) {
				JSONClass done = new JSONClass();
				done["person"] = p.uid;
				done["path"] = path;
				done["ok"] = JSONRaw.Bool(true);
				done["added"] = existing.uid;
				done["alreadyActive"] = JSONRaw.Bool(true);
				return done;
			}

			HashSet<string> before = Mt.Run(() => ActiveClothingUids(Selector(p)), 15000);
			int libraryBefore = Mt.Run(() => {
				DAZClothingItem[] its = Selector(p).clothingItems;
				return its == null ? 0 : its.Length;
			}, 15000);

			Mt.Run(() => { try { Selector(p).SetActiveClothingItem(creator, true); } catch { } }, 15000);
			Component drc = PollComponent(creator, 40);
			if (drc == null) {
				JSONClass fail = new JSONClass();
				fail["person"] = p.uid;
				fail["path"] = path;
				fail["ok"] = JSONRaw.Bool(false);
				fail["reason"] = "no DAZRuntimeCreator component on the clothing creator item";
				fail["hint"] = "Open " + p.uid + " > Clothing in VaM once (this initialises the runtime creator), then retry.";
				return fail;
			}

			Mt.Run(() => Invoke(drc, "LoadFromPath", path), 15000);

			// LoadFromPath starts an async asset load; poll until a new item appears, becomes
			// active, or the deadline passes.
			DateTime deadline = DateTime.UtcNow.AddSeconds(waitSeconds);
			string addedUid = null;
			string createdUid = null;
			while (DateTime.UtcNow < deadline) {
				JSONClass snap = Mt.Run(() => {
					DAZCharacterSelector sel = Selector(p);
					DAZClothingItem[] its = sel.clothingItems;
					JSONClass o = new JSONClass();
					int n = its == null ? 0 : its.Length;
					o["library"] = JSONRaw.Num(n);
					DAZClothingItem found = FindClothingItem(sel, wanted);
					if (found == creator) found = null;
					if (found != null && found.active) o["active"] = found.uid;
					else if (found != null) o["present"] = found.uid;
					return o;
				}, 15000);
				if (snap["active"] != null) { addedUid = snap["active"].Value; break; }
				if (snap["present"] != null) createdUid = snap["present"].Value;
				Thread.Sleep(500);
			}

			// Fallback: any newly worn item we did not have before (covers renamed uids).
			if (addedUid == null) {
				HashSet<string> after = Mt.Run(() => ActiveClothingUids(Selector(p)), 15000);
				foreach (string uid in after) {
					if (!before.Contains(uid)) { addedUid = uid; break; }
				}
			}

			// The item may already sit in the person's library (inactive): LoadFromPath then
			// does nothing useful, but activating it is exactly what the Clothing tab's
			// toggle does, and it works without the runtime creator.
			bool activated = false;
			if (addedUid == null && createdUid != null) {
				string target = createdUid;
				addedUid = Mt.Run(() => {
					DAZCharacterSelector sel = Selector(p);
					DAZClothingItem it = FindClothingItem(sel, target);
					if (it == null || it == CreatorFor(sel)) return null;
					sel.SetActiveClothingItem(it, true);
					return it.active ? it.uid : null;
				}, 15000);
				activated = addedUid != null;
			}

			JSONClass r = new JSONClass();
			r["person"] = p.uid;
			r["path"] = path;
			if (addedUid != null) {
				r["ok"] = JSONRaw.Bool(true);
				r["added"] = addedUid;
				if (activated) r["activated"] = JSONRaw.Bool(true);
				r["worn"] = JSONRaw.Num(Mt.Run(() => ActiveClothingUids(Selector(p)).Count, 15000));
				return r;
			}

			int libraryAfter = Mt.Run(() => {
				DAZClothingItem[] its = Selector(p).clothingItems;
				return its == null ? 0 : its.Length;
			}, 15000);
			r["ok"] = JSONRaw.Bool(false);
			r["reason"] = createdUid != null
				? "item was created but never became active"
				: "VaM created no clothing item for this path";
			r["libraryBefore"] = JSONRaw.Num(libraryBefore);
			r["libraryAfter"] = JSONRaw.Num(libraryAfter);
			r["hint"] = "If nothing changed, VaM's runtime clothing creator is not initialised: "
				+ "open " + p.uid + " > Clothing in VaM once, then retry. Paths must point at a .vam "
				+ "clothing item (see list_clothing_presets), not at a folder.";
			return r;
		}

		/// <summary>Poller tool. Adds a hair item to a person from its .vam/.json path.</summary>
		public JSONNode AddHairItem(JSONNode args) {
			Atom p = Mt.Run(() => RequirePerson(S(args, "person")), 15000);
			string path = S(args, "path");
			if (path == "") throw new ApiError("path required (hair item .vam/.json file, e.g. from list_hair_presets)");
			DAZHairGroup creator = Mt.Run(delegate {
				DAZCharacterSelector sel = Selector(p);
				return sel.gender == DAZCharacterSelector.Gender.Male ? sel.maleHairCreatorItem : sel.femaleHairCreatorItem;
			}, 15000);
			if (creator == null) throw new ApiError("no hair creator item available");
			Mt.Run(() => { try { Invoke(Selector(p), "SetActiveHairItem", creator, true); } catch { } }, 15000);
			Component drc = PollComponent(creator, 40);
			if (drc == null) throw new ApiError("no DAZRuntimeCreator component on the creator item (character may still be loading; retry in a few seconds)");
			Mt.Run(() => Invoke(drc, "LoadFromPath", path), 15000);
			JSONClass r = new JSONClass();
			r["person"] = p.uid;
			r["path"] = path;
			r["loading"] = JSONRaw.Bool(true);
			return r;
		}

		/// <summary>Poll (on the HTTP thread, via the dispatcher) until the component appears or the deadline passes.</summary>
		private Component PollComponent(Component root, int seconds) {
			DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
			while (DateTime.UtcNow < deadline) {
				Component c = Mt.Run(() => FindComponent(root, "DAZRuntimeCreator"), 10000);
				if (c != null) return c;
				Thread.Sleep(500);
			}
			return null;
		}

		public JSONNode RemoveClothingItem(JSONNode args) {
			Atom p = RequirePerson(S(args, "person"));
			DAZCharacterSelector sel = Selector(p);
			string id = S(args, "id");
			if (id == "") throw new ApiError("id required (see list_clothing_items)");
			DAZClothingItem item = FindClothingItem(sel, id);
			if (item == null) throw new ApiError("clothing item not found: " + id + " (see list_clothing_items all=true)");
			if (item == CreatorFor(sel)) throw new ApiError("refusing to remove the clothing creator item; remove the garments instead");
			string removedUid = item.uid;
			sel.SetActiveClothingItem(item, false);
			DAZClothingItemControl ctrl = item.GetComponentInChildren<DAZClothingItemControl>();
			if (ctrl != null) {
				try { ctrl.Delete(); } catch (Exception e) { Log.Debug("clothing delete: " + e.Message); }
			}
			// Delete() is deferred to the end of the frame and this tool runs on the main
			// thread, so we can only report the immediate, verifiable effects here.
			DAZClothingItem[] left = Selector(p).clothingItems;
			JSONClass r = new JSONClass();
			r["person"] = p.uid;
			r["removed"] = removedUid;
			r["on"] = JSONRaw.Bool(item.active);
			r["libraryCount"] = JSONRaw.Num(left == null ? 0 : left.Length);
			r["note"] = "VaM destroys the item at the end of the frame; call list_clothing_items to confirm it is gone.";
			return r;
		}

		public JSONNode RemoveHairItem(JSONNode args) {
			Atom p = RequirePerson(S(args, "person"));
			DAZCharacterSelector sel = Selector(p);
			string id = S(args, "id");
			if (id == "") throw new ApiError("id required (see list_hair_items)");
			DAZHairGroup item = null;
			DAZHairGroup[] items = sel.hairItems;
			if (items != null) {
				foreach (DAZHairGroup it in items) {
					if (it != null && it.uid == id) { item = it; break; }
				}
			}
			if (item == null) throw new ApiError("hair item not found: " + id);
			Invoke(sel, "SetActiveHairItem", item, false);
			DAZHairGroupControl ctrl = item.GetComponentInChildren<DAZHairGroupControl>();
			if (ctrl != null) {
				try { ctrl.Delete(); } catch (Exception e) { Log.Debug("hair delete: " + e.Message); }
			}
			JSONClass r = new JSONClass();
			r["person"] = p.uid;
			r["removed"] = id;
			return r;
		}

		public JSONNode SetClothingItemOn(JSONNode args) {
			Atom p = RequirePerson(S(args, "person"));
			DAZCharacterSelector sel = Selector(p);
			string id = S(args, "id");
			if (id == "") throw new ApiError("id required (see list_clothing_items)");
			bool on = Bool(args, "on", true);
			DAZClothingItem item = FindClothingItem(sel, id);
			if (item == null) throw new ApiError("clothing item not found: " + id + " (see list_clothing_items all=true)");
			sel.SetActiveClothingItem(item, on);
			JSONClass r = new JSONClass();
			r["person"] = p.uid;
			r["id"] = item.uid;
			r["on"] = JSONRaw.Bool(item.active);
			r["changed"] = JSONRaw.Bool(item.active == on);
			return r;
		}

		public JSONNode SetHairItemOn(JSONNode args) {
			Atom p = RequirePerson(S(args, "person"));
			DAZCharacterSelector sel = Selector(p);
			string id = S(args, "id");
			bool on = Bool(args, "on", true);
			DAZHairGroup item = null;
			DAZHairGroup[] items = sel.hairItems;
			if (items != null) {
				foreach (DAZHairGroup it in items) {
					if (it != null && it.uid == id) { item = it; break; }
				}
			}
			if (item == null) throw new ApiError("hair item not found: " + id);
			Invoke(sel, "SetActiveHairItem", item, on);
			JSONClass r = new JSONClass();
			r["person"] = p.uid;
			r["id"] = id;
			r["on"] = JSONRaw.Bool(on);
			return r;
		}

		// ===================== expressions =====================

		private static bool LooksLikeExpression(DAZMorph m) {
			string n = (m.displayName != null ? m.displayName : "").ToLowerInvariant();
			string g = (m.group != null ? m.group : "").ToLowerInvariant();
			string reg = (m.region != null ? m.region : "").ToLowerInvariant();
			if (g.IndexOf("expression") >= 0) return true;
			if (reg.IndexOf("face") < 0 && reg.IndexOf("head") < 0) return false;
			string[] kws = {
				"smile", "frown", "surpris", "neutral", "angr", "sad", "happy", "pleasure", "pain", "pout",
				"grin", "laugh", "cry", "blush", "kiss", "tongue", "bite", "eye roll", "extreme pleasure",
				"enjoying", "taking it", "mouth resting", "lip bite", "shy", "squint", "wink", "grit",
				"moan", "lust", "ecstasy", "agony", "scream", "whimper", "sulk", "smirk", "scowl", "snarl",
				"glare", "flirt", "seduct", "sultry", "open mouth", "relaxed", "lips"
			};
			foreach (string k in kws) {
				if (n.IndexOf(k) >= 0) return true;
			}
			return false;
		}

		public JSONNode ListExpressions(JSONNode args) {
			Atom p = RequirePerson(S(args, "person"));
			DAZCharacterSelector sel = Selector(p);
			JSONArray arr = new JSONArray();
			foreach (DAZMorph m in AllMorphs(sel)) {
				if (!LooksLikeExpression(m)) continue;
				JSONClass row = new JSONClass();
				row["name"] = m.displayName != null ? m.displayName : "";
				row["uid"] = m.uid != null ? m.uid : "";
				row["region"] = m.region != null ? m.region : "";
				row["value"] = F(m.morphValue);
				arr.Add(row);
			}
			JSONClass r = new JSONClass();
			r["person"] = p.uid;
			r["count"] = JSONRaw.Num(arr.Count);
			r["expressions"] = arr;
			return r;
		}

		public JSONNode SetExpression(JSONNode args) {
			Atom p = RequirePerson(S(args, "person"));
			DAZCharacterSelector sel = Selector(p);
			string name = S(args, "name");
			if (name == "") throw new ApiError("name required (see list_expressions)");
			float value = Num(args, "value", 1f);
			bool reset = Bool(args, "reset", true);
			int cleared = 0;
			if (reset) {
				foreach (DAZMorph m in AllMorphs(sel)) {
					if (LooksLikeExpression(m) && m.morphValue != 0f) {
						m.morphValue = 0f;
						cleared++;
					}
				}
			}
			DAZMorph target = FindMorph(sel, name);
			if (target == null) throw new ApiError("expression morph not found: " + name + " (see list_expressions)");
			target.morphValue = value;
			JSONClass r = new JSONClass();
			r["person"] = p.uid;
			r["name"] = target.displayName != null ? target.displayName : name;
			r["value"] = F(target.morphValue);
			r["cleared"] = JSONRaw.Num(cleared);
			return r;
		}

		// ===================== controls =====================

		public JSONNode ListControls(JSONNode args) {
			Atom p = RequirePerson(S(args, "person"));
			List<string> names = SC.GetFreeControllerNamesInAtom(p.uid);
			JSONArray arr = new JSONArray();
			if (names != null) {
				foreach (string n in names) arr.Add(n);
			}
			JSONClass r = new JSONClass();
			r["person"] = p.uid;
			r["count"] = JSONRaw.Num(arr.Count);
			r["controls"] = arr;
			return r;
		}

		private static FreeControllerV3 GetControl(Atom p, string controlId) {
			JSONStorable st = p.GetStorableByID(controlId);
			FreeControllerV3 fc = st as FreeControllerV3;
			if (fc == null) {
				string hint = "";
				try {
					List<string> names = SC.GetFreeControllerNamesInAtom(p.uid);
					if (names != null && names.Count > 0) hint = "available: " + string.Join(", ", names.ToArray());
				} catch { }
				throw new ApiError("control not found: " + controlId + (hint != "" ? " (" + hint + ")" : ""));
			}
			return fc;
		}

		public JSONNode GetControl(JSONNode args) {
			Atom p = RequirePerson(S(args, "person"));
			string controlId = S(args, "control");
			if (controlId == "") throw new ApiError("control required (see list_controls)");
			FreeControllerV3 fc = GetControl(p, controlId);
			JSONClass r = new JSONClass();
			r["person"] = p.uid;
			r["control"] = controlId;
			r["position"] = Vec(fc.control.position);
			r["rotation"] = Vec(fc.control.eulerAngles);
			return r;
		}

		public JSONNode SetControl(JSONNode args) {
			Atom p = RequirePerson(S(args, "person"));
			string controlId = S(args, "control");
			if (controlId == "") throw new ApiError("control required (see list_controls)");
			FreeControllerV3 fc = GetControl(p, controlId);
			if (Has(args, "position")) fc.SetPositionNoForce(ParseVec3(args["position"]));
			if (Has(args, "rotation")) fc.SetRotationNoForce(ParseVec3(args["rotation"]));
			JSONClass r = new JSONClass();
			r["person"] = p.uid;
			r["control"] = controlId;
			r["position"] = Vec(fc.control.position);
			r["rotation"] = Vec(fc.control.eulerAngles);
			return r;
		}

		public JSONNode SetGaze(JSONNode args) {
			Atom p = RequirePerson(S(args, "person"));
			JSONStorable lookAt = p.GetStorableByID("lookAt");
			if (lookAt == null) throw new ApiError("no lookAt storable on " + p.uid);
			JSONClass applied = new JSONClass();
			float amount = Num(args, "amount", -1f);
			string targetControl = S(args, "targetControl");
			if (amount >= 0f) {
				if (lookAt.GetFloatParamNames().Contains("gazeAmount")) {
					lookAt.SetFloatParamValue("gazeAmount", amount);
					applied["gazeAmount"] = F(amount);
				}
			}
			if (targetControl != "") {
				if (lookAt.GetStringChooserParamNames().Contains("target")) {
					lookAt.SetStringChooserParamValue("target", targetControl);
					applied["target"] = targetControl;
				} else if (lookAt.GetStringParamNames().Contains("target")) {
					lookAt.SetStringParamValue("target", targetControl);
					applied["target"] = targetControl;
				}
			}
			if (applied.Count == 0) {
				throw new ApiError("lookAt storable has no controllable gaze params; use list_storable_params with storable=lookAt to see what it offers");
			}
			JSONClass r = new JSONClass();
			r["person"] = p.uid;
			r["applied"] = applied;
			return r;
		}

		// ===================== generic param access =====================

		public JSONNode ListAtomStorables(JSONNode args) {
			Atom a = FindAtom(S(args, "uid"));
			List<string> ids = a.GetStorableIDs();
			JSONArray arr = new JSONArray();
			if (ids != null) {
				foreach (string id in ids) {
					JSONStorable st = a.GetStorableByID(id);
					if (st == null) continue;
					JSONClass row = new JSONClass();
					row["id"] = id;
					row["type"] = st.GetType().Name;
					arr.Add(row);
				}
			}
			JSONClass r = new JSONClass();
			r["uid"] = a.uid;
			r["count"] = JSONRaw.Num(arr.Count);
			r["storables"] = arr;
			return r;
		}

		public JSONNode ListStorableParams(JSONNode args) {
			Atom a = FindAtom(S(args, "uid"));
			string sid = S(args, "storable");
			JSONStorable st = a.GetStorableByID(sid);
			if (st == null) throw new ApiError("storable not found: " + sid + " on " + a.uid);
			JSONClass r = new JSONClass();
			r["uid"] = a.uid;
			r["storable"] = sid;
			r["type"] = st.GetType().Name;

			JSONArray floats = new JSONArray();
			foreach (string n in SortedNames(st.GetFloatParamNames())) {
				try {
					JSONClass p = new JSONClass();
					p["name"] = n;
					p["value"] = F(st.GetFloatParamValue(n));
					JSONStorableFloat f = GetFloatParamObj(st, n);
					if (f != null) {
						p["min"] = F(f.min);
						p["max"] = F(f.max);
						p["default"] = F(f.defaultVal);
					}
					floats.Add(p);
				} catch (Exception e) { Log.Debug("float param " + n + ": " + e.Message); }
			}
			r["floats"] = floats;

			JSONArray bools = new JSONArray();
			foreach (string n in SortedNames(st.GetBoolParamNames())) {
				try {
					JSONClass p = new JSONClass();
					p["name"] = n;
					p["value"] = JSONRaw.Bool(st.GetBoolParamValue(n));
					bools.Add(p);
				} catch (Exception e) { Log.Debug("bool param " + n + ": " + e.Message); }
			}
			r["bools"] = bools;

			JSONArray strings = new JSONArray();
			foreach (string n in SortedNames(st.GetStringParamNames())) {
				try {
					JSONClass p = new JSONClass();
					p["name"] = n;
					p["value"] = st.GetStringParamValue(n);
					strings.Add(p);
				} catch (Exception e) { Log.Debug("string param " + n + ": " + e.Message); }
			}
			r["strings"] = strings;

			JSONArray urls = new JSONArray();
			foreach (string n in SortedNames(st.GetUrlParamNames())) {
				try {
					JSONClass p = new JSONClass();
					p["name"] = n;
					p["value"] = st.GetUrlParamValue(n);
					urls.Add(p);
				} catch (Exception e) { Log.Debug("url param " + n + ": " + e.Message); }
			}
			r["urls"] = urls;

			JSONArray choosers = new JSONArray();
			foreach (string n in SortedNames(st.GetStringChooserParamNames())) {
				try {
					JSONClass p = new JSONClass();
					p["name"] = n;
					p["value"] = st.GetStringChooserParamValue(n);
					JSONStorableStringChooser ch = GetChooserObj(st, n);
					if (ch != null && ch.choices != null) {
						JSONArray choices = new JSONArray();
						foreach (string c in ch.choices) choices.Add(c);
						p["choices"] = choices;
					}
					choosers.Add(p);
				} catch (Exception e) { Log.Debug("chooser param " + n + ": " + e.Message); }
			}
			r["choosers"] = choosers;

			JSONArray colors = new JSONArray();
			foreach (string n in SortedNames(st.GetColorParamNames())) {
				try {
					JSONClass p = new JSONClass();
					p["name"] = n;
					p["value"] = ColorToHex(st.GetColorParamValue(n));
					colors.Add(p);
				} catch (Exception e) { Log.Debug("color param " + n + ": " + e.Message); }
			}
			r["colors"] = colors;

			JSONArray actions = new JSONArray();
			foreach (string n in SortedNames(st.GetActionNames())) {
				try {
					JSONClass p = new JSONClass();
					p["name"] = n;
					actions.Add(p);
				} catch (Exception e) { Log.Debug("action " + n + ": " + e.Message); }
			}
			r["actions"] = actions;

			JSONArray customParams = new JSONArray();
			try {
				string[] custom = st.GetCustomParamNames();
				if (custom != null) {
					foreach (string n in custom) {
						JSONClass p = new JSONClass();
						p["name"] = n;
						customParams.Add(p);
					}
				}
			} catch (Exception e) { Log.Debug("custom params: " + e.Message); }
			r["customParams"] = customParams;

			JSONArray properties = new JSONArray();
			AddSimpleMembers(properties, st, 200);
			r["properties"] = properties;
			return r;
		}

		private static List<string> SortedNames(List<string> names) {
			List<string> list = names != null ? new List<string>(names) : new List<string>();
			list.Sort();
			return list;
		}

		private static JSONStorableFloat GetFloatParamObj(JSONStorable st, string name) {
			Dictionary<string, JSONStorableFloat> d = FloatParams(st);
			if (d == null) return null;
			JSONStorableFloat f;
			return d.TryGetValue(name, out f) ? f : null;
		}

		private static JSONStorableStringChooser GetChooserObj(JSONStorable st, string name) {
			Dictionary<string, JSONStorableStringChooser> d = ChooserParams(st);
			if (d == null) return null;
			JSONStorableStringChooser c;
			return d.TryGetValue(name, out c) ? c : null;
		}

		// ---------- generic property access (covers customParamNames like gravityX, DAZBone.position, ...) ----------

		private static bool IsSimpleType(Type t) {
			return t == typeof(float) || t == typeof(double) || t == typeof(int) || t == typeof(long) || t == typeof(bool)
				|| t == typeof(string) || t == typeof(Vector3) || t == typeof(Color) || t.IsEnum;
		}

		private static MemberInfo FindPropOrField(Type t, string name) {
			BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
			PropertyInfo p = t.GetProperty(name, flags);
			if (p != null) {
				if (p.GetIndexParameters().Length > 0) return null;
				if (!IsSimpleType(p.PropertyType)) return null;
				if (p.GetGetMethod() == null && p.GetSetMethod() == null) return null;
				return p;
			}
			foreach (PropertyInfo pp in t.GetProperties(flags)) {
				if (pp.GetIndexParameters().Length > 0) continue;
				if (string.Equals(pp.Name, name, StringComparison.OrdinalIgnoreCase) && IsSimpleType(pp.PropertyType)) return pp;
			}
			FieldInfo f = t.GetField(name, flags);
			if (f != null && IsSimpleType(f.FieldType)) return f;
			foreach (FieldInfo ff in t.GetFields(flags)) {
				if (string.Equals(ff.Name, name, StringComparison.OrdinalIgnoreCase) && IsSimpleType(ff.FieldType)) return ff;
			}
			return null;
		}

		private static Type MemberType(MemberInfo m) {
			if (m is PropertyInfo) return ((PropertyInfo)m).PropertyType;
			return ((FieldInfo)m).FieldType;
		}

		private static object GetMemberValue(MemberInfo m, object target) {
			if (m is PropertyInfo) return ((PropertyInfo)m).GetValue(target, null);
			return ((FieldInfo)m).GetValue(target);
		}

		private static void SetMemberValue(MemberInfo m, object target, object value) {
			if (m is PropertyInfo) {
				((PropertyInfo)m).SetValue(target, value, null);
			} else {
				((FieldInfo)m).SetValue(target, value);
			}
		}

		public static JSONNode SerializePropValue(object v) {
			if (v == null) return JSONRaw.Null();
			if (v is float) return JSONRaw.Num((float)v);
			if (v is double) return JSONRaw.Num((float)(double)v);
			if (v is int) return JSONRaw.Num((int)v);
			if (v is long) return JSONRaw.Num((long)v);
			if (v is bool) return JSONRaw.Bool((bool)v);
			if (v is string) return new JSONData((string)v);
			if (v is Vector3) {
				Vector3 vec = (Vector3)v;
				JSONClass r = new JSONClass();
				r["x"] = F(vec.x);
				r["y"] = F(vec.y);
				r["z"] = F(vec.z);
				return r;
			}
			if (v is Color) {
				Color c = (Color)v;
				return new JSONData("#" + ColorUtility.ToHtmlStringRGBA(c));
			}
			if (v.GetType().IsEnum) return new JSONData(v.ToString());
			return new JSONData(v.ToString());
		}

		public static object ParsePropValue(Type pt, JSONNode value) {
			if (pt == typeof(float)) return value.AsFloat;
			if (pt == typeof(double)) return (double)value.AsFloat;
			if (pt == typeof(int)) return value.AsInt;
			if (pt == typeof(long)) return (long)value.AsFloat;
			if (pt == typeof(bool)) return value.AsBool;
			if (pt == typeof(string)) return value.Value;
			if (pt == typeof(Vector3)) return ParseVec3(value);
			if (pt == typeof(Color)) {
				Color c;
				if (value.Value.StartsWith("#") && ColorUtility.TryParseHtmlString(value.Value, out c)) return c;
				JSONArray a = value.AsArray;
				if (a != null && a.Count >= 3) {
					return new Color(a[0].AsFloat, a[1].AsFloat, a[2].AsFloat, a.Count > 3 ? a[3].AsFloat : 1f);
				}
				throw new ApiError("bad color: " + value.Value + " (use #RRGGBB or [r,g,b,a])");
			}
			if (pt.IsEnum) return Enum.Parse(pt, value.Value, true);
			throw new ApiError("unsupported property type: " + pt.Name);
		}

		private static void AddSimpleMembers(JSONArray arr, object target, int cap) {
			Type t = target.GetType();
			HashSet<string> seen = new HashSet<string>();
			int count = 0;
			BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
			List<string> names = new List<string>();
			foreach (PropertyInfo p in t.GetProperties(flags)) {
				if (p.GetIndexParameters().Length > 0) continue;
				if (IsSimpleType(p.PropertyType) && p.GetGetMethod() != null) names.Add(p.Name);
			}
			foreach (FieldInfo f in t.GetFields(flags)) {
				if (IsSimpleType(f.FieldType)) names.Add(f.Name);
			}
			names.Sort(StringComparer.OrdinalIgnoreCase);
			foreach (string n in names) {
				if (!seen.Add(n)) continue;
				try {
					MemberInfo m = FindPropOrField(t, n);
					if (m == null) continue;
					JSONClass row = new JSONClass();
					row["name"] = n;
					row["type"] = MemberType(m).Name;
					row["value"] = SerializePropValue(GetMemberValue(m, target));
					arr.Add(row);
					count++;
					if (count >= cap) break;
				} catch { }
			}
		}

		public static string ColorToHex(HSVColor hsv) {
			Color c = Color.HSVToRGB(hsv.H, hsv.S, hsv.V);
			return "#" + ColorUtility.ToHtmlStringRGB(c);
		}

		public static HSVColor ParseColor(string s) {
			Color c;
			if (s.StartsWith("#")) {
				if (!ColorUtility.TryParseHtmlString(s, out c)) throw new ApiError("bad color: " + s + " (use #RRGGBB or r,g,b[,a])");
			} else {
				string[] parts = s.Split(',');
				if (parts.Length < 3) throw new ApiError("bad color: " + s + " (use #RRGGBB or r,g,b[,a])");
				c = new Color(
					float.Parse(parts[0].Trim(), CultureInfo.InvariantCulture),
					float.Parse(parts[1].Trim(), CultureInfo.InvariantCulture),
					float.Parse(parts[2].Trim(), CultureInfo.InvariantCulture),
					parts.Length > 3 ? float.Parse(parts[3].Trim(), CultureInfo.InvariantCulture) : 1f
				);
			}
			float h, s2, v;
			Color.RGBToHSV(c, out h, out s2, out v);
			HSVColor hsv = new HSVColor();
			hsv.H = h;
			hsv.S = s2;
			hsv.V = v;
			return hsv;
		}

		public JSONNode GetParam(JSONNode args) {
			Atom a = FindAtom(S(args, "uid"));
			JSONStorable st = a.GetStorableByID(S(args, "storable"));
			if (st == null) throw new ApiError("storable not found: " + S(args, "storable") + " on " + a.uid);
			string name = S(args, "param");
			if (name == "") throw new ApiError("param required");
			JSONClass r = new JSONClass();
			r["uid"] = a.uid;
			r["storable"] = S(args, "storable");
			r["param"] = name;
			if (st.GetFloatParamNames().Contains(name)) {
				r["type"] = "float";
				r["value"] = F(st.GetFloatParamValue(name));
				JSONStorableFloat f = GetFloatParamObj(st, name);
				if (f != null) {
					r["min"] = F(f.min);
					r["max"] = F(f.max);
				}
			} else if (st.GetBoolParamNames().Contains(name)) {
				r["type"] = "bool";
				r["value"] = JSONRaw.Bool(st.GetBoolParamValue(name));
			} else if (st.IsUrlJSONParam(name)) {
				r["type"] = "url";
				r["value"] = st.GetUrlParamValue(name);
			} else if (st.GetStringParamNames().Contains(name)) {
				r["type"] = "string";
				r["value"] = st.GetStringParamValue(name);
			} else if (st.GetStringChooserParamNames().Contains(name)) {
				r["type"] = "chooser";
				r["value"] = st.GetStringChooserParamValue(name);
				JSONStorableStringChooser ch = GetChooserObj(st, name);
				if (ch != null && ch.choices != null) {
					JSONArray choices = new JSONArray();
					foreach (string c in ch.choices) choices.Add(c);
					r["choices"] = choices;
				}
			} else if (st.GetColorParamNames().Contains(name)) {
				r["type"] = "color";
				r["value"] = ColorToHex(st.GetColorParamValue(name));
			} else {
				// fall back to a public property/field (covers customParamNames like gravityX, DAZBone.position)
				MemberInfo m = FindPropOrField(st.GetType(), name);
				if (m == null) throw new ApiError("param not found: " + name + " on " + S(args, "storable") + " (see list_storable_params)");
				r["type"] = "property";
				r["propertyType"] = MemberType(m).Name;
				r["value"] = SerializePropValue(GetMemberValue(m, st));
			}
			return r;
		}

		public JSONNode SetParam(JSONNode args) {
			Atom a = FindAtom(S(args, "uid"));
			string sid = S(args, "storable");
			JSONStorable st = a.GetStorableByID(sid);
			if (st == null) throw new ApiError("storable not found: " + sid + " on " + a.uid);
			string name = S(args, "param");
			if (name == "") throw new ApiError("param required");
			JSONNode value = args["value"];
			if (value == null) throw new ApiError("value required");
			string type = S(args, "type");
			if (type == "" || type == "auto") {
				if (st.GetFloatParamNames().Contains(name)) type = "float";
				else if (st.GetBoolParamNames().Contains(name)) type = "bool";
				else if (st.IsUrlJSONParam(name)) type = "url";
				else if (st.GetStringParamNames().Contains(name)) type = "string";
				else if (st.GetStringChooserParamNames().Contains(name)) type = "chooser";
				else if (st.GetColorParamNames().Contains(name)) type = "color";
				else if (FindPropOrField(st.GetType(), name) != null) type = "property";
			}
			JSONClass r = new JSONClass();
			r["uid"] = a.uid;
			r["storable"] = sid;
			r["param"] = name;
			switch (type) {
				case "float":
					st.SetFloatParamValue(name, value.AsFloat);
					r["type"] = "float";
					r["value"] = F(st.GetFloatParamValue(name));
					break;
				case "bool":
					st.SetBoolParamValue(name, value.AsBool);
					r["type"] = "bool";
					r["value"] = JSONRaw.Bool(st.GetBoolParamValue(name));
					break;
				case "string":
					st.SetStringParamValue(name, value.Value);
					r["type"] = "string";
					r["value"] = st.GetStringParamValue(name);
					break;
				case "url": {
					string url = value.Value;
					st.SetUrlParamValue(name, url);
					JSONStorableUrl urlParam = st.GetUrlJSONParam(name);
					r["type"] = "url";
					if (urlParam != null && url != "" && (args["reload"] == null || args["reload"].AsBool)) {
						urlParam.Reload();
					}
					r["value"] = st.GetUrlParamValue(name);
					break;
				}
				case "chooser":
					st.SetStringChooserParamValue(name, value.Value);
					r["type"] = "chooser";
					r["value"] = st.GetStringChooserParamValue(name);
					break;
				case "color":
					st.SetColorParamValue(name, ParseColor(value.Value));
					r["type"] = "color";
					r["value"] = ColorToHex(st.GetColorParamValue(name));
					break;
				case "property": {
					MemberInfo m = FindPropOrField(st.GetType(), name);
					if (m == null) throw new ApiError("property not found: " + name + " on " + sid);
					SetMemberValue(m, st, ParsePropValue(MemberType(m), value));
					r["type"] = "property";
					r["propertyType"] = MemberType(m).Name;
					r["value"] = SerializePropValue(GetMemberValue(m, st));
					break;
				}
				default:
					throw new ApiError("cannot determine param type for " + name + "; specify type: float|bool|string|url|chooser|color|property");
			}
			return r;
		}

		public JSONNode CallAction(JSONNode args) {
			Atom a = FindAtom(S(args, "uid"));
			JSONStorable st = a.GetStorableByID(S(args, "storable"));
			if (st == null) throw new ApiError("storable not found: " + S(args, "storable") + " on " + a.uid);
			string actionName = S(args, "action");
			if (actionName == "") throw new ApiError("action required");
			JSONStorableAction action = st.GetAction(actionName);
			if (action == null) throw new ApiError("action not found: " + actionName + " (see list_storable_params)");
			if (action.actionCallback != null) action.actionCallback();
			JSONClass r = new JSONClass();
			r["uid"] = a.uid;
			r["storable"] = S(args, "storable");
			r["action"] = actionName;
			r["called"] = JSONRaw.Bool(true);
			return r;
		}

		// ===================== camera =====================

		private static Camera MonitorCamera() {
			Camera cam = SC.MonitorCenterCamera;
			if (cam == null) throw new ApiError("no monitor camera available");
			return cam;
		}

		public JSONNode GetCamera() {
			Camera cam = MonitorCamera();
			JSONClass r = new JSONClass();
			r["position"] = Vec(cam.transform.position);
			r["rotation"] = Vec(cam.transform.eulerAngles);
			r["fov"] = F(cam.fieldOfView);
			return r;
		}

		public JSONNode SetCamera(JSONNode args) {
			Camera cam = MonitorCamera();
			if (Has(args, "position")) cam.transform.position = ParseVec3(args["position"]);
			if (Has(args, "rotation")) cam.transform.rotation = Quaternion.Euler(ParseVec3(args["rotation"]));
			if (Has(args, "fov")) cam.fieldOfView = Num(args, "fov", 60f);
			return GetCamera();
		}

		/// <summary>Largest PNG returned inline as base64 (bigger images stay on disk only).</summary>
		private const int MaxInlineImageBytes = 4 * 1024 * 1024;

		private static volatile PreviewGrabber grabber;

		/// <summary>Attach the back-buffer grabber to the monitor camera, once.</summary>
		private static PreviewGrabber Grabber() {
			if (grabber != null) return grabber;
			GameObject go = MonitorCamera().gameObject;
			PreviewGrabber existing = go.GetComponent<PreviewGrabber>();
			if (existing != null) { grabber = existing; return grabber; }
			grabber = go.AddComponent<PreviewGrabber>();
			return grabber;
		}

		/// <summary>
		/// Capture the real window back buffer (what VaM renders on screen, so person
		/// skin/clothing are included - a plain cam.Render() drops DAZCharacter meshes,
		/// which is why captures used to show floating hair and cuffs on empty walls).
		/// The grab happens at the end of the frame, so the handler (HTTP thread) schedules
		/// it on the main thread and then polls for the sequence bump. One frame may be
		/// several seconds on an idle VaM (IdleSaver's 1 fps), hence the 20 s default poll.
		/// </summary>
		public JSONNode CaptureView(JSONNode args) {
			string mode = S(args, "mode");
			if (mode == "") mode = "window";
			if (mode == "camera") return CaptureViewCamera(args);
			if (mode != "window") throw new ApiError("mode must be 'window' (default) or 'camera'");

			int w = (int)Num(args, "width", 1280);
			int h = (int)Num(args, "height", 720);
			if (w < 64 || h < 64) throw new ApiError("width/height too small");
			if (w > 4096 || h > 4096) throw new ApiError("width/height too large (max 4096)");
			bool returnImage = Bool(args, "return_image", false);
			string path = S(args, "path");
			if (path == "") path = "Saves/PluginData/vam-mcp/preview.png";
			int timeoutMs = (int)Num(args, "timeout_ms", 20000);

			// Schedule the grab on the main thread, then poll PreviewGrabber fields
			// from this HTTP thread (both sides use volatile/Interlocked accessors).
			long startSeq = 0;
			bool done = false;
			PreviewGrabber g = Mt.Run(delegate {
				PreviewGrabber gg = Grabber();
				startSeq = Interlocked.Read(ref gg.lastSeq);
				gg.reqWidth = w;
				gg.reqHeight = h;
				gg.reqPath = path;
				gg.lastBytes = null;
				gg.lastError = null;
				gg.pending = true;
				return gg;
			});

			JSONClass r = new JSONClass();
			r["mode"] = "window";
			r["path"] = path;
			r["width"] = JSONRaw.Num(w);
			r["height"] = JSONRaw.Num(h);
			long deadline = Environment.TickCount + timeoutMs;
			while (Environment.TickCount < deadline) {
				Thread.Sleep(50);
				if (Interlocked.Read(ref g.lastSeq) != startSeq) { done = true; break; }
				if (g.lastError != null) break; // grab attempt errored (volatile read)
			}
			string err = g.lastError;
			if (done && err == null) {
				r["pending"] = JSONRaw.Bool(false);
				byte[] lastBytes = g.lastBytes;
				int pngBytes = lastBytes != null ? lastBytes.Length : 0;
				r["bytes"] = JSONRaw.Num(pngBytes);
				r["actual_width"] = JSONRaw.Num(g.lastWidth);
				r["actual_height"] = JSONRaw.Num(g.lastHeight);
				if (returnImage && lastBytes != null && lastBytes.Length <= MaxInlineImageBytes) {
					r["image_base64"] = Convert.ToBase64String(lastBytes);
					r["image_mime"] = "image/png";
				} else if (returnImage && lastBytes != null) {
					r["note"] = "image too large to inline (" + pngBytes + " bytes); read it from path instead";
				}
			} else {
				r["pending"] = JSONRaw.Bool(true);
				if (err != null) r["note"] = "grab error (file path may still be from an older capture): " + err;
				else r["note"] = "no frame rendered within " + timeoutMs + " ms (is VaM minimized, or IdleEaver holding the sim at 1 fps?) - read the file after a delay instead";
			}
			return r;
		}

		public JSONNode CaptureViewCamera(JSONNode args) {
			Camera cam = MonitorCamera();
			int w = (int)Num(args, "width", 1280);
			int h = (int)Num(args, "height", 720);
			if (w < 64 || h < 64) throw new ApiError("width/height too small");
			if (w > 4096 || h > 4096) throw new ApiError("width/height too large (max 4096)");
			bool returnImage = Bool(args, "return_image", false);
			string path = S(args, "path");
			if (path == "") path = "Saves/PluginData/vam-mcp/preview.png";
			FileManagerSecure.CreateDirectory("Saves/PluginData/vam-mcp");

			RenderTexture rt = new RenderTexture(w, h, 24);
			RenderTexture oldTarget = cam.targetTexture;
			RenderTexture oldActive = RenderTexture.active;
			Texture2D tex = null;
			string base64 = null;
			int pngBytes = 0;
			try {
				cam.targetTexture = rt;
				cam.Render();
				RenderTexture.active = rt;
				tex = new Texture2D(w, h, TextureFormat.RGB24, false);
				tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
				tex.Apply();
				byte[] bytes = tex.EncodeToPNG();
				pngBytes = bytes.Length;
				FileManagerSecure.WriteAllBytes(path, bytes);
				if (returnImage && bytes.Length <= MaxInlineImageBytes) base64 = Convert.ToBase64String(bytes);
			} finally {
				cam.targetTexture = oldTarget;
				RenderTexture.active = oldActive;
				if (tex != null) UnityEngine.Object.Destroy(tex);
				rt.Release();
			}
			JSONClass r = new JSONClass();
			r["mode"] = "camera";
			r["path"] = path;
			r["width"] = JSONRaw.Num(w);
			r["height"] = JSONRaw.Num(h);
			r["bytes"] = JSONRaw.Num(pngBytes);
			if (base64 != null) {
				// McpServer turns these two into an MCP image content block.
				r["image_base64"] = base64;
				r["image_mime"] = "image/png";
			} else if (returnImage) {
				r["note"] = "image too large to inline (" + pngBytes + " bytes); read it from path instead";
			}
			return r;
		}

		// ===================== simulation =====================

		public JSONNode SetSimulation(JSONNode args) {
			JSONClass applied = new JSONClass();
			if (Has(args, "paused")) {
				bool paused = Bool(args, "paused", false);
				Time.timeScale = paused ? 0f : 1f;
				applied["paused"] = JSONRaw.Bool(paused);
			}
			if (Has(args, "timeScale")) {
				Time.timeScale = Num(args, "timeScale", 1f);
			}
			applied["timeScale"] = F(Time.timeScale);
			return applied;
		}

		public JSONNode ResetSimulation() {
			SC.ResetSimulation(10, "VaMMCP", false);
			JSONClass r = new JSONClass();
			r["ok"] = JSONRaw.Bool(true);
			return r;
		}

		// ===================== generic helpers for the extended tools =====================

		/// <summary>Case-insensitive action lookup; returns null if absent so the caller can build a helpful error.</summary>
		private static JSONStorableAction FindActionCI(JSONStorable st, string wanted) {
			if (st == null || wanted == "") return null;
			JSONStorableAction a = st.GetAction(wanted);
			if (a != null) return a;
			try {
				foreach (string n in st.GetActionNames()) {
					if (string.Equals(n, wanted, StringComparison.OrdinalIgnoreCase)) return st.GetAction(n);
				}
			} catch { }
			return null;
		}

		private static string ActionHelp(JSONStorable st) {
			List<string> names = new List<string>();
			try { foreach (string n in st.GetActionNames()) names.Add(n); } catch { }
			return names.Count > 0 ? "available actions: " + string.Join(", ", names.ToArray()) : "no actions on this storable";
		}

		/// <summary>Find a storable on an atom by exact id, then by case-insensitive exact, then by substring; null if none match.</summary>
		private static JSONStorable ResolveStorable(Atom a, string sid, string contains, out string usedId) {
			usedId = sid;
			if (sid != "") {
				JSONStorable st = a.GetStorableByID(sid);
				if (st != null) return st;
				foreach (string id in a.GetStorableIDs()) {
					if (string.Equals(id, sid, StringComparison.OrdinalIgnoreCase)) {
						JSONStorable s2 = a.GetStorableByID(id);
						if (s2 != null) { usedId = id; return s2; }
					}
				}
			}
			if (contains != null) {
				foreach (string id in a.GetStorableIDs()) {
					if (id.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0) {
						JSONStorable s3 = a.GetStorableByID(id);
						if (s3 != null) { usedId = id; return s3; }
					}
				}
			}
			return null;
		}

		private static JSONStorable RequireResolvedStorable(Atom a, string sid, string contains, string what) {
			string used;
			JSONStorable st = ResolveStorable(a, sid, contains, out used);
			if (st == null) {
				StringBuilder sb = new StringBuilder();
				sb.Append(what).Append(" storable not found on ").Append(a.uid);
				if (sid != "") sb.Append(" (asked for \"").Append(sid).Append("\")");
				sb.Append("; storables: ").Append(string.Join(", ", a.GetStorableIDs().ToArray()));
				throw new ApiError(sb.ToString());
			}
			return st;
		}

		/// <summary>Find a float param by case-insensitive substring (exact match first). Returns null when nothing matches.</summary>
		private static string MatchFloatParam(JSONStorable st, string needle) {
			if (needle == "") return null;
			IList<string> names = st.GetFloatParamNames();
			foreach (string n in names) if (string.Equals(n, needle, StringComparison.OrdinalIgnoreCase)) return n;
			foreach (string n in names) if (n.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return n;
			return null;
		}

		private static string MatchBoolParam(JSONStorable st, string needle) {
			if (needle == "") return null;
			IList<string> names = st.GetBoolParamNames();
			foreach (string n in names) if (string.Equals(n, needle, StringComparison.OrdinalIgnoreCase)) return n;
			foreach (string n in names) if (n.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return n;
			return null;
		}

		private static string MatchChooserParam(JSONStorable st, string needle) {
			if (needle == "") return null;
			IList<string> names = st.GetStringChooserParamNames();
			foreach (string n in names) if (string.Equals(n, needle, StringComparison.OrdinalIgnoreCase)) return n;
			foreach (string n in names) if (n.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return n;
			return null;
		}

		private static string MatchStringParam(JSONStorable st, string needle) {
			if (needle == "") return null;
			IList<string> names = st.GetStringParamNames();
			foreach (string n in names) if (string.Equals(n, needle, StringComparison.OrdinalIgnoreCase)) return n;
			foreach (string n in names) if (n.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return n;
			return null;
		}

		private static string MatchUrlParam(JSONStorable st, string needle) {
			if (needle == "") return null;
			IList<string> names = st.GetUrlParamNames();
			foreach (string n in names) if (string.Equals(n, needle, StringComparison.OrdinalIgnoreCase)) return n;
			foreach (string n in names) if (n.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return n;
			return null;
		}

		private static string MatchColorParam(JSONStorable st, string needle) {
			if (needle == "") return null;
			IList<string> names = st.GetColorParamNames();
			foreach (string n in names) if (string.Equals(n, needle, StringComparison.OrdinalIgnoreCase)) return n;
			foreach (string n in names) if (n.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return n;
			return null;
		}

		/// <summary>Compact param snapshot of one storable, bounded per kind so listings stay readable.
		/// Deep inspection should use list_storable_params.</summary>
		private static JSONClass ParamSnapshot(JSONStorable st, int floats, int bools, int choosers, int strings, int urls) {
			JSONClass r = new JSONClass();
			int i = 0;
			foreach (string n in st.GetFloatParamNames()) {
				if (floats >= 0 && i++ >= floats) break;
				r[n] = F(st.GetFloatParamValue(n));
			}
			i = 0;
			foreach (string n in st.GetBoolParamNames()) {
				if (bools >= 0 && i++ >= bools) break;
				r[n] = JSONRaw.Bool(st.GetBoolParamValue(n));
			}
			i = 0;
			foreach (string n in st.GetStringChooserParamNames()) {
				if (choosers >= 0 && i++ >= choosers) break;
				r[n] = st.GetStringChooserParamValue(n);
			}
			i = 0;
			foreach (string n in st.GetStringParamNames()) {
				if (strings >= 0 && i++ >= strings) break;
				string v = st.GetStringParamValue(n);
				if (v != null && v.Length > 160) v = v.Substring(0, 160) + "...";
				r[n] = v;
			}
			i = 0;
			foreach (string n in st.GetUrlParamNames()) {
				if (urls >= 0 && i++ >= urls) break;
				r[n] = st.GetUrlParamValue(n);
			}
			return r;
		}

		// ===================== scene animation / timeline =====================

		private static bool AtomTypeMatches(Atom a, string wanted) {
			if (a == null || wanted == "") return false;
			return a.type.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0;
		}

		public JSONNode ListAnimationPatterns(JSONNode args) {
			string search = S(args, "search").ToLowerInvariant();
			JSONArray arr = new JSONArray();
			foreach (Atom a in SC.GetAtoms()) {
				if (a == null) continue;
				if (!AtomTypeMatches(a, "AnimationPattern") && !AtomTypeMatches(a, "AnimationStep")) continue;
				if (search != "" && a.uid.ToLowerInvariant().IndexOf(search) < 0) continue;
				JSONClass row = AtomInfo(a);
				string used;
				JSONStorable st = ResolveStorable(a, "AnimationPattern", "animation", out used);
				if (st == null) st = ResolveStorable(a, "AnimationStep", "animation step", out used);
				if (st == null) st = ResolveStorable(a, "", "anim", out used);
				if (st != null) {
					row["storable"] = used;
					row["params"] = ParamSnapshot(st, 30, 15, 10, 4, 4);
					row["actions"] = ActionHelp(st);
				}
				arr.Add(row);
			}
			JSONClass r = new JSONClass();
			r["count"] = JSONRaw.Num(arr.Count);
			r["patterns"] = arr;
			return r;
		}

		private static Atom FindAtomQuiet(string uid) { return SC.GetAtomByUid(uid); }

		public JSONNode ListAnimationSteps(JSONNode args) {
			string patternUid = S(args, "pattern_uid");
			JSONArray arr = new JSONArray();
			int unresolved = 0;
			foreach (Atom a in SC.GetAtoms()) {
				if (a == null || !AtomTypeMatches(a, "AnimationStep")) continue;
				string used;
				JSONStorable st = ResolveStorable(a, "AnimationStep", "step", out used);
				JSONClass row = AtomInfo(a);
				if (st != null) {
					row["storable"] = used;
					row["params"] = ParamSnapshot(st, 20, 10, 10, 6, 0);
					string linked = "";
					try { if (MatchStringParam(st, "pattern") != null) linked = st.GetStringParamValue(MatchStringParam(st, "pattern")); } catch { }
					if (linked == "") try { if (MatchChooserParam(st, "pattern") != null) linked = st.GetStringChooserParamValue(MatchStringParam(st, "pattern")); } catch { }
					if (linked != "") {
						row["pattern"] = linked;
						if (patternUid == "" || string.Equals(linked, patternUid, StringComparison.OrdinalIgnoreCase)) arr.Add(row);
						continue;
					}
				}
				// linkage unknown: report the step (flagged) so the agent can match params by name
				row["pattern"] = "(unresolved)";
				unresolved++;
				if (patternUid == "") arr.Add(row);
			}
			JSONClass r = new JSONClass();
			r["count"] = JSONRaw.Num(arr.Count);
			r["unresolvedLinkage"] = JSONRaw.Num(unresolved);
			r["steps"] = arr;
			if (unresolved > 0 && patternUid != "") {
				r["note"] = "some AnimationStep atoms report no pattern linkage param; use list_atoms/list_animation_patterns and match controller/min/max params manually, or attach steps via the AnimationPattern UI tab";
			}
			return r;
		}

		/// <summary>Poller tool. Create an AnimationPattern atom (timeline container).</summary>
		public JSONNode CreateAnimationPattern(JSONNode args) {
			JSONClass sub = new JSONClass();
			sub["type"] = "AnimationPattern";
			if (Has(args, "uid")) sub["uid"] = args["uid"];
			return AddAtom(sub);
		}

		/// <summary>Poller tool. Create an AnimationStep atom and best-effort configure it against its pattern.</summary>
		public JSONNode CreateAnimationStep(JSONNode args) {
			JSONClass sub = new JSONClass();
			sub["type"] = "AnimationStep";
			if (Has(args, "uid")) sub["uid"] = args["uid"];
			JSONNode created = AddAtom(sub);
			Atom a = FindAtom(created["uid"]);
			string used;
			JSONStorable st = ResolveStorable(a, "AnimationStep", "step", out used);
			JSONClass applied = new JSONClass();
			if (st != null) {
				string transition = S(args, "transition_type");
				if (transition != "") {
					string p = MatchChooserParam(st, "transition");
					if (p == null) p = MatchFloatParam(st, "transition");
					try {
						if (p != null && MatchFloatParam(st, p) == null) {
							st.SetStringChooserParamValue(p, transition);
							applied[p] = transition;
						} else if (p != null) {
							st.SetFloatParamValue(p, NumArg(transition));
							applied[p] = F(NumArg(transition));
						} else applied["transitionType"] = "not found (check param names via list_storable_params)";
					} catch (Exception e) {
						applied["transitionType.error"] = e.Message;
					}
				}
				if (Has(args, "duration")) {
					string p = MatchFloatParam(st, "duration");
					if (p == null) p = MatchFloatParam(st, "length");
					if (p != null) {
						st.SetFloatParamValue(p, Num(args, "duration", 1f));
						applied[p] = F(st.GetFloatParamValue(p));
					} else applied["duration"] = "not found";
				}
				// try to auto-add/open the step inside the pattern: many setups expose an Add/Assign action
				string patternUid = S(args, "pattern_uid");
				JSONStorableAction link = null;
				if (st != null) {
					foreach (string n in st.GetActionNames()) {
						if (n.IndexOf("add", StringComparison.OrdinalIgnoreCase) >= 0 && n.IndexOf("pattern", StringComparison.OrdinalIgnoreCase) >= 0) { link = st.GetAction(n); break; }
					}
				}
				if (link == null && patternUid != "") {
					Atom pat = FindAtomQuiet(patternUid);
					if (pat != null) {
						string pu;
						JSONStorable pst = ResolveStorable(pat, "AnimationPattern", "animation", out pu);
						if (pst != null) {
							foreach (string n in pst.GetActionNames()) {
								string ln = n.ToLowerInvariant();
								if ((ln.IndexOf("add") >= 0 || ln.IndexOf("attach") >= 0 || ln.IndexOf("register") >= 0) && (ln.IndexOf("step") >= 0 || ln.IndexOf("animation") >= 0)) { link = pst.GetAction(n); break; }
							}
						}
					}
				}
				if (link == null) {
					if (st.GetActionNames().Contains("Link To Animation Pattern")) link = st.GetAction("Link To Animation Pattern");
				}
				if (link != null && link.actionCallback != null) {
					try { link.actionCallback(); applied["linked"] = link.name; } catch (Exception e) { applied["linked.error"] = e.Message; }
				}
			}
			created["applied"] = applied;
			created["note"] = "attaching the step to its pattern happens in the VaM UI when no auto-link action exists; verify with list_animation_steps";
			return created;
		}

		private static float NumArg(string raw) {
			if (raw == null) return 0f;
			return (float)System.Convert.ChangeType(raw, typeof(float), CultureInfo.InvariantCulture);
		}

		public JSONNode AnimationPatternAction(JSONNode args) {
			Atom a = FindAtom(S(args, "pattern_uid"));
			string used;
			JSONStorable st = ResolveStorable(a, "AnimationPattern", "animation", out used);
			if (st == null) throw new ApiError("no AnimationPattern storable on " + a.uid + "; storables: " + string.Join(", ", a.GetStorableIDs().ToArray()));
			string action = S(args, "action");
			if (action == "") throw new ApiError("action required (Play, Pause, Stop, SetTime/ScrubToTime or any action name shown in list)");
			JSONClass r = new JSONClass();
			r["uid"] = a.uid;
			r["storable"] = used;
			string la = action.ToLowerInvariant();
			if (la == "settime" || la == "scrubtotime" || la == "scrub") {
				float t = Num(args, "time", 0f);
				string p = MatchFloatParam(st, "time");
				if (p != null) {
					st.SetFloatParamValue(p, t);
					r["time"] = F(t);
					return r;
				}
				throw new ApiError("no 'time' param on this pattern; " + ActionHelp(st));
			}
			JSONStorableAction act = FindActionCI(st, action);
			if (act == null && la == "play") act = FindActionCI(st, "Play");
			if (act == null && la == "stop") act = FindActionCI(st, "Stop");
			if (act == null) throw new ApiError("action not found: " + action + "; " + ActionHelp(st));
			if (act.actionCallback != null) act.actionCallback();
			r["called"] = act.name;
			return r;
		}

		public JSONNode AnimationStepAction(JSONNode args) {
			Atom a = FindAtom(S(args, "step_uid"));
			string used;
			JSONStorable st = ResolveStorable(a, "AnimationStep", "step", out used);
			if (st == null) throw new ApiError("no AnimationStep storable on " + a.uid + "; storables: " + string.Join(", ", a.GetStorableIDs().ToArray()));
			string action = S(args, "action");
			if (action == "") throw new ApiError("action required (Play, Stop, SetTransition, SetDuration or any action name)");
			JSONClass r = new JSONClass();
			r["uid"] = a.uid;
			r["storable"] = used;
			string la = action.ToLowerInvariant();
			if (la == "settransition") {
				string want = S(args, "value");
				string p = MatchChooserParam(st, "transition");
				if (p == null) throw new ApiError("no transition param; " + ActionHelp(st));
				st.SetStringChooserParamValue(p, want);
				r["transition"] = st.GetStringChooserParamValue(p);
				return r;
			}
			if (la == "setduration") {
				string p = MatchFloatParam(st, "duration");
				if (p == null) throw new ApiError("no duration param; " + ActionHelp(st));
				st.SetFloatParamValue(p, Num(args, "value", 1f));
				r["duration"] = F(st.GetFloatParamValue(p));
				return r;
			}
			JSONStorableAction act = FindActionCI(st, action);
			if (act == null) throw new ApiError("action not found: " + action + "; " + ActionHelp(st));
			if (act.actionCallback != null) act.actionCallback();
			r["called"] = act.name;
			return r;
		}

		public JSONNode RecordSceneAnimation(JSONNode args) {
			Atom a = FindAtom(S(args, "pattern_uid"));
			JSONStorable st = RequireResolvedStorable(a, "AnimationPattern", "animation", "AnimationPattern");
			string action = S(args, "action").ToLowerInvariant();
			if (action != "start" && action != "stop") throw new ApiError("action required: start or stop");
			foreach (string n in st.GetActionNames()) {
				string ln = n.ToLowerInvariant();
				bool matchesStart = action == "start" && (ln.IndexOf("record") >= 0 && ln.IndexOf("stop") < 0);
				bool matchesStop = action == "stop" && ln.IndexOf("stop") >= 0 && (ln.IndexOf("record") >= 0 || ln.IndexOf("end") >= 0);
				if (matchesStart || matchesStop) {
					JSONStorableAction act = st.GetAction(n);
					if (act != null && act.actionCallback != null) {
						act.actionCallback();
						JSONClass r = new JSONClass();
						r["uid"] = a.uid;
						r["called"] = n;
						r["recordPhysics"] = JSONRaw.Bool(Bool(args, "record_physics", false));
						return r;
					}
				}
			}
			throw new ApiError("this AnimationPattern exposes no Record action; VaM's built-in controller recording is UI-only (" + ActionHelp(st) + "). Alternative: drive FreeControllerV3 controls with set_control from your agent script, or capture poses with save_pose per frame");
		}

		// ===================== triggers =====================

		public JSONNode ListTriggers(JSONNode args) {
			string search = S(args, "search").ToLowerInvariant();
			JSONArray arr = new JSONArray();
			foreach (Atom a in SC.GetAtoms()) {
				if (a == null) continue;
				foreach (string sid in a.GetStorableIDs()) {
					if (sid.IndexOf("trigger", StringComparison.OrdinalIgnoreCase) < 0) continue;
					JSONStorable st = a.GetStorableByID(sid);
					if (st == null) continue;
					if (search != "" && a.uid.ToLowerInvariant().IndexOf(search) < 0 && sid.ToLowerInvariant().IndexOf(search) < 0) continue;
					JSONClass row = new JSONClass();
					row["uid"] = a.uid;
					row["type"] = a.type;
					row["storable"] = sid;
					row["params"] = ParamSnapshot(st, 10, 8, 4, 6, 2);
					row["actions"] = ActionHelp(st);
					arr.Add(row);
					break; // one trigger row per atom keeps listings small
				}
				if (arr.Count >= 80) break;
			}
			JSONClass r = new JSONClass();
			r["count"] = JSONRaw.Num(arr.Count);
			r["triggers"] = arr;
			return r;
		}

		public JSONNode TriggerAction(JSONNode args) {
			string tuid = S(args, "trigger_uid");
			if (tuid == "") throw new ApiError("trigger_uid required (see list_triggers)");
			Atom a;
			string sid = S(args, "storable");
			// accept "atom uid" or "atom/Trigger" combined
			int slice = tuid.IndexOf('/');
			if (slice >= 0 && ParamEmpty(sid)) {
				sid = tuid.Substring(slice + 1);
				tuid = tuid.Substring(0, slice);
			}
			a = FindAtom(tuid);
			string la = S(args, "action").ToLowerInvariant();
			if (la == "fire") la = "trigger";
			if (la == "" ) throw new ApiError("action required: Fire, SetFloatParam, SetBoolParam, Reset or any action name");
			string used;
			JSONStorable st = ResolveStorable(a, sid, sid == "" ? "trigger" : null, out used);
			if (st == null) throw new ApiError("no trigger storable on " + a.uid + "; storables: " + string.Join(", ", a.GetStorableIDs().ToArray()));
			JSONClass r = new JSONClass();
			r["uid"] = a.uid;
			r["storable"] = used;
			if (la == "setfloatparam") {
				string p = S(args, "param");
				st.SetFloatParamValue(p, Num(args, "value", 0f));
				r["param"] = p;
				r["value"] = F(st.GetFloatParamValue(p));
				return r;
			}
			if (la == "setboolparam") {
				string p = S(args, "param");
				st.SetBoolParamValue(p, Bool(args, "value", true));
				r["param"] = p;
				r["value"] = JSONRaw.Bool(st.GetBoolParamValue(p));
				return r;
			}
			JSONStorableAction act = FindActionCI(st, la);
			if (act == null) throw new ApiError("action not found: " + S(args, "action") + "; " + ActionHelp(st));
			if (act.actionCallback != null) act.actionCallback();
			r["called"] = act.name;
			return r;
		}

		private static bool ParamEmpty(string s) { return string.IsNullOrEmpty(s); }

		/// <summary>Poller tool. Create a UI trigger atom (buttons/toggles etc.) and point at its Trigger storable.</summary>
		public JSONNode CreateTrigger(JSONNode args) {
			string type = S(args, "type");
			if (type == "") type = "UIButton";
			JSONClass sub = new JSONClass();
			sub["type"] = type;
			if (Has(args, "uid")) sub["uid"] = args["uid"];
			JSONNode created = AddAtom(sub);
			try {
				Atom a = FindAtom(created["uid"]);
				string used;
				JSONStorable st = ResolveStorable(a, "Trigger", "trigger", out used);
				if (st != null) {
					created["storable"] = used;
					created["params"] = ParamSnapshot(st, 10, 8, 4, 6, 4);
					created["actions"] = ActionHelp(st);
				}
			} catch (Exception e) { created["note"] = e.Message; }
			return created;
		}

		public JSONNode TriggerUI(JSONNode args) {
			Atom a = FindAtom(S(args, "trigger_uid"));
			string la = S(args, "action").ToLowerInvariant();
			if (la == "" ) throw new ApiError("action required: Show, Hide or Refresh");
			string used;
			JSONStorable st = ResolveStorable(a, "Trigger", "trigger", out used);
			if (st == null) throw new ApiError("no trigger storable on " + a.uid);
			foreach (string n in st.GetActionNames()) {
				string ln = n.ToLowerInvariant();
				bool isShow = (la == "show" && (ln.IndexOf("open") >= 0 || ln.IndexOf("show") >= 0));
				bool isHide = (la == "hide" && (ln.IndexOf("close") >= 0 || ln.IndexOf("hide") >= 0 || ln.IndexOf("dismiss") >= 0));
				bool isRefresh = (la == "refresh" && (ln.IndexOf("trigger") >= 0 || ln.IndexOf("fire") >= 0 || ln.IndexOf("apply") >= 0));
				if (isShow || isHide || isRefresh) {
					JSONStorableAction act = st.GetAction(n);
					if (act != null && act.actionCallback != null) {
						act.actionCallback();
						JSONClass r = new JSONClass();
						r["uid"] = a.uid;
						r["called"] = n;
						return r;
					}
				}
			}
			throw new ApiError("no matching action for " + S(args, "action") + "; " + ActionHelp(st) + "; VaM opens trigger UIs by double-clicking the atom in the scene panel — automate that with press_key/tab keys if needed");
		}

		// ===================== physics =====================

		private static readonly string[] PhysicsKeywords = { "physics", "collision", "force", "spring", "joint", "dynamics", "gravity" };

		public JSONNode ListPhysicsObjects(JSONNode args) {
			string search = S(args, "search").ToLowerInvariant();
			JSONArray arr = new JSONArray();
			foreach (Atom a in SC.GetAtoms()) {
				if (a == null) continue;
				if (search != "" && a.uid.ToLowerInvariant().IndexOf(search) < 0) continue;
				JSONArray phys = new JSONArray();
				foreach (string sid in a.GetStorableIDs()) {
					bool hits = false;
					foreach (string k in PhysicsKeywords) {
						if (sid.ToLowerInvariant().IndexOf(k) >= 0) { hits = true; break; }
					}
					if (hits) phys.Add(sid);
				}
				if (phys.Count > 0) {
					JSONClass row = AtomInfo(a);
					row["physicsStorables"] = phys;
					arr.Add(row);
					if (arr.Count >= 120) break;
				}
			}
			JSONClass r = new JSONClass();
			r["count"] = JSONRaw.Num(arr.Count);
			r["objects"] = arr;
			return r;
		}

		public JSONNode PhysicsObjectAction(JSONNode args) {
			Atom a = FindAtom(S(args, "uid"));
			string la = S(args, "action").ToLowerInvariant();
			if (la == "" ) throw new ApiError("action required: EnablePhysics, DisablePhysics, SetCollision, AddForce, SetMass, SetDrag");
			JSONClass r = new JSONClass();
			r["uid"] = a.uid;
			bool wantEnable = la == "enablephysics";
			bool wantDisable = la == "disablephysics";
			if (wantEnable || wantDisable) {
				int touched = 0;
				foreach (string sid in a.GetStorableIDs()) {
					JSONStorable st = a.GetStorableByID(sid);
					if (st == null) continue;
					string p = MatchBoolParam(st, "enabled");
					if (p == null) p = MatchBoolParam(st, "simulate");
					if (p == null) p = MatchBoolParam(st, "on/isactive");
					if (p == null && (sid.IndexOf("physics", StringComparison.OrdinalIgnoreCase) >= 0 || sid.IndexOf("collision", StringComparison.OrdinalIgnoreCase) >= 0)) p = MatchBoolParam(st, "on");
					if (p == null) continue;
					st.SetBoolParamValue(p, wantEnable);
					touched++;
					r[sid + "." + p] = JSONRaw.Bool(st.GetBoolParamValue(p));
				}
				r["touched"] = JSONRaw.Num(touched);
				return r;
			}
			if (la == "setcollision") {
				int touched = 0;
				foreach (string sid in a.GetStorableIDs()) {
					JSONStorable st = a.GetStorableByID(sid);
					if (st == null) continue;
					string p = MatchBoolParam(st, "collision");
					if (p == null) p = MatchBoolParam(st, "collide");
					if (p == null) continue;
					st.SetBoolParamValue(p, Bool(args, "value", true));
					touched++;
					r[sid + "." + p] = JSONRaw.Bool(st.GetBoolParamValue(p));
				}
				r["touched"] = JSONRaw.Num(touched);
				return r;
			}
			// single-storable operations for the first physicsy storable we find
			JSONStorable target = null;
			string usedSid = "";
			foreach (string sid in a.GetStorableIDs()) {
				foreach (string k in new string[] { "force", "physics", "collision", "dynamics" }) {
					if (sid.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) {
						JSONStorable st = a.GetStorableByID(sid);
						if (st != null) { target = st; usedSid = sid; break; }
					}
				}
				if (target != null) break;
			}
			if (target == null) throw new ApiError("no physics storable on " + a.uid + "; storables: " + string.Join(", ", a.GetStorableIDs().ToArray()));
			r["storable"] = usedSid;
			if (la == "addforce") {
				string p = MatchFloatParam(target, "force");
				if (p == null) throw new ApiError("no force param on " + usedSid + "; " + ActionHelp(target));
				float magnitude = Num(args, "magnitude", Num(args, "value", target.GetFloatParamValue(p)));
				target.SetFloatParamValue(p, magnitude);
				r["force"] = F(target.GetFloatParamValue(p));
				string dx = MatchFloatParam(target, "x");
				string dy = MatchFloatParam(target, "y");
				string dz = MatchFloatParam(target, "z");
				if (Has(args, "direction")) {
					Vector3 d = ParseVec3(args["direction"]);
					if (dx != null) target.SetFloatParamValue(dx, d.x);
					if (dy != null) target.SetFloatParamValue(dy, d.y);
					if (dz != null) target.SetFloatParamValue(dz, d.z);
				}
				return r;
			}
			if (la == "setmass" || la == "setdrag") {
				string p = MatchFloatParam(target, la == "setmass" ? "mass" : "drag");
				if (p == null) throw new ApiError("no such param on " + usedSid + "; " + ActionHelp(target));
				target.SetFloatParamValue(p, Num(args, "value", 1f));
				r["param"] = p;
				r["value"] = F(target.GetFloatParamValue(p));
				return r;
			}
			throw new ApiError("unknown physics action: " + S(args, "action") + "; " + ActionHelp(target));
		}

		/// <summary>Poller tool. Create a ConfigurableJoint/SpringJoint/FixedJoint between two atoms using Unity's legacy joints.</summary>
		public JSONNode CreatePhysicsJoint(JSONNode args) {
			string uidA = S(args, "atom_a");
			string uidB = S(args, "atom_b");
			string jointType = S(args, "joint_type").ToLowerInvariant();
			if (jointType == "") jointType = "configurablejoint";
			bool spring = jointType.IndexOf("spring") >= 0;
			bool fixedJ = jointType.IndexOf("fixed") >= 0;
			GameObject ga = null, gb = null;
			Rigidbody rbA = null, rbB = null;
			Atom aa = FindAtom(uidA), ab = FindAtom(uidB);
			Mt.Run(delegate {
				ga = aa.mainController != null ? aa.mainController.gameObject : aa.gameObject;
				gb = ab.mainController != null ? ab.mainController.gameObject : ab.gameObject;
				rbA = ga.GetComponent<Rigidbody>();
				if (rbA == null) rbA = ga.AddComponent<Rigidbody>();
				rbB = gb.GetComponent<Rigidbody>();
				if (rbB == null) rbB = gb.AddComponent<Rigidbody>();
			}, 10000);
			if (rbA == null || rbB == null) throw new ApiError("could not obtain Rigidbody hosts for " + uidA + " / " + uidB);
			Joint joint = Mt.Run(delegate {
				if (spring) {
					SpringJoint sj = ga.AddComponent<SpringJoint>();
					sj.connectedBody = rbB;
					return sj as Joint;
				}
				if (fixedJ) {
					FixedJoint fj = ga.AddComponent<FixedJoint>();
					fj.connectedBody = rbB;
					return fj as Joint;
				}
				ConfigurableJoint cj = ga.AddComponent<ConfigurableJoint>();
				cj.connectedBody = rbB;
				return cj as Joint;
			}, 10000);
			if (joint == null) throw new ApiError("joint creation failed");
			Mt.Run(delegate {
				Vector3 local = ga.transform.InverseTransformPoint(gb.transform.position);
				joint.anchor = local;
				ConfigurableJoint cj = joint as ConfigurableJoint;
				if (cj != null && Has(args, "anchor")) cj.anchor = ParseVec3(args["anchor"]);
				ConfigurableJoint cj2 = joint as ConfigurableJoint;
				SpringJoint sj = joint as SpringJoint;
				if (Has(args, "connected_anchor")) {
					if (cj2 != null) cj2.connectedAnchor = ParseVec3(args["connected_anchor"]);
					else if (sj != null) sj.connectedAnchor = ParseVec3(args["connected_anchor"]);
				}
			}, 10000);
			JSONClass r = new JSONClass();
			r["joint"] = joint.GetType().Name;
			r["atom_a"] = uidA;
			r["atom_b"] = uidB;
			r["note"] = "legacy Unity joint on top of VaM's own physics; VaM soft-body physics may fight it — verify visuals and consider a dedicated plugin (BoneControl/BodyAttach) instead";
			return r;
		}

		// ===================== scene lighting / audio / misc =====================

		public JSONNode ListLights(JSONNode args) {
			JSONArray arr = new JSONArray();
			foreach (Atom a in SC.GetAtoms()) {
				if (a == null) continue;
				if (a.type.IndexOf("light", StringComparison.OrdinalIgnoreCase) < 0) continue;
				JSONClass row = AtomInfo(a);
				string used;
				JSONStorable st = ResolveStorable(a, "light", "light", out used);
				if (st != null) {
					row["storable"] = used;
					row["params"] = ParamSnapshot(st, 20, 10, 6, 4, 2);
				}
				arr.Add(row);
				if (arr.Count >= 60) break;
			}
			JSONClass r = new JSONClass();
			r["count"] = JSONRaw.Num(arr.Count);
			r["lights"] = arr;
			return r;
		}

		public JSONNode LightAction(JSONNode args) {
			Atom a = FindAtom(S(args, "uid"));
			JSONStorable st = RequireResolvedStorable(a, "light", "light", "Light");
			string la = S(args, "action").ToLowerInvariant();
			if (la == "" ) throw new ApiError("action required: Toggle, SetColor, SetIntensity, SetRange, SetSpotAngle, SetType");
			JSONClass r = new JSONClass();
			r["uid"] = a.uid;
			r["storable"] = a.GetStorableIDs().Contains("light") ? "light" : "(resolved)";
			if (la == "toggle") {
				string p = MatchBoolParam(st, "enabled");
				if (p == null) p = MatchBoolParam(st, "on");
				if (p == null) throw new ApiError("no enabled param; " + ActionHelp(st));
				st.SetBoolParamValue(p, !st.GetBoolParamValue(p));
				r["enabled"] = JSONRaw.Bool(st.GetBoolParamValue(p));
				return r;
			}
			if (la == "setcolor") {
				string p = MatchColorParam(st, "color");
				if (p == null) throw new ApiError("no color param; " + ActionHelp(st));
				st.SetColorParamValue(p, ParseColor(S(args, "value")));
				r["color"] = ColorToHex(st.GetColorParamValue(p));
				return r;
			}
			if (la == "setintensity" || la == "setrange" || la == "setspotangle") {
				string p = MatchFloatParam(st, la == "setintensity" ? "intensity" : la == "setrange" ? "range" : "spot");
				if (p == null) throw new ApiError("no float param for " + S(args, "action") + "; " + ActionHelp(st));
				st.SetFloatParamValue(p, Num(args, "value", 1f));
				r["param"] = p;
				r["value"] = F(st.GetFloatParamValue(p));
				return r;
			}
			if (la == "settype") {
				string p = MatchChooserParam(st, "type");
				if (p == null) throw new ApiError("no type chooser; " + ActionHelp(st));
				st.SetStringChooserParamValue(p, S(args, "value"));
				r["type"] = st.GetStringChooserParamValue(p);
				return r;
			}
			throw new ApiError("unknown light action: " + S(args, "action") + "; " + ActionHelp(st));
		}

		public JSONNode ListAudioSources(JSONNode args) {
			JSONArray arr = new JSONArray();
			foreach (Atom a in SC.GetAtoms()) {
				if (a == null) continue;
				if (a.type.IndexOf("audio", StringComparison.OrdinalIgnoreCase) < 0) continue;
				JSONClass row = AtomInfo(a);
				string used;
				JSONStorable st = ResolveStorable(a, "AudioSource", "audio", out used);
				if (st != null) {
					row["storable"] = used;
					row["params"] = ParamSnapshot(st, 14, 8, 4, 8, 4);
					row["actions"] = ActionHelp(st);
				}
				arr.Add(row);
				if (arr.Count >= 60) break;
			}
			JSONClass r = new JSONClass();
			r["count"] = JSONRaw.Num(arr.Count);
			r["audio"] = arr;
			return r;
		}

		public JSONNode AudioSourceAction(JSONNode args) {
			Atom a = FindAtom(S(args, "uid"));
			JSONStorable st = RequireResolvedStorable(a, "AudioSource", "audio", "AudioSource");
			string la = S(args, "action").ToLowerInvariant();
			if (la == "" ) throw new ApiError("action required: Play, Stop, Pause, SetVolume, SetClip, SetLoop");
			JSONClass r = new JSONClass();
			r["uid"] = a.uid;
			if (la == "play" || la == "stop" || la == "pause") {
				JSONStorableAction act = FindActionCI(st, la);
				if (act != null && act.actionCallback != null) { act.actionCallback(); r["called"] = act.name; return r; }
				// fallback: drive play/loop bools
				string p = la == "play" ? MatchBoolParam(st, "play") : null;
				if (p != null) { st.SetBoolParamValue(p, la == "play"); r[p] = JSONRaw.Bool(st.GetBoolParamValue(p)); return r; }
				throw new ApiError("no " + la + " action; " + ActionHelp(st));
			}
			if (la == "setvolume") {
				string p = MatchFloatParam(st, "volume");
				if (p == null) throw new ApiError("no volume param; " + ActionHelp(st));
				st.SetFloatParamValue(p, Num(args, "value", 1f));
				r["volume"] = F(st.GetFloatParamValue(p));
				return r;
			}
			if (la == "setclip") {
				string p = MatchUrlParam(st, "url");
				if (p == null) p = MatchUrlParam(st, "source");
				if (p == null) throw new ApiError("no audio url param; " + ActionHelp(st));
				st.SetUrlParamValue(p, S(args, "value"));
				r["url"] = st.GetUrlParamValue(p);
				return r;
			}
			if (la == "setloop") {
				string p = MatchBoolParam(st, "loop");
				if (p == null) throw new ApiError("no loop param; " + ActionHelp(st));
				st.SetBoolParamValue(p, Bool(args, "value", true));
				r["loop"] = JSONRaw.Bool(st.GetBoolParamValue(p));
				return r;
			}
			throw new ApiError("unknown audio action: " + S(args, "action") + "; " + ActionHelp(st));
		}

		// ===================== user preferences =====================

		/// <summary>Read the '.val' member of a JSONStorable param object (it is a public field on most kinds).</summary>
		private static object ReadPrefVal(object p) {
			Type t = p.GetType();
			FieldInfo f = t.GetField("val", BindingFlags.Public | BindingFlags.Instance);
			if (f != null) return f.GetValue(p);
			PropertyInfo pr = t.GetProperty("val", BindingFlags.Public | BindingFlags.Instance);
			return pr != null ? pr.GetValue(p, null) : null;
		}

		private static void WritePrefVal(object p, object value) {
			Type t = p.GetType();
			FieldInfo f = t.GetField("val", BindingFlags.Public | BindingFlags.Instance);
			if (f != null) { f.SetValue(p, value); return; }
			PropertyInfo pr = t.GetProperty("val", BindingFlags.Public | BindingFlags.Instance);
			if (pr != null) { pr.SetValue(p, value, new object[0]); return; }
			throw new ApiError("param object has no writable 'val' member");
		}

		private static List<object> UserPrefParams() {
			List<object> found = new List<object>();
			Type t = typeof(UserPreferences);
			if (t == null) return found;
			FieldInfo[] fs = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			foreach (FieldInfo f in fs) {
				string tn = f.FieldType.Name;
				if (tn == "JSONStorableFloat" || tn == "JSONStorableBool" || tn == "JSONStorableString" ||
					tn == "JSONStorableStringChooser" || tn == "JSONStorableUrl" || tn == "JSONStorableColor") {
					object v = f.GetValue(UserPreferences.singleton);
					if (v != null) found.Add(v);
				}
			}
			return found;
		}

		/// <summary>The JSONStorable that owns the user preference params: CoreControl's UserPreferences
		/// storable (whose param names start with 'pref'); falls back to SuperController's storable form.</summary>
		/// <summary>Public, renderable properties of UserPreferences (bool/float/int/string/enum only).</summary>
		private static List<PropertyInfo> PrefProps() {
			List<PropertyInfo> found = new List<PropertyInfo>();
			PropertyInfo[] props = typeof(UserPreferences).GetProperties(BindingFlags.Public | BindingFlags.Instance);
			foreach (PropertyInfo p in props) {
				if (p == null || !p.CanRead) continue;
				Type t = p.PropertyType;
				if (t == typeof(bool) || t == typeof(float) || t == typeof(int) || t == typeof(string) || t.IsEnum) found.Add(p);
			}
			found.Sort((a, b) => string.Compare(a.Name, b.Name));
			return found;
		}

		/// <summary>Public primitive fields of UserPreferences the properties do not cover (e.g. physicsRate).</summary>
		private static List<FieldInfo> PrefFields(List<string> excludeNames) {
			List<FieldInfo> found = new List<FieldInfo>();
			FieldInfo[] fields = typeof(UserPreferences).GetFields(BindingFlags.Public | BindingFlags.Instance);
			foreach (FieldInfo f in fields) {
				if (f == null) continue;
				Type t = f.FieldType;
				if (!(t == typeof(bool) || t == typeof(float) || t == typeof(int) || t == typeof(string) || t.IsEnum)) continue;
				if (excludeNames != null && excludeNames.Contains(f.Name)) continue;
				found.Add(f);
			}
			found.Sort((a, b) => string.Compare(a.Name, b.Name));
			return found;
		}

		private static string PrefToString(object val) {
			if (val == null) return "";
			return val.ToString();
		}

		public JSONNode GetUserPreferences(JSONNode args) {
			string category = S(args, "category").ToLowerInvariant();
			JSONClass r = new JSONClass();
			JSONArray items = new JSONArray();
			int total = 0;
			UserPreferences up = UserPreferences.singleton;
			if (up == null) throw new ApiError("UserPreferences.singleton not initialised yet");
			List<string> propNames = new List<string>();
			foreach (PropertyInfo p in PrefProps()) {
				total++;
				propNames.Add(p.Name);
				string n = p.Name;
				if (category != "" && n.ToLowerInvariant().IndexOf(category) < 0) continue;
				JSONClass row = new JSONClass();
				row["name"] = n;
				row["kind"] = "property";
				row["type"] = p.PropertyType.Name;
				object val = null;
				try { val = p.GetValue(up, null); } catch (Exception e) { row["value.error"] = e.Message; }
				row["value"] = PrefToString(val);
				items.Add(row);
			}
			foreach (FieldInfo f in PrefFields(propNames)) {
				total++;
				string n = f.Name;
				if (category != "" && n.ToLowerInvariant().IndexOf(category) < 0) continue;
				JSONClass row = new JSONClass();
				row["name"] = n;
				row["kind"] = "field";
				row["type"] = f.FieldType.Name;
				object val = null;
				try { val = f.GetValue(up); } catch (Exception e) { row["value.error"] = e.Message; }
				row["value"] = PrefToString(val);
				items.Add(row);
			}
			r["count"] = JSONRaw.Num(items.Count);
			r["total"] = JSONRaw.Num(total);
			r["prefs"] = items;
			if (category == "" && items.Count >= 400) r["note"] = "pass a category filter (e.g. 'physics', 'vr', 'render') to keep the list small";
			return r;
		}

		public JSONNode SetUserPreferences(JSONNode args) {
			string name = S(args, "key");
			if (name == "") throw new ApiError("key required (see get_user_preferences)");
			JSONNode value = args["value"];
			if (value == null) throw new ApiError("value required");
			UserPreferences up = UserPreferences.singleton;
			if (up == null) throw new ApiError("UserPreferences.singleton not initialised yet");
			JSONClass r = new JSONClass();
			r["key"] = name;
			Type ut = typeof(UserPreferences);
			PropertyInfo prop = ut.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
			if (prop == null) {
				foreach (PropertyInfo pp in ut.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
					if (string.Equals(pp.Name, name, StringComparison.OrdinalIgnoreCase)) { prop = pp; break; }
				}
			}
			if (prop != null && prop.CanWrite) {
				try {
					r["kind"] = "property";
					r["type"] = prop.PropertyType.Name;
					object converted;
					if (prop.PropertyType == typeof(bool)) converted = value.AsBool;
					else if (prop.PropertyType == typeof(float)) converted = value.AsFloat;
					else if (prop.PropertyType == typeof(int)) converted = value.AsInt;
					else if (prop.PropertyType == typeof(string)) converted = value.Value;
					else if (prop.PropertyType.IsEnum) converted = Enum.Parse(prop.PropertyType, value.Value, true);
					else converted = value.Value;
					prop.SetValue(up, converted, new object[0]);
					object rd = prop.GetValue(up, null);
					r["value"] = PrefToString(rd);
				} catch (Exception e) {
					throw new ApiError("failed to set pref " + name + ": " + e.Message);
				}
				try { up.SavePreferences(); r["saved"] = JSONRaw.Bool(true); } catch (Exception se) { r["saved"] = JSONRaw.Bool(false); r["save.error"] = se.Message; }
				return r;
			}
			FieldInfo field = ut.GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
			if (field == null) {
				foreach (FieldInfo ff in ut.GetFields(BindingFlags.Public | BindingFlags.Instance)) {
					if (string.Equals(ff.Name, name, StringComparison.OrdinalIgnoreCase)) { field = ff; break; }
				}
			}
			if (field != null) {
				try {
					r["kind"] = "field";
					r["type"] = field.FieldType.Name;
					object converted;
					if (field.FieldType == typeof(bool)) converted = value.AsBool;
					else if (field.FieldType == typeof(float)) converted = value.AsFloat;
					else if (field.FieldType == typeof(int)) converted = value.AsInt;
					else if (field.FieldType == typeof(string)) converted = value.Value;
					else if (field.FieldType.IsEnum) converted = Enum.Parse(field.FieldType, value.Value, true);
					else converted = value.Value;
					field.SetValue(up, converted, BindingFlags.Public | BindingFlags.Instance, null, null);
					object rd = field.GetValue(up);
					r["value"] = PrefToString(rd);
				} catch (Exception e) {
					throw new ApiError("failed to set pref " + name + ": " + e.Message);
				}
				try { up.SavePreferences(); r["saved"] = JSONRaw.Bool(true); } catch (Exception se) { r["saved"] = JSONRaw.Bool(false); r["save.error"] = se.Message; }
				return r;
			}
			throw new ApiError("user preferences key not found: " + name + " (see get_user_preferences)");
		}

		// ===================== scene / template browser =====================

		public JSONNode SceneBrowser(JSONNode args) {
			string action = S(args, "action").ToLowerInvariant();
			if (action == "list") return ListScenes(args);
			if (action == "load") return LoadScene(args);
			if (action == "new") return NewScene();
			if (action == "save") return SaveScene(args);
			if (action == "delete") {
				string path = S(args, "path");
				if (path == "") throw new ApiError("path required");
				if (S(args, "confirm").ToLowerInvariant() != "true") throw new ApiError("delete requires confirm:true (scene files are not recoverable)");
				if (!FileManagerSecure.FileExists(path)) throw new ApiError("scene file not found: " + path);
				string abs = Application.dataPath + "/.." + (path.StartsWith("/") ? "" : "/") + path;
				System.IO.File.Delete(abs);
				JSONClass r = new JSONClass();
				r["deleted"] = path;
				return r;
			}
			throw new ApiError("action required: List, Load, New, Save, Delete");
		}

		public JSONNode TemplateBrowser(JSONNode args) {
			string action = S(args, "action").ToLowerInvariant();
			if (action == "list") {
				JSONClass r = new JSONClass();
				r["looks"] = ListLooks(args)["count"];
				r["poses"] = ListPoses(args)["count"];
				r["scenes"] = ListScenes(args)["count"];
				r["clothing"] = ListClothingPresets(args)["count"];
				r["hair"] = ListHairPresets(args)["count"];
				r["note"] = "templates = look/pose/scene/clothing/hair presets; pass a category to Load";
				return r;
			}
			if (action == "load") {
				string cat = S(args, "category").ToLowerInvariant();
				if (cat == "look") return LoadLook(args);
				if (cat == "pose") return LoadPose(args);
				if (cat == "scene") return LoadScene(args);
				if (cat == "clothing") return LoadClothingPreset(args);
				if (cat == "hair") return LoadHairPreset(args);
				throw new ApiError("category required: look, pose, scene, clothing or hair");
			}
			throw new ApiError("action required: List or Load");
		}

		// ===================== atom UI (control, move, geometry) =====================

		public JSONNode AtomControl(JSONNode args) {
			Atom a = FindAtom(S(args, "uid"));
			string la = S(args, "action").ToLowerInvariant();
			if (la == "" ) throw new ApiError("action required: Select, FreezePhysics, UnfreezePhysics, DisableCollision, EnableCollision, IsolateEdit, EndIsolate, SetNameSpace");
			JSONClass r = new JSONClass();
			r["uid"] = a.uid;
			if (la == "select") {
				Exception last = null;
				foreach (string m in new string[] { "SelectAtom", "SetSelectedAtom", "Select" }) {
					try {
						if (m == "Select") Invoke(SC, "SelectAtom", a);
						else Invoke(SC, m, a);
						r["selected"] = JSONRaw.Bool(true);
						r["via"] = m;
						return r;
					} catch (Exception e) { last = e; }
				}
				throw new ApiError("atom selection not available via any known API (" + (last != null ? last.Message : "no match") + "); click the atom in the VaM scene panel instead");
			}
			if (la == "freezephysics" || la == "unfreezephysics") {
				bool freeze = la == "freezephysics";
				int touched = 0;
				// physics freeze = the FreeControllerV3 position/rotation state choosers ("Freeze" vs "Off" = physics-driven)
				string wantState = freeze ? "Freeze" : "Off";
				foreach (string sid in a.GetStorableIDs()) {
					if (!sid.EndsWith("Control", StringComparison.OrdinalIgnoreCase) && sid != "control") continue;
					JSONStorable st = a.GetStorableByID(sid);
					if (st == null) continue;
					try {
						string ps = MatchChooserParam(st, "positionState");
						string rs = MatchChooserParam(st, "rotationState");
						if (ps != null) { st.SetStringChooserParamValue(ps, wantState); touched++; r[sid + ".positionState"] = st.GetStringChooserParamValue(ps); }
						if (rs != null) { st.SetStringChooserParamValue(rs, wantState); touched++; r[sid + ".rotationState"] = st.GetStringChooserParamValue(rs); }
					} catch (Exception e) { r[sid + ".error"] = e.Message; }
				}
				r["touched"] = JSONRaw.Num(touched);
				r["note"] = freeze ? "controllers forced to Freeze (physics stops moving them); UnfreezePhysics sets them back to Off (physics-driven)"
					: "controllers returned to Off (physics-driven); a captured On-state pose would need set_control";
				return r;
			}
			bool enableCollision = la == "enablecollision";
			if (la == "disablecollision" || enableCollision) {
				int touched = 0;
				foreach (string sid in a.GetStorableIDs()) {
					JSONStorable st = a.GetStorableByID(sid);
					if (st == null) continue;
					string p = MatchBoolParam(st, "collision");
					if (p == null) continue;
					st.SetBoolParamValue(p, enableCollision);
					touched++;
					r[sid + "." + p] = JSONRaw.Bool(st.GetBoolParamValue(p));
				}
				r["touched"] = JSONRaw.Num(touched);
				return r;
			}
			if (la == "isolateedit" || la == "endisolate") {
				bool want = la == "isolateedit";
				Exception last = null;
				foreach (string m in new string[] { "SetAtomIsolate", "IsolateAtom", "ToggleIsolate" }) {
					try { Invoke(SC, m, a, want); r["isolate"] = JSONRaw.Bool(want); r["via"] = m; return r; } catch (Exception e) { last = e; }
				}
				foreach (string m in new string[] { "Isolate", "EndIsolate" }) {
					try { Invoke(a, m); r["isolate"] = JSONRaw.Bool(want); r["via"] = m; return r; } catch (Exception e) { last = e; }
				}
				throw new ApiError("isolate not available via any known API (" + (last != null ? last.Message : "no match") + "); use VaM's scene editor isolation instead");
			}
			if (la == "disablerender" || la == "enablerender") {
				// hiding an atom safely = VaM's own on toggle; killing mainController GameObjects breaks physics
				JSONNode sub = new JSONClass();
				sub["uid"] = a.uid;
				sub["on"] = JSONRaw.Bool(la == "enablerender");
				JSONNode res = SetAtomOn(sub);
				res["note"] = "DisableRender/EnableRender are mapped to the atom power toggle (VaM has no per-atom render-only hide without plugins)";
				return res;
			}
			if (la == "setnamespace") {
				throw new ApiError("SetNameSpace is not supported: nameSpace systems are plugin-specific; nothing settable here");
			}
			throw new ApiError("unknown atom action: " + S(args, "action"));
		}

		public JSONNode AtomMove(JSONNode args) {
			Atom a = FindAtom(S(args, "uid"));
			string la = S(args, "action").ToLowerInvariant();
			if (la == "" ) throw new ApiError("action required: SetPosition, SetRotation, SetScale, SnapToGround, AlignTo, ResetTransform");
			JSONClass r = new JSONClass();
			r["uid"] = a.uid;
			if (la == "setposition" || la == "setrotation" || la == "setscale") {
				JSONNode sub = new JSONClass();
				sub["uid"] = a.uid;
				if (la == "setposition") sub["position"] = args["value"];
				if (la == "setrotation") sub["rotation"] = args["value"];
				if (la == "setscale") sub["scale"] = args["value"];
				return SetAtomTransform(sub);
			}
			if (la == "resettransform") {
				JSONNode sub = new JSONClass();
				sub["uid"] = a.uid;
				sub["position"] = new JSONArray();
				sub["position"].Add(F(0f)); sub["position"].Add(F(0f)); sub["position"].Add(F(0f));
				sub["rotation"] = new JSONArray();
				sub["rotation"].Add(F(0f)); sub["rotation"].Add(F(0f)); sub["rotation"].Add(F(0f));
				sub["scale"] = new JSONArray();
				sub["scale"].Add(F(1f)); sub["scale"].Add(F(1f)); sub["scale"].Add(F(1f));
				return SetAtomTransform(sub);
			}
			if (la == "alignto") {
				Atom target = FindAtom(S(args, "target_uid"));
				JSONNode sub = new JSONClass();
				sub["uid"] = a.uid;
				sub["position"] = Vec(target.transform.position);
				sub["rotation"] = Vec(target.transform.eulerAngles);
				if (Bool(args, "scale", false)) sub["scale"] = Vec(target.transform.localScale);
				r["alignedTo"] = target.uid;
				JSONNode res = SetAtomTransform(sub);
				res["alignedTo"] = target.uid;
				return res;
			}
			if (la == "snaptoground") {
				// honest best-effort: drop the atom so its lowest reported bbox Y matches y=0;
				// VaM has no baked-in snap API here, so we use the renderer bounds of the atom's collider/renderer.
				Mt.Run(delegate {
					Renderer[] rs = a.GetComponentsInChildren<Renderer>();
					if (rs != null && rs.Length > 0) {
						Bounds b = rs[0].bounds;
						for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
						a.transform.position = new Vector3(a.transform.position.x, a.transform.position.y - b.min.y, a.transform.position.z);
						r["newY"] = F(a.transform.position.y);
						return;
					}
					a.transform.position = new Vector3(a.transform.position.x, 0f, a.transform.position.z);
					r["newY"] = F(0f);
				}, 10000);
				r["note"] = "snapped via renderer bounds (when present) or dropped to y=0";
				return r;
			}
			throw new ApiError("unknown atom move action: " + S(args, "action"));
		}

		public JSONNode AtomGeometryChoices(JSONNode args) {
			Atom a = FindAtom(S(args, "uid"));
			string la = S(args, "action").ToLowerInvariant();
			if (la == "") throw new ApiError("action required: List or Select");
			string used;
			JSONStorable st = ResolveStorable(a, S(args, "storable") != "" ? S(args, "storable") : "asset", "asset", out used);
			if (st == null) throw new ApiError("no asset storable on " + a.uid + "; storables: " + string.Join(", ", a.GetStorableIDs().ToArray()));
			JSONClass r = new JSONClass();
			r["uid"] = a.uid;
			r["storable"] = used;
			if (la == "list") {
				JSONArray ch = new JSONArray();
				foreach (string name in SortedNames(st.GetStringChooserParamNames())) {
					JSONClass row = new JSONClass();
					row["param"] = name;
					try {
						row["value"] = st.GetStringChooserParamValue(name);
						JSONStorableStringChooser obj = GetChooserObj(st, name);
						if (obj != null && obj.choices != null) {
							JSONArray choices = new JSONArray();
							foreach (string c in obj.choices) choices.Add(c);
							row["choices"] = choices;
						}
					} catch (Exception e) { row["error"] = e.Message; }
					ch.Add(row);
				}
				r["choosers"] = ch;
				return r;
			}
			if (la == "select") {
				string p = S(args, "param");
				if (p == "") p = MatchChooserParam(st, "assetName");
				if (p == null) throw new ApiError("no assetName chooser; " + ActionHelp(st));
				string choice = S(args, "choice");
				st.SetStringChooserParamValue(p, choice);
				r["param"] = p;
				r["value"] = st.GetStringChooserParamValue(p);
				return r;
			}
			throw new ApiError("unknown action: " + S(args, "action"));
		}

		// ===================== person animation =====================

		public JSONNode PersonAnimation(JSONNode args) {
			Atom p = RequirePerson(S(args, "person"));
			string la = S(args, "action").ToLowerInvariant();
			if (la == "") throw new ApiError("action required: ArmRecord, RecordAnimation, PlaybackEnabled, PlayAnimation, StopAnimation");
			JSONClass r = new JSONClass();
			r["uid"] = p.uid;
			// find the per-person animation storable (id pattern varies by setup)
			JSONStorable st = null;
			string used = "";
			foreach (string sid in p.GetStorableIDs()) {
				if (sid.IndexOf("animation", StringComparison.OrdinalIgnoreCase) < 0) continue;
				st = p.GetStorableByID(sid);
				used = sid;
				break;
			}
			if (st == null) {
				throw new ApiError("no animation storable on " + p.uid + "; VaM drives person animation via AnimationPattern atoms (list_animation_patterns) or pose presets (load_pose); a plugin may be needed for bone recording");
			}
			r["storable"] = used;
			if (la == "armrecord" || la == "recordanimation") {
				JSONStorableAction act = FindActionCI(st, "record");
				if (act == null) act = FindActionCI(st, "arm");
				if (act == null) act = FindActionCI(st, "start");
				if (act != null && act.actionCallback != null) { act.actionCallback(); r["called"] = act.name; return r; }
				string bp = MatchBoolParam(st, "record");
				if (bp != null) { st.SetBoolParamValue(bp, true); r[bp] = JSONRaw.Bool(st.GetBoolParamValue(bp)); return r; }
				throw new ApiError("no record action/param on " + used + "; " + ActionHelp(st));
			}
			if (la == "playbackenabled") {
				string bp = MatchBoolParam(st, "playback");
				if (bp == null) bp = MatchBoolParam(st, "enabled");
				if (bp == null) throw new ApiError("no playback param on " + used + "; " + ActionHelp(st));
				st.SetBoolParamValue(bp, Bool(args, "value", true));
				r[bp] = JSONRaw.Bool(st.GetBoolParamValue(bp));
				return r;
			}
			if (la == "playanimation" || la == "stopanimation") {
				bool play = la == "playanimation";
				JSONStorableAction act = FindActionCI(st, play ? "play" : "stop");
				if (act != null && act.actionCallback != null) { act.actionCallback(); r["called"] = act.name; return r; }
				string bp = MatchBoolParam(st, "play");
				if (bp == null) bp = MatchBoolParam(st, "playing");
				if (bp != null) { st.SetBoolParamValue(bp, play); r[bp] = JSONRaw.Bool(st.GetBoolParamValue(bp)); return r; }
				throw new ApiError("no play/stop control on " + used + "; " + ActionHelp(st));
			}
			throw new ApiError("unknown animation action: " + S(args, "action") + "; " + ActionHelp(st));
		}

		// ===================== custom unity assets =====================

		public JSONNode ListCUA(JSONNode args) {
			JSONArray arr = new JSONArray();
			foreach (Atom a in SC.GetAtoms()) {
				if (a == null) continue;
				if (!AtomTypeMatches(a, "CustomUnityAsset")) continue;
				JSONClass row = AtomInfo(a);
				string used;
				JSONStorable st = ResolveStorable(a, "asset", "asset", out used);
				if (st != null) {
					row["storable"] = used;
					string up = MatchUrlParam(st, "assetUrl");
					if (up != null) row["assetUrl"] = st.GetUrlParamValue(up);
					string cp = MatchChooserParam(st, "assetName");
					if (cp != null) row["assetName"] = st.GetStringChooserParamValue(cp);
					string pb = MatchBoolParam(st, "loaded");
					if (pb != null) row["loaded"] = JSONRaw.Bool(st.GetBoolParamValue(pb));
				}
				arr.Add(row);
			}
			JSONClass r = new JSONClass();
			r["count"] = JSONRaw.Num(arr.Count);
			r["cuas"] = arr;
			return r;
		}

		public JSONNode CUAAction(JSONNode args) {
			Atom a = FindAtom(S(args, "uid"));
			string la = S(args, "action").ToLowerInvariant();
			if (la == "" ) throw new ApiError("action required: SetAssetUrl, SetAssetName, LoadAsset, UnloadAsset, ReloadAsset");
			JSONClass r = new JSONClass();
			r["uid"] = a.uid;
			string used;
			JSONStorable st = ResolveStorable(a, "asset", "asset", out used);
			if (st == null) throw new ApiError("no asset storable on " + a.uid + "; storables: " + string.Join(", ", a.GetStorableIDs().ToArray()));
			r["storable"] = used;
			if (la == "setasseturl") {
				string p = MatchUrlParam(st, "assetUrl");
				if (p == null) throw new ApiError("no assetUrl url param; " + ActionHelp(st));
				st.SetUrlParamValue(p, S(args, "value"));
				r["assetUrl"] = st.GetUrlParamValue(p);
				return r;
			}
			if (la == "setassetname") {
				string p = MatchChooserParam(st, "assetName");
				if (p == null) throw new ApiError("no assetName chooser; " + ActionHelp(st));
				st.SetStringChooserParamValue(p, S(args, "value"));
				r["assetName"] = st.GetStringChooserParamValue(p);
				return r;
			}
			foreach (string n in st.GetActionNames()) {
				string ln = n.ToLowerInvariant();
				bool hit = (la == "loadasset" && ln.IndexOf("load") >= 0 && ln.IndexOf("unload") < 0) ||
					(la == "unloadasset" && ln.IndexOf("unload") >= 0) ||
					(la == "reloadasset" && (ln.IndexOf("reload") >= 0 || ln.IndexOf("reset") >= 0));
				if (hit) {
					JSONStorableAction act = st.GetAction(n);
					if (act != null && act.actionCallback != null) { act.actionCallback(); r["called"] = n; return r; }
				}
			}
			throw new ApiError("CUA load flows through setting assetUrl/assetName (SetAssetUrl/SetAssetName); no LoadAsset-style action on this atom: " + ActionHelp(st));
		}

		// ===================== package manager =====================

		public JSONNode PackageManager(JSONNode args) {
			string action = S(args, "action").ToLowerInvariant();
			if (action == "listenabled" || action == "list") {
				return ListPackages();
			}
			if (action == "scanpackages" || action == "refreshpackages") {
				Exception last = null;
				foreach (string m in new string[] { "StartAddonPackageScan", "ScanPackages", "RefreshPackages", "RefreshAddonPackages" }) {
					try { Invoke(SC, m); JSONClass r = new JSONClass(); r["called"] = m; r["ok"] = JSONRaw.Bool(true); return r; } catch (Exception e) { last = e; }
				}
				throw new ApiError("package rescan not available via any known API (" + (last != null ? last.Message : "no match") + "); restart VaM to rescan AddonPackages");
			}
			if (action == "enablepackage" || action == "disablepackage") {
				throw new ApiError("per-package enable/disable is only exposed in VaM's own Package Manager UI; to remove a package, move its .var file out of AddonPackages (or use action=deletepackage)");
			}
			if (action == "deletepackage") {
				string pkg = S(args, "package");
				if (pkg == "") throw new ApiError("package required (file name of an installed .var)");
				if (S(args, "confirm").ToLowerInvariant() != "true") throw new ApiError("delete requires confirm:true (.var files are not recoverable from this plugin)");
				if (!pkg.EndsWith(".var")) pkg += ".var";
				string dir = Application.dataPath + "/../AddonPackages";
				string found = null;
				foreach (string f in System.IO.Directory.GetFiles(dir, "*.var")) {
					if (string.Equals(System.IO.Path.GetFileName(f), pkg, StringComparison.OrdinalIgnoreCase)) { found = f; break; }
				}
				if (found == null) throw new ApiError("package not found in AddonPackages: " + pkg);
				System.IO.File.Delete(found);
				JSONClass r = new JSONClass();
				r["deleted"] = System.IO.Path.GetFileName(found);
				r["note"] = "the package disappears after VaM rescans; a running scene referencing it keeps working until reload";
				return r;
			}
			throw new ApiError("action required: ScanPackages, RefreshPackages, ListEnabled, EnablePackage, DisablePackage, DeletePackage");
		}

		// ===================== scene state inspection / debugging =====================

		public JSONNode SceneState(JSONNode args) {
			string detail = S(args, "detail").ToLowerInvariant();
			if (detail == "") detail = "summary";
			Dictionary<string, int> byType = new Dictionary<string, int>();
			JSONClass r = new JSONClass();
			r["plugin"] = "VaMMCP";
			r["time"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
			foreach (Atom a in SC.GetAtoms()) {
				if (a == null) continue;
				if (!byType.ContainsKey(a.type)) byType[a.type] = 0;
				byType[a.type]++;
			}
			JSONArray types = new JSONArray();
			int atoms = 0;
			foreach (KeyValuePair<string, int> kv in byType) {
				JSONClass row = new JSONClass();
				row["type"] = kv.Key;
				row["count"] = JSONRaw.Num(kv.Value);
				types.Add(row);
				atoms += kv.Value;
			}
			r["atoms"] = JSONRaw.Num(atoms);
			r["atomTypes"] = types;
			r["timeScale"] = F(Time.timeScale);
			try { r["loadingScene"] = JSONRaw.Bool(SC.isLoading); } catch { }
			try {
				Camera cam = SC.MonitorCenterCamera;
				if (cam != null) {
					r["camera"] = new JSONClass();
					r["camera"]["position"] = Vec(cam.transform.position);
					r["camera"]["rotation"] = Vec(cam.transform.eulerAngles);
				}
			} catch { }
			if (detail == "persons" || detail == "full") {
				JSONNode summary = ListPersons();
				r["persons"] = summary["persons"];
			}
			if (detail == "plugins" || detail == "full") {
				JSONNode plugins = ListPlugins(new JSONClass());
				r["plugins"] = plugins["plugins"];
				r["pluginCount"] = plugins["count"];
			}
			if (detail == "physics" || detail == "full") {
				r["physics"] = ListPhysicsObjects(new JSONClass())["objects"];
			}
			return r;
		}

		private static void AtomTreeWalk(Transform t, int depth, int maxDepth, JSONArray outArr, int cap) {
			if (outArr.Count >= cap) return;
			JSONClass node = new JSONClass();
			node["name"] = t.name;
			StringBuilder comps = new StringBuilder();
			Component[] cs = t.GetComponents<Component>();
			if (cs != null) {
				for (int i = 0; i < cs.Length; i++) {
					if (cs[i] == null) { comps.Append("(missing);"); continue; }
					comps.Append(cs[i].GetType().Name);
					comps.Append(";");
				}
			}
			node["components"] = comps.ToString();
			node["depth"] = JSONRaw.Num(depth);
			if (t.parent != null) node["parent"] = t.parent.name;
			outArr.Add(node);
			if (depth >= maxDepth) return;
			foreach (Transform c in t) AtomTreeWalk(c, depth + 1, maxDepth, outArr, cap);
		}

		public JSONNode AtomTree(JSONNode args) {
			string rootUid = S(args, "root_uid");
			int maxDepth = (int)Num(args, "max_depth", 2);
			if (maxDepth < 1) maxDepth = 1;
			if (maxDepth > 5) maxDepth = 5;
			JSONArray arr = new JSONArray();
			if (rootUid != "") {
				Atom a = FindAtom(rootUid);
				JSONClass info = AtomInfo(a);
				JSONArray nodes = new JSONArray();
				AtomTreeWalk(a.mainController != null ? a.mainController.transform : a.transform, 0, maxDepth, nodes, 400);
				info["nodes"] = nodes;
				info["nodeLimit"] = JSONRaw.Num(400);
				JSONArray single = new JSONArray();
				single.Add(info);
				arr = single;
			} else {
				foreach (Atom a in SC.GetAtoms()) {
					if (a == null) continue;
					JSONClass info = AtomInfo(a);
					JSONArray nodes = new JSONArray();
					AtomTreeWalk(a.mainController != null ? a.mainController.transform : a.transform, 0, 1, nodes, 12);
					info["nodes"] = nodes;
					arr.Add(info);
					if (arr.Count >= 60) break;
				}
			}
			JSONClass r = new JSONClass();
			r["count"] = JSONRaw.Num(arr.Count);
			r["atoms"] = arr;
			return r;
		}

		public JSONNode PersonState(JSONNode args) {
			Atom p = RequirePerson(S(args, "person"));
			bool includeHidden = Bool(args, "include_hidden", false);
			JSONClass r = new JSONClass();
			r["uid"] = p.uid;
			try {
				DAZCharacterSelector sel = Selector(p);
				try { r["character"] = sel.GetStringChooserParamValue("characterSelection"); } catch { r["character"] = ""; }
				r["gender"] = sel.gender.ToString();
				int morphs = 0;
				JSONArray top = new JSONArray();
				foreach (DAZMorph m in AllMorphs(sel)) {
					try {
						float v = m.morphValue;
						if (Math.Abs(v) < 0.0005f) {
							if (includeHidden) { morphs++; }
							continue;
						}
						morphs++;
						if (top.Count < 40) {
							JSONClass row = new JSONClass();
							row["name"] = m.displayName;
							row["value"] = F(v);
							top.Add(row);
						}
					} catch { }
				}
				r["nonZeroMorphs"] = JSONRaw.Num(morphs);
				r["topMorphs"] = top;
			} catch (Exception e) { r["morphs.error"] = e.Message; }
			JSONNode cloth = ListClothingItems(args);
			r["clothingCount"] = cloth["count"];
			JSONNode hair = ListHairItems(args);
			r["hairCount"] = hair["count"];
			JSONClass controls = new JSONClass();
			foreach (string id in p.GetStorableIDs()) {
				if (id.EndsWith("Control", StringComparison.OrdinalIgnoreCase)) controls[id] = JSONRaw.Bool(true);
			}
			r["controls"] = controls;
			return r;
		}

		public JSONNode PhysicsState() {
			JSONClass r = new JSONClass();
			r["timeScale"] = F(Time.timeScale);
			r["fixedDeltaTime"] = F(Time.fixedDeltaTime);
			JSONNode objs = ListPhysicsObjects(new JSONClass());
			r["physicsObjectCount"] = objs["count"];
			r["objects"] = objs["objects"];
			return r;
		}

		public JSONNode SimulationPerformance() {
			JSONClass r = new JSONClass();
			r["frameCount"] = JSONRaw.Num(Time.frameCount);
			r["deltaTime"] = F(Time.deltaTime);
			r["unscaledDeltaTime"] = F(Time.unscaledDeltaTime);
			r["timeScale"] = F(Time.timeScale);
			r["realtimeSinceStartup"] = F(Time.realtimeSinceStartup);
			r["maxDelta"] = F(Time.maximumDeltaTime);
			try { r["vSync"] = JSONRaw.Num(QualitySettings.vSyncCount); } catch { }
			r["gcHeapBytes"] = JSONRaw.Num(GC.GetTotalMemory(false));
			int atoms = 0;
			foreach (Atom a in SC.GetAtoms()) if (a != null) atoms++;
			r["atoms"] = JSONRaw.Num(atoms);
			return r;
		}

		private static bool ParamValueMatches(JSONStorable st, string name, string kind, string want) {
			try {
				if (kind == "float") {
					float v = st.GetFloatParamValue(name);
					float w;
					return float.TryParse(want, out w) ? Math.Abs(v - w) < 1e-5f : false;
				}
				if (kind == "bool") {
					bool v = st.GetBoolParamValue(name);
					return v ? (want == "true" || want == "1") : (want == "false" || want == "0");
				}
				if (kind == "string") return string.Equals(st.GetStringParamValue(name), want, StringComparison.OrdinalIgnoreCase);
				if (kind == "chooser") return string.Equals(st.GetStringChooserParamValue(name), want, StringComparison.OrdinalIgnoreCase);
			} catch { }
			return false;
		}

		public JSONNode FindAtoms(JSONNode args) {
			string nameF = S(args, "name");
			string typeF = S(args, "type");
			string compF = S(args, "component");
			string propF = S(args, "property");
			string valF = S(args, "value");
			string tagF = S(args, "tag");
			if (tagF != "") throw new ApiError("tag filter is not supported by VaM's runtime; use name/type/component/property filters");
			JSONArray arr = new JSONArray();
			foreach (Atom a in SC.GetAtoms()) {
				if (a == null) continue;
				if (nameF != "" && a.uid.IndexOf(nameF, StringComparison.OrdinalIgnoreCase) < 0
					&& (a.name == null || a.name.IndexOf(nameF, StringComparison.OrdinalIgnoreCase) < 0)) continue;
				if (typeF != "" && !string.Equals(a.type, typeF, StringComparison.OrdinalIgnoreCase)) continue;
				bool keep = nameF == "" && typeF == "";
				if (compF != "") {
					if (a.mainController == null) continue;
					Component[] cs = a.mainController.GetComponentsInChildren<Component>(true);
					bool found = false;
					foreach (Component c in cs) {
						if (c != null && c.GetType().Name.IndexOf(compF, StringComparison.OrdinalIgnoreCase) >= 0) { found = true; break; }
					}
					if (!found) continue;
					keep = true;
				}
				if (propF != "") {
					bool matched = false;
					foreach (string sid in a.GetStorableIDs()) {
						if (matched) break;
						JSONStorable st = a.GetStorableByID(sid);
						if (st == null) continue;
						foreach (string n in st.GetFloatParamNames()) {
							if ((n.IndexOf(propF, StringComparison.OrdinalIgnoreCase) >= 0 || string.Equals(n, propF, StringComparison.OrdinalIgnoreCase))
								&& (valF == "" || ParamValueMatches(st, n, "float", valF))) { matched = true; break; }
						}
						if (matched) break;
						foreach (string n in st.GetBoolParamNames()) {
							if ((n.IndexOf(propF, StringComparison.OrdinalIgnoreCase) >= 0 || string.Equals(n, propF, StringComparison.OrdinalIgnoreCase))
								&& (valF == "" || ParamValueMatches(st, n, "bool", valF))) { matched = true; break; }
						}
						if (matched) break;
						foreach (string n in st.GetStringChooserParamNames()) {
							if ((n.IndexOf(propF, StringComparison.OrdinalIgnoreCase) >= 0 || string.Equals(n, propF, StringComparison.OrdinalIgnoreCase))
								&& (valF == "" || ParamValueMatches(st, n, "chooser", valF))) { matched = true; break; }
						}
						if (matched) break;
						foreach (string n in st.GetStringParamNames()) {
							if ((n.IndexOf(propF, StringComparison.OrdinalIgnoreCase) >= 0 || string.Equals(n, propF, StringComparison.OrdinalIgnoreCase))
								&& (valF == "" || ParamValueMatches(st, n, "string", valF))) { matched = true; break; }
						}
					}
					if (!matched) continue;
					keep = true;
				}
				if (!keep) continue;
				arr.Add(AtomInfo(a));
				if (arr.Count >= 100) break;
			}
			JSONClass r = new JSONClass();
			r["count"] = JSONRaw.Num(arr.Count);
			r["matches"] = arr;
			if (arr.Count >= 100) r["note"] = "result truncated at 100 atoms";
			return r;
		}

		private static void CollectParamMap(JSONStorable st, Dictionary<string, float> floats, Dictionary<string, bool> bools, Dictionary<string, string> values, int cap) {
			int i = 0;
			foreach (string n in st.GetFloatParamNames()) {
				if (i++ >= cap) break;
				try { floats[n] = st.GetFloatParamValue(n); } catch { }
			}
			i = 0;
			foreach (string n in st.GetBoolParamNames()) {
				if (i++ >= cap) break;
				try { bools[n] = st.GetBoolParamValue(n); } catch { }
			}
			i = 0;
			foreach (string n in st.GetStringChooserParamNames()) {
				if (i++ >= cap) break;
				try { values[n] = st.GetStringChooserParamValue(n); } catch { }
			}
			i = 0;
			foreach (string n in st.GetStringParamNames()) {
				if (i++ >= cap) break;
				try { values[n] = st.GetStringParamValue(n); } catch { }
			}
		}

		public JSONNode StorableDiff(JSONNode args) {
			Atom a = FindAtom(S(args, "uid_a"));
			Atom b = FindAtom(S(args, "uid_b"));
			JSONStorable sa = RequireResolvedStorable(a, S(args, "storable_a"), null, "storable A");
			JSONStorable sb = RequireResolvedStorable(b, S(args, "storable_b"), null, "storable B");
			Dictionary<string, float> fa = new Dictionary<string, float>();
			Dictionary<string, bool> ba = new Dictionary<string, bool>();
			Dictionary<string, string> va = new Dictionary<string, string>();
			Dictionary<string, float> fb = new Dictionary<string, float>();
			Dictionary<string, bool> bb = new Dictionary<string, bool>();
			Dictionary<string, string> vb = new Dictionary<string, string>();
			CollectParamMap(sa, fa, ba, va, 200);
			CollectParamMap(sb, fb, bb, vb, 200);
			int same = 0, diff = 0;
			JSONArray diffs = new JSONArray();
			JSONArray onlyA = new JSONArray();
			JSONArray onlyB = new JSONArray();
			foreach (KeyValuePair<string, float> kv in fa) {
				if (!fb.ContainsKey(kv.Key)) { onlyA.Add(kv.Key); continue; }
				if (Math.Abs(kv.Value - fb[kv.Key]) > 1e-5f) {
					JSONClass row = new JSONClass();
					row["param"] = kv.Key;
					row["a"] = F(kv.Value);
					row["b"] = F(fb[kv.Key]);
					diffs.Add(row);
					diff++;
				} else same++;
			}
			foreach (KeyValuePair<string, bool> kv in ba) {
				if (!bb.ContainsKey(kv.Key)) { onlyA.Add(kv.Key); continue; }
				if (kv.Value != bb[kv.Key]) {
					JSONClass row = new JSONClass();
					row["param"] = kv.Key;
					row["a"] = JSONRaw.Bool(kv.Value);
					row["b"] = JSONRaw.Bool(bb[kv.Key]);
					diffs.Add(row);
					diff++;
				} else same++;
			}
			foreach (KeyValuePair<string, string> kv in va) {
				if (!vb.ContainsKey(kv.Key)) { onlyA.Add(kv.Key); continue; }
				if (!string.Equals(kv.Value, vb[kv.Key], StringComparison.OrdinalIgnoreCase)) {
					JSONClass row = new JSONClass();
					row["param"] = kv.Key;
					row["a"] = kv.Value != null && kv.Value.Length > 160 ? kv.Value.Substring(0, 160) + "..." : kv.Value;
					row["b"] = vb[kv.Key] != null && vb[kv.Key].Length > 160 ? vb[kv.Key].Substring(0, 160) + "..." : vb[kv.Key];
					diffs.Add(row);
					diff++;
				} else same++;
			}
			foreach (string k in fb.Keys) if (!fa.ContainsKey(k)) onlyB.Add(k);
			foreach (string k in bb.Keys) if (!ba.ContainsKey(k)) onlyB.Add(k);
			foreach (string k in vb.Keys) if (!va.ContainsKey(k) && !fb.ContainsKey(k)) onlyB.Add(k);
			JSONClass r = new JSONClass();
			r["same"] = JSONRaw.Num(same);
			r["different"] = JSONRaw.Num(diff);
			r["onlyInA"] = onlyA;
			r["onlyInB"] = onlyB;
			r["diffs"] = diffs;
			return r;
		}

		public JSONNode SceneDependencies() {
			HashSet<string> installed = new HashSet<string>();
			try {
				string dir = Application.dataPath + "/../AddonPackages";
				foreach (string f in System.IO.Directory.GetFiles(dir, "*.var")) {
					installed.Add(System.IO.Path.GetFileName(f));
				}
			} catch { }
			HashSet<string> seen = new HashSet<string>();
			JSONArray rows = new JSONArray();
			foreach (Atom a in SC.GetAtoms()) {
				if (a == null) continue;
				JSONClass row = new JSONClass();
				foreach (string sid in a.GetStorableIDs()) {
					JSONStorable st = a.GetStorableByID(sid);
					if (st == null) continue;
					string[] urls = null;
					try { urls = st.GetUrlParamNames().ToArray(); } catch { continue; }
					if (urls == null) continue;
					foreach (string n in urls) {
						string v;
						try { v = st.GetUrlParamValue(n); } catch { continue; }
						if (v == null || v == "" || seen.Contains(v)) continue;
						seen.Add(v);
						row["atom"] = a.uid;
						row["storable"] = sid;
						row["param"] = n;
						row["path"] = v;
						string pak = v.IndexOf(":") >= 0 ? v.Substring(0, v.IndexOf(":")) : "";
						bool missing = pak != "" && !installed.Contains(pak) && !System.IO.File.Exists(Application.dataPath + "/../" + v.Replace('/', System.IO.Path.DirectorySeparatorChar));
						row["missing"] = JSONRaw.Bool(missing);
						rows.Add(row);
						if (rows.Count >= 80) return FinishRefs(rows, seen);
					}
				}
			}
			return FinishRefs(rows, seen);
		}

		private static JSONNode FinishRefs(JSONArray rows, HashSet<string> seen) {
			JSONClass rr = new JSONClass();
			rr["count"] = JSONRaw.Num(rows.Count);
			rr["refs"] = rows;
			rr["unique"] = JSONRaw.Num(seen.Count);
			rr["note"] = "scans url params for package paths (name.N:/rev or disk); missing=true when no matching .var and the file does not exist";
			return rr;
		}

		public JSONNode PerformanceProfile(JSONNode args) {
			float durationMs = Num(args, "duration_ms", 3000f);
			if (durationMs < 250f) durationMs = 250f;
			if (durationMs > 30000f) durationMs = 30000f;
			DateTime end = DateTime.UtcNow.AddMilliseconds(durationMs);
			int lastFrame = -1;
			int frames = 0;
			List<float> sampleMs = new List<float>();
			float stallTime = 0f;
			const int win = 50;
			while (DateTime.UtcNow < end) {
				System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
				bool advanced = false;
				DateTime frameStarted = DateTime.UtcNow;
				while (sw.Elapsed.TotalMilliseconds < win) {
					int fc = Time.frameCount;
					if (lastFrame == -1) lastFrame = fc;
					if (fc > lastFrame) {
						if (!advanced && frames > 0) {
							float gap = (float)(DateTime.UtcNow - frameStarted).TotalMilliseconds;
							sampleMs.Add(gap);
						}
						advanced = true;
						lastFrame = fc;
						frames++;
						frameStarted = DateTime.UtcNow;
					}
					Thread.Sleep(1);
				}
				if (!advanced) stallTime += (float)sw.Elapsed.TotalMilliseconds;
			}
			float avg = 0f;
			float min = 0f, max = 0f;
			if (sampleMs.Count > 0) {
				min = sampleMs[0];
				max = sampleMs[0];
				foreach (float f in sampleMs) { avg += f; if (f < min) min = f; if (f > max) max = f; }
				avg /= sampleMs.Count;
			}
			int atoms = 0;
			foreach (Atom a in SC.GetAtoms()) if (a != null) atoms++;
			JSONClass r = new JSONClass();
			r["durationMs"] = F(durationMs);
			r["frames"] = JSONRaw.Num(frames);
			r["avgFrameMs"] = F(avg);
			r["minFrameMs"] = F(min);
			r["maxFrameMs"] = F(max);
			r["fpsEstimate"] = avg > 0f ? F(1000f / avg) : JSONRaw.Num(0f);
			r["stallMs"] = F(stallTime);
			r["atoms"] = JSONRaw.Num(atoms);
			r["gcHeapBytes"] = JSONRaw.Num(GC.GetTotalMemory(false));
			return r;
		}

		// ===================== person hand control =====================

		public JSONNode PersonHandControl(JSONNode args) {
			Atom p = RequirePerson(S(args, "person"));
			string hand = S(args, "hand").ToLowerInvariant();
			if (hand != "left" && hand != "right") throw new ApiError("hand required: left or right");
			string ctrlId = (hand == "left" ? "lHandControl" : "rHandControl");
			string la = S(args, "action").ToLowerInvariant();
			if (la == "" ) throw new ApiError("action required: SetTarget, SetWeight, SetRotation, AutoGrasp, Release");
			JSONClass r = new JSONClass();
			r["uid"] = p.uid;
			r["hand"] = hand;
			string used;
			JSONStorable st = ResolveStorable(p, ctrlId, hand == "left" ? "lhand" : "rhand", out used);
			if (st == null) throw new ApiError("no " + ctrlId + " control on " + p.uid + "; storables: " + string.Join(", ", p.GetStorableIDs().ToArray()));
			r["storable"] = used;
			if (la == "settarget") {
				string target = S(args, "target_uid");
				// FreeControllerV3 look-at: try a look-at target chooser first
				string lookParam = MatchChooserParam(st, "lookat");
				if (lookParam != null) {
					st.SetStringChooserParamValue(lookParam, target);
					r["lookAt"] = st.GetStringChooserParamValue(lookParam);
					return r;
				}
				throw new ApiError("no look-at target chooser on the hand control (available params visible via list_storable_params storable=" + used + "); position the hand with set_control instead");
			}
			if (la == "autograsp" || la == "release") {
				string graspParam = MatchBoolParam(st, "grasp");
				if (graspParam != null) { st.SetBoolParamValue(graspParam, la == "autograsp"); r["grasp"] = JSONRaw.Bool(st.GetBoolParamValue(graspParam)); return r; }
				graspParam = MatchFloatParam(st, "grasp");
				if (graspParam != null) { st.SetFloatParamValue(graspParam, la == "autograsp" ? Num(args, "value", 1f) : 0f); r["grasp"] = F(st.GetFloatParamValue(graspParam)); return r; }
				throw new ApiError("no grasp param on the hand control; " + ActionHelp(st));
			}
			throw new ApiError("unknown hand action: " + S(args, "action") + "; weights/grasp live on the hands look plugin if present — see list_plugins");
		}

		// ===================== person material =====================

		public JSONNode PersonMaterial(JSONNode args) {
			Atom p = RequirePerson(S(args, "person"));
			string material = S(args, "material");
			if (material == "") throw new ApiError("material required (e.g. skin, genitals, eyes, mouth, nails)");
			string matched = null;
			foreach (string sid in p.GetStorableIDs()) {
				if (string.Equals(sid, material, StringComparison.OrdinalIgnoreCase)) { matched = sid; break; }
			}
			if (matched == null) {
				foreach (string sid in p.GetStorableIDs()) {
					if (sid.IndexOf(material, StringComparison.OrdinalIgnoreCase) >= 0) { matched = sid; break; }
				}
			}
			if (matched == null) throw new ApiError("no material storable matching \"" + material + "\" on " + p.uid + "; storables: " + string.Join(", ", p.GetStorableIDs().ToArray()));
			string la = S(args, "action").ToLowerInvariant();
			if (la == "" ) throw new ApiError("action required: ListParams, GetParam, SetParam");
			if (la == "listparams") {
				JSONClass r = new JSONClass();
				r["uid"] = p.uid;
				r["storable"] = matched;
				r["params"] = ParamSnapshot(p.GetStorableByID(matched), 60, 40, 10, 8, 8);
				return r;
			}
			JSONNode sub = new JSONClass();
			sub["uid"] = p.uid;
			sub["storable"] = matched;
			sub["param"] = args["param"];
			if (Has(args, "value")) sub["value"] = args["value"];
			if (Has(args, "type")) sub["type"] = args["type"];
			if (Has(args, "reload")) sub["reload"] = args["reload"];
			return la == "getparam" ? GetParam(sub) : SetParam(sub);
		}

		// ===================== hub (community resources) =====================

		private static string hubApiUrlCache;

		private static string HubApiUrl() {
			if (hubApiUrlCache != null) return hubApiUrlCache;
			try {
				Atom core = SC.GetAtomByUid("CoreControl");
				if (core != null) {
					JSONStorable st = core.GetStorableByID("HubDownloader");
					if (st != null) {
						FieldInfo f = st.GetType().GetField("apiUrl", BindingFlags.NonPublic | BindingFlags.Instance);
						if (f != null) {
							object v = f.GetValue(st);
							if (v != null && v.ToString() != "") {
								hubApiUrlCache = v.ToString();
								return hubApiUrlCache;
							}
						}
					}
				}
			} catch (Exception e) {
				Log.Debug("HubApiUrl: " + e.Message);
			}
			return "https://hub.virtamate.com/api";
		}

		/// <summary>Poller tool. Browse the VaM Hub (community resources) via the game's own Hub API.
		/// Uses UnityWebRequest (Unity's TLS) — plain HttpWebRequest cannot speak TLS1.2 on the .NET 3.5 profile.</summary>
		public JSONNode HubBrowse(JSONNode args) {
			string apiUrl = Mt.Run(() => HubApiUrl(), 10000);
			JSONClass body = new JSONClass();
			body["source"] = "VaM";
			body["action"] = "getResources";
			body["latest_image"] = "Y";
			// The Hub API takes its parameters as strings; these are request fields, not our output.
			body["perpage"] = ((int)Num(args, "perpage", 20)).ToString();
			body["page"] = ((int)Num(args, "page", 1)).ToString();
			string search = S(args, "search");
			if (search != "") {
				body["search"] = search;
				body["searchall"] = "true";
			}
			string sort = S(args, "sort");
			if (sort != "") body["sort"] = sort;
			string type = S(args, "type");
			if (type != "") body["type"] = type;
			string postData = body.ToString();

			object gate = new object();
			bool done = false;
			string result = null;
			string error = null;
			Mt.Run(delegate {
				try {
					UnityEngine.Networking.UnityWebRequest req = new UnityEngine.Networking.UnityWebRequest(apiUrl, "POST");
					req.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(Encoding.UTF8.GetBytes(postData));
					req.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
					req.SetRequestHeader("Content-Type", "application/json");
					req.SetRequestHeader("Accept", "application/json");
					SC.StartCoroutine(DoHubPost(req, gate, delegate(string r) {
						lock (gate) { result = r; done = true; }
					}, delegate(string e) {
						lock (gate) { error = e; done = true; }
					}));
				} catch (Exception e) {
					lock (gate) { error = e.Message; done = true; }
				}
			}, 15000);

			DateTime deadline = DateTime.UtcNow.AddSeconds(90);
			while (DateTime.UtcNow < deadline) {
				Thread.Sleep(200);
				bool f;
				string r, e;
				lock (gate) { f = done; r = result; e = error; }
				if (f) {
					if (e != null) throw new ApiError("hub request failed: " + e);
					JSONNode resp = JSON.Parse(r);
					if (resp == null) throw new ApiError("hub returned invalid JSON");
					JSONClass outNode = new JSONClass();
					outNode["apiUrl"] = apiUrl;
					if (resp["resources"] != null) {
						JSONArray resources = resp["resources"].AsArray;
						if (resources == null) throw new ApiError("hub response has no resources array");
						JSONArray clean = new JSONArray();
						foreach (JSONNode res in resources) {
							JSONClass row = new JSONClass();
							row["id"] = res["resource_id"] != null ? res["resource_id"].Value : "";
							row["package_id"] = res["package_id"] != null ? res["package_id"].Value : "";
							row["title"] = res["title"] != null ? res["title"].Value : "";
							row["author"] = res["username"] != null ? res["username"].Value : "";
							row["version"] = res["version_string"] != null ? res["version_string"].Value : "";
							row["type"] = res["type"] != null ? res["type"].Value : "";
							row["category"] = res["category"] != null ? res["category"].Value : "";
							row["downloads"] = res["download_count"] != null ? res["download_count"].Value : "";
							row["rating"] = res["rating_avg"] != null ? res["rating_avg"].Value : "";
							row["tags"] = res["tags"] != null ? res["tags"].Value : "";
							row["tagline"] = res["tag_line"] != null ? res["tag_line"].Value : "";
							clean.Add(row);
						}
						outNode["count"] = JSONRaw.Num(clean.Count);
						outNode["resources"] = clean;
					} else {
						outNode["raw"] = r.Substring(0, Math.Min(500, r.Length));
					}
					if (resp["numPages"] != null) outNode["numPages"] = resp["numPages"].Value;
					if (resp["numResources"] != null) outNode["numResources"] = resp["numResources"].Value;
					return outNode;
				}
			}
			throw new ApiError("hub request timed out after 90s");
		}

		private IEnumerator DoHubPost(UnityEngine.Networking.UnityWebRequest req, object gate, Action<string> onDone, Action<string> onError) {
			yield return req.SendWebRequest();
			try {
				if (req.isNetworkError || req.isHttpError) {
					onError(req.error + " | " + (req.downloadHandler != null ? req.downloadHandler.text : ""));
				} else {
					onDone(req.downloadHandler.text);
				}
			} finally {
				req.Dispose();
			}
		}

		/// <summary>Poller tool. Get full detail for one Hub resource: package name, all version files, dependencies.</summary>
		public JSONNode HubDetail(JSONNode args) {
			string apiUrl = Mt.Run(() => HubApiUrl(), 10000);
			string package = S(args, "package");
			string resourceId = S(args, "resource_id");
			if (package == "" && resourceId == "") throw new ApiError("package name or resource_id required");
			JSONClass body = new JSONClass();
			body["source"] = "VaM";
			body["action"] = "getResourceDetail";
			body["latest_image"] = "Y";
			if (package != "") body["package_name"] = package;
			if (resourceId != "") body["resource_id"] = resourceId;
			string postData = body.ToString();

			object gate = new object();
			bool done = false;
			string result = null;
			string error = null;
			Mt.Run(delegate {
				try {
					UnityEngine.Networking.UnityWebRequest req = new UnityEngine.Networking.UnityWebRequest(apiUrl, "POST");
					req.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(Encoding.UTF8.GetBytes(postData));
					req.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
					req.SetRequestHeader("Content-Type", "application/json");
					req.SetRequestHeader("Accept", "application/json");
					SC.StartCoroutine(DoHubPost(req, gate, delegate(string r2) {
						lock (gate) { result = r2; done = true; }
					}, delegate(string e2) {
						lock (gate) { error = e2; done = true; }
					}));
				} catch (Exception e3) {
					lock (gate) { error = e3.Message; done = true; }
				}
			}, 15000);

			DateTime deadline = DateTime.UtcNow.AddSeconds(60);
			while (DateTime.UtcNow < deadline) {
				Thread.Sleep(200);
				bool f;
				string r2, e2;
				lock (gate) { f = done; r2 = result; e2 = error; }
				if (f) {
					if (e2 != null) throw new ApiError("hub request failed: " + e2);
					JSONNode resp = JSON.Parse(r2);
					if (resp == null) throw new ApiError("hub returned invalid JSON");
					JSONClass outNode = new JSONClass();
					if (resp["title"] != null) outNode["title"] = resp["title"].Value;
					if (resp["package_name"] != null) outNode["package"] = resp["package_name"].Value;
					if (resp["username"] != null) outNode["author"] = resp["username"].Value;
					if (resp["version_string"] != null) outNode["version"] = resp["version_string"].Value;
					JSONArray files = resp["hubFiles"] != null ? resp["hubFiles"].AsArray : null;
					if (files != null) {
						JSONArray clean = new JSONArray();
						foreach (JSONNode fnode in files) {
							JSONClass row = new JSONClass();
							row["filename"] = fnode["filename"] != null ? fnode["filename"].Value : "";
							row["version"] = fnode["version_string"] != null ? fnode["version_string"].Value : "";
							row["size"] = fnode["size"] != null ? fnode["size"].Value : "";
							row["download_url"] = fnode["download_url"] != null ? fnode["download_url"].Value : "";
							clean.Add(row);
						}
						outNode["files"] = clean;
					}
					JSONArray deps = resp["dependencies"] != null ? resp["dependencies"].AsArray : null;
					if (deps != null) {
						JSONArray cleanDeps = new JSONArray();
						foreach (JSONNode dn in deps) cleanDeps.Add(dn["package_name"] != null ? dn["package_name"].Value : "");
						outNode["dependencies"] = cleanDeps;
					}
					return outNode;
				}
			}
			throw new ApiError("hub request timed out after 60s");
		}

		/// <summary>Poller tool. Download a package from the VaM Hub using the game's own downloader (handles auth + install into AddonPackages).</summary>
		public JSONNode HubDownload(JSONNode args) {
			string package = S(args, "package");
			if (package == "") throw new ApiError("package required (search with hub_browse)");
			object gate = new object();
			bool finished = false;
			string error = null;
			bool started = Mt.Run(delegate {
				try {
					if (UserPreferences.singleton != null) UserPreferences.singleton.enableHubDownloader = true;
				} catch { }
				Atom core = SC.GetAtomByUid("CoreControl");
				if (core == null) throw new ApiError("no CoreControl atom in the scene");
				JSONStorable st = core.GetStorableByID("HubDownloader");
				MVR.Hub.HubDownloader hd = st as MVR.Hub.HubDownloader;
				if (hd == null) throw new ApiError("no HubDownloader on CoreControl");
				MVR.Hub.HubDownloader.SuccessCallback ok = delegate {
					lock (gate) { finished = true; }
				};
				MVR.Hub.HubDownloader.ErrorCallback err = delegate(string e) {
					lock (gate) { finished = true; error = e; }
				};
				return hd.DownloadPackages(ok, err, package);
			}, 15000);
			if (!started) throw new ApiError("hub download could not start (hub downloader disabled?)");
			DateTime deadline = DateTime.UtcNow.AddSeconds(240);
			while (DateTime.UtcNow < deadline) {
				Thread.Sleep(1000);
				bool f;
				string e;
				lock (gate) { f = finished; e = error; }
				if (f) {
					if (e != null) throw new ApiError("hub download failed: " + e);
					JSONClass r = new JSONClass();
					r["package"] = package;
					r["downloaded"] = JSONRaw.Bool(true);
					r["note"] = "the package is installed into AddonPackages; list_packages will show it after the package scan refreshes";
					return r;
				}
			}
			throw new ApiError("hub download timed out after 240s");
		}

		// ===================== skin subsurface scattering =====================

		public JSONNode SetSkinSss(JSONNode args) {
			Atom p = RequirePerson(S(args, "person"));
			JSONStorable skin = p.GetStorableByID("skin");
			if (skin == null) throw new ApiError("no skin storable on " + p.uid);
			string color = S(args, "color");
			if (color == "") throw new ApiError("color required (#RRGGBB; a reddish tint like #C06060 gives a classic subsurface look)");
			if (!skin.GetColorParamNames().Contains("Subsurface Color")) {
				throw new ApiError("skin has no 'Subsurface Color' param on " + p.uid);
			}
			skin.SetColorParamValue("Subsurface Color", ParseColor(color));
			JSONClass r = new JSONClass();
			r["person"] = p.uid;
			r["param"] = "Subsurface Color";
			r["value"] = ColorToHex(skin.GetColorParamValue("Subsurface Color"));
			return r;
		}

		// ===================== plugin management (VaM native plugins) =====================

		private static FieldInfo pluginsField;
		private static List<MVRPlugin> GetPlugins(MVRPluginManager pm) {
			try {
				if (pluginsField == null) pluginsField = typeof(MVRPluginManager).GetField("plugins", BindingFlags.NonPublic | BindingFlags.Instance);
				if (pluginsField != null) return (List<MVRPlugin>)pluginsField.GetValue(pm);
			} catch { }
			return new List<MVRPlugin>();
		}

		private static MVRPluginManager PluginManagerFor(Atom target) {
			JSONStorable st = target.GetStorableByID("PluginManager");
			MVRPluginManager pm = st as MVRPluginManager;
			if (pm == null) throw new ApiError("no PluginManager storable on " + target.uid);
			return pm;
		}

		/// <summary>Resolve the plugin target atom: empty uid = CoreControl (session plugins), else the given atom (e.g. a Person).</summary>
		private static Atom PluginTarget(string uid) {
			if (uid == "" || uid == "CoreControl") {
				Atom core = SC.GetAtomByUid("CoreControl");
				if (core == null) throw new ApiError("no CoreControl atom (session plugins unavailable)");
				return core;
			}
			return FindAtom(uid);
		}

		public JSONNode ListPlugins(JSONNode args) {
			Atom target = PluginTarget(S(args, "uid"));
			MVRPluginManager pm = PluginManagerFor(target);
			JSONArray arr = new JSONArray();
			foreach (MVRPlugin p in GetPlugins(pm)) {
				JSONClass row = new JSONClass();
				row["uid"] = p.uid;
				row["path"] = (p.pluginURLJSON != null && p.pluginURLJSON.val != null) ? p.pluginURLJSON.val : "";
				row["loaded"] = JSONRaw.Bool((p.scriptControllers != null && p.scriptControllers.Count > 0));
				arr.Add(row);
			}
			JSONClass r = new JSONClass();
			r["atom"] = target.uid;
			r["count"] = JSONRaw.Num(arr.Count);
			r["plugins"] = arr;
			return r;
		}

		/// <summary>Poller tool. Add a VaM plugin (.cs/.cslist/.dll) to an atom's PluginManager (default: CoreControl = session plugins; pass uid=<person> for a person's plugins).</summary>
		public JSONNode AddPlugin(JSONNode args) {
			string path = S(args, "path");
			if (path == "") throw new ApiError("path required (.cs/.cslist/.dll plugin path, e.g. Custom/Scripts/MyPlugin.cs)");
			Atom target = PluginTarget(S(args, "uid"));
			MVRPluginManager pm = PluginManagerFor(target);
			MVRPlugin created = Mt.Run(delegate {
				try {
					if (UserPreferences.singleton != null) UserPreferences.singleton.enablePlugins = true;
				} catch { }
				MVRPlugin p = pm.CreatePlugin();
				p.pluginURLJSON.val = path; // triggers the async load (compile for .cs)
				return p;
			}, 15000);
			DateTime deadline = DateTime.UtcNow.AddSeconds(60);
			while (DateTime.UtcNow < deadline) {
				Thread.Sleep(500);
				bool loaded = Mt.Run(delegate {
					foreach (MVRPlugin p in GetPlugins(pm)) {
						if (p == created && p.scriptControllers != null && p.scriptControllers.Count > 0) return true;
					}
					return false;
				}, 10000);
				if (loaded) {
					JSONClass r = new JSONClass();
					r["atom"] = target.uid;
					r["plugin_uid"] = created.uid;
					r["path"] = path;
					r["loaded"] = JSONRaw.Bool(true);
					r["note"] = "the plugin registers itself as a storable; configure it via list_storable_params / get_param / set_param with uid=<atom> storable=<plugin_uid>";
					return r;
				}
			}
			throw new ApiError("plugin load timed out after 60s (check the VaM console/output_log.txt for compile errors)");
		}

		public JSONNode RemovePlugin(JSONNode args) {
			string pluginUid = S(args, "plugin_uid");
			if (pluginUid == "") throw new ApiError("plugin_uid required (see list_plugins)");
			Atom target = PluginTarget(S(args, "uid"));
			MVRPluginManager pm = PluginManagerFor(target);
			pm.RemovePluginWithUID(pluginUid);
			JSONClass r = new JSONClass();
			r["atom"] = target.uid;
			r["removed"] = pluginUid;
			return r;
		}

		// ===================== eval (escape hatch) =====================

		private static DynamicCSharp.ScriptDomain evalDomain;
		private static readonly Dictionary<string, DynamicCSharp.ScriptProxy> evalCache = new Dictionary<string, DynamicCSharp.ScriptProxy>();

		private static DynamicCSharp.ScriptDomain EvalDomain() {
			if (evalDomain == null) {
				evalDomain = DynamicCSharp.ScriptDomain.CreateDomain("VaMMCP_Eval", true);
			}
			return evalDomain;
		}

		/// <summary>Wrap user code in a full class we control (the library's own eval template is unusable: it inserts the code at class-member level).</summary>
		private static string BuildEvalSource(string code) {
			System.Text.StringBuilder sb = new System.Text.StringBuilder();
			sb.Append("using System;\n");
			sb.Append("using System.Linq;\n");
			sb.Append("using System.Collections.Generic;\n");
			sb.Append("using UnityEngine;\n");
			sb.Append("using SimpleJSON;\n");
			sb.Append("using MVR;\n");
			sb.Append("using MVR.FileManagementSecure;\n");
			sb.Append("public class _EvalProgram_" + Guid.NewGuid().ToString("N").Substring(0, 8) + " {\n");
			sb.Append("  public SuperController sc;\n");
			sb.Append("  public string __error = \"\";\n");
			sb.Append("  public object Run() {\n");
			sb.Append("    try {\n");
			if (code.IndexOf(';') >= 0) {
				// statements mode: execute as-is, return null unless the code returns itself
				sb.Append(code);
				sb.Append("\n      return null;\n");
			} else {
				// expression mode
				sb.Append("      return (" + code + ");\n");
			}
			sb.Append("    } catch (System.Exception __e) {\n");
			// NOTE: __e.ToString() — NOT __e.GetType().Name: Type.Name is declared on System.Reflection.MemberInfo,
			// which VaM's DynamicCSharp sandbox prohibits (NamespaceRestriction).
			sb.Append("      __error = __e.ToString();\n");
			sb.Append("      return null;\n");
			sb.Append("    }\n");
			sb.Append("  }\n");
			sb.Append("}\n");
			return sb.ToString();
		}

		public JSONNode EvalCs(JSONNode args) {
			string code = S(args, "code");
			if (code == "") throw new ApiError("code required");
			DynamicCSharp.ScriptDomain domain = EvalDomain();
			string source = BuildEvalSource(code);
			DynamicCSharp.ScriptProxy proxy;
			if (!evalCache.TryGetValue(source, out proxy)) {
				DynamicCSharp.ScriptType type = domain.CompileAndLoadScriptSource(source);
				if (type == null) throw new ApiError("eval compilation failed (see VaM output_log.txt for compiler errors)");
				proxy = type.CreateInstance();
				if (proxy == null) throw new ApiError("eval failed to instantiate compiled type");
				evalCache[source] = proxy;
			}
			try {
				proxy.Fields["sc"] = SuperController.singleton;
			} catch (Exception e) {
				throw new ApiError("eval bind failed: " + e.Message);
			}
			object result;
			try {
				result = proxy.Call("Run");
			} catch (Exception e) {
				Exception inner = e;
				while (inner.InnerException != null) inner = inner.InnerException;
				throw new ApiError("eval error: " + inner.Message);
			}
			string err = "";
			try {
				object errObj = proxy.Fields["__error"];
				if (errObj != null) err = errObj.ToString();
			} catch { }
			if (err != "") throw new ApiError("eval error: " + err);
			JSONClass r = new JSONClass();
			r["result"] = SerializeValue(result);
			return r;
		}

		public static JSONNode SerializeValue(object o) {
			if (o == null) return JSONRaw.Null();
			if (o is string) return new JSONData((string)o);
			if (o is bool) return JSONRaw.Bool((bool)o);
			if (o is int) return JSONRaw.Num((int)o);
			if (o is long) return JSONRaw.Num((long)o);
			if (o is float) return JSONRaw.Num((float)o);
			if (o is double) return JSONRaw.Num((float)(double)o);
			if (o is Vector3) {
				Vector3 v = (Vector3)o;
				JSONClass r = new JSONClass();
				r["x"] = F(v.x);
				r["y"] = F(v.y);
				r["z"] = F(v.z);
				return r;
			}
			IEnumerable e = o as IEnumerable;
			if (e != null) {
				JSONArray arr = new JSONArray();
				foreach (object x in e) {
					try { arr.Add(SerializeValue(x)); } catch { }
				}
				return arr;
			}
			return new JSONData(o.ToString());
		}
	}
}
