# 工具参考（73 个）

*[English](TOOLS.md)*

所有工具通过 `tools/call` 调用：`{ "name": "...", "arguments": { ... } }`。
参数标 `*` 为必填、`?` 为可选。`person` 省略时默认使用场景中第一个 Person。数值数组均为 `[x, y, z]`。

`eval_cs` 仅在 `Security.AllowEval=true` 时注册，所以默认安装暴露 72 个工具。

## 状态与会话

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `status` | — | VaM 运行时状态：插件版本、VaM 版本、原子/人物数量、端点、eval 开关 |

## 场景

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `list_scenes` | `search?` | 列出 `Saves/scene` 下的场景 |
| `load_scene` | `path`*, `merge?` | 加载场景（`merge=true` 合并进当前场景）；支持 .var 包路径（`Package.1:/Saves/scene/x.json`）。加载在随后几秒内完成——再次加载前用 `atom_info` 轮询到 `loading=false` |
| `new_scene` | — | 新建空场景（**丢弃当前场景**；空场景无默认灯光，记得打灯/开环境光） |
| `atom_info` | — | 加载器诊断：`atoms` 字典 / `atomsList` / `startingAtoms` 数量、`loading` 标志，以及所有已销毁、uid 重复、不在字典中、不在列表中的条目。`list` > `dict` 正是 VaM 报 "Destination array was not long enough" 并卡死加载前的致命状态 |
| `loader_heal` | `clearLoading?` | 无需重启 VaM 修复加载器卡死：按 `atoms` 字典重建 `atomsList`、清理失效的 `startingAtoms`、清除卡住的 `_isLoading`。注意该修复不会取消已中断的加载协程：除非愿意承担风险，下一步加载前请重启 VaM |
| `save_scene` | `path?` | 保存场景（默认 `Saves/scene/VaMMCP_<时间戳>.json`；覆盖已存在文件会自动先删后存） |
| `press_key` | `key`*, `times?`, `focus?` | 向 VaM 发送真实按键（Escape 可关闭保存截图对话框等模态提示）；绝不抢焦点——仅当 VaM 已持有焦点时发送，否则拒绝（`focus=true` 显式允许把窗口提到前台） |
| `ui_hud` | `action`* | `hide`/`show` VaM 主 HUD（启动器面板：Hub、场景浏览器、Create）。`hide` 让截图透过面板看到场景视图，同时 VaM 保持非前台 |

## Atom

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `list_atom_types` | — | 可创建的原子类型（按分类，来自 VaM 运行时数据） |
| `list_atoms` | `type?`, `query?`, `detail?`, `fields?`, `exclude?`, `include_off?`, `limit?`, `offset?`, `sort?`, `precision?` | 精简的 atom 列表。`detail` 选择列预设（`ids`、`placement`（默认：uid/type/name/on/pos）、`contents`、`health`、`full`）；`fields` 用显式列覆盖（`uid,type,name,on,pos,rot,scale,bbox,size,children,renderers,colliders,rigidbodies,storables,subScene,asset,sims,controls,worn,hair,torn`）；`exclude` 再减去。`query` 匹配 uid/名称/storable id。向量按 `precision` 位小数取整（3 = 毫米）。返回 `total`/`returned`/`hasMore`，配合 `offset` 分页不会含糊 |
| `list_objects` | `type?`, `query?`, `detail?`, `fields?`, `exclude?`, `bbox?`, `include_off?`, `limit?`, `offset?`, `sort?`, `precision?` | 与 `list_atoms` 相同的投影/分页，另加 `bbox` —— 六个数 `[center.xyz, size.xyz]`。**默认不再计算包围盒**（每个 atom 四组数组让每次列表都很贵）；需要范围时用 `fields=...,bbox` 或 `bbox=true` |
| `add_atom` | `type`*, `uid?` | 创建原子（Person、Light、AnimationPattern 等），等待其存在（按 uid 轮询，60s 超时） |
| `remove_atom` | `uid`* | 删除原子 |
| `set_atom_on` | `uid`*, `on`* | 显示/隐藏 |
| `get_atom_transform` | `uid`* | 世界坐标/欧拉角/缩放 |
| `set_atom_transform` | `uid`*, `position?`, `rotation?`, `scale?`, `relative?` | 变换（`relative=true` 为相对位移） |

