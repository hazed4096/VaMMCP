using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using SimpleJSON;
using MVR.FileManagementSecure;
using MeshVR;

namespace VaMMCP.Api {
	/// <summary>
	/// Scene introspection: what is in the scene, in what state, and — just as
	/// important — what the caller did *not* ask to see. Every listing here takes a
	/// projection (`fields`), a bound (`limit`/`offset`) and a filter (`query`/`type`)
	/// so an agent can ask for exactly the rows and columns it needs instead of
	/// paying for a full dump on every call.
	///
	/// Conventions shared by the listings:
	///   detail=ids|placement|contents|health|full  picks a preset column set
	///   fields=a,b,c                               explicit columns (overrides detail)
	///   exclude=a,b                                drop columns from either
	///   limit/offset                               page through rows
	///   precision=N                                decimal places for vectors (default 3)
	/// A listing always reports how much it did not return (`total`, `returned`,
	/// `hasMore`) so the agent can tell "that is all" from "there is more".
	/// </summary>
	public partial class VaMApi {

		// ===================== projection helpers =====================

		private static HashSet<string> Csv(string csv) {
			HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (string.IsNullOrEmpty(csv)) return set;
			string[] parts = csv.Split(',');
			for (int i = 0; i < parts.Length; i++) {
				string p = parts[i].Trim();
				if (p != "") set.Add(p);
			}
			return set;
		}

		private static bool Want(HashSet<string> set, string name) {
			return set == null || set.Count == 0 || set.Contains(name);
		}

		/// <summary>Sections: an empty request means "the defaults", otherwise only
		/// the names that were asked for. `all` turns everything on.</summary>
		private static bool SectionOn(HashSet<string> sections, string name, bool defaultOn) {
			if (sections == null || sections.Count == 0) return defaultOn;
			if (sections.Contains("all") || sections.Contains("*")) return true;
			return sections.Contains(name);
		}

		private static void AddAll(HashSet<string> dst, string csv) {
			foreach (string s in Csv(csv)) dst.Add(s);
		}

		private static int ClampI(int v, int lo, int hi) {
			if (v < lo) return lo;
			if (v > hi) return hi;
			return v;
		}

		private static int Precision(JSONNode args) {
			int p = (int)Num(args, "precision", 3f);
			return ClampI(p, 0, 6);
		}

		private static float Rounded(float v, int precision) {
			if (float.IsNaN(v) || float.IsInfinity(v)) return v;
			double f = Math.Pow(10.0, precision);
			return (float)(Math.Round(v * f, MidpointRounding.AwayFromZero) / f);
		}

		private static JSONArray VecP(Vector3 v, int precision) {
			JSONArray a = new JSONArray();
			a.Add(JSONRaw.Num(Rounded(v.x, precision)));
			a.Add(JSONRaw.Num(Rounded(v.y, precision)));
			a.Add(JSONRaw.Num(Rounded(v.z, precision)));
			return a;
		}

		private static int IntOf(JSONNode n, int def) {
			if (n == null) return def;
			try { return n.AsInt; } catch { return def; }
		}

		private const string FIELDS_IDS = "uid,type";
		private const string FIELDS_PLACEMENT = "uid,type,name,on,pos";
		private const string FIELDS_CONTENTS =
			"uid,type,name,on,pos,children,renderers,colliders,rigidbodies,storables,subScene";
		private const string FIELDS_HEALTH =
			"uid,type,name,on,pos,children,renderers,colliders,rigidbodies,storables,subScene," +
			"sims,controls,worn,hair,torn";
		private const string FIELDS_FULL =
			"uid,type,name,on,pos,rot,scale,children,bbox,renderers,colliders,rigidbodies," +
			"storables,subScene,sims,controls,worn,hair,torn,asset";

		private static HashSet<string> Preset(string detail) {
			switch ((detail ?? "").ToLowerInvariant()) {
				case "ids": case "id": return Csv(FIELDS_IDS);
				case "contents": return Csv(FIELDS_CONTENTS);
				case "health": return Csv(FIELDS_HEALTH);
				case "full": return Csv(FIELDS_FULL);
				default: return Csv(FIELDS_PLACEMENT);
			}
		}

		/// <summary>Resolve the column set for an atom listing. `detail` sets the
		/// preset, `fields` overrides it, `exclude` subtracts. The legacy
		/// fields=contents / fields=health spellings still work.</summary>
		private static HashSet<string> RowFields(JSONNode args) {
			string detail = S(args, "detail");
			HashSet<string> want = Preset(detail);
			HashSet<string> explicitSet = Csv(S(args, "fields"));
			if (explicitSet.Count > 0) {
				bool legacyContents = explicitSet.Remove("contents");
				bool legacyHealth = explicitSet.Remove("health");
				bool legacyFull = explicitSet.Remove("full");
				bool legacyPlacement = explicitSet.Remove("placement");
				if (explicitSet.Count == 0) {
					if (legacyHealth) want = Preset("health");
					else if (legacyFull) want = Preset("full");
					else if (legacyContents) want = Preset("contents");
					else if (legacyPlacement) want = Preset("placement");
				} else {
					want = explicitSet;
					if (legacyPlacement) AddAll(want, FIELDS_PLACEMENT);
					if (legacyContents) AddAll(want, FIELDS_CONTENTS);
					if (legacyHealth) AddAll(want, FIELDS_HEALTH);
					if (legacyFull) AddAll(want, FIELDS_FULL);
				}
			}
			HashSet<string> exclude = Csv(S(args, "exclude"));
			foreach (string e in exclude) want.Remove(e);
			// legacy shortcut: bbox=true means "add the bounding box column"
			if (Bool(args, "bbox", false)) want.Add("bbox");
			want.Remove("uid"); // identity is always emitted
			if (want.Count == 0) want.Add("type");
			return want;
		}

		private static void Put(JSONClass row, HashSet<string> want, string key, JSONNode value) {
			if (Want(want, key)) row[key] = value;
		}

