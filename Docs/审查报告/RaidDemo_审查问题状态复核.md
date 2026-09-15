# RaidDemo 审查问题状态复核（72 条 → 当前 HEAD）

> 对 [`RaidDemo_只读代码审查报告.md`](RaidDemo_只读代码审查报告.md) 的 72 条编号问题做一次
> **只读状态复核**：逐条判定"在当前代码上是否仍然成立"，并给出当前文件与行号证据。
> 复核期间未修改任何受控源码、配置、场景或资源。

## 一、复核元数据

| 项目 | 内容 |
| --- | --- |
| 被复核报告 | [`RaidDemo_只读代码审查报告.md`](RaidDemo_只读代码审查报告.md) |
| 原审查基线 | `84f37fd`（2026-09-14，M9 中期） |
| 复核时 HEAD | `7616783`（M9 后段 + M10 全部完成后） |
| 复核方式 | 只读：读代码、`rg` 定位、对照 `Docs/` 与包配置；不改代码、不跑构建 |
| 复核范围 | 报告全部 72 条编号问题（`RD-AUD-001` ~ `073`，其中 `009` 未使用） |
| 状态口径 | `仍存在` / `已修` / `口径变化`（行为已变、原结论不再准确，但也不算改对）/ `无法判定`（静态阅读看不出，需实机） |

**为什么要复核**：原报告自己写明"结论没有在落盘时对当前 HEAD 逐条复验"，而基线之后累计约
314 个文件、+31,734 / −13,828 行的改动。直接按旧结论动手，很可能修到已经不存在的代码。

---

## 二、汇总

### 2.1 本轮已修清单（2026-09-15）

复核完成后立刻执行了"第一档 + 联机权威链"两批修复（每批都带测试与真机/自动验收）：

| 批次 | 编号 | 交付与证据 |
| --- | --- | --- |
| 一 | `010` `027` `028` `029` `046` `047` `053` `070` `072` | 提交 `3a901bb`：EditMode **688 / 688**（新增 6 条测试，含体力恢复延迟、AI 穿透刻度、仓库满不吞战利品、非法输入校验、协议字节上限） |
| 二 | `041` `042` `043` `044` `045` `049` `051` `061` | 提交见仓库历史：服务器补战局超时与结算时钟、客户端不再自行结算、联机不写本地进度、医疗品改由服务器执行、结算数据全部以服务器为准；EditMode **692 / 692** |
| 二 · 真机 | `041` `042` `049` `051` | `Builds/raid-timeout-check.ps1` **5/5 PASS**：服务器按 `-raidDuration 40` 在 40 秒收尾并回屋；服务器日志 `结算：玩家 1 时间耗尽，带出价值 0，击杀 0，用时 40 秒`（用时 = 战局时长，而不是服务器已运行 54 秒 → `051` 有数值级证据）；客户端日志 `本局按服务器结算：TimeExpired …（本地进度不做改动）` |

> 真机验收还顺手抓到一个新缺陷：客户端把服务器下发的"时间耗尽"映射成了"阵亡"（结算面板会写错结论），
> 已修（`RaidSession.NotifyTimeExpired` + 三分支映射）并复验通过。这条记在 §7.4。

| 领域 | 条数 | 仍存在 | 已修 | 部分修复 | 口径变化 | 无法判定 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Session / Lobby / Network | 17 | 13 | 2 | 2 | 0 | 0 |
| Kernel / Data / Simulation / Inventory / Combat / Save | 17 | 15 | 0 | 0 | 2 | 0 |
| AI / Raid / Meta / Presentation / UI / Bootstrap / Editor | 20 | 17 | 0 | 1 | 2 | 0 |
| 架构 / 规范 / 依赖 / 文档 | 18 | 17 | 0 | 0 | 0 | 1 |
| **合计** | **72** | **62** | **2** | **3** | **4** | **1** |

状态口径补充：`部分修复` = 报告的原始结论已不成立，但同一处缺陷仍以较弱的形式存在（例如加了守卫、影响降级），
不能直接当"已修"划掉；`口径变化` = 行为已变、原结论不再准确，通常需要改的是**结论与文档**而不是代码。

---

## 三、Session / Lobby / Network（17 条）

复核人：主代理 ｜ 方式：读代码 + `rg` 定位当前实现