## 人物

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `add_person` | `uid?` | 添加人物（uid 可能被 VaM 重命名，以返回值为准），最长等待 90s |
| `list_persons` | — | 人物列表（uid/角色名/性别/位置），并附每人的已穿服装（`worn` 显示名 + `wornCount`）——用于发现换角色后仍穿着原来源 outfit 的人 |
| `list_looks` | `search?` | 外观预设列表（`Saves/Person/Appearance`） |
| `load_look` | `path`*, `person?` | 应用外观预设（角色/服装/发型/morph 增量；异步，稍后验证） |
| `save_look` | `path?`, `person?` | 导出外观预设（**注意**：VaM 的预设保存不含完整材质参数，跨场景搬运后需按需补设皮肤参数） |
| `set_character` | `person?`, `name`* | 切换基础角色网格（名称见 `get_param storable=geometry param=characterSelection` 的 choices）；异步加载 |

## 捏人（Morph）

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `morph_action` | `person?`, `action`*, `name?`, `value?`, `region?`, `search?`, `group?`, `limit?` | `list`（名称/uid/区域/分组/当前值，可用 `search`/`region`/`group`/`limit` 过滤）、`set`（`name`、`value`，典型范围 -1..1）、`get`（`name`）、`reset`（全部归零，或只归零某个 `region`） |

## 服装 / 发型

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `clothing_action` | `person?`, `action`*, `path?`, `uid?`, `on?`, `search?` | `list_presets`（磁盘 **加递归扫描所有包** 的 `Custom/Clothing`）、`list_items`（实际穿着项：uid/名称/active）、`load_preset`（`path`）、`add_item`（`path`：优先激活库中已有项目，否则等待 VaM 确认已穿上，无法添加时返回 `ok:false` 与提示）、`remove_item`、`set_item_on`（`on`）、`open_tab`。另有 `list_item_morphs` / `set_item_morph` 处理服装 morph。条目动作按以 `id` 传入的条目 uid 匹配（取自 `list_items`；schema 里的 `uid` 字段不会被读取），删除延迟到帧末 |
| `hair_action` | `person?`, `action`*, `path?`, `uid?`, `on?`, `search?` | 发型（`Custom/Hair`）：`list_presets`、`list_items`、`load_preset`、`add_item`（依次按 uid == path、`dynamicRuntimeLoadPath`、唯一显示名匹配已安装发型，等待发型状态稳定后用 `SetActiveHairItem` 切换）、`remove_item`、`set_item_on`（`on`，条目匹配与服装相同，用 `id`） |

## 姿态 / 表情

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `pose_action` | `person?`, `action`*, `path?`, `name?` | `list`（姿态预设，`Saves/Person/Pose`）、`load`（`path`，应用到指定人物）、`save`（`path?`，姿态+物理）、`savefull`（全量：外观+姿态+物理 → `Saves/Person/full/`） |
| `expression_action` | `person?`, `action`*, `name?`, `value?` | `list`（表情 morph，启发式过滤）、`set`（`name`，`value` 默认 1；默认先清空其他表情） |