		// ===================== atom rows =====================

		private static bool AtomTextMatches(Atom a, string query) {
			if (a.uid != null && a.uid.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;
			if (a.name != null && a.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;
			try {
				foreach (string sid in a.GetStorableIDs()) {
					if (sid != null && sid.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;
				}
			} catch { }
			return false;
		}

		private static string FirstUrlOn(Atom a) {
			try {
				foreach (string sid in a.GetStorableIDs()) {
					JSONStorable st = a.GetStorableByID(sid);
					if (st == null) continue;
					foreach (string n in st.GetUrlParamNames()) {
						string v = st.GetUrlParamValue(n);
						if (!string.IsNullOrEmpty(v)) return v;
					}
				}
			} catch { }
			return "";
		}

		private static void SortAtoms(List<Atom> list, string sort) {
			string s = (sort ?? "").ToLowerInvariant();
			if (s == "" || s == "scene") return;
			list.Sort(delegate(Atom x, Atom y) {
				int c;
				if (s == "type") c = string.Compare(x.type, y.type, StringComparison.OrdinalIgnoreCase);
				else if (s == "x") c = x.transform.position.x.CompareTo(y.transform.position.x);
				else if (s == "y") c = x.transform.position.y.CompareTo(y.transform.position.y);
				else if (s == "z") c = x.transform.position.z.CompareTo(y.transform.position.z);
				else c = string.Compare(x.uid, y.uid, StringComparison.OrdinalIgnoreCase);
				if (c == 0) c = string.Compare(x.uid, y.uid, StringComparison.OrdinalIgnoreCase);
				return c;
			});
		}

		private JSONClass SceneRow(Atom a, HashSet<string> want, int precision) {
			JSONClass row = new JSONClass();
			row["uid"] = a.uid;
			Put(row, want, "type", a.type);
			if (Want(want, "name")) {
				string n = a.name;
				if (!string.IsNullOrEmpty(n) && n != a.uid) row["name"] = n;
			}
			Put(row, want, "on", JSONRaw.Bool(a.on));
			if (Want(want, "pos")) row["pos"] = VecP(a.transform.position, precision);
			if (Want(want, "rot")) row["rot"] = VecP(a.transform.eulerAngles, precision);
			if (Want(want, "scale")) row["scale"] = VecP(a.transform.lossyScale, precision);
			if (Want(want, "children")) row["children"] = JSONRaw.Num(a.transform.childCount);
			if (Want(want, "renderers")) row["renderers"] = JSONRaw.Num(CountComponents<Renderer>(a));
			if (Want(want, "colliders")) row["colliders"] = JSONRaw.Num(CountComponents<Collider>(a));
			if (Want(want, "rigidbodies")) row["rigidbodies"] = JSONRaw.Num(CountComponents<Rigidbody>(a));
			if (Want(want, "storables")) {
				int n = 0;
				try { n = a.GetStorableIDs().Count; } catch { }
				row["storables"] = JSONRaw.Num(n);
			}
			if (Want(want, "subScene")) {
				string sub = SubSceneOf(a);
				if (sub != "") row["subScene"] = sub;
			}
			if (Want(want, "asset")) {
				string asset = FirstUrlOn(a);
				if (asset != "") row["asset"] = asset;
			}
			if (Want(want, "bbox") || Want(want, "size")) {
				Bounds b;
				bool have = AtomBounds(a, out b);
				if (have) {
					// [center.xyz, size.xyz] - same six numbers either way, no null padding
					JSONArray box = new JSONArray();
					box.Add(JSONRaw.Num(Rounded(b.center.x, precision)));
					box.Add(JSONRaw.Num(Rounded(b.center.y, precision)));
					box.Add(JSONRaw.Num(Rounded(b.center.z, precision)));
					box.Add(JSONRaw.Num(Rounded(b.size.x, precision)));
					box.Add(JSONRaw.Num(Rounded(b.size.y, precision)));
					box.Add(JSONRaw.Num(Rounded(b.size.z, precision)));
					if (Want(want, "bbox")) row["bbox"] = box;
					if (Want(want, "size")) {
						JSONArray sz = new JSONArray();
						sz.Add(JSONRaw.Num(Rounded(b.size.x, precision)));
						sz.Add(JSONRaw.Num(Rounded(b.size.y, precision)));
						sz.Add(JSONRaw.Num(Rounded(b.size.z, precision)));
						row["size"] = sz;
					}
				}
			}
			if (Want(want, "sims") || Want(want, "controls") || Want(want, "worn") || Want(want, "hair") || Want(want, "torn")) {
				if (a.type == "Person") {
					JSONClass audit = PersonAudit(a);
					if (Want(want, "sims")) {
						row["sims"] = (IntOf(audit["simsOn"], 0).ToString() + "/" + IntOf(audit["simsTotal"], 0).ToString());
					}
					if (Want(want, "controls")) row["controls"] = PersonControlStates(audit);
					if (Want(want, "worn")) {
						row["worn"] = JSONRaw.Num(IntOf(audit["wornCount"], 0));
					}
					if (Want(want, "hair")) {
						row["hair"] = JSONRaw.Num(IntOf(audit["hairItemsOn"], 0));
					}
					if (Want(want, "torn") && audit["torn"] != null) row["torn"] = JSONRaw.Bool(true);
				}
			}
			return row;
		}

		private static bool AtomBounds(Atom a, out Bounds bounds) {
			bounds = new Bounds();
			try {
				Renderer[] rs = a.GetComponentsInChildren<Renderer>(true);
				if (rs == null || rs.Length == 0) return false;
				bool have = false;
				for (int i = 0; i < rs.Length; i++) {
					if (rs[i] == null || !rs[i].enabled) continue;
					if (!have) { bounds = rs[i].bounds; have = true; }
					else bounds.Encapsulate(rs[i].bounds);
				}
				return have;
			} catch { return false; }
		}

		/// <summary>Control census as [On, Off, other] - compact, documented order.</summary>
		private static JSONArray PersonControlStates(JSONNode audit) {
			JSONArray a = new JSONArray();
			JSONNode st = audit["states"];
			a.Add(st != null ? st["On"] : JSONRaw.Num(0));
			a.Add(st != null ? st["Off"] : JSONRaw.Num(0));
			a.Add(st != null ? st["other"] : JSONRaw.Num(0));
			return a;
		}

		// ===================== listings =====================

		private JSONNode SceneListing(JSONNode args, string key, int defaultLimit) {
			string typeF = S(args, "type");
			string query = S(args, "query");
			HashSet<string> want = RowFields(args);
			int precision = Precision(args);
			bool includeOff = Bool(args, "include_off", true);
			int limit = ClampI((int)Num(args, "limit", defaultLimit), 0, 2000);
			int offset = Math.Max(0, (int)Num(args, "offset", 0));

			List<Atom> hits = new List<Atom>();
			foreach (Atom a in SC.GetAtoms()) {
				if (a == null) continue;
				if (!includeOff && !a.on) continue;
				if (typeF != "" && !string.Equals(a.type, typeF, StringComparison.OrdinalIgnoreCase)) continue;
				if (query != "" && !AtomTextMatches(a, query)) continue;
				hits.Add(a);
			}
			SortAtoms(hits, S(args, "sort"));

			int total = hits.Count;
			int take = total - offset;
			if (take < 0) take = 0;
			if (limit > 0 && take > limit) take = limit;

			JSONArray rows = new JSONArray();
			for (int i = 0; i < take; i++) rows.Add(SceneRow(hits[offset + i], want, precision));

			JSONClass r = new JSONClass();
			r["total"] = JSONRaw.Num(total);
			r["returned"] = JSONRaw.Num(rows.Count);
			if (offset > 0) r["offset"] = JSONRaw.Num(offset);
			if (offset + rows.Count < total) r["hasMore"] = JSONRaw.Bool(true);
			r[key] = rows;
			if (rows.Count < total) r["hint"] = "narrow with query/type/exclude, or page with offset";
			return r;
		}

		public JSONNode ListObjects(JSONNode args) {
			return SceneListing(args, "objects", 0);
		}

		public JSONNode ListAtoms(JSONNode args) {
			return SceneListing(args, "atoms", 0);
		}

		// ===================== scene state =====================

		/// <summary>Scene overview. `sections` selects what to include (defaults are
		/// the cheap ones); `uid` switches to a per-atom dossier.</summary>
		public JSONNode SceneState(JSONNode args) {
			string uid = S(args, "uid");
			string detail = (S(args, "detail") ?? "").ToLowerInvariant();
			bool full = detail == "full";
			HashSet<string> sections = Csv(S(args, "sections"));
			int precision = Precision(args);

			if (uid != "") {
				if (detail == "summary") full = false;
				return AtomDossier(uid, args, sections, full, precision);
			}

			// legacy detail values still select the section they used to add
			if (sections.Count == 0 && detail != "" && detail != "full" && detail != "summary") {
				if (detail == "diagnostics") { sections.Add("health"); sections.Add("warnings"); }
				else sections.Add(detail);
			}

			bool wantMeta = SectionOn(sections, "meta", true);
			bool wantTypes = SectionOn(sections, "types", true) || SectionOn(sections, "counts", false);
			bool wantPersons = SectionOn(sections, "persons", true);
			bool wantWarnings = SectionOn(sections, "warnings", true);
			bool wantAtoms = SectionOn(sections, "atoms", false);
			bool wantHealth = SectionOn(sections, "health", false) || full;
			bool wantPlugins = SectionOn(sections, "plugins", false);
			bool wantPhysics = SectionOn(sections, "physics", false);
			bool wantCameras = SectionOn(sections, "cameras", false) || full;
			bool wantPerf = SectionOn(sections, "performance", false);
			bool wantAnimation = SectionOn(sections, "animation", false);
			bool wantControls = SectionOn(sections, "controls", false);

			JSONClass r = new JSONClass();
			Dictionary<string, int> byType = new Dictionary<string, int>();
			List<Atom> persons = new List<Atom>();
			int atoms = 0;
			foreach (Atom a in SC.GetAtoms()) {
				if (a == null) continue;
				atoms++;
				if (!byType.ContainsKey(a.type)) byType[a.type] = 0;
				byType[a.type] = byType[a.type] + 1;
				if (a.type == "Person") persons.Add(a);
			}

			if (wantMeta) {
				JSONClass meta = new JSONClass();
				meta["atoms"] = JSONRaw.Num(atoms);
				meta["persons"] = JSONRaw.Num(persons.Count);
				meta["types"] = JSONRaw.Num(byType.Count);
				meta["frame"] = JSONRaw.Num(Time.frameCount);
				meta["timeScale"] = F(Time.timeScale);
				try { meta["loading"] = JSONRaw.Bool(SC.isLoading); } catch { }
				try {
					float sd = Time.smoothDeltaTime;
					if (sd > 0.0001f) meta["fps"] = JSONRaw.Num((int)Math.Round(1.0f / sd));
				} catch { }
				// only meaningful after this API loaded a scene; empty otherwise
				if (lastScenePath != "") meta["file"] = lastScenePath;
				r["scene"] = meta;
			}

			if (wantTypes) {
				JSONClass types = new JSONClass();
				foreach (KeyValuePair<string, int> kv in byType) types[kv.Key] = JSONRaw.Num(kv.Value);
				r["types"] = types;
			}

			JSONArray warnings = new JSONArray();
			bool needDiagnostics = wantWarnings || wantHealth;
			JSONNode diag = null;
			if (needDiagnostics) {
				try { diag = Diagnostics(); } catch { diag = null; }
				if (diag != null && diag["warnings"] != null) warnings = diag["warnings"].AsArray;
				if (warnings == null) warnings = new JSONArray();
			}

			if (wantPersons) {
				JSONArray arr = new JSONArray();
				if (diag != null && diag["persons"] != null) {
					JSONArray auditRows = diag["persons"].AsArray;
					for (int i = 0; i < auditRows.Count; i++) arr.Add(PersonBrief(auditRows[i], full));
				} else {
					for (int i = 0; i < persons.Count; i++) arr.Add(PersonBrief(PersonAudit(persons[i]), full));
				}
				r["persons"] = arr;
				if (wantControls) r["personControls"] = PersonControlRows(persons, precision);
			}

			if (wantAtoms) {
				JSONNode listing = SceneListing(args, "atoms", 60);
				r["atoms"] = listing["atoms"];
				r["atomsTotal"] = listing["total"];
				if (listing["hasMore"] != null) r["atomsHasMore"] = listing["hasMore"];
			}

			if (wantHealth) {
				JSONClass health = new JSONClass();
				if (diag != null) {
					health["hiddenAtoms"] = diag["hiddenAtoms"];
					JSONNode h = diag["health"];
					if (h != null) {
						health["tornPersons"] = h["tornPersons"];
						health["overlappingPersons"] = h["overlappingPersons"];
						health["noRenderers"] = h["noRenderers"];
						health["nonFiniteTransforms"] = h["nonFiniteTransforms"];
					}
					health["warnings"] = warnings;
				} else {
					health["error"] = "diagnostics unavailable";
				}
				r["health"] = health;
			} else if (wantWarnings && warnings.Count > 0) {
				r["warnings"] = warnings;
			}

			if (wantPlugins) {
				JSONClass plugins = new JSONClass();
				try {
					JSONNode list = ListPlugins(new JSONClass());
					plugins["atom"] = list["atom"];
					plugins["count"] = list["count"];
					JSONArray rows = new JSONArray();
					JSONArray all = list["plugins"] != null ? list["plugins"].AsArray : null;
					if (all != null) {
						for (int i = 0; i < all.Count && i < 40; i++) {
							JSONClass row = new JSONClass();
							row["uid"] = all[i]["uid"];
							row["path"] = all[i]["path"];
							row["loaded"] = all[i]["loaded"];
							rows.Add(row);
						}
					}
					plugins["plugins"] = rows;
				} catch (Exception e) {
					plugins["error"] = e.Message;
				}
				r["plugins"] = plugins;
			}

			if (wantPhysics) {
				JSONClass physics = new JSONClass();
				physics["timeScale"] = F(Time.timeScale);
				physics["fixedDeltaTime"] = F(Time.fixedDeltaTime);
				int objectCount = 0, storableCount = 0;
				try {
					JSONNode objs = ListPhysicsObjects(new JSONClass());
					objectCount = IntOf(objs["count"], 0);
					JSONArray all = objs["objects"] != null ? objs["objects"].AsArray : null;
					if (all != null) {
						for (int i = 0; i < all.Count; i++) {
							JSONArray ps = all[i]["physicsStorables"] != null ? all[i]["physicsStorables"].AsArray : null;
							if (ps != null) storableCount += ps.Count;
						}
					}
				} catch { }
				physics["objects"] = JSONRaw.Num(objectCount);
				physics["storables"] = JSONRaw.Num(storableCount);
				r["physics"] = physics;
			}

			if (wantCameras) {
				JSONArray cams = new JSONArray();
				try {
					Camera monitor = SC.MonitorCenterCamera;
					Camera[] all = Camera.allCameras;
					if (all != null) {
						for (int i = 0; i < all.Length && i < 24; i++) {
							Camera c = all[i];
							if (c == null) continue;
							JSONClass row = new JSONClass();
							row["name"] = c.gameObject.name;
							row["on"] = JSONRaw.Bool(c.enabled);
							row["depth"] = F(c.depth);
							row["fov"] = F(c.fieldOfView);
							row["pos"] = VecP(c.transform.position, precision);
							if (monitor != null && c == monitor) row["role"] = "monitor";
							cams.Add(row);
						}
					}
				} catch { }
				r["cameras"] = cams;
			}

			if (wantPerf) {
				try { r["performance"] = SimulationPerformance(); } catch { }
			}

			if (wantAnimation) {
				JSONClass anim = new JSONClass();
				JSONArray patterns = new JSONArray();
				int count = 0;
				foreach (Atom a in SC.GetAtoms()) {
					if (a == null || !AtomTypeMatches(a, "AnimationPattern")) continue;
					count++;
					if (patterns.Count < 20) patterns.Add(a.uid);
				}
				anim["patterns"] = JSONRaw.Num(count);
				anim["names"] = patterns;
				r["animation"] = anim;
			}

			if (sections.Count == 0 && detail != "full") {
				r["hint"] = "sections=atoms|health|plugins|physics|cameras|performance|animation|controls; uid=<uid> for a full atom dossier; list_objects takes fields/query/limit";
			}
			return r;
		}

		private static JSONClass PersonBrief(JSONNode audit, bool full) {
			JSONClass row = new JSONClass();
			row["uid"] = audit["uid"];
			row["on"] = audit["on"];
			row["controls"] = PersonControlStates(audit);
			row["sims"] = (IntOf(audit["simsOn"], 0).ToString() + "/" + IntOf(audit["simsTotal"], 0).ToString());
			if (audit["wornCount"] != null) row["worn"] = audit["wornCount"];
			if (audit["hairItemsOn"] != null) row["hair"] = audit["hairItemsOn"];
			if (audit["torn"] != null) row["torn"] = JSONRaw.Bool(true);
			if (audit["hipChest"] != null) row["hipChest"] = audit["hipChest"];
			if (audit["nonFiniteControls"] != null) row["nonFinite"] = audit["nonFiniteControls"];
			if (audit["hip"] != null) row["hip"] = audit["hip"];
			if (full) {
				if (audit["worn"] != null) row["wornItems"] = audit["worn"];
				if (audit["hairs"] != null) row["hairItems"] = audit["hairs"];
				if (audit["subScene"] != null) row["subScene"] = audit["subScene"];
				if (audit["renderers"] != null) row["renderers"] = audit["renderers"];
				if (audit["renderersOn"] != null) row["renderersOn"] = audit["renderersOn"];
			}
			return row;
		}

		private static JSONArray PersonControlRows(List<Atom> persons, int precision) {
			JSONArray outRows = new JSONArray();
			for (int i = 0; i < persons.Count; i++) {
				Atom p = persons[i];
				List<string> ids = p.GetStorableIDs();
				if (ids == null) continue;
				for (int j = 0; j < ids.Count; j++) {
					string sid = ids[j];
					if (sid == null || !sid.EndsWith("Control")) continue;
					FreeControllerV3 fc = p.GetStorableByID(sid) as FreeControllerV3;
					if (fc == null) continue;
					JSONClass row = new JSONClass();
					row["person"] = p.uid;
					row["id"] = sid;
					row["state"] = fc.currentPositionState.ToString();
					row["pos"] = VecP(fc.transform.position, precision);
					outRows.Add(row);
					if (outRows.Count >= 120) return outRows;
				}
			}
			return outRows;
		}

		// ===================== atom dossier =====================

		/// <summary>Everything known about one atom: its storables (with parameter
		/// counts), controls and their states, sims, plugin instances and asset URLs.
		/// This is the deep view; `sections` keeps it to the part the caller needs.</summary>
		private JSONNode AtomDossier(string uid, JSONNode args, HashSet<string> sections, bool full, int precision) {
			Atom a = FindAtom(uid);
			int rowLimit = ClampI((int)Num(args, "limit", 200f), 1, 2000);

			bool wantMeta = SectionOn(sections, "meta", true);
			bool wantStorables = SectionOn(sections, "storables", true);
			bool wantControls = SectionOn(sections, "controls", true);
			bool wantSims = SectionOn(sections, "sims", false) || full;
			bool wantPlugins = SectionOn(sections, "plugins", false) || full;
			bool wantUrls = SectionOn(sections, "urls", false);
			bool wantChildren = SectionOn(sections, "children", false);

			JSONClass r = new JSONClass();
			r["uid"] = a.uid;
			r["type"] = a.type;

			if (wantMeta) {
				if (!string.IsNullOrEmpty(a.name) && a.name != a.uid) r["name"] = a.name;
				r["on"] = JSONRaw.Bool(a.on);
				JSONClass t = new JSONClass();
				t["pos"] = VecP(a.transform.position, precision);
				t["rot"] = VecP(a.transform.eulerAngles, precision);
				t["scale"] = VecP(a.transform.lossyScale, precision);
				r["transform"] = t;
				if (a.transform.parent != null) r["parent"] = a.transform.parent.name;
				r["children"] = JSONRaw.Num(a.transform.childCount);
				string sub = SubSceneOf(a);
				if (sub != "") r["subScene"] = sub;
				r["renderers"] = JSONRaw.Num(CountComponents<Renderer>(a));
				r["colliders"] = JSONRaw.Num(CountComponents<Collider>(a));
				r["rigidbodies"] = JSONRaw.Num(CountComponents<Rigidbody>(a));
			}

			List<string> ids = a.GetStorableIDs();
			int storTotal = ids != null ? ids.Count : 0;

			if (wantStorables) {
				JSONArray arr = new JSONArray();
				for (int i = 0; i < storTotal && i < rowLimit; i++) {
					JSONStorable st = a.GetStorableByID(ids[i]);
					if (st == null) continue;
					JSONClass row = new JSONClass();
					row["id"] = ids[i];
					row["type"] = st.GetType().Name;
					// parameter counts only: values are what list_storable_params is for
					row["f"] = JSONRaw.Num(SafeCount(st.GetFloatParamNames()));
					row["b"] = JSONRaw.Num(SafeCount(st.GetBoolParamNames()));
					row["c"] = JSONRaw.Num(SafeCount(st.GetStringChooserParamNames()));
					row["s"] = JSONRaw.Num(SafeCount(st.GetStringParamNames()));
					arr.Add(row);
				}
				r["storables"] = arr;
				r["storableCount"] = JSONRaw.Num(storTotal);
			}

			if (wantControls) {
				JSONArray arr = new JSONArray();
				for (int i = 0; i < storTotal && arr.Count < rowLimit; i++) {
					string sid = ids[i];
					if (sid == null || !sid.EndsWith("Control")) continue;
					FreeControllerV3 fc = a.GetStorableByID(sid) as FreeControllerV3;
					if (fc == null) continue;
					JSONClass row = new JSONClass();
					row["id"] = sid;
					row["state"] = fc.currentPositionState.ToString();
					row["pos"] = VecP(fc.transform.position, precision);
					arr.Add(row);
				}
				r["controls"] = arr;
			}

			if (wantSims) {
				JSONArray arr = new JSONArray();
				for (int i = 0; i < storTotal && arr.Count < rowLimit; i++) {
					string sid = ids[i];
					if (sid == null) continue;
					if (!(sid.EndsWith("Sim") || sid == "SoftBodyPhysicsEnabler")) continue;
					JSONStorable st = a.GetStorableByID(sid);
					if (st == null) continue;
					JSONStorableBool b = st.GetBoolJSONParam("simEnabled");
					if (b == null) b = st.GetBoolJSONParam("simulationEnabled");
					if (b == null && sid == "SoftBodyPhysicsEnabler") b = st.GetBoolJSONParam("enabled");
					if (b == null) continue;
					JSONClass row = new JSONClass();
					row["id"] = sid;
					row["on"] = JSONRaw.Bool(b.val);
					arr.Add(row);
				}
				r["sims"] = arr;
			}

			if (wantPlugins) {
				JSONClass plugins = new JSONClass();
				try {
					JSONClass q = new JSONClass();
					q["uid"] = a.uid;
					JSONNode list = ListPlugins(q);
					plugins["count"] = list["count"];
					plugins["plugins"] = list["plugins"];
				} catch (Exception e) {
					plugins["error"] = e.Message;
				}
				r["plugins"] = plugins;
			}

			if (wantUrls) {
				JSONArray arr = new JSONArray();
				for (int i = 0; i < storTotal && arr.Count < rowLimit; i++) {
					JSONStorable st = a.GetStorableByID(ids[i]);
					if (st == null) continue;
					IList<string> names;
					try { names = st.GetUrlParamNames(); } catch { continue; }
					if (names == null) continue;
					for (int j = 0; j < names.Count; j++) {
						string v = "";
						try { v = st.GetUrlParamValue(names[j]); } catch { }
						if (string.IsNullOrEmpty(v)) continue;
						JSONClass row = new JSONClass();
						row["storable"] = ids[i];
						row["param"] = names[j];
						row["value"] = v;
						arr.Add(row);
					}
				}
				r["urls"] = arr;
			}

			if (wantChildren) {
				JSONArray arr = new JSONArray();
				for (int i = 0; i < a.transform.childCount && i < 40; i++) {
					Transform c = a.transform.GetChild(i);
					if (c == null) continue;
					arr.Add(c.name);
				}
				r["childNames"] = arr;
			}

			r["hint"] = "sections=controls|sims|plugins|urls|children to add detail; list_storable_params uid=<uid> for values";
			return r;
		}

		private static int SafeCount(IList<string> list) {
			try { return list != null ? list.Count : 0; } catch { return 0; }
		}

		// ===================== atom tree =====================

		/// <summary>Hierarchy under each atom (or one atom). Nested `children`
		/// arrays; `components` is opt-in because a component list per node is the
		/// single biggest source of needless output.</summary>
		public JSONNode AtomTree(JSONNode args) {
			string rootUid = S(args, "root_uid");
			int maxDepth = ClampI((int)Num(args, "max_depth", 2f), 1, 8);
			int nodeLimit = ClampI((int)Num(args, "limit", 50f), 1, 400);
			int atomsLimit = ClampI((int)Num(args, "atoms_limit", 40f), 1, 400);
			string query = S(args, "query");
			bool components = Bool(args, "components", false);
			string detail = (S(args, "detail") ?? "").ToLowerInvariant();
			if (detail == "summary") { maxDepth = 1; components = false; }
			int precision = Precision(args);

			JSONClass r = new JSONClass();
			if (rootUid != "") {
				Atom a = FindAtom(rootUid);
				Transform t = a.mainController != null ? a.mainController.transform : a.transform;
				JSONClass info = new JSONClass();
				info["uid"] = a.uid;
				info["type"] = a.type;
				info["root"] = TreeWalk(t, 0, maxDepth, query, components, nodeLimit, precision);
				r["atoms"] = new JSONArray();
				((JSONArray)r["atoms"]).Add(info);
				r["count"] = JSONRaw.Num(1);
			} else {
				JSONArray arr = new JSONArray();
				int total = 0;
				foreach (Atom a in SC.GetAtoms()) {
					if (a == null) continue;
					total++;
					if (arr.Count >= atomsLimit) continue;
					JSONClass info = new JSONClass();
					info["uid"] = a.uid;
					info["type"] = a.type;
					if (!a.on) info["on"] = JSONRaw.Bool(false);
					Transform t = a.mainController != null ? a.mainController.transform : a.transform;
					info["root"] = TreeWalk(t, 0, maxDepth, query, components, nodeLimit, precision);
					arr.Add(info);
				}
				r["count"] = JSONRaw.Num(arr.Count);
				r["atomsTotal"] = JSONRaw.Num(total);
				if (arr.Count < total) r["hint"] = "atoms_limit reached; pass root_uid for one atom";
				r["atoms"] = arr;
			}
			return r;
		}

		private static JSONNode TreeWalk(Transform t, int depth, int maxDepth, string query,
										bool components, int nodeLimit, int precision) {
			if (t == null) return JSONRaw.Null();
			JSONClass node = new JSONClass();
			node["name"] = t.name;
			if (components) {
				StringBuilder comps = new StringBuilder();
				Component[] cs = t.GetComponents<Component>();
				if (cs != null) {
					for (int i = 0; i < cs.Length; i++) {
						if (cs[i] == null) { comps.Append("(missing);"); continue; }
						if (comps.Length > 0) comps.Append(';');
						comps.Append(cs[i].GetType().Name);
					}
				}
				node["components"] = comps.ToString();
			}
			if (depth == 0) node["pos"] = VecP(t.position, precision);
			int kids = t.childCount;
			if (kids == 0 || depth >= maxDepth) return node;
			JSONArray children = new JSONArray();
			for (int i = 0; i < kids && children.Count < nodeLimit; i++) {
				Transform c = t.GetChild(i);
				if (c == null) continue;
				if (query != "" && !TreeMatches(c, query, 3)) continue;
				children.Add(TreeWalk(c, depth + 1, maxDepth, query, components, nodeLimit, precision));
			}
			if (children.Count > 0) node["children"] = children;
			if (children.Count < kids) node["childrenShown"] = JSONRaw.Num(children.Count);
			return node;
		}

		private static bool TreeMatches(Transform t, string query, int depth) {
			if (t.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;
			if (depth <= 0) return false;
			for (int i = 0; i < t.childCount; i++) {
				Transform c = t.GetChild(i);
				if (c != null && TreeMatches(c, query, depth - 1)) return true;
			}
			return false;
		}

		// ===================== person state =====================

		/// <summary>One person, split into sections so a caller can take morphs
		/// without dragging 40 control rows along. Morph/item lists are capped by
		/// morph_limit/item_limit and report their true counts.</summary>
		public JSONNode PersonState(JSONNode args) {
			Atom p = RequirePerson(S(args, "person"));
			HashSet<string> sections = Csv(S(args, "sections"));
			bool full = (S(args, "detail") ?? "").ToLowerInvariant() == "full";
			bool includeHidden = Bool(args, "include_hidden", false);
			int morphLimit = ClampI((int)Num(args, "morph_limit", full ? 40f : 10f), 0, 300);
			int itemLimit = ClampI((int)Num(args, "item_limit", full ? 200f : 20f), 0, 800);
			int precision = Precision(args);

			bool wantCharacter = SectionOn(sections, "character", true);
			bool wantMorphs = SectionOn(sections, "morphs", true);
			bool wantExpressions = SectionOn(sections, "expressions", false);
			bool wantClothing = SectionOn(sections, "clothing", true);
			bool wantHair = SectionOn(sections, "hair", true);
			bool wantControlSummary = SectionOn(sections, "controls", true);
			bool wantControlRows = SectionOn(sections, "control_rows", false) || full;
			bool wantSims = SectionOn(sections, "sims", false) || full;

			JSONClass r = new JSONClass();
			r["uid"] = p.uid;
			r["on"] = JSONRaw.Bool(p.on);

			DAZCharacterSelector sel = null;
			try { sel = Selector(p); } catch { }

			if (wantCharacter && sel != null) {
				JSONClass ch = new JSONClass();
				try { ch["character"] = sel.GetStringChooserParamValue("characterSelection"); } catch { }
				try { ch["gender"] = sel.gender.ToString(); } catch { }
				r["character"] = ch;
			}

			JSONClass audit = PersonAudit(p);

			if (wantMorphs && sel != null) {
				JSONArray top = new JSONArray();
				int nonZero = 0;
				try {
					foreach (DAZMorph m in AllMorphs(sel)) {
						try {
							float v = m.morphValue;
							if (Math.Abs(v) < 0.0005f) {
								if (includeHidden) nonZero++;
								continue;
							}
							nonZero++;
							if (top.Count < morphLimit) {
								JSONClass row = new JSONClass();
								row["name"] = m.displayName;
								row["v"] = F(v);
								top.Add(row);
							}
						} catch { }
					}
				} catch { }
				r["nonZeroMorphs"] = JSONRaw.Num(nonZero);
				r["topMorphs"] = top;
				if (nonZero > top.Count) r["morphsTruncated"] = JSONRaw.Num(nonZero - top.Count);
			}

			if (wantExpressions && sel != null) {
				JSONArray arr = new JSONArray();
				try {
					foreach (DAZMorph m in AllMorphs(sel)) {
						try {
							if (!LooksLikeExpression(m)) continue;
							if (Math.Abs(m.morphValue) < 0.0005f && !includeHidden) continue;
							JSONClass row = new JSONClass();
							row["name"] = m.displayName;
							row["v"] = F(m.morphValue);
							arr.Add(row);
							if (arr.Count >= 60) break;
						} catch { }
					}
				} catch { }
				r["expressions"] = arr;
			}

			if (wantClothing) {
				JSONClass cl = new JSONClass();
				cl["worn"] = JSONRaw.Num(IntOf(audit["wornCount"], 0));
				JSONArray items = new JSONArray();
				JSONArray all = audit["worn"] != null ? audit["worn"].AsArray : null;
				if (all != null) {
					for (int i = 0; i < all.Count && i < itemLimit; i++) items.Add(all[i]);
				}
				cl["items"] = items;
				if (all != null && all.Count > items.Count) cl["truncated"] = JSONRaw.Num(all.Count - items.Count);
				r["clothing"] = cl;
			}

			if (wantHair) {
				JSONClass hr = new JSONClass();
				hr["on"] = JSONRaw.Num(IntOf(audit["hairItemsOn"], 0));
				JSONArray items = new JSONArray();
				JSONArray all = audit["hairs"] != null ? audit["hairs"].AsArray : null;
				if (all != null) {
					for (int i = 0; i < all.Count && i < itemLimit; i++) items.Add(all[i]);
				}
				hr["items"] = items;
				r["hair"] = hr;
			}

			if (wantControlSummary) {
				JSONClass c = new JSONClass();
				c["total"] = audit["controls"];
				c["states"] = PersonControlStates(audit); // [On, Off, other]
				if (audit["nonFiniteControls"] != null) c["nonFinite"] = audit["nonFiniteControls"];
				if (audit["hipChest"] != null) c["hipChest"] = audit["hipChest"];
				if (audit["torn"] != null) c["torn"] = JSONRaw.Bool(true);
				r["controls"] = c;
			}
			if (wantControlRows) {
				List<Atom> one = new List<Atom>();
				one.Add(p);
				r["controlRows"] = PersonControlRows(one, precision);
			}

			if (wantSims) {
				JSONClass s = new JSONClass();
				s["on"] = audit["simsOn"];
				s["total"] = audit["simsTotal"];
				r["sims"] = s;
			}

			if (audit["subScene"] != null) r["subScene"] = audit["subScene"];
			if (sections.Count == 0 && !full) {
				r["hint"] = "sections=morphs|clothing|hair|controls|control_rows|sims|expressions|character; morph_limit/item_limit cap the lists";
			}
			return r;
		}

		// ===================== physics state =====================

		/// <summary>Simulation timing plus a counted summary of physics storables.
		/// The per-object list is opt-in (detail=objects) because it is large.</summary>
		public JSONNode PhysicsState(JSONNode args) {
			JSONClass r = new JSONClass();
			r["timeScale"] = F(Time.timeScale);
			r["fixedDeltaTime"] = F(Time.fixedDeltaTime);
			r["frame"] = JSONRaw.Num(Time.frameCount);
			try {
				float sd = Time.smoothDeltaTime;
				if (sd > 0.0001f) r["fps"] = JSONRaw.Num((int)Math.Round(1.0f / sd));
			} catch { }

			int objectCount = 0, storableCount = 0, enabledCount = 0;
			JSONArray rows = new JSONArray();
			string detail = (S(args, "detail") ?? "").ToLowerInvariant();
			bool wantObjects = detail == "objects" || detail == "full" || Has(args, "limit");
			int limit = ClampI((int)Num(args, "limit", 60f), 1, 400);

			try {
				JSONNode objs = ListPhysicsObjects(new JSONClass());
				objectCount = IntOf(objs["count"], 0);
				JSONArray all = objs["objects"] != null ? objs["objects"].AsArray : null;
				if (all != null) {
					for (int i = 0; i < all.Count; i++) {
						JSONArray ps = all[i]["physicsStorables"] != null ? all[i]["physicsStorables"].AsArray : null;
						if (ps == null) continue;
						storableCount += ps.Count;
						if (wantObjects && rows.Count < limit) {
							JSONClass row = new JSONClass();
							row["uid"] = all[i]["uid"];
							row["type"] = all[i]["type"];
							row["storables"] = ps;
							rows.Add(row);
						}
					}
				}
			} catch (Exception e) {
				r["error"] = e.Message;
			}

			JSONClass sims = new JSONClass();
			foreach (Atom a in SC.GetAtoms()) {
				if (a == null) continue;
				int total = 0;
				enabledCount += SimsOn(a, out total);
			}
			sims["on"] = JSONRaw.Num(enabledCount);
			r["objects"] = JSONRaw.Num(objectCount);
			r["storables"] = JSONRaw.Num(storableCount);
			r["sims"] = sims;
			if (wantObjects) {
				r["list"] = rows;
				if (rows.Count < objectCount) r["truncated"] = JSONRaw.Bool(true);
			}
			return r;
		}

		// ===================== find atoms =====================

		/// <summary>Filtered atom search. Filters are conjunctive; `fields`/`limit`
		/// keep the result to the rows the caller actually needs.</summary>
		public JSONNode FindAtoms(JSONNode args) {
			string nameF = S(args, "name");
			string typeF = S(args, "type");
			string compF = S(args, "component");
			string propF = S(args, "property");
			string valF = S(args, "value");
			string tagF = S(args, "tag");
			if (tagF != "") throw new ApiError("tag filter is not supported by VaM's runtime; use name/type/component/property filters");
			HashSet<string> want = RowFields(args);
			int precision = Precision(args);
			int limit = ClampI((int)Num(args, "limit", 50f), 1, 500);
			int offset = Math.Max(0, (int)Num(args, "offset", 0));

			List<Atom> hits = new List<Atom>();
			foreach (Atom a in SC.GetAtoms()) {
				if (a == null) continue;
				if (nameF != "" && a.uid.IndexOf(nameF, StringComparison.OrdinalIgnoreCase) < 0
					&& (a.name == null || a.name.IndexOf(nameF, StringComparison.OrdinalIgnoreCase) < 0)) continue;
				if (typeF != "" && !string.Equals(a.type, typeF, StringComparison.OrdinalIgnoreCase)) continue;
				if (compF != "") {
					if (a.mainController == null) continue;
					Component[] cs = a.mainController.GetComponentsInChildren<Component>(true);
					bool found = false;
					foreach (Component c in cs) {
						if (c != null && c.GetType().Name.IndexOf(compF, StringComparison.OrdinalIgnoreCase) >= 0) { found = true; break; }
					}
					if (!found) continue;
				}
				if (propF != "") {
					if (!AtomHasProperty(a, propF, valF)) continue;
				}
				hits.Add(a);
			}
			SortAtoms(hits, S(args, "sort"));

			JSONArray arr = new JSONArray();
			int total = hits.Count;
			int take = total - offset;
			if (take < 0) take = 0;
			if (take > limit) take = limit;
			for (int i = 0; i < take; i++) arr.Add(SceneRow(hits[offset + i], want, precision));

			JSONClass r = new JSONClass();
			r["total"] = JSONRaw.Num(total);
			r["returned"] = JSONRaw.Num(arr.Count);
			if (offset > 0) r["offset"] = JSONRaw.Num(offset);
			if (offset + arr.Count < total) r["hasMore"] = JSONRaw.Bool(true);
			r["matches"] = arr;
			return r;
		}

		private static bool AtomHasProperty(Atom a, string propF, string valF) {
			foreach (string sid in a.GetStorableIDs()) {
				JSONStorable st = a.GetStorableByID(sid);
				if (st == null) continue;
				foreach (string n in st.GetFloatParamNames()) {
					if ((n.IndexOf(propF, StringComparison.OrdinalIgnoreCase) >= 0 || string.Equals(n, propF, StringComparison.OrdinalIgnoreCase))
						&& (valF == "" || ParamValueMatches(st, n, "float", valF))) return true;
				}
				foreach (string n in st.GetBoolParamNames()) {
					if ((n.IndexOf(propF, StringComparison.OrdinalIgnoreCase) >= 0 || string.Equals(n, propF, StringComparison.OrdinalIgnoreCase))
						&& (valF == "" || ParamValueMatches(st, n, "bool", valF))) return true;
				}
				foreach (string n in st.GetStringChooserParamNames()) {
					if ((n.IndexOf(propF, StringComparison.OrdinalIgnoreCase) >= 0 || string.Equals(n, propF, StringComparison.OrdinalIgnoreCase))
						&& (valF == "" || ParamValueMatches(st, n, "chooser", valF))) return true;
				}
				foreach (string n in st.GetStringParamNames()) {
					if ((n.IndexOf(propF, StringComparison.OrdinalIgnoreCase) >= 0 || string.Equals(n, propF, StringComparison.OrdinalIgnoreCase))
						&& (valF == "" || ParamValueMatches(st, n, "string", valF))) return true;
				}
			}
			return false;
		}
	}
}
