# RaidDemo 只读代码审查报告

> 本文档保存的是上一轮只读代码审查的完整结论，按原编号顺序整理。审查期间未修改受控源码、配置、场景、预制体或资源。

## 一、报告元数据

| 项目 | 内容 |
| --- | --- |
| 审查基线 | `84f37fd50e0a14e064075cd843f3eb423964fcbc` |
| 基线提交时间 | 2026-09-14 13:56:19 +0800 |
| 基线提交说明 | `fix: 搜刮"双击时好时坏、拖不动"——同步时复用物品对象，界面不再把对象当身份` |
| 审查完成时间 | 2026-09-14 |
| 报告落盘时间 | 2026-09-15 |
| 落盘时当前 HEAD | `7616783adc697c18ef2768733ab2aa6b706bbd5a` |
| Unity 版本 | `6000.6.0f1` |
| 审查性质 | 只读审查，不修改代码、配置、场景或资源 |
| 编译结果 | 通过，无 C# 错误或 `warning CS` |
| EditMode | `604 / 604` 通过 |
| PlayMode | `3 / 3` 通过 |
| P0 | 0 |
| P1 | 14 |
| P2 | 24 |
| P3 | 34 |
| 编号问题总数 | 72 |

编号范围为 `RD-AUD-001` 至 `RD-AUD-073`，其中 `RD-AUD-009` 未使用，因此去重后的编号问题总数为 72。

### 适用性说明

本报告对应审查基线 `84f37fd`。报告落盘时，项目当前 HEAD 已推进到 `7616783`，期间包含 M10、Addressables、HybridCLR 探针、启动器、联机流程、重连、服务端存档和热更新等大量提交。基线之后累计有约 314 个文件、31,734 行新增和 13,828 行删除。

因此：

- 本报告可以作为基线审查记录和问题追踪清单使用。
- 报告中的结论没有在落盘时对 `7616783` 重新逐条复验。
- 修复前应先按“对应代码是否仍存在、行为是否已变化”对编号问题做一次状态复核。
- 低风险 P3 和横向规范项尤其可能已经因后续重构、拆分或新增代码而变化。

### 审查范围

本轮重点检查：

- 工程拓扑、程序集边界和模块依赖。
- Kernel、Shared、Data、Simulation、Input、Inventory、Combat。
- AI、Raid、Meta、Diagnostics、Presentation。
- UI、Bootstrap、Session、Lobby、Network 和 Editor。
- 代码结构、命名、分类、生命周期、配置、测试和文档口径。
- 方法长度、参数数量、嵌套深度、日志、公开字段和 XML 文档等横向规范。

本轮没有深入：

- Scene / Prefab 内部的全部序列化引用和美术布局。
- 真实双客户端、断线重连和云主机网络时序。
- Unity Profiler、内存快照和 GC 统计。
- 从空 `Library` 开始的干净包解析与构建。
- 编辑器工具的鼠标交互和视觉结果人工验收。
- 修复完成后的端到端回归。

---

## 二、执行摘要

本轮共记录 72 个编号问题，没有 P0。最高风险集中在五类：

1. **联机权威链没有闭合**：客户端仍本地推进倒计时和撤离，服务端没有战局总时长超时，客户端忽略服务器结算字段，全员结算后无法回到房间。
2. **联机结算会污染单机存档**：在线战局结束会直接修改本地 `MetaProgress`、仓库、装备和任务进度。
3. **物品运行时状态没有真正绑定到物品实例**：同型号武器共享弹匣、护甲卸下再穿上会恢复耐久、拆分和存档会丢失状态。
4. **存在确定性、数据保存和网络输入校验缺口**：体力恢复延迟错误、存档替换非原子、NaN 输入可污染服务端状态、非法装备槽可导致服务端下标异常。
5. **规范门禁没有真正执行**：方法 50 行、参数数量、嵌套深度、无 `Debug.Log`、无运行时 `Find` 等规则只存在于文档，代码中有大量偏差。

