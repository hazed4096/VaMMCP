# VaMMCP — Virt-A-Mate × MCP

[![CI](https://github.com/Rorical/VaMMCP/actions/workflows/ci.yml/badge.svg)](https://github.com/Rorical/VaMMCP/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

*[English](README.md)*

让任何 MCP 客户端（Claude Code / Codex / Cursor / Claude Desktop / …）通过自然语言**完整控制 VaM**：
搭场景、加 Atom、捏人（Morph）、换外观/服装/发型、摆姿势、做表情、控制骨骼、调任意参数、
操作相机截图、浏览/下载 Hub 社区资源、挂载配置 VaM 插件、皮肤次表面反射等。

**MCP server 直接内置于 BepInEx 插件进程**，走 Streamable HTTP 传输（规范 2025-06-18），
Agent 直连 `http://127.0.0.1:9837/mcp`，无需 Python、无外部进程、无轮询文件。

```
MCP 客户端 ──HTTP/JSON-RPC──▶ VaMMCP (BepInEx 插件, VaM 进程内)
                               ├─ 后台线程: 最小 HTTP server (仅绑定 127.0.0.1, Origin 校验)
                               ├─ JSON-RPC / MCP 协议层 (initialize / tools/list / tools/call / resources)
                               ├─ 主线程调度器 (队列 → Unity Update)
                               └─ VaM 控制层 (SuperController / Atom / JSONStorable / DAZMorph / MVRPlugin …)
```

> ⚠️ 本项目是**非官方**社区项目，与 Meshed VR / Virt-A-Mate / VaM Hub 无任何关联。
> 请仅在你合法拥有的 VaM 副本上使用，并遵守 VaM 的 EULA 与 Hub 内容许可。

## ✨ 功能一览（73 个工具）

| 分类 | 工具 |
| --- | --- |
| 状态与会话 | `status` |
| 场景 | `list_scenes` `load_scene` `new_scene` `atom_info` `loader_heal` `save_scene` `press_key` `ui_hud` |
| Atom | `list_atom_types` `list_atoms` `list_objects` `add_atom` `remove_atom` `set_atom_on` `get_atom_transform` `set_atom_transform` |
| 人物 | `add_person` `list_persons` `list_looks` `load_look` `save_look` `set_character` |
| 捏人 | `morph_action`（list / set / get / reset） |
| 服装/发型 | `clothing_action` `hair_action`（预设、条目、增删/穿脱） |
| 姿态/表情 | `pose_action`（list / load / save / savefull）`expression_action` |
| 骨骼控制 | `list_controls` `get_control` `set_control` `set_control_state` `control_pose` `person_teleport` `person_point_at` `set_gaze` |
| 通用参数（长尾全覆盖） | `list_atom_storables` `list_storable_params` `get_param` `set_param` `call_action` |
| 相机 | `camera_action`（get / set / aim / capture，默认使用自有 MCP 相机） |
| 模拟 | `simulation_action`（set / reset） |
| 动画 | `animation_action`（模式、步骤、录制） |
| 触发器 | `trigger_action` `trigger_ui` |
| 物理 | `physics_action`（对象、关节） |
| 灯光/音频 | `light_action` `audio_action` |
| 用户偏好 | `get_user_preferences` `set_user_preferences` |
| 场景浏览器/模板 | `scene_browser` `template_browser` |
| 原子 UI | `atom_action` `atom_geometry_choices` |
| 人物动画 | `person_animation` |
| 自定义 Unity 资源 | `cua_action` |
| 包管理 | `package_manager` |
| 手部控制/材质 | `person_hand_control` `person_material` |
| 场景状态/诊断 | `scene_state` `atom_tree` `person_state` `physics_state` `simulation_performance` `find_atoms` `storable_diff` `scene_dependencies` `performance_profile` |
| Hub 社区资源 | `hub_action`（browse / detail / download） |
| 皮肤 | `set_skin_sss`（次表面反射 `_SubdermisColor`） |
| 插件管理 | `plugin_action`（list / add / remove） |
| 逃生舱（默认关） | `eval_cs` |

完整参考：**[docs/TOOLS.zh-CN.md](docs/TOOLS.zh-CN.md)**。

**设计要点**：任何 VaM UI 滑块本质上都是 JSONStorable 参数——
`list_storable_params` 枚举 → `set_param` 设置，这就是"全面"的来源；
Morph 是捏人核心（`DAZMorph.morphValue`）；插件加载后即成为 atom 的 storable，通用层直接覆盖。

## 📦 环境要求

- Virt-A-Mate 1.20+（开发验证于 1.22.0.13 / Unity 2018.1.9f2 / Mono）
- BepInEx 5.4.x（x64）已安装到 VaM 根目录（`winhttp.dll` + `BepInEx/`）
- 支持 Streamable HTTP 的 MCP 客户端

从源码构建还需要 .NET SDK。插件目标框架是 **net35**——VaM 运行在 .NET 3.5 API 级别。

## 🚀 安装

### 方式 A —— 直接用编译好的 DLL（推荐，无需任何工具链）

1. 若尚未安装 BepInEx 5.4.21 x64：[下载](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.21)
   后解压到 `VaM.exe` 所在目录，先启动一次 VaM 让它生成目录结构。
2. 从 [最新 release](https://github.com/Rorical/VaMMCP/releases/latest) 下载 `VaMMCP.dll`。
3. 放进 `<VaM>\BepInEx\plugins\`。
4. 启动 VaM，检查 `<VaM>\BepInEx\LogOutput.log` 出现
   `VaMMCP ready. MCP endpoint: http://127.0.0.1:9837/mcp`。

### 方式 B —— PowerShell 一键安装（Windows）

自动装 BepInEx（如缺失）并拉取最新 release：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install.ps1
# 或指定 VaM 目录：
powershell -ExecutionPolicy Bypass -File scripts\install.ps1 -VamRoot "D:\VaM"
```

### 方式 C —— 从源码构建（Linux/macOS/WSL/Windows）

```bash
./scripts/install-bepinex.sh    # 仅在未装 BepInEx 时需要
./scripts/deploy.sh             # 构建 + 部署到 <VaM>/BepInEx/plugins/VaMMCP.dll
```

两个脚本默认仓库放在 VaM 目录内；放在别处时：

```bash
export VAM_ROOT="D:/path/to/VaM"        # scripts 读取
# 或: dotnet build src/VaMMCP.csproj -c Release -p:VaMRoot=D:/path/to/VaM
```

## 🔌 接入 MCP 客户端

核心配置（VaM 运行期间）：

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

各客户端详见 [docs/clients.zh-CN.md](docs/clients.zh-CN.md)。冒烟测试：`./scripts/smoke-test.sh`。

## ⚙️ 配置（BepInEx/config/com.vammcp.core.cfg）

| 键 | 默认 | 说明 |
| --- | --- | --- |
| Server.Enabled | true | 是否随 VaM 启动 MCP server |
| Server.Port | 9837 | 监听端口（仅绑定 127.0.0.1） |
| Security.AllowEval | false | 是否开放 `eval_cs`（进程内执行任意 C#，高危） |
| Security.EvalTimeoutSec | 30 | eval 超时（秒） |

配置文件在插件首次随 VaM 启动后生成。

## 🧱 架构与开发

```
src/
├── Plugin.cs              # BepInEx 入口、配置、主线程调度
├── MainThread.cs          # HTTP 线程 → Unity 主线程的队列调度器（同步等待）
├── Util.cs                # 日志 / ApiError
├── Mcp/
│   ├── HttpServer.cs      # 最小 HTTP/1.1 server（Streamable HTTP 传输、Origin 校验、keep-alive）
│   ├── McpServer.cs       # MCP 协议：initialize / tools/list / tools/call / resources
│   ├── Tool.cs            # 工具定义（名称/描述/JSON Schema/处理器/超时）
│   └── ToolRegistry.cs    # 73 个工具注册
└── Api/
    └── VaMApi.cs          # VaM 控制层（全部工具实现）
```

详见 [docs/DEVELOPMENT.zh-CN.md](docs/DEVELOPMENT.zh-CN.md)（构建约束、线程模型、VaM 的各种坑），
贡献指南见 [CONTRIBUTING.md](CONTRIBUTING.md)。

## 🔒 安全说明

- HTTP server 只绑定 `127.0.0.1`，并校验 Origin 头（防 DNS rebinding）。**没有鉴权**：
  本机任何能访问 loopback 的程序都能操作 VaM，请按普通本地开发服务器对待。
- `eval_cs` 默认关闭；开启等于给 Agent 本机任意代码执行权（且受 VaM 运行时沙箱限制：
  System.IO / System.Reflection / System.AppDomain / UnityEditor / Mono.Cecil 禁用）
- 下载 Hub 内容请遵守 Hub 的内容许可；付费内容需要你在 VaM 内登录 Hub 账户

安全问题上报见 [SECURITY.md](SECURITY.md)。

## 📄 许可证

[MIT](LICENSE)。本项目按"现状"提供，作者不对任何用途承担担保或责任。