| 编号 | 原结论（≤20 字） | 现状 | 当前证据（文件:行） | 说明 |
| --- | --- | --- | --- | --- |
| RD-AUD-041 | 客户端本地结算、不等服务器 | **仍存在** | `Bootstrap/SceneBootstrap.RaidFlow.cs:34`、`Bootstrap/SceneBootstrap.Multiplayer.Raid.cs:95` | `UpdateRaid` 无联机守卫，本地 tick `RaidSession` 与 `ExtractionTracker`；读秒走满会本地走结算 |
| RD-AUD-042 | 服务端没有战局总时长超时 | **仍存在** | `Session/Network/ServerRuntime.Raid.Extraction.cs:106`、`:138`、`:168` | 服务端只生成 `Extracted` / `Killed` 两种结果；`TimeExpired` 仅存在于本地 `Modules/Raid/RaidSession.cs:96` |
| RD-AUD-043 | 联机结算污染单机存档 | **部分修复** | 已加守卫：`Bootstrap/RaidFlowController.Save.cs:107`；仍存在：`Bootstrap/SceneBootstrap.RaidFlow.cs:362`、`:380`、`Bootstrap/RaidFlowController.Pause.cs:80` | 联机期间不落盘（`if (MultiplayerClientSession.IsActive) return;`），但本地 `MetaProgress` 仍被改写，离开联机后任意一次 `SaveNow` 会把污染写回单机存档 |
| RD-AUD-044 | 联机战斗/医疗没接到服务端权威 | **仍存在** | `Bootstrap/SceneBootstrap.ItemUse.cs:27` | 使用完成只改本地 `m_CombatWorld`；`Session/Network` 下没有治疗/生命写入路径 |
| RD-AUD-045 | 服务端击杀数恒为 0 | **仍存在** | `Session/Network/ServerRuntime.Raid.cs:43`、`:232`；`Modules/Raid/RaidSession.cs:112` | 服务端只定义并下发 `progress.Kills`，全仓只有本地 session 做 `Kills++`；服务器击杀只推进任务（`ServerRuntime.Combat.cs:284`） |
| RD-AUD-046 | 非法槽位直接强转 | **部分修复（影响降级）** | 入口仍无校验：`Session/Network/ServerRuntime.Inventory.Commands.cs:249`、`:256`；兜底：`Shared/Commands/GameCommand.cs:322`-`:331` | `Get` 仍是 `m_Slots[(int)slot]` 直索引，但 `CommandRouter` 已把 `Execute` 包在 try/catch 里转成 `CommandResult.Fail(InternalError)` → 后果是"命令被拒 + 一条警告"，不是报告说的未捕获异常 |
| RD-AUD-047 | 移动/朝向无 NaN 与范围校验 | **仍存在** | `Session/Network/ServerRuntime.Movement.cs:150`-`:153` | 直接拿消息分量构造 `PlayerMoveIntent`，没有有限性或范围检查 |
| RD-AUD-048 | 登录无限速/无退避 | **仍存在** | `Session/Network/ServerRuntime.Lobby.Requests.cs`（`HandleLobbyLogin`）；`Session/Lobby/ServerIdentityStore.cs` | 登录处理器没有尝试计数与退避；每次登录同步执行 PBKDF2（10 万次） |
| RD-AUD-049 | 结算界面不用服务端字段 | **仍存在** | `Bootstrap/SceneBootstrap.Multiplayer.Raid.cs:27`、`:75`；`Bootstrap/SceneBootstrap.RaidFlow.cs:349` | `LocalOutcome` 只有写入、没有读取方；结算仍用本地 `RaidResult.Create(evt.Kills, evt.ElapsedSeconds, …)` |
| RD-AUD-050 | 全员结算后回不到房间 | **已修** | `Session/Lobby/MultiplayerClientSession.Lobby.cs:241` | `RaidEndMessage` → `ReturnFromRaid(sceneName)`，P4.5-b 的"全员回共享安全屋"已接线 |
| RD-AUD-051 | 本局用时用的是服务器运行时长 | **仍存在** | `Session/Network/ServerRuntime.Raid.cs:221`；`Modules/Simulation/Player/MovementServerWorld.cs:331` | `elapsed = m_World.SimulationTime`；该时钟只在 `InitializeMovement()` 建世界时归零，进图/重开都不重置 |
| RD-AUD-052 | 同一 tick 重复结算并重复广播 | **已修** | `Session/Network/ServerRuntime.Raid.Extraction.cs:50`、`:165`、`:177`；`ServerRuntime.Raid.cs:155` | 四条 `Settled` 守卫（其中一条注释直接引用 U-86 的时序兜底）覆盖了被重复处理的路径 |
| RD-AUD-053 | 协议字段按字符校验、中文会溢出 | **仍存在** | `Session/Lobby/LobbyProtocol.cs:161`、`:193` | 房间名上限 24 个**字符**，而 `RoomName` 是 `FixedString64Bytes`（可用 61 字节）；24 个汉字约 72 字节 |
| RD-AUD-054 | 换包失败只回滚最后一件 | **仍存在** | `Bootstrap/SafeHouseBootstrap.Inventory.cs:133`-`:137`；`Bootstrap/SceneBootstrap.Inventory.cs:139` | 失败时只把当前件的 `Rotated` 还原后 `return`，之前 `AutoPlace` 成功那些件的朝向已经写进旧网格 |
| RD-AUD-055 | 队友离开后残留幽灵视图 | **仍存在** | `Session/Network/MultiplayerMovementLink.Snapshots.cs:181`、`:251` | `ClearRemoteViews()` 只在 `NotifyDisconnected()`（整条连接断开）时调用；单个玩家离开没有清理路径 |
| RD-AUD-056 | 服务器拒绝装备后不下发权威状态 | **仍存在** | `Session/Network/ServerRuntime.Inventory.Commands.cs:261`-`:265` | 拒绝分支只写一行警告就 `return`，文件里自己的注释也写着"纠正通路留给 P5" |
| RD-AUD-057 | 状态页每请求枚举网卡 + 单线程串行 | **仍存在** | `Session/Network/ServerRuntime.Status.cs:48`；`Session/Lobby/Dashboard/ServerDashboard.Http.cs:19` | 每次构建状态快照都调用 `ServerAddressReporter.EnumerateLanIPv4()`；HTTP 侧是单线程 `AcceptLoop` |