建议不要先做泛化重构。优先顺序应当是：

```text
先修 P1 数据完整性与联机权威
    ↓
再修 P2 核心确定性、协议、存档与配置一致性
    ↓
最后做架构拆分、死代码清理和规范门禁
```

---

## 三、风险矩阵

| 风险域 | P1 | P2 | P3 | 代表问题 |
| --- | ---: | ---: | ---: | --- |
| 架构与程序集边界 | 1 | 4 | 3 | `Bootstrap` 过重、Diagnostics 反向依赖、asmdef 文档脱节 |
| Kernel / Shared / Data / Simulation / Input | 3 | 5 | 9 | 体力恢复、物品状态、移动预测、服务定位、排序守恒 |
| Inventory / Combat | 2 | 2 | 5 | 装备状态、跨网格唯一性、弹药穿透、HUD 权威 |
| AI / Raid / Meta / Presentation | 2 | 3 | 8 | AI 穿透量纲、撤离入库丢失、诊断视线、医疗消耗 |
| UI / Bootstrap / Editor | 0 | 7 | 9 | 本地结算、结算界面、资源生命周期、Editor 工具可靠性 |
| Session / Lobby / Network | 6 | 3 | 0 | 服务端超时、击杀统计、非法输入、登录拒绝服务、固定字符串溢出 |
| 横向规范 | 0 | 1 | 0 | 长方法、重复常量、日志、公开字段、文档和包版本漂移 |

---

## 四、P1：必须先处理

### 架构与边界

- **RD-AUD-001 [P1][架构]** `Bootstrap` 已不再是 ADR 所述的“极薄启动入口”：Session / Lobby / Network 约 58 个文件、9,418 非空行；`ServerRuntime` 已扩展到 20 个 partial 文件、3,743 非空行，承载 AI、移动、背包、撤离和结算。影响 HybridCLR 热更边界和编译增量，修复工作量为 L。

### Kernel、Simulation 与 Inventory

- **RD-AUD-010 [P1][Simulation]** 体力恢复延迟比配置多 1 秒：`PlayerMovementSimulator.cs:22/166/217/232/237` 使用 `-1` 作为计时器初值和重置值，实际约 2.5 秒 / 4 秒，而文档要求 1.5 秒 / 3 秒；测试注释还固化了错误行为。工作量为 S。
- **RD-AUD-011 [P1][Data/Combat/Inventory]** 物品运行时状态没有绑定实例：`ItemInstance.State` 基本未用于生产逻辑；`PlayerWeapon.cs:33/81` 用 `IWeaponStats` 资产作为弹匣状态键，同型号两把枪共享弹匣；`CombatantState.SetArmor` 重新装备会重置护甲耐久；`MetaSaveMapper` 不保存该状态，`Split` 也不复制状态。影响武器、护甲、存档和堆叠语义，工作量为 L。
- **RD-AUD-012 [P1][Combat/联机]** 联机弹药 HUD 不读取服务器权威：客户端本地不推进武器，HUD 仍每帧读取本地 `PlayerWeaponController`；服务器开火事件不带弹匣余量，换弹扣弹后也不会广播弹药挂容器。工作量为 M。

### AI、Raid 与 Meta

- **RD-AUD-027 [P1][AI]** AI 武器穿透力疑似量纲错误：`AiDirector.cs:125` 固定为 `0.45f`，而玩家弹药穿透力是 12/15/22，护甲值最低为 10；代入 `DamageCalculator` 后 AI 对任何有效护甲都得到 0 穿透。属于核心战斗规则不一致，工作量为 S。
- **RD-AUD-028 [P1][Meta/Raid]** 仓库满时撤离入库会先移除物品再尝试放置，放置失败后没有回滚，只累计失败数量并写日志。文档和 README 均承诺撤离物品“全部入库”，但实现会永久销毁战利品，工作量为 M。

### Session、Lobby 与 Network

