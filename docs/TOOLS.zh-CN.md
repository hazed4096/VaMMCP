# 工具参考（64 个）

*[English](TOOLS.md)*

所有工具通过 `tools/call` 调用：`{ "name": "...", "arguments": { ... } }`。
参数标 `*` 为必填、`?` 为可选。`person` 省略时默认使用场景中第一个 Person。数值数组均为 `[x, y, z]`。

`eval_cs` 仅在 `Security.AllowEval=true` 时注册，所以默认安装暴露 62 个工具。

## 状态与会话

### status
VaM 运行时状态：插件版本、VaM 版本、原子/人物数量、端点、eval 开关。

## 场景

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `list_scenes` | `search?` | 列出 `Saves/scene` 下的场景 |
| `load_scene` | `path`*, `merge?` | 加载场景（`merge=true` 合并进当前场景）；支持 .var 包路径（`Package.1:/Saves/scene/x.json`） |
| `new_scene` | — | 新建空场景（**丢弃当前场景**；空场景无默认灯光，记得打灯/开环境光） |
| `save_scene` | `path?` | 保存场景（默认 `Saves/scene/VaMMCP_<时间戳>.json`；覆盖已存在文件会自动先删后存） |
| `press_key` | `key`*、`times?`、`focus?` | 向 VaM 发送真实按键（Escape 可关闭保存截图对话框等模态提示）；绝不抢焦点 - 仅当 VaM 已持有焦点时发送，否则拒绝（`focus=true` 显式允许把窗口提到前台） |

## Atom

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `list_atom_types` | — | 可创建的原子类型（按分类，来自 VaM 运行时数据） |
| `list_atoms` | `type?` | 场景原子列表（uid/type/on/位置/旋转） |
| `add_atom` | `type`*, `uid?` | 创建原子，等待其就绪（最长 90s，按 uid 轮询） |
| `remove_atom` | `uid`* | 删除原子 |
| `set_atom_on` | `uid`*, `on`* | 显示/隐藏 |
| `get_atom_transform` | `uid`* | 世界坐标/欧拉角/缩放 |
| `set_atom_transform` | `uid`*, `position?`, `rotation?`, `scale?`, `relative?` | 变换（`relative=true` 为相对位移） |

## 人物

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `add_person` | `uid?` | 添加人物（uid 可能被 VaM 重命名，以返回值为准） |
| `list_persons` | — | 人物列表（uid/角色名/性别/位置） |
| `list_looks` | `search?` | 外观预设列表（`Saves/Person/Appearance`） |
| `load_look` | `path`*, `person?` | 应用外观预设（角色/服装/发型/morph 增量；异步，稍后验证） |
| `save_look` | `path?`, `person?` | 导出外观预设（**注意**：VaM 的预设保存不含完整材质参数，跨场景搬运后需按需补设皮肤参数） |
| `set_character` | `person?`, `name`* | 切换基础角色网格（名称见 `get_param storable=geometry param=characterSelection` 的 choices） |

## 预设导出

| 工具 | 说明 |
| --- | --- |
| `save_look` | 外观（角色/服装/发型/morph）→ `Saves/Person/Appearance/` |
| `save_pose` | 姿态+物理 → `Saves/Person/Pose/` |
| `save_full_preset` | 全量（外观+姿态+物理）→ `Saves/Person/full/` |

## 捏人（Morph）

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `list_morphs` | `person?`, `search?`, `region?`, `group?`, `limit?` | 搜索 morph（含名称/uid/区域/分组/当前值） |
| `set_morph` | `person?`, `name`*, `value`* | 设置 morph 值（典型范围 -1..1） |
| `get_morph` | `person?`, `name`* | 读取 morph 值 |
| `reset_morphs` | `person?`, `region?` | 归零（可按区域） |

## 服装 / 发型

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `list_packages` | — | 已安装的 .var 包 |
| `list_clothing_presets` | `search?` | 服装预设/物品：磁盘 + **递归扫描所有包**的 `Custom/Clothing`（.vam/.json） |
| `load_clothing_preset` | `path`*, `person?` | 应用服装预设 |
| `list_clothing_items` | `person?`、`all?` | 实际穿着的服装（uid/名称/active）及 `worn` 摘要；`all=true` 同时返回未穿着的库项目 |
| `add_clothing_item` | `path`*、`person?`、`wait_seconds?` | 添加服装（`.vam` 包路径）；优先激活库中已有项目，否则等待 VaM 确认已穿上，无法添加时返回 `ok:false` 与提示 |
| `remove_clothing_item` | `id`*, `person?` | 移除服装（删除延迟到帧末，可用 list_clothing_items 确认） |
| `set_clothing_item_on` | `id`*, `on`*, `person?` | 穿/脱 |
| `list_hair_presets` / `load_hair_preset` / `list_hair_items` / `add_hair_item` / `remove_hair_item` / `set_hair_item_on` | 同服装 | 发型（`Custom/Hair`） |