**本领域小结**：17 条里 **13 条仍存在**、**2 条已修**（`050` 全员回屋链路、`052` 结算时序守卫）、
**2 条部分修复**（`043` 联机期间已落盘闸门但内存仍被改写、`046` 入口仍无校验但异常有兜底）。
值得注意的是 `043` 的闸门与 `052` 的守卫这两条**只修了"最危险的那一半"**，
剩下的一半正好是"离开联机之后才会发作"的类型，验收时容易漏。

---

## 四、Kernel / Data / Simulation / Inventory / Combat / Save（17 条）

复核状态：**17 条全部完成**。方式：读代码 + `rg` 定位 + `git log -S` 溯源（用于判断"原结论是否本就误判"）。

| 编号 | 原结论（≤20 字） | 现状 | 当前证据（文件:行） | 说明 |
| --- | --- | --- | --- | --- |
| RD-AUD-010 | 体力恢复比配置多 1 秒 | **仍存在** | `Modules/Simulation/Player/PlayerMovementSimulator.cs:22`、`:166`、`:217`、`:232` | 计时器初值与两处重置值都是 `-1f`，恢复分支又先 `+= deltaTime` 再与延迟比较 → 实际是"配置 + 1 秒"：常规 1.5→2.5、力竭 3→4。设计文档写的是 1.5 / 3；测试用宽裕时间把偏差吞掉了，没有固化正确值 |
| RD-AUD-011 | 物品运行时状态没绑到实例 | **仍存在** | `Modules/Data/Items/ItemInstance.cs:63`、`:163`、`:181`；`Modules/Combat/PlayerWeapon.cs:33`、`:95`；`Modules/Combat/CombatantState.cs:152`；`Modules/Meta/MetaSaveMapper.cs:47` | 四条子结论全部成立：`ItemInstance.State` 仍无生产调用方（注释自称"M2 没有调用方"）、`Split` 不复制状态、武器弹匣以 `IWeaponStats` 资产为键（同型共享）、`SetArmor` 重置耐久、存档不含 `ItemState` |
| RD-AUD-012 | 联机弹药 HUD 不读服务器权威 | **口径变化** | `Bootstrap/SceneBootstrap.Multiplayer.Combat.cs:161`、`:228`；`Modules/UI/Combat/CombatHud.cs:256` | 基线之后（`c8fcb31`）新增了 `ApplyLocalMagazineAmmo`：服务器开火/换弹事件带回 `MagazineAmmo`，客户端写回本地 runtime → "事件不带弹匣余量"已不成立。残留：装备变更时是否也下行弹匣、弹挂容器广播覆盖度需实机确认 |
| RD-AUD-013 | 超预算的累加时间没丢弃 | **仍存在** | `Modules/Simulation/Player/MovementServerWorld.cs:226`-`:235`（类注释在 `:37`） | 注释写"宁可丢弃多余的时间"，但循环退出后 `m_StepAccumulator` 只减掉已执行的步数，超预算部分留在累加器里下一帧继续补 —— 防补帧螺旋只做了一半 |
| RD-AUD-014 | 对账只比位置，其他状态不回滚 | **仍存在** | `Modules/Simulation/Player/MovementPredictionBuffer.cs:143`、`:150`；快照字段 `PlayerMovementSimulator.cs:176`-`:182` | 权威快照带体力、力竭标记、恢复计时器与朝向，但 `Reconcile` 只用位置算误差；朝向与体力偏差不会被发现（`Replay` 本来会恢复快照，问题在"发现不了"） |
| RD-AUD-015 | 传送不清已应用输入 | **仍存在** | `Modules/Simulation/Player/MovementServerWorld.cs:186`-`:189`、`:320`、`:324` | `TryTeleport` 清了 `PendingInputs`、归位了序号，但没清 `AppliedInput`；`StepOnce` 无新输入时继续沿用上一条，重开后玩家会沿旧方向多走一步。唯一调用方是重开流程 `ServerRuntime.Raid.Restart.cs:94` |
| RD-AUD-016 | 跨网格没有实例唯一性约束 | **仍存在** | `Modules/Inventory/InventoryGrid.Placement.cs:73`-`:95`；`InventoryGrid.cs:214` | `Place` 只查"本网格是否已含该实例"，没有全局注册表；`ItemInstance` 的注释还明确写着"不使用全局实例注册表"，同一实例进两个网格不会被任何一层拦住 |
| RD-AUD-017 | 嵌套容器深度硬编码、无调用方 | **仍存在** | `Modules/Inventory/ContainerRegistry.cs:221`-`:236` | 新网格固定 `depth: 1`；全仓（含 Tests）除定义外无 `GetOrCreateNested` 调用方，嵌套容器仍是半成品 |
| RD-AUD-018 | 存档主档替换不是原子操作 | **仍存在** | `Kernel/Unity/SaveFileStore.cs:136`-`:152` | 流程是"写临时档 → 读回校验 → 旧档备份 → `File.Copy(临时档, 主档, overwrite: true)`"；最后一步是覆盖写而非原子重命名，中途崩溃会留下截断主档（有备份档可人工恢复，但没有自动回滚） |
| RD-AUD-019 | 命令路由的强制转换在 try 之外 | **仍存在** | `Shared/Commands/GameCommand.cs:322`（`try` 从 `:325` 开始） | 处理器按 `CommandType` 字符串注册；若同一 key 注册成不匹配的 `ICommandHandler<TCommand>`，第 322 行的强转会抛 `InvalidCastException`，不转成 `CommandResult`，与"统一返回 CommandResult"的承诺不符 |
| RD-AUD-020 | 对象池允许同一对象重复归还 | **仍存在** | `Kernel/Pure/ObjectPool.cs:107`-`:121`、`:174`-`:176` | `Release` 只判 null 与已释放，没有"已在池中"标记或代际号；同一对象还两次会被压栈两次，之后 `Get` 两次拿到同一实例（`Scope.Dispose` 同样没有二次归还保护） |
| RD-AUD-021 | 覆盖注册不释放旧服务；工厂返回 null 不拒 | **仍存在** | `Kernel/Pure/ServiceLocator.cs:40`-`:61`、`:66`-`:85`、`:116`-`:127` | `overwrite: true` 直接覆盖且不 `Dispose` 旧实例；`RegisterLazy` 只校验工厂非 null，`TryGet` 把工厂返回值直接缓存并返回 true → 工厂返回 null 时调用方拿到"成功 + null" |
| RD-AUD-022 | 时间服务没有消费者、timeScale 直写 | **仍存在（数字变大）** | `Kernel/Unity/TimeService.cs:17`、`:48`、`:112`；`Bootstrap/RaidFlowController.cs:132` 等 **19 处** | `ITimeService` 全工程无消费者；直写 `Time.timeScale` 已从报告的约 14 处增至 **19 处**（`RaidFlowController` 9、`Pause` 3、`Multiplayer.Scenes` 3、`CharacterSelect` 2、`Multiplayer` 1、`Save` 1） |
| RD-AUD-023 | Sort 会静默丢弃找不到坐标的物品 | **仍存在** | `Modules/Inventory/InventoryGrid.Transfer.cs:276`-`:292`；`InventoryGrid.cs:326`-`:330` | `Sort` 只把 `TryGetOrigin` 成功的物品收进快照，随后 `ClearAll()` 清空物品与占用表；占用表损坏时"找不到坐标"的物品根本不在回滚快照里，`RestoreLayout` 也救不回来 |
| RD-AUD-024 | 内容校验放行部分非法参数 | **部分成立（一处是原报告误判）** | `Modules/Data/Content/WeaponStats.cs:167`、`:194`、`:209`；`ArmorStats.cs:55`-`:69`；`DamageCalculator.cs:71`；`PlayerMovementProfile.cs:172`-`:205` | "未拒绝负散布"**不成立**：`Validate` 自 `30762e2`（M3）起就检查负散布与最大/最小关系。仍成立：散布无 NaN 检查（NaN 比较恒 false 可绕过三个判断）、`WearFactor == 0` 被当"用默认值 0.35"、`PlayerMovementProfile.Validate` 不校验加速度与体力参数 |
| RD-AUD-025 | 初次装弹与换弹的穿透力来源不同 | **仍存在** | `Modules/Combat/PlayerWeaponController.cs:154`-`:158`、`:319`、`:335` | 装备武器时用 `AmmoReserve.PeekPenetration(背包)` 定穿透力，换弹时用 `AmmoReserve.Consume(弹药挂)` 的穿透力；弹挂与背包装不同型号同口径弹时，同一个弹匣威力前后不一致 |
| RD-AUD-026 | 瞄准注释与实现不符、失败路径重复告警 | **仍存在** | `Modules/Input/Runtime/PlayerInputCollector.cs:321`-`:346`；`PlayerInputCollector.Aiming.cs:14`-`:34` | 动作表缺失时 `Initialize` 打一条警告就返回且不置 `m_IsInitialized`，而它被多个意图入口反复调用 → 配置错误时重复告警；瞄准注释描述"射线与地面求交"，实现是平面求交，措辞仍不一致 |