## 骨骼控制

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `list_controls` | `person?` | FreeControllerV3 控制点（headControl / chestControl / lHandControl / …） |
| `get_control` | `person?`, `control`* | 控制点世界位置/欧拉角 |
| `set_control` | `person?`, `control`*, `position?`, `rotation?` | 移动/旋转控制点（状态需为 `On`——见 `control_pose`） |
| `set_control_state` | `person?`, `control`*, `positionState?`, `rotationState?` | 设置控制点的 `positionState`/`rotationState`：On/Off/Follow/Hold/Lock/ParentLink/PhysicsLink/Comply（rotationState 还支持 LookAt） |
| `control_pose` | `person?`, `control`*, `position?`, `rotation?` | 一次调用：先把两个状态都设为 `On`，再同时设置位置与旋转 |
| `person_teleport` | `person?`, `position`*, `rotation?`, `reset?` | 瞬移整个人物：先捕获并关闭碰撞参数、无强制地写 hipControl（无补间）、恢复状态选择器与碰撞值，可选核心 ResetSimulation（`reset` 默认 true，约 10s）让布料/软体按新位置重新求解 |
| `person_point_at` | `person?`, `target?`, `targetControl?`, `hand?`, `up?` | 旋转 `lHandControl`/`rHandControl`（状态强制 On），用 LookRotation 指向 `target` `[x,y,z]` 或 `targetControl` 指定的原子。位置不变——配合 `set_control` 伸展手臂。`hand` 为 Left、Right 或 Both（默认） |
| `set_gaze` | `person?`, `amount?`, `targetControl?` | 驱动原生 `eyeTargetControl`：`"Camera"`/原子 uid = 看向它（位置状态强制 On），`"None"` = Off（回到姿态驱动）。`targetControl` 实际必填（尽管 schema 标为可选）。VaM 眼睛会完全跟随目标，`amount` 小于 1 会被忽略（响应里有说明）；原版 Person 上没有 `lookAt` storable |

## 通用参数（长尾全覆盖）

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `list_atom_storables` | `uid`* | 原子全部 storable（含类型） |
| `list_storable_params` | `uid`*, `storable`* | 参数内省：floats（含 min/max/default）/bools/strings/urls/choosers/colors/actions/customParams/properties。url 参数包含自定义贴图槽（`customTexture_MainTex`、`customTexture_AlphaTex` 等） |
| `get_param` | `uid`*, `storable`*, `param`* | 读参数（float/bool/string/url/chooser/color 自动识别，兜底公共属性） |
| `set_param` | `uid`*, `storable`*, `param`*, `value`*, `type?`, `reload?` | 写参数（颜色支持 `#RRGGBB` 或 `r,g,b,a`；url 参数设置文件/图片路径并默认重新加载，传 `reload=false` 跳过；未知参数+数值按 float 兜底，否则报清晰错误） |
| `call_action` | `uid`*, `storable`*, `action`* | 触发 storable 的动作（按钮） |

`type` 可强制指定：`float|bool|string|url|chooser|color|property`（`property` 走反射读写公共属性，覆盖 customParamNames 如 `gravityX`）。

## 相机

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `camera_action` | `action`*, `camera?`, `editor?`, `position?`, `rotation?`, `uid?`, `target_uid?`, `person?`, `control?`, `mode?`, `fov?`, `width?`, `height?`, `path?`, `return_image?` | 相机 `get` / `set` / `aim` / `capture` / `mcp_create` / `mcp_get` / `mcp_set` / `mcp_capture` |

相机细节：

- 默认所有动作都驱动 VaMMCP **自有**相机（uid `mcp_camera`），绝不移动用户的编辑器视图。传 `camera="editor"`（或 `editor=true`）才读写/对准编辑器（监视器）相机；`mcp_create`/`mcp_get`/`mcp_set`/`mcp_capture` 是自有相机的显式别名。
- `aim` 对准原子 `uid`（或 `target_uid`），或 `person` 加其 `control`（默认 `hipControl`），也可先用 `position` 移动相机。优先用它代替手算 yaw：Unity yaw-180 视角是镜像的（屏幕右 = -X）。
- 截图模式：`mcp`（默认——自有相机走正常帧循环渲染）、`editor`（监视器相机走帧循环渲染，遵守 `width`/`height`）、`window`（抓取呈现帧的后备缓冲，失败时自动回退到屏幕抓取；此时 `width`/`height` 即窗口尺寸）。默认输出 `Saves/PluginData/vam-mcp/preview.png`。
- 所有截图路径结束后都会恢复相机状态（target texture、enabled 标志、临时 RenderTexture），并在 `via` 字段报告实际出图路径：`mcp-render`、`monitor-render`、`backbuffer`、`screen` 或 `none`。
- `return_image=true` 时同时以 MCP image 块返回 PNG——分辨率别开太大（如 640×360），超过 4 MB 只落盘不内联。