- **RD-AUD-041 [P1][联机权威]** 联机客户端仍运行本地 `RaidSession` 倒计时和本地 `ExtractionTracker`：本地读条完成会触发 `NotifyExtracted()`、广播本地 `RaidEndedEvent` 并显示结算界面，不等待服务器裁定。工作量为 M。
- **RD-AUD-042 [P1][服务端规则]** 服务端只处理倒地、救援、撤离读秒和死亡，没有战局总时长超时判定；服务端永远不会生成 `RaidOutcome.TimeExpired`。工作量为 S～M。
- **RD-AUD-043 [P1][数据完整性]** 联机结算直接使用本地 `MetaProgress`：撤离会 `DepositLoadoutToStash()` 和上报任务进度，阵亡会 `ClearLoadout()`，随后写本地存档；暂停退出联机战局也会清空本地装备。违反单机 / 联机存档隔离规则，工作量为 M。
- **RD-AUD-044 [P1][联机战斗]** 联机客户端跳过 `InitializeAi()`，因此 `m_PlayerCombatantId` 从未赋值，保持为 0。非 0 号客户端的医疗品使用、受击标记、击杀统计和任务进度都会错误或完全失效，工作量为 M。
- **RD-AUD-045 [P1][权威结算]** `RaidProgress.Kills` 从未累加，服务器结算消息中的击杀数恒为 0；当前客户端忽略服务器结果字段，所以问题被界面暂时掩盖。工作量为 S～M。
- **RD-AUD-046 [P1][网络输入校验]** 服务端把上行消息中的 `Slot` 直接转换为 `EquipmentSlot`，`EquipmentLoadout.Unequip` 再直接索引长度 5 的数组；非法 `Slot=255` 可触发未捕获的 `IndexOutOfRangeException`。未知 `Kind` 也会被当作卸下或移动处理。工作量为 S。
- **RD-AUD-047 [P1][网络输入校验]** `Move` / `Look` 没有 NaN、Infinity 或向量范围校验；`Vector2F.IsNearlyZero` 对 NaN 返回 false，归一化后会污染权威位置和快照并广播给其他客户端。工作量为 S。
- **RD-AUD-048 [P1][服务端安全]** 未登录连接可以反复发送登录请求：已有账号执行 PBKDF2 100,000 次，新昵称同步创建账号并写整个 `accounts.json`；没有速率限制、失败退避或待处理登录上限。攻击者可以造成主循环阻塞和账号文件增长。工作量为 M～L。

---

## 五、P2：数据一致性、协议和可靠性

### 架构与程序集

- **RD-AUD-002 [P2][架构]** 存在三个 God Class：`SceneBootstrap` 25 文件 / 4,602 非空行、`RaidFlowController` 6 / 1,090、`ServerRuntime` 20 / 3,743。职责边界需要按“装配 / 局外进度 / 权威模拟”重新划分。
- **RD-AUD-003 [P2][架构]** `Diagnostics` 反向依赖 `Presentation` 和 `UI`，并在 `AiDebugShapeRenderer.cs:47` 直接 `new NavMeshGroundHeightProvider()`；调试程序集应只依赖只读契约，不能反向绑定表现实现。
- **RD-AUD-004 [P2][文档/asmdef]** ADR-004 记载 13 个程序集，实际有 19 个；`Simulation`、`Presentation`、`Diagnostics`、`Bootstrap.Editor`、`Tests.PlayMode` 以及 `AI -> Simulation` 依赖未登记。
- **RD-AUD-005 [P2][asmdef]** `RaidDemo.Kernel.Pure.asmdef` 的 `noEngineReferences` 仍为 `false`；主设计文档第 409 行还错误地声称 `Shared` 为 false，实际 `Shared` 和 `Data` 都是 true。

### Kernel、Simulation、Inventory 与 Save