**本领域小结**：17 条里 **15 条仍存在**、**2 条口径变化**（`012`、`024`——都是"行为已变 / 原结论部分不成立"）、0 已修、0 无法判定。
其中 `010`、`011` 是"改一处就见效"的小改动；`024` 的"未拒绝负散布"是**原报告误判**，修的时候应剔除。
需要实机确认的三条：`012`（弹挂容器广播）、`016`（真实流程能否把同一实例放进两个网格）、`023`（占用表损坏是否真发生过）。

---

## 五、AI / Raid / Meta / Presentation / UI / Bootstrap / Editor（20 条）

复核状态：**20 条全部完成**（P1 两条 027 / 028 由 P1 专项复核产出，其余由主代理复核）。

| 编号 | 原结论（≤20 字） | 现状 | 当前证据（文件:行） | 说明 |
| --- | --- | --- | --- | --- |
| RD-AUD-027 | AI 穿透力量纲错误 | **仍存在** | `Modules/AI/AiDirector.cs:125`；`Modules/Combat/DamageCalculator.cs:64` | `WeaponPenetration = 0.45f`，公式是 `Clamp01((Penetration - 护甲) / 护甲值)`；玩家弹药穿透为 12~22、护甲最低 10 → AI 穿甲系数恒 0 |
| RD-AUD-028 | 仓库满时撤离入库会吞战利品 | **仍存在** | `Modules/Meta/MetaProgress.cs:327`、`:332`、`:338` | `DrainGrid` 先 `grid.Remove(item)` 再 `Stash.AutoPlace(item)`，失败只 `failed++` → 物品永久销毁。（装备槽那条路是安全的：`Unequip` 先判容量） |
| RD-AUD-029 | 带出价值在入库前就算好了 | **仍存在** | `Modules/Raid/RaidResult.cs:52`；`Bootstrap/SceneBootstrap.RaidFlow.cs:349`、`:362` | `ExtractedValue` 在构造结果对象时求和，真正入库在之后 → 仓库满被丢弃的物品仍计入带出价值与任务进度（与 `028` 同源） |
| RD-AUD-030 | 诊断面板与真实 AI 的视线高度不同 | **仍存在** | `Modules/Diagnostics/AiDetectionDiagnostics.cs:159`；`Modules/AI/AiAgent.cs:344` | 诊断用两参 `ToEyePosition(pos, eyeHeight)`（地面高度恒 0），真实 AI 用三参版本 → 盆地/平台地图上调试结论可能与实际不符 |
| RD-AUD-031 | 医疗读条期间可把物品搬走白嫖回血 | **仍存在** | `Bootstrap/SceneBootstrap.ItemUse.cs:27`-`:33`、`:48`；`Modules/Raid/ItemUseInteraction.cs:69` | 完成时先回血再 `ConsumeOne`，而 `ConsumeOne` 找不到宿主格子就直接 return；取消条件只覆盖阵亡/受伤/再按一次，没有"物品被移走" |
| RD-AUD-032 | 医疗消耗不发背包变更事件 | **仍存在** | `Bootstrap/SceneBootstrap.ItemUse.cs`（全文件无 `InventoryChangedEvent`） | 消耗走 `Split`/`Remove` 直接改网格 → 已打开的背包界面不会立刻刷新 |
| RD-AUD-033 | 领奖先加钱、后发物品 | **口径变化** | `Modules/Meta/QuestSystem.Progress.cs:121`、`:155`、`:163` | 新增了"先查空间再发钱"的前置检查（注释写明动机），但顺序仍是先加钱后放置；`AutoPlace` 真失败时仍有"钱到账、物品没发"的窗口（变窄未消除） |
| RD-AUD-034 | 上交拆分后回放忽略放置结果 | **口径变化** | `Modules/Meta/QuestSystem.Progress.cs:314`-`:318` | 现在检查 `Place(rest, origin, rotated)` 的返回值，失败时兜底 `AutoPlace(rest)`；兜底那次的返回值仍被忽略 |
| RD-AUD-035 | AI 记忆过期只在测试里用 | **仍存在** | `Modules/AI/AiSensesMemory.cs`（`DiscardIfExpired`）仅被 `Tests/EditMode/AiSensesMemoryTests.cs` 调用 | 生产代码无调用方；`AiContext` 仍只看 `HasMemory` 判定威胁坐标是否可用 |
| RD-AUD-036 | 伤害数字每次新建并销毁 | **仍存在** | `Modules/Presentation/Combat/DamageNumberView.cs:73`、`:88`、`:121` | 每次命中 `new GameObject` + 读 `Camera.main` + `Destroy`，与本工程其它高频表现的对象池做法不一致 |
| RD-AUD-037 | 弧形体力条创建运行时材质 | **仍存在** | `Modules/Presentation/Player/StaminaArcView.cs:148`、`:98` | 每条线 `new Material(Shader.Find("Sprites/Default"))`；`OnDestroy` 只退订、不销毁材质 |
| RD-AUD-038 | 表现层插值与帧率相关 | **仍存在** | `Modules/Presentation/AI/EnemyAgentView.cs:144`；`Modules/Presentation/AI/RemoteEnemyView.cs:37` | 固定系数 `Mathf.Lerp(..., 0.35f)`；同层的 `PlayerMotor` 已改成帧率无关写法 |
| RD-AUD-039 | 调试工具"关闭即零开销"不成立 | **仍存在** | `Modules/Diagnostics/AiDebugOverlay.cs:155`、`:186`；`Modules/Diagnostics/AiDebugPanel.cs:76` | Overlay 每帧检测按键；Panel 隐藏时仍无条件订阅 `AiStateChangedEvent` |
| RD-AUD-040 | 表现层热路径里运行时 Find | **仍存在** | `Modules/Presentation/Player/PlayerCharacterView.cs:157` | 仍在用 `FindFirstObjectByType<PlayerWeaponView>()`，与"运行时无 Find"的硬规则冲突 |
| RD-AUD-058 | 事件订阅被丢弃、组件销毁后不解除 | **部分成立** | `Bootstrap/SceneBootstrap.Combat.cs:88` | `DamageAppliedEvent` 一侧确认仍存在（`Subscribe` 返回值未接）；`InventoryScreenController` 的 `EncumbranceChangedEvent` 本轮未复现，需再核 |
| RD-AUD-059 | 非 Editor 代码多处运行时 Find | **仍存在** | 统计 **17 处**（非 Tests/非 Editor 的 `Find*` 与 `Camera.main`）；例：`Bootstrap/SafeHouseBootstrap.Interaction.cs:87` | 报告称约 20 处，且点名的"每帧 FindObjectsByType"仍在 |
| RD-AUD-060 | 玩家碰撞体清理不完整 | **仍存在** | `Session/Network/ServerRuntime.Movement.cs:117`；`ServerRuntime.Players.cs:90`-`:95` | `ShutdownMovement` 只 `m_PlayerColliders.Clear()`，不销毁 GameObject；离场路径也只移除字典项 |
| RD-AUD-061 | 结算带出价值漏算装备槽 | **仍存在** | `Session/Network/ServerRuntime.Raid.cs:257`-`:265` | `ResolveCarriedValue` 只累加 `loadout.Backpack` 与 `loadout.AmmoPouch`，武器 / 护甲 / 背包装备槽未计入 |
| RD-AUD-062 | Editor 工具先删资产再造 | **仍存在** | `PlayerCharacterBuilder.cs:105`、`BasinTerrainMaterialBuilder.cs:57`、`UiAssetTool.cs:115`、`EnemyCharacterBuilder.cs:173` | 4 处在重建前 `AssetDatabase.DeleteAsset`，生成或导入异常时旧资产已经没了 |
| RD-AUD-063 | 死代码与过期注释 | **仍存在** | `Bootstrap/SceneBootstrap.Multiplayer.Raid.cs:24`（`m_NextRestartRequestTime` 全仓仅此一处）；`Modules/UI/Inventory/UiFontProvider.cs:18` | 字段没有任何读写方；`UiFontProvider` 仍被 Diagnostics 使用（其注释是否仍写"TMP 未导入"本轮未逐句核） |