## 模拟

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `simulation_action` | `action`*, `paused?`, `timeScale?` | `set`（`paused` 和/或 `timeScale`）、`reset`（核心 ResetSimulation；消除软体/布料抖动） |

## 动画

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `animation_action` | `action`*, `pattern_uid?`, `step_uid?`, `name?`, `time?` | `list_patterns`、`list_steps`（`pattern_uid`）、`create_pattern`（AnimationPattern 原子）、`create_step`（`pattern_uid`，AnimationStep 带 `transitionToTime`；存在自动链接动作时挂到 pattern 上）、`record`（开始/停止场景录制；会给出解释错误：VaM 自带控制器录制仅 UI 可用）、`pattern` / `step`（pattern 或 step 动作，如 Play/Stop） |

## 触发器

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `trigger_action` | `action`*, `uid?`, `name?` | `list`（带名称含 trigger 的 storable 及其参数/动作）、`create`（创建 UIButton 触发原子；用 `uid` 指定名字——`name` 不参与 create），或任意触发动作：`Fire` 脉冲 `trigger.active` true→false，与 VaM 按钮处理器完全一致（没有配置接收动作也不报错），`SetFloatParam`/`SetBoolParam` 写 `param`，其他名字按动作调用。`uid` 支持原子 uid 或 `原子/Trigger`；默认 storable 为精确 id `Trigger` |

## 物理

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `physics_action` | `action`*, `uid?`, `name?` | `list_objects`（原子上的刚体）、`create_joint`（两个原子间的旧式 Unity 关节），或物理对象动作：`SetMass`、`SetDrag`、`AddForce`（冲量幅值，默认 +Y；模拟必须醒着——IdleSaver 冻结时不动）。缺失时自动给 mainController 加 Rigidbody |

## 灯光 / 音频

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `light_action` | `uid?`, `action`*, `color?`, `value?` | `list`（灯光及参数快照）或灯光动作：`Toggle`、`SetColor`（`#RRGGBB`）、`SetIntensity`、`SetRange`、`SetSpotAngle`、`SetType`（数值或类型词放在 `value`） |
| `audio_action` | `uid?`, `action`*, `name?`, `value?` | `list_sources`（含 `playingClip`、`playingClipReady`）、`list_clips`（内嵌 + URL 剪辑，带类别与 `ready`），或音源动作：`SetClip`、`Play`、`PlayLoop`、`Pause`、`UnPause`、`TogglePause`、`Stop`、`StopLoop`、`SetVolume`、`SetLoop`、`ClearQueue`。`SetClip` 在插件内解码本地 PCM WAV；ogg/mp3 走 VaM URL 剪辑队列 |

## 用户偏好

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `get_user_preferences` | `category?` | 当前 UserPreferences（图形、性能、VR、导航、安全等） |
| `set_user_preferences` | `key`*, `value`* | 写入一项偏好（如 `renderScale`、`vrMirrorMode`、`autoSaveInterval`）并保存 |

## 场景浏览器 / 模板

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `scene_browser` | `action`*, `path?` | `List`、`Load`、`New`、`Save`、`Delete`（`path`；**删除**需 `confirm: true`——场景文件不可恢复） |
| `template_browser` | `action`*, `category?` | `List`（各模板类别的数量）、`Load`（`category`：look、pose、scene、clothing 或 hair） |

## 原子 UI

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `atom_action` | `uid`*, `action`*, `target_uid?`, `value?` | 控制：`Select`（对主控制器调用 SuperController.SelectController）、`FreezePhysics`/`UnfreezePhysics`（控制器位置/旋转状态 Freeze↔Off；`FreezePhysics` 传 `value=false` 即解冻）、`DisableCollision`/`EnableCollision`、`DisableRender`/`EnableRender`（映射到原子电源开关）、`EndIsolate`、`LoadUserDefaults`（mainPresetControl.LoadUserDefaults——仅在存在*用户已保存的默认预设*时生效，否则 `loaded:true` 无变化）。移动：`SetPosition`/`SetRotation`/`SetScale`（`value` 为向量）、`SnapToGround`、`AlignTo`（`target_uid`）、`ResetTransform`、`ParentTo`（`value` = 父原子 uid，如 SubScene）、`Unparent`，另有 `WheelCreate`（`value` = `pivotUid|atomUid;atomUid;…`）、`WheelAngle`、`WheelStatus` 用于整组刚性旋转 |
| `atom_geometry_choices` | `uid`*, `action`*, `choice?` | 有几何选项的原子（CustomUnityAsset）：`List`（选择器 + 候选项）、`Select`（`choice`） |