- **RD-AUD-013 [P2][Simulation]** `MovementServerWorld.Advance` 在达到 `MaxStepsPerAdvance` 后没有丢弃超预算的累加时间，与“宁可丢弃多余时间”的注释矛盾，可能形成补帧死亡螺旋。
- **RD-AUD-014 [P2][Simulation]** `MovementPredictionBuffer.Reconcile` 只比较位置，不比较朝向、体力、冲刺计时器；完整快照已经下发，但多数状态仍不回滚校正。
- **RD-AUD-015 [P2][Simulation]** `MovementServerWorld.TryTeleport` 只清 `HasPendingInput`，不清 `AppliedInput`；重开后玩家可能沿旧方向继续移动。
- **RD-AUD-016 [P2][Inventory]** `InventoryGrid.Place` 没有跨网格唯一性约束；同一 `ItemInstance` 可以被放入多个网格，导致归属和存档歧义。
- **RD-AUD-017 [P2][Inventory]** `ContainerRegistry.GetOrCreateNested` 深度硬编码为 1，并且没有生产调用方，嵌套容器设计实际处于半成品状态。
- **RD-AUD-018 [P2][Save]** `SaveFileStore.Save` 的主档替换不是原子操作；写入或替换中断时可能留下缺少主档的状态。

### Raid、Meta 与 Diagnostics

- **RD-AUD-029 [P2][Raid/Meta]** `RaidResult.Create` 在仓库入库前计算 `ExtractedValue`，仓库满而丢弃的物品仍计入带出价值和任务进度，结算显示与真实仓库增量不一致。
- **RD-AUD-030 [P2][Diagnostics]** `AiDetectionDiagnostics` 使用两参 `ToEyePosition`，地面高度固定为 0；真实 `AiAgent` 使用地面高度三参版本。在盆地、平台等多层地图上，调试面板可能与 AI 实际视线结论不一致。
- **RD-AUD-031 [P2][医疗]** 使用医疗品的读条期间可以把物品移到战利品箱；完成时 `ConsumeOne` 找不到背包或弹药挂中的实例会直接返回，但回血已经生效，形成免费治疗漏洞。

### 联机、UI 与服务端周边

- **RD-AUD-049 [P2][联机结算]** 客户端保存了 `LocalOutcome`，但结算界面仍用本地 `RaidSession`、本地 `m_Loadout` 重建 `RaidResult`，服务器 `CarriedValue`、`Kills`、`ElapsedSeconds` 实际没有进入界面。
- **RD-AUD-050 [P2][联机流程]** 全员结算后服务器回到 `Waiting`，但客户端 `ApplyRoomState` 不会从 `InRaid` 转回 `InRoom`，`ReturnFromRaid()` 也没有调用方；玩家无法在同一连接中继续下一局。
- **RD-AUD-051 [P2][结算数据]** `SettlePlayer` 使用服务器累计 `m_World.SimulationTime` 作为本局用时；该时钟从服务器启动开始累加，开局和重开均未重置。
- **RD-AUD-052 [P2][服务端收尾]** 同一 tick 内最后一名玩家结算后，外层 `m_RaidActorBuffer` 可能继续处理已结算玩家；这些玩家会因已移出世界而被重新判定为阵亡并再次广播 `Killed`。触发取决于字典迭代顺序。
- **RD-AUD-053 [P2][协议]** 房间名和提示文本的校验按字符数，网络字段按 UTF-8 字节容量：24 个汉字房间名约 72 字节，超过 `FixedString64Bytes` 的 61 字节；16 个汉字昵称的错误提示约 135 字节，超过 `FixedString128Bytes` 的 125 字节。
- **RD-AUD-054 [P2][Meta/背包]** 换包失败时只恢复当前失败物品的旋转状态；之前试放成功的物品仍保留 `AutoPlace` 写入的新朝向，导致旧网格朝向与占用表不一致，并可能写入错误存档。
- **RD-AUD-055 [P2][联机表现]** 远端玩家离开房间或断开后，`OnSnapshotBatch` 只是停止推送数据，不会清理对应 `RemotePlayerView`；幽灵队友会停留在最后位置。
- **RD-AUD-056 [P2][联机装备]** 客户端先本地执行装备/卸下再上行；服务器拒绝时只写日志并返回，不发送权威容器内容纠正客户端，武器权威状态会长期漂移。
- **RD-AUD-057 [P2][Dashboard]** 每次状态页快照都会在主线程调用 `ServerAddressReporter.EnumerateLanIPv4()`，后台 HTTP 监听又使用单线程串行 `AcceptLoop`；公开只读状态页可被高频请求或慢连接拖慢服务器。
- **RD-AUD-064 [P2][规范]** 单方法 50 行上限有约 64 个运行时代码方法和 16 个 Editor 方法违反；典型包括 250 行的 `LaunchOptions.TryParse`、146 行的 `M7AudioAssetBuilder.BuildAll`、137 行的 `LootContainerSync.Apply` 和 106 行的 `SceneBootstrap.Update`。
- **RD-AUD-070 [P2][依赖配置]** `Packages/manifest.json:6` 要求 Cinemachine `3.1.7`，`packages-lock.json:51-52` 和实际包缓存却是 `6.6.0`；README 仍写 3.1.7。当前编译日志还出现 Cinemachine / URP 包内警告，不能笼统宣称“零警告”。