**本领域小结**：20 条里 **17 条仍存在**、**3 条口径变化**（`033`、`034`、`058`——都是"已加防护但结论部分保留"）。

---

## 六、架构 / 规范 / 依赖 / 文档（18 条）

复核状态：**18 条全部完成**（原派发子代理连续三次任务空投递，改由主代理执行）。

| 编号 | 原结论（≤20 字） | 现状 | 当前证据（文件:行） | 说明 |
| --- | --- | --- | --- | --- |
| RD-AUD-001 | `Bootstrap` 不再是极薄启动层 | **仍存在（且加重）** | `Bootstrap/Session/Lobby`（23 文件 / 4404 非空行）、`Bootstrap/Session/Network`（56 文件 / 9402 非空行） | `ServerRuntime` 已从报告时的 20 个 partial 涨到 **28 个 / 5667 行** |
| RD-AUD-002 | 三个 God Class | **仍存在（且加重）** | `SceneBootstrap`（23 文件 / 4120）、`RaidFlowController`（8 / 1291）、`ServerRuntime`（28 / 5667） | 三处规模都比报告时更大；`RaidFlowController` 从 6 文件涨到 8 |
| RD-AUD-003 | Diagnostics 反向依赖表现层 | **仍存在** | `Modules/Diagnostics/RaidDemo.Diagnostics.asmdef` | 引用列表里仍有 `RaidDemo.Presentation` 与 `RaidDemo.UI` |
| RD-AUD-004 | ADR 登记的 13 个程序集与实际不符 | **仍存在** | `Docs/Decisions/ADR-004-程序集划分方案.md:29`；`Assets/Game/**/*.asmdef` | ADR 写"共 13 个程序集"，实际 **20 个** asmdef；全文没有 `Simulation` / `Presentation` / `Diagnostics` / `Bootstrap.Editor` / `Tests.PlayMode` 的登记 |
| RD-AUD-005 | `Kernel.Pure` 未开 `noEngineReferences`；文档口径错 | **仍存在** | `Kernel/Pure/RaidDemo.Kernel.Pure.asmdef:13`；`Shared:15`、`Data:15`；`Docs/00_项目工程规范与系统设计.md`（"Kernel.Pure 与 Shared 保持 false"） | 实际 `Kernel.Pure=false`、`Shared=true`、`Data=true`；文档仍写 Shared 是 false，与代码相反 |
| RD-AUD-006 | 多个 asmdef 引用了用不到的程序集 | **无法判定** | 抽样失败：`Kernel.Unity` 暴露的命名空间是 `RaidDemo.Kernel`（91 个文件在用）、`Data.Content` 暴露 `RaidDemo.Data` | 程序集名与命名空间不同名，抽样口径不可靠；需要逐程序集反查引用，本轮未做 |
| RD-AUD-007 | 玩家生命值 100 散在三处 | **仍存在** | `Bootstrap/SceneBootstrap.Ai.cs:26`、`Bootstrap/SafeHouseBootstrap.Combat.cs:30`、`Session/Network/ServerCombatCoordinator.cs:32` | 三处各自定义（两处 `const`、一处 `const int`），没有共享契约常量 |
| RD-AUD-008 | 静态容量告警标记跨会话残留 | **仍存在** | `Session/Network/LootContainerSync.cs:42`、`:200` | `static readonly HashSet` 只增不清，重连/重开后不会重置 |
| RD-AUD-064 | 50 行方法上限没门禁 | **仍存在** | 统计：方法体 > 50 行 **64 个**（`Assets/Game` 排除 Tests/Editor，374 个文件） | 与报告"约 64 个运行时方法"吻合；`PathRulesTests` 只查 400 行文件与绝对路径 |
| RD-AUD-065 | 4 参数上限没门禁 | **仍存在（条数口径不同）** | 报告点名处仍在：`Modules/UI/Theme/UiFactory.cs:309`、`Modules/UI/Raid/CharacterSelectScreen.cs:70` | 多行为签名让自动统计口径差异很大（本次脚本给 8~120 之间），未复现报告的 42；结论"存在超限方法"成立，精确条数需统一口径 |
| RD-AUD-066 | 魔法数字与重复方法 | **仍存在** | `EnsureFolder` **5 处**（报告说 3）：`GreyboxSceneBuilder.cs:315`、`BasinTerrainMeshBuilder.cs:163`、`ItemContentBuilder.cs:257`、`SafeHouseSceneBuilder.Helpers.cs:65`、`M7MaterialLibrary.cs:119`；`IsValidText`/`IsDigits` 各 **2 份**：`LobbyProtocol.cs:239/265`、`LobbyText.cs:178/204` | 重复实现随新增代码变多了 |
| RD-AUD-067 | 约 80 处裸 `Debug.Log` | **仍存在（加重）** | 统计：非 Tests / 非 Editor 的 `Debug.Log*` **111 处** | 统计命令见本节末 |
| RD-AUD-068 | 约 275 个公开字段；约 82 个多类型文件 | **仍存在** | 统计：`public ... ;` 字段 **332 个**；含多个类型的文件 **80 个** | 332 略高于报告的 275；多类型文件 80 与报告 82 基本持平 |
| RD-AUD-069 | 公开 API 的 XML 文档不全 | **仍存在** | `Kernel/Pure/ObjectPool.cs:143` `public void Dispose()`（报告点名处，仍无 `<summary>`） | 逐条核了报告点名的例子，仍未补 |
| RD-AUD-070 | Cinemachine 版本漂移 | **仍存在** | `Packages/manifest.json:7` = `3.1.7`；`Packages/packages-lock.json:67` = `6.6.0`（`source: builtin`） | manifest / lock / 实际包三处不一致，README 也仍写 3.1.7 |
| RD-AUD-071 | 没有 `.editorconfig` / `.ruleset` 门禁 | **仍存在** | 两个文件都不存在；`Tests/EditMode/PathRulesTests.cs` 只有 400 行文件与绝对路径两类规则 | 方法长度、参数、嵌套、日志、Find、字段可见性、XML 文档都还没有自动门禁 |
| RD-AUD-072 | README 口径漂移 | **仍存在** | `README.md:16`-`:32` | 顶部仍以 M7 / M8 为当前状态，仍写 407 / 415 / 425 / 428 测试数（当前是 682） |
| RD-AUD-073 | 时间与随机源抽象不一致 | **仍存在** | `Kernel/Unity/TimeService.cs:17`（`ITimeService`）之外全仓无引用；直写 `Time.timeScale` **19 处**（分文件明细见第四节 `022`） | `ITimeService` 仍是死抽象，`Time.timeScale` 仍被直接写；抽象边界前后不一致 |