## 姿态 / 表情

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `list_poses` | `search?` | 姿态预设（`Saves/Person/Pose`） |
| `load_pose` | `path`*, `person?`, `all?` | 应用姿态（`all=true` 应用到所有人） |
| `save_pose` | `path?`, `person?` | 导出姿态 |
| `list_expressions` | `person?` | 表情 morph 列表（启发式过滤） |
| `set_expression` | `person?`, `name`*, `value?`, `reset?` | 设置表情（默认先清空其他表情再设，value 默认 1） |

## 骨骼控制

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `list_controls` | `person?` | FreeControllerV3 控制点（headControl / chestControl / …） |
| `get_control` | `person?`, `control`* | 控制点世界位置/欧拉角 |
| `set_control` | `person?`, `control`*, `position?`, `rotation?` | 移动/旋转控制点 |
| `set_gaze` | `person?`, `targetControl?` | 驱动原生 `eyeTargetControl`：`"Camera"`/原子 uid = 看向它（强制位置/旋转状态 On），`"None"` = Off（回到姿态驱动）。Person 上没有 `lookAt` storable |

## 通用参数（长尾全覆盖）

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `list_atom_storables` | `uid`* | 原子全部 storable（含类型） |
| `list_storable_params` | `uid`*, `storable`* | 参数内省：floats（含 min/max/default）/bools/strings/urls/choosers/colors/actions/customParams/properties |
| `get_param` | `uid`*, `storable`*, `param`* | 读参数（float/bool/string/url/chooser/color/property 自动识别） |
| `set_param` | `uid`*, `storable`*, `param`*, `value`*, `type?`, `reload?` | 写参数（颜色支持 `#RRGGBB` 或 `r,g,b,a`；url 参数设置文件/图片路径并默认重新加载，传 `reload=false` 跳过；未知参数+数值按 float 兜底，否则报清晰错误） |
| `call_action` | `uid`*, `storable`*, `action`* | 触发 storable 的动作（按钮） |

`type` 可强制指定：`float|bool|string|url|chooser|color|property`（`property` 走反射读写公共属性，覆盖 customParamNames 如 `gravityX`）。

## 相机

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `get_camera` | — | 监视器相机位置/旋转/FOV |
| `set_camera` | `position?`, `rotation?`, `fov?` | 移动相机 |
| `capture_view` | `width?`, `height?`, `path?`, `return_image?`, `mode?`, `timeout_ms?` | 渲染截图（默认 `Saves/PluginData/vam-mcp/preview.png`）。`mode=window`（默认）抓真实后备缓冲，人物皮肤/衣服渲染正确（等待下一帧；空闲省电类插件可能拖慢）。**已知限制：某些启动下 window 抓取即使 VaM 活动也返回全黑——此时用 `mode=camera`。** `mode=camera` 快速从监视器相机重渲染；能渲染真实场景（已验证：相机移动+环境+Hub 面板），但人物网格可能变黑。`return_image=true` 时同时以 MCP image 块返回 PNG——分辨率别开太大（如 640×360），超过 4 MB 只落盘不内联。肤色/衣服颜色仍以 VaM 窗口目视为准，截图仅供参考 |

## 模拟

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `set_simulation` | `paused?`, `timeScale?` | 暂停/恢复/时间缩放 |
| `reset_simulation` | — | 重置物理模拟（软体/布料抖动） |