---

## 六、P3：规范、生命周期和可优化项

### 架构、程序集和内容质量

- **RD-AUD-006 [P3][asmdef]** 多个 asmdef 引用了 `Kernel.Unity` / `Data.Content` 等程序集，但对应代码没有实际使用，增加了依赖面和编译重编范围。
- **RD-AUD-007 [P3][重复常量]** 玩家生命值 100 分别出现在 `SceneBootstrap.Ai.cs:26`、`SafeHouseBootstrap.Combat.cs:30`、`ServerCombatCoordinator.cs:32`，应统一为共享契约常量或配置来源。
- **RD-AUD-008 [P3][联机日志状态]** `LootContainerSync` 的静态 `s_ReportedCapacityMismatch` 跨会话残留，初始化或重连后不会清理，可能压制新会话真正需要显示的容量警告。
- **RD-AUD-019 [P3][命令路由]** `CommandRouter.Dispatch` 在 `try` 之外执行强制转换；若 handler 类型或命令载荷异常，异常不会转化成统一的 `CommandResult`。
- **RD-AUD-020 [P3][ObjectPool]** `ObjectPool.Release` 允许同一对象多次归还；缺少“已归还”或代际检查，池使用错误可能静默放大。
- **RD-AUD-021 [P3][ServiceLocator]** 覆盖注册不会释放旧服务，工厂返回 null 也没有拒绝；服务生命周期和失败语义不明确。
- **RD-AUD-022 [P3][时间服务]** `ITimeService` / `UnityTimeService` 全工程没有消费者，同时 `RaidFlowController` 多处直接写 `Time.timeScale`，缓存值与实际值可能漂移。
- **RD-AUD-023 [P3][InventoryGrid]** `Sort` 在 `ClearAll` 前只收集能找到 origin 的物品；若占用表已损坏，找不到 origin 的物品会被静默删除，`RestoreLayout` 也救不回来。
- **RD-AUD-024 [P3][内容校验]** `WeaponStats` 未拒绝负散布或 NaN；`ArmorStats` 允许 `WearFactor == 0`，但 `DamageCalculator` 把 0 当作“使用默认值”；`PlayerMovementProfile.Validate` 未检查加速度和体力参数。
- **RD-AUD-025 [P3][弹药]** 初次装备武器时从背包取弹药的穿透力，换弹时却从弹药挂取弹；两条路径可能使用不同弹药定义，导致同一弹匣威力不一致。
- **RD-AUD-026 [P3][Input]** 瞄准输入的部分注释与实现不一致，初始化失败路径还会重复告警；属于可读性和日志噪声问题。