本节统计命令（可复现，均由主代理在本机执行）：

```powershell
# 064 / 065 / 068：方法长度、参数个数、公开字段、多类型文件
pwsh -NoProfile -File Builds/norm-scan.ps1

# 067：裸日志
$files = Get-ChildItem Assets/Game -Recurse -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '\\(Tests|Editor)\\' }
($files | Select-String -Pattern 'Debug\.Log' | Measure-Object).Count
```

---

## 七、复核结论

**一句话**：报告作为"问题清单"依然有效，但**直接照它开工是不安全的**——
72 条里 2 条已修、3 条只剩弱形式、4 条结论本身要改（其中 `024` 的"未拒绝负散布"是原报告误判），
另有 1 条静态读不出来。真正意义上的"待修"是 **62 条仍在**，且它们的严重性排序与原报告不完全一致。

### 7.1 需要先改结论、再谈修不修的（4 + 1 条）

| 编号 | 该怎么处理 |
| --- | --- |
| `024` | **原报告误判**：负散布检查自 M3 起就在。把这条从修复清单里去掉，只保留"散布无 NaN 校验""`WearFactor == 0` 被当默认值""移动参数未校验" |
| `012` | 口径已变（服务器会下发弹匣余量）。改成"装备变更时的弹药下行覆盖度"这个新问题 |
| `033` `034` | 已加防护（先查空间 / 检查放置结果），结论要改成"窗口变窄但未消除" |
| `046` `058` | 描述要改：`046` 不是"未捕获异常"（有 `CommandResult` 兜底），`058` 只剩 `DamageAppliedEvent` 一侧 |
| `006` | 静态读不出来，需要**逐程序集反查引用**才能判定 |