## 人物动画

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `person_animation` | `person?`, `action`*, `value?` | 作用于人物身上所有 `…Animation` storable：`ArmRecord`/`RecordAnimation`（录制动作或 bool 参数）、`PlaybackEnabled`（`value` true/false）、`PlayAnimation`、`StopAnimation` |

## 自定义 Unity 资源（CUA）

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `cua_action` | `uid?`, `action`*, `name?` | `list`（CUA 原子及 assetUrl/assetName/loaded），或对原子的 asset storable 执行 `SetAssetUrl`、`SetAssetName`、`LoadAsset`、`UnloadAsset`、`ReloadAsset` |

## 包管理

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `package_manager` | `action`*, `package?` | `ScanPackages`/`RefreshPackages`（调用 VaM 暴露的第一个扫描 API）、`ListEnabled`/`list`、`DeletePackage`（`package`；需 `confirm: true`——.var 文件不可恢复）。逐包 `EnablePackage`/`DisablePackage` 在 VaM 里仅 UI 可用，会返回说明错误 |

## 触发器 UI

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `trigger_ui` | `trigger_uid`*, `action`* | 调用 Trigger storable 上匹配的动作来 `Show`、`Hide` 或 `Refresh` 触发器面板 |

## 手部控制 / 材质

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `person_hand_control` | `person?`, `hand`*, `action`*, `target_uid?`, `value?` | 手部控制点（`hand` 为 left/right）：`SetTarget`（原子 uid 或 `"Camera"`）设置手的 `lookAt` 并把 rotationState 切到 `LookAt`；`SetRotation`（`value` 为欧拉角数组）走 `SetRotationNoForce`；`Release` 把 rotationState 恢复 Off。原版 FreeControllerV3 没有 IK 权重参数，所以 `SetWeight`/`AutoGrasp` 会带解释报错 |
| `person_material` | `person?`, `material`*, `action`*, `param?`, `value?` | 按名称或子串匹配材质 storable（skin、genitals、eyes、mouth、nails 等）：`ListParams`、`GetParam`、`SetParam`（`param`、`value`）——通用参数工具的薄封装 |