## 触发器 / 动画 / 音频 / 物理对象

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `create_trigger` / `list_triggers` | `create_trigger`: `type?`（默认 UIButton）, `uid?` | VaM 触发原子；控其 `Trigger` storable（见 `trigger_action`） |
| `trigger_action` | `trigger_uid`*, `action`*, `storable?`, `param?`, `value?` | `Fire` 与 VaM 按钮处理器完全一致地触发（对 `UIButtonTrigger`/`VariableTrigger` 脉冲 `trigger.active` true→false；没有配置接收动作也不报错）。`SetFloatParam`/`SetBoolParam` 写 `param`；其他名字按动作调用。支持 `原子/Trigger` uid；默认精确匹配 `Trigger` |
| `create_animation_pattern` / `list_animation_patterns` | `uid?` / `search?` | AnimationPattern 原子（含其步骤，依赖 VaM 自动链接） |
| `create_animation_step` | `pattern_uid`*, `uid?`, `duration?` | AnimationStep（`transitionToTime`）；无自动链接动作时需在 VaM UI 里挂接 |
| `list_animation_steps` | `pattern_uid`? | 某模式的步骤（部分步骤不暴露链接参数，见返回提示） |
| `animation_pattern_action` / `animation_step_action` | `pattern_uid`/`step_uid`*, `action`*, `value?` | Play/Stop/… 动作；`Step SetTransition` 数值=秒（写 `transitionToTime`），字符串=过渡类型选择 |
| `record_scene_animation` | `pattern_uid`*, `action`* (start/stop) | 会给出解释错误：VaM 自带控制器录制仅 UI 可用（pattern 无 Record 动作） |
| `list_audio_clips` | `type?` (all/embedded/url), `category?`, `search?` | 枚举 VaM 已注册的音频（内嵌包音频如 FemaleBreath/Music/SoundFX + URL 剪辑），按类别分组。返回的 `uid` 可直接作为 `audio_source_action` Play/SetClip 的 clip；`category` 大小写不敏感（验证：新启动 332 条内嵌剪辑、11 类） |
| `create_audio_source` / `list_audio_sources` / `audio_source_action` | `audio_source_action`: `uid`*, `action`*, `clip?`/`value?` | 动作：SetClip, Play, PlayLoop, Pause, UnPause, Stop, SetVolume, SetLoop 等。`SetClip` 插件内直接解码本地 PCM WAV（16bit/float32）；ogg/mp3 走 VaM URL 队列，Sound 页签未打开前可能一直 `ready=false`。`Play`/`PlayLoop` 复用上次 `SetClip`；`list_audio_sources` 显示 `playingClip`。`Play` 现在也能解析内嵌剪辑（已实测 Music 剪辑） |
| `physics_object_action` | `uid`*, `action`*, `value`/`direction?` | mainController GameObject 上的 Rigidbody（缺失会自动加）：`SetMass`、`SetDrag`、`AddForce`（冲量幅值，默认 +Y；模拟必须醒着——IdleSaver 冻结时不动） |

## 原子 UI 辅助

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `atom_control` | `uid`*, `action`*, `value?` | `Select`（SuperController.SelectController 选主控制器）、`FreezePhysics`/`UnfreezePhysics`（控制器位置/旋转状态 Freeze↔Off；`value=false` 解冻）、`DisableCollision`/`EnableCollision`、`DisableRender`/`EnableRender`（映射到原子电源开关）、`EndIsolate`（EndIsolateEditAtom；进入隔离仅 UI）、`LoadUserDefaults`（mainPresetControl.LoadUserDefaults——与 VaM 自己的添加流程相同；仅在存在*用户已保存的默认预设*时生效，否则 `loaded:true` 无变化——已实测普通 Cube） |
| `atom_move` | `uid`*, `action`*, `value?` | SetPosition/SetRotation/SetScale/SnapToGround/AlignTo/ResetTransform |
| `atom_geometry_choices` | `uid`*, `action`, `choice?` | 原子几何选项（CustomUnityAsset 等） |
| `storable_diff` | `uid_a`*, `storable_a`*, `uid_b`*, `storable_b`* | 两个 storable 求差（相同/不同数 + 变化参数） |

## Hub 社区资源

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `hub_browse` | `search?`, `page?`, `perpage?`, `sort?`, `type?` | 搜索 VaM Hub（返回 id/title/作者/版本/类型/下载量/评分/tags） |
| `hub_detail` | `package?`, `resource_id?` | 资源详情：包名 + 所有版本 .var 文件 + 依赖 |
| `hub_download` | `package`* | 用游戏自带下载器下载安装（处理鉴权/依赖；免费内容匿名可用，付费需在 VaM 登录） |

## 皮肤

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `set_skin_sss` | `person?`, `color`* | 设置次表面反射色（`_SubdermisColor`；粉调如 `#FFC9C9` 显白皙） |

## 插件管理（VaM 原生插件）

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `list_plugins` | `uid?` | 插件列表（空 uid = CoreControl 会话插件；传人物 uid 看人物插件） |
| `add_plugin` | `path`*, `uid?` | 挂载 .cs/.cslist/.dll 插件，等待编译完成；加载后插件成为 storable，用 `list_storable_params`/`set_param` 配置（storable id 形如 `plugin#0_MVRPlugin.类名`） |
| `remove_plugin` | `plugin_uid`*, `uid?` | 移除插件 |

## 逃生舱

### eval_cs（默认关闭，`Security.AllowEval=true` 开启）
- 无 `;` → 表达式模式，返回表达式的值
- 含 `;` → 语句模式（需要值时用显式 `return`）
- `sc` 绑定 `SuperController.singleton`
- 受 VaM 运行时沙箱限制：System.IO / System.Reflection / System.AppDomain / UnityEditor / Mono.Cecil 禁用（注意 `GetType().Name` 会引用 System.Reflection，用 `ToString()` 代替）