### 7.2 建议的修复分档（按"影响 ÷ 成本"排）

| 档 | 条目 | 为什么排这一档 |
| --- | --- | --- |
| **第一档：小改动、直接影响玩法正确性** | `010`（体力恢复少 1 秒）、`027`（AI 穿透恒 0）、`028`（仓库满吞战利品）、`029`（带出价值虚高）、`046`（槽位入口校验）、`047`（NaN 校验）、`053`（协议按字节校验）、`070`（依赖版本对齐）、`072`（README 状态） | 都是一处或少数几处的改动，其中 `027`/`028`/`029` 玩家能直接撞到，`072` 影响的是别人对项目的第一印象 |
| **第二档：联机权威与数据完整性（要双客户端验证）** | `041`、`042`、`043`、`044`、`045`、`049`、`051`、`052` | 这组是一个依赖链（客户端不自结算 → 服务器出超时 → 结算数据打通 → 不再写本地存档），拆开做会反复返工；验收需要真实双客户端 |
| **第三档：确定性与协议存量** | `011`（物品状态绑实例）、`013`~`018`、`025`、`036`~`040`、`055`~`057`、`060`~`063` | 多数是"当前不发作但会积累"的问题；其中 `011` 改造面最大，建议单独排期 |
| **第四档：规范与架构（建议 M11 之后）** | `001`~`008`、`064`~`069`、`071`、`073` | 属于长期可维护性；`001`/`002` 是重构级别，`064`~`068` 是"先立门禁再谈修"的类型（现状：>50 行方法 64 个、裸日志 111 处、公开字段 332 个） |

### 7.3 必须实机确认的条目

`012`（弹挂广播覆盖度）、`016`（同一实例能否进两个网格）、`023`（占用表损坏是否真发生过）、
`041`/`052`（需要"服务器 + 双客户端"日志才能定案的时序类问题）。
这五条单靠静态阅读无法收口，其余 67 条已可按上表直接排期。

### 7.4 复核过程中发现的新增事实（原报告没有的）

1. `RaidFlowController.SaveNow` 已经有联机闸门、`RaidEndMessage` 回屋链路、服务器结算/生命/弹药镜像通道——
   这三条是基线之后新增的缓解措施，评估时必须算进去；
2. `Turn` 类规范的偏离量比原报告更大：`ServerRuntime` 从 20 个 partial（3,743 行）涨到 **28 个（5,667 行）**，
   裸日志 80 → **111 处**，公开字段 275 → **332 个**；
3. `PathRulesTests`（400 行文件上限 + 绝对路径）是**唯一在跑**的规范门禁，其余规范仍只写在文档里。