### AI、Raid、Meta 和表现层

- **RD-AUD-032 [P3][医疗/UI]** 医疗品消耗使用 `Split` / `Remove` 直接改网格，不发布 `InventoryChangedEvent`；已打开的背包界面不会立即刷新。
- **RD-AUD-033 [P3][任务领奖]** `QuestSystem.TryClaim` 先加钱、后放奖励物品；虽有前置空间检查，但缺少失败回滚，仍存在“钱到账、物品没发放”的半完成窗口。
- **RD-AUD-034 [P3][任务上交]** `TryConsumeFromStash` 在拆分后回放剩余堆时忽略 `AutoPlace` 返回值；异常布局下可能静默丢件。
- **RD-AUD-035 [P3][AI 记忆]** `AISensesMemory.DiscardIfExpired` 只在测试中使用；`AiContext.TryResolveThreatPosition` 只判断 `HasMemory`，撤退状态可能继续使用过期坐标。
- **RD-AUD-036 [P3][表现/性能]** 伤害数字每次命中都新建 `GameObject` + `TextMesh` 并 `Destroy`，每次生成还访问 `Camera.main`；与本工程其他高频表现使用对象池的原则不一致。
- **RD-AUD-037 [P3][表现/资源]** `StaminaArcView` 每条线创建运行时 `Material`，`OnDestroy` 只释放订阅不销毁材质；同类问题也出现在静态生成贴图和共享材质上。
- **RD-AUD-038 [P3][表现/帧率]** `EnemyAgentView` 和 `RemoteEnemyView` 使用固定系数 `Mathf.Lerp(..., 0.35f)`，动画速度平滑与帧率相关；`PlayerMotor` 已使用指数跟随。
- **RD-AUD-039 [P3][Diagnostics]** README 声称调试工具关闭时零开销，但 `AiDebugOverlay.Update` 仍每帧检测按键，`AiDebugPanel` 隐藏时仍接收状态事件并格式化日志。
- **RD-AUD-040 [P3][表现/规范]** `PlayerCharacterView.Update` 仍可能在热路径调用 `FindFirstObjectByType<PlayerWeaponView>()`；这与“运行时无 Find”的硬性规则冲突。

### UI、Bootstrap 与 Editor

- **RD-AUD-058 [P3][事件生命周期]** `InventoryScreenController` 丢弃 `EncumbranceChangedEvent` 订阅，`SceneBootstrap.Combat` 丢弃 `DamageAppliedEvent` 订阅；组件销毁后可能留下悬挂回调或重复订阅。
- **RD-AUD-059 [P3][运行时 Find]** 非 Editor 代码约有 20 个 `Find*` / `Camera.main` 调用点。`SafeHouseBootstrap.Interaction.cs:84` 每帧执行 `FindObjectsByType`；输入端、说明牌和伤害数字也存在热路径查找。
- **RD-AUD-060 [P3][对象生命周期]** `ServerRuntime.ShutdownMovement` 只清空玩家字典，不销毁 `m_PlayerColliders`；`SpawnPlayerIntoWorld` 的 `TryAddPlayer` 失败分支也只移除字典项，可能留下幽灵碰撞体。
- **RD-AUD-061 [P3][结算完整性]** 服务端 `ResolveCarriedValue` 只统计背包与弹药挂，明确漏算武器、护甲和背包装备槽；P5 接入服务端存档前必须修正。
- **RD-AUD-062 [P3][Editor 工具]** 多个 Editor 资产重建工具先 `DeleteAsset` 再创建或写入新资产；生成或导入异常时旧资产已经被删除。
- **RD-AUD-063 [P3][死代码/注释]** `MultiplayerClientSession.ReturnFromRaid`、`m_NextRestartRequestTime` 没有生产读取方；`SceneBootstrap.Multiplayer.Inventory` 的旧注释仍说部分命令保持本地，实际已经全部覆盖；`UiFontProvider` 仍描述 TMP 尚未导入，但 UI 已大量使用 TMP。

