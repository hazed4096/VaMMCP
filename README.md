# VaMMCP — Virt-A-Mate × MCP

[![CI](https://github.com/Rorical/VaMMCP/actions/workflows/ci.yml/badge.svg)](https://github.com/Rorical/VaMMCP/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

*[中文文档](README.zh-CN.md)*

Control [Virt-A-Mate](https://hub.virtamate.com/) from any MCP client (Claude Code, Codex,
Cursor, Claude Desktop, …) in plain language: build scenes, add atoms, sculpt morphs, swap
looks/clothing/hair, pose characters, drive expressions and bones, tweak any parameter,
move the camera and take screenshots, browse and download Hub content, manage VaM plugins,
and more.

**The MCP server runs inside the BepInEx plugin, in the VaM process**, over Streamable HTTP
(spec 2025-06-18). Your agent connects straight to `http://127.0.0.1:9837/mcp` — no Python,
no side-car process, no file polling.

```
MCP client ──HTTP/JSON-RPC──▶ VaMMCP (BepInEx plugin, inside the VaM process)
                                ├─ background thread: tiny HTTP server (loopback-only, Origin-checked)
                                ├─ JSON-RPC / MCP layer (initialize / tools/list / tools/call / resources)
                                ├─ main-thread dispatcher (queue → Unity Update)
                                └─ VaM control layer (SuperController / Atom / JSONStorable / DAZMorph / MVRPlugin …)
```

> ⚠️ **Unofficial community project.** Not affiliated with, endorsed by, or connected to
> Meshed VR, Virt-A-Mate or the VaM Hub. Use it only with a copy of VaM you legally own, and
> respect the VaM EULA and the licence of any Hub content you download.

## ✨ Tools at a glance (73)

| Category | Tools |
| --- | --- |
| Status / session | `status` |
| Scene | `list_scenes` `load_scene` `new_scene` `atom_info` `loader_heal` `save_scene` `press_key` `ui_hud` |
| Atom | `list_atom_types` `list_atoms` `list_objects` `add_atom` `remove_atom` `set_atom_on` `get_atom_transform` `set_atom_transform` |
| Person | `add_person` `list_persons` `list_looks` `load_look` `save_look` `set_character` |
| Morphs | `morph_action` (list / set / get / reset) |
| Clothing / hair | `clothing_action` `hair_action` (presets, items, add/remove/wear) |
| Pose / expression | `pose_action` (list / load / save / savefull) `expression_action` |
| Controls | `list_controls` `get_control` `set_control` `set_control_state` `control_pose` `person_teleport` `person_point_at` `set_gaze` |
| Generic parameters (long tail) | `list_atom_storables` `list_storable_params` `get_param` `set_param` `call_action` |
| Camera | `camera_action` (get / set / aim / capture, own MCP camera by default) |
| Simulation | `simulation_action` (set / reset) |
| Animation | `animation_action` (patterns, steps, record) |
| Triggers | `trigger_action` `trigger_ui` |
| Physics | `physics_action` (objects, joints) |
| Lights / audio | `light_action` `audio_action` |
| User preferences | `get_user_preferences` `set_user_preferences` |
| Scene browser / templates | `scene_browser` `template_browser` |
| Atom UI | `atom_action` `atom_geometry_choices` |
| Person animation | `person_animation` |
| Custom Unity assets | `cua_action` |
| Package manager | `package_manager` |
| Hand control / material | `person_hand_control` `person_material` |
| Scene state / inspection | `scene_state` `atom_tree` `person_state` `physics_state` `simulation_performance` `find_atoms` `storable_diff` `scene_dependencies` `performance_profile` |
| Hub | `hub_action` (browse / detail / download) |
| Skin | `set_skin_sss` (subdermis colour) |
| Plugins | `plugin_action` (list / add / remove) |
| Escape hatch (off by default) | `eval_cs` |

Full reference: **[docs/TOOLS.md](docs/TOOLS.md)**.

**Design note:** every slider in the VaM UI is ultimately a JSONStorable parameter — enumerate
with `list_storable_params`, write with `set_param`. That is where the "covers everything"
claim comes from. Morphs are the heart of character sculpting (`DAZMorph.morphValue`), and a
loaded VaM plugin simply becomes another storable on the atom, so the generic layer reaches it too.

## 📦 Requirements

- Virt-A-Mate 1.20+ (developed against 1.22.0.13 / Unity 2018.1.9f2 / Mono)
- BepInEx 5.4.x (x64) installed in the VaM folder (`winhttp.dll` + `BepInEx/`)
- An MCP client that speaks Streamable HTTP

Building from source additionally needs the .NET SDK. The plugin targets **net35** because VaM
runs at the .NET 3.5 API level.

## 🚀 Install

### Option A — prebuilt DLL (recommended, no toolchain needed)

1. Install BepInEx 5.4.21 x64 into your VaM folder if you have not already
   ([download](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.21) → unzip into the folder
   containing `VaM.exe` → launch VaM once so BepInEx creates its directories).
2. Download `VaMMCP.dll` from the [latest release](https://github.com/Rorical/VaMMCP/releases/latest).
3. Drop it into `<VaM>\BepInEx\plugins\`.
4. Start VaM. `<VaM>\BepInEx\LogOutput.log` should contain
   `VaMMCP ready. MCP endpoint: http://127.0.0.1:9837/mcp`.

### Option B — PowerShell installer (Windows)

Run in the VaM folder; it installs BepInEx if missing and fetches the latest VaMMCP release:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install.ps1
# or point it somewhere else:
powershell -ExecutionPolicy Bypass -File scripts\install.ps1 -VamRoot "D:\VaM"
```

### Option C — build from source (Linux/macOS/WSL/Windows)

```bash
./scripts/install-bepinex.sh    # only if BepInEx is not installed yet
./scripts/deploy.sh             # build + copy to <VaM>/BepInEx/plugins/VaMMCP.dll
```

Both scripts assume the repo sits inside the VaM folder. Otherwise point them at it:

```bash
export VAM_ROOT="D:/path/to/VaM"        # read by the scripts
# or: dotnet build src/VaMMCP.csproj -c Release -p:VaMRoot=D:/path/to/VaM
```

## 🔌 Connect an MCP client

With VaM running:

```json
{
  "mcpServers": {
    "vam": {
      "type": "http",
      "url": "http://127.0.0.1:9837/mcp"
    }
  }
}
```

```bash
claude mcp add vam --transport http http://127.0.0.1:9837/mcp
codex mcp add vam --transport http http://127.0.0.1:9837/mcp
```

Per-client instructions: [docs/clients.md](docs/clients.md). Smoke test: `./scripts/smoke-test.sh`.

## ⚙️ Configuration (`BepInEx/config/com.vammcp.core.cfg`)

| Key | Default | Meaning |
| --- | --- | --- |
| Server.Enabled | true | Start the MCP server when VaM launches |
| Server.Port | 9837 | Listening port (bound to 127.0.0.1 only) |
| Security.AllowEval | false | Expose `eval_cs` (runs arbitrary C# in-process — dangerous) |
| Security.EvalTimeoutSec | 30 | `eval_cs` timeout in seconds |

The file appears after the first launch with the plugin installed.

## 🧱 Architecture

```
src/
├── Plugin.cs              # BepInEx entry point, config, main-thread pump
├── MainThread.cs          # HTTP thread → Unity main thread dispatcher (blocking)
├── Util.cs                # logging / ApiError
├── Mcp/
│   ├── HttpServer.cs      # minimal HTTP/1.1 server (Streamable HTTP, Origin check, keep-alive)
│   ├── McpServer.cs       # MCP protocol: initialize / tools/list / tools/call / resources
│   ├── Tool.cs            # tool definition (name / description / JSON schema / handler / timeout)
│   └── ToolRegistry.cs    # all 73 tool registrations
└── Api/
    └── VaMApi.cs          # VaM control layer (every tool implementation)
```

See [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) for build details, threading rules and a long
list of VaM-specific pitfalls. Contributions welcome — [CONTRIBUTING.md](CONTRIBUTING.md).

## 🔒 Security

- The HTTP server binds to `127.0.0.1` only and validates the `Origin` header (DNS-rebinding
  protection). There is **no authentication**: anything that can reach loopback on your machine
  can drive VaM. Treat it like any other local dev server.
- `eval_cs` is disabled by default. Enabling it gives your agent arbitrary code execution inside
  the VaM process (still subject to VaM's runtime sandbox: System.IO, System.Reflection,
  System.AppDomain, UnityEditor and Mono.Cecil are blocked).
- Hub downloads are subject to Hub content licences; paid content requires you to be signed into
  your Hub account inside VaM.

Reporting a vulnerability: [SECURITY.md](SECURITY.md).

## 📄 Licence

[MIT](LICENSE). Provided "as is", without warranty or liability of any kind.