## 场景状态 / 诊断

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `scene_state` | `sections?`, `uid?`, `detail?`, `query?`, `type?`, `fields?`, `limit?`, `offset?`, `precision?` | 场景总览：计数、按类型普查、每个角色的状态（`controls` = `[On, Off, other]`、`sims` = 开启/总数、穿着/头发数量、torn 标记）以及 `warnings`。`sections` 按需追加：`atoms`、`health`、`plugins`、`physics`、`cameras`、`performance`、`animation`、`controls` 或 `all`。传 `uid=<uid>` 则改为该 atom 的**档案**：各 storable 及参数计数（`f`/`b`/`c`/`s`）、控制点 id/状态/位置、sims、插件实例与资源 `urls`。`detail=full` 打开较重的分区；旧的 section 名（`persons`、`plugins`、`physics`、`diagnostics`）仍可用 |
| `atom_tree` | `root_uid?`, `max_depth?`, `query?`, `components?`, `limit?`, `atoms_limit?`, `detail?`, `precision?` | 层级以**嵌套 `children` 数组**返回（`name`，根节点另带 `pos`），不是扁平列表。组件是可选（`components=true`）——每节点的组件字符串曾是最大的无用输出源。`query` 只保留包含该名称的子树；`limit` 限制每节点子项、`atoms_limit` 限制 atom 数、`max_depth` 1..8（默认 2）；`detail=summary` 等于深度 1 且不带组件 |
| `person_state` | `person?`, `sections?`, `detail?`, `morph_limit?`, `item_limit?`, `include_hidden?`, `precision?` | 按分区查询一个角色：`character`、`morphs`、`clothing`、`hair`、`controls`、`control_rows`、`sims`、`expressions`（或 `all`）。列表有上限（`morph_limit` 默认 10 / full 40，`item_limit` 默认 20 / full 200），且响应总带有真实总数和 `morphsTruncated`/`truncated` 余量，被截断的列表不会被误认为完整。控制点逐行（id、位置状态、位置）需显式打开 |
| `physics_state` | `detail?`, `limit?` | 仿真时间（时间缩放、固定步长、fps）加计数摘要：物理对象、物理 storable、已开启的 sim。逐 atom 列表需 `detail=objects`（或给 `limit`），因为它会占满整个响应 |
| `simulation_performance` | — | 帧计数与各 delta、timeScale、vSync、GC 堆与原子数 |
| `find_atoms` | `name?`, `type?`, `component?`, `property?`, `value?`, `tag?`, `detail?`, `fields?`, `exclude?`, `limit?`, `offset?`, `sort?`, `precision?` | 按 uid/名称子串、atom 类型、组件类型名或参数名（可带 `value`）做与（AND）过滤。结果是投影（`detail`/`fields`）并带 `total`/`returned`/`hasMore`；`limit` 默认 50。`tag` 是 VaM 运行时不支持的，会带提示报错 |
| `storable_diff` | `uid_a`*, `storable_a`*, `uid_b`*, `storable_b`*, `param?`, `limit?` | 比较两个 storable：相同/不同计数、变化的参数、只在一侧存在的参数。`param` 只保留匹配的参数名，`limit` 限制行数（默认 100）并给出 `truncated` 计数 |
| `scene_dependencies` | — | 扫描 url 参数中的包路径，标记 .var 未安装且文件不存在的引用 |
| `performance_profile` | `duration_ms?` | 短时性能采样（限制 250–30000 ms）：帧数、平均/最小/最大帧时间、FPS 估计、卡顿时间、GC 堆。跑在 HTTP worker 线程上轮询实时帧计数，不占用主线程调度器 |

## Hub 社区资源

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `hub_action` | `action`*, `search?`, `package?`, `resource_id?`, `page?`, `perpage?`, `sort?`, `type?` | `browse`（`search`/`page`/`perpage`/`sort`/`type`；id/title/作者/版本/类型/下载量/评分/tags）、`detail`（`package` 或 `resource_id`；所有版本 .var + 依赖）、`download`（`package`，走 VaM 自带下载器，处理鉴权/依赖；免费内容匿名可用，付费需在 VaM 登录）。超时较长，轮询帧循环 |

## 皮肤

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `set_skin_sss` | `person?`, `color`* | 设置皮肤次表面散射色（皮肤材质的 `Subsurface Color` / `_SubdermisColor`；粉调如 `#FFC9C9` 显白皙） |

## 插件管理（VaM 原生插件）

| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `plugin_action` | `action`*, `path?`, `uid?` | `list`（`uid?`——空 uid = CoreControl 会话插件；传人物 uid 看人物插件）、`add`（`path`：挂载 .cs/.cslist/.dll 插件并等待编译；加载后插件成为 storable，用 `list_storable_params`/`set_param` 配置——storable id 形如 `plugin#0_MVRPlugin.类名`）、`remove`（`uid`：卸载插件） |

## 逃生舱

### eval_cs（默认关闭，`Security.AllowEval=true` 开启）
- 无 `;` → 表达式模式，返回表达式的值
- 含 `;` → 语句模式（需要值时用显式 `return`）
- `sc` 绑定 `SuperController.singleton`
- 受 VaM 运行时沙箱限制：System.IO / System.Reflection / System.AppDomain / UnityEditor / Mono.Cecil 禁用（注意 `GetType().Name` 会引用 System.Reflection，用 `ToString()` 代替）