### 横向规范

- **RD-AUD-065 [P3][方法结构]** 约 42 个运行时方法超过 4 个参数，约 17 个 Editor 方法超限；运行时代码至少 2 处嵌套超过 4 层，Editor 工具至少 3 处。典型为 `PlaceCliffRow` 12 参数、`UiFactory.CreateAnchoredLabel` 10 参数、`InventoryScreenController.Initialize` 10 参数。
- **RD-AUD-066 [P3][魔法数字/重复代码]** 除 007 外，还有枪口高度 1.05、固定步长 1/60、撤离时长 10、UI 参考分辨率 1920×1080 等多份常量；同时存在 9 组完全重复方法，包括 `BindOcclusionPeephole`、三份 `EnsureFolder`、两份 `IsValidText` / `IsDigits`、两份 `ResolveDesignSpeed` 和两套 UI 焦点 / 键盘订阅。
- **RD-AUD-067 [P3][日志规范]** 非 Editor / 非 Tests 代码中约 80 处直接 `Debug.Log` / `LogWarning` / `LogError`，绕过 `LogService`；正式构建无法统一按日志级别过滤。
- **RD-AUD-068 [P3][字段与文件粒度]** 非测试源码中约 275 个公开字段声明；约 82 个文件包含主类型之外的公开或 internal 类型。协议 DTO、存档 DTO 和布局辅助类可能是有意例外，但当前规范没有记录该例外。
- **RD-AUD-069 [P3][XML 文档]** 公开 API 的 XML 文档覆盖不完整，已确认的例子包括 `ObjectPool.Dispose`、`ObjectPool.Scope.Dispose`、`QuestCatalog` 的部分任务 ID 常量和 `Vector2F` 运算符重载。
- **RD-AUD-071 [P3][自动门禁]** 工程没有项目级 `.editorconfig` 或 `.ruleset`；`PathRulesTests` 只检查 400 行文件上限和绝对路径，不检查方法长度、参数数量、嵌套、日志、运行时 Find、字段可见性或 XML 文档。
- **RD-AUD-072 [P3][文档漂移]** README 顶部仍以 M7 / M8 为当前状态，测试数写成 407 / 415 / 425 / 428，与当前 604 不一致；Netcode 已在 `Packages/manifest.json` 中安装，但 README 仍写“当前尚未加入包依赖”；M9 文档顶部仍写 P2-2 进行中；排障记录仍保留与实际 CLI 验证相反的 PlayMode 结论。
- **RD-AUD-073 [P3][抽象一致性]** `ITimeService` 没有消费者，`Time.timeScale` 仍有约 14 处直接赋值；`WeaponRuntime` / `PlayerWeapon` 依赖具体 `DeterministicRandom`，而 `LootRoller` 使用 `IRandomProvider`；表现层又使用 `System.Random`，抽象边界前后不一致。

---

## 七、已通过项

以下内容本轮没有发现新增问题：

- C# 代码编译通过，无 `warning CS`。
- EditMode 604 / 604 通过，PlayMode 3 / 3 通过。
- 单文件 400 行规则全部通过。
- 绝对路径检查通过。
- 已跟踪资源没有缺失 `.meta`。
- `.meta` GUID 无重复。
- 没有 `TODO`、`FIXME`、`NotImplementedException`。
- 没有嵌套 `#region`。
- 游戏逻辑没有直接使用 `UnityEngine.Random`。
- 事件总线、命令路由、背包放置和大部分 UI 操作都保持了清晰的命令 / 事件链路。
- `PathRulesTests` 的 6 项测试本身全部通过。

需要注意：`PathRulesTests` 通过只代表文件长度和绝对路径两项规则通过，不代表第五轮列出的其他规范项通过。

---

## 八、建议修复顺序与验收门禁

### 第一批：数据完整性与联机权威

优先处理以下互相依赖的问题：

1. **联机结算和本地存档隔离**：`RD-AUD-041`、`042`、`043`、`044`、`045`、`049`、`050`、`051`、`052`。
2. **物品状态与核心数值**：`RD-AUD-010`、`011`、`027`、`028`、`029`。
3. **网络输入与登录安全**：`RD-AUD-046`、`047`、`048`。

验收要求：

- 单机存档在联机开始、结算、阵亡、退赛后字节内容不变。
- 两个客户端完成一局后可以不重连进入第二局。
- 服务端能主动生成超时、撤离和阵亡结果，客户端不能自行结束战局。
- 同型号两把武器的弹匣、护甲耐久、拆分和存档状态互相独立。
- 非法槽位、NaN、Infinity 和超限消息不会改变服务端权威状态。

### 第二批：确定性、协议与配置一致性

处理：

- 移动仿真：`RD-AUD-013`、`014`、`015`。
- 背包与存档：`RD-AUD-016`、`017`、`018`、`023`。
- 协议与资源：`RD-AUD-053`、`054`、`055`、`056`、`057`。
- 配置与架构：`RD-AUD-002`、`003`、`004`、`005`、`064`、`070`。

验收要求：

- 固定输入序列在客户端和服务端得到一致结果。
- 容器、装备和存档状态不存在跨网格共享或静默丢失。
- 默认锁文件可在干净 `Library` 下重新解析，manifest、lock、缓存和文档版本一致。
- 每个长方法拆分后都有原有行为测试，不需要靠新增编号掩盖回归。

### 第三批：质量、生命周期和规范门禁

处理：

- asmdef 清理：`RD-AUD-006`、`007`、`008`。
- Kernel / Inventory / Combat 防御性缺口：`RD-AUD-019`～`026`。
- UI 和表现层生命周期：`RD-AUD-032`～`040`、`058`～`063`。
- 横向规范：`RD-AUD-065`～`069`、`071`～`073`。

验收要求：

- 非测试代码不再有裸 `Debug.Log`、运行时 `Find` 和未跟踪事件订阅。
- 方法、参数、嵌套、公开字段和 XML 文档有自动门禁。
- 编辑器反复进入 / 退出播放模式后，运行时材质、贴图、视图和订阅数量不增长。
- README 只保留一个当前状态源，历史测试数字明确标注为里程碑快照。

---

## 九、审查盲区

本次审查明确没有深入以下范围：

- Scene / Prefab 内部的序列化引用和美术布局。
- 真实双客户端、断线重连和云主机网络时序。
- Unity Profiler、内存快照和 GC 统计。
- 从空 `Library` 开始的干净包解析与构建。
- 编辑器工具的鼠标交互和视觉结果人工验收。
- 修复后的端到端回归。

因此，P1 中涉及联机状态、服务端阻塞和运行时资源的问题，建议在实施修复前先用双客户端或修改过的测试客户端做一次动态复现。

### 建议补查顺序

如果继续补查，推荐顺序为：

```text
1. 双客户端联机 E2E
2. Scene / Prefab 关键接线检查
3. 临时工程干净包解析验证
4. Profiler / GC 量测
5. Editor 工具幂等性验证
6. 修复后的完整回归
```

优先级最高的三项是双客户端联机 E2E、Scene / Prefab 关键接线检查和临时工程干净包解析验证。它们可以把最重要的 P1 / P2 从静态确认提升为运行时确认，并直接决定修复方案是否需要调整。

---

## 十、审查产物

原始验证产物保存在本机临时目录：

```text
C:\Users\DongSu\AppData\Local\Temp\RaidDemoAudit\round0
C:\Users\DongSu\AppData\Local\Temp\RaidDemoAudit\round5
```

包括：

- `compile.log`
- `editmode.log`
- `editmode-results.xml`
- `playmode.log`
- `playmode-results.xml`

这些是过程证据，不是最终审查报告；最终整理结论以本文档为准。
