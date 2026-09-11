# 03 武器与战斗（M3）

> 本文是 M3「战斗」的详细设计文档，按 `Docs/README.md` 的约定在里程碑开始时创建。
> 上游依据：`Docs/00_项目工程规范与系统设计.md` 第 7.3 节（武器与弹道）、7.12 节（贪婪循环）。
>
> 阶段：M3 ｜ 状态：**代码完成，待实机验收**（2026-09-11）

---

## 1. 范围与完成判据

| 项目 | 内容 |
| --- | --- |
| 交付物 | 武器数据、射击链路（单发/连发/全自动）、射线弹道、散布、换弹与弹药消耗、护甲等级减伤与耐久、受击反馈、灰盒靶子 |
| 完成判据 | 8 项伤害测试全绿，能与靶子交火 |
| 实际结果 | 全工程 **218 项 EditMode 测试全绿**，其中 M3 新增 42 项（伤害结算 15、武器运行时 13、战斗命令链路 14）；射击链路已用运行时真实射线验证 |

**本阶段不做**：枪械音效与复杂 VFX（M7）、武器改装（P-01）、多武器快捷切换、AI 使用武器（M4）、瞄准镜。

---

## 2. 设计前提

> 本项目对标《逃离鸭科夫》，射击体验偏向**爽快**而非拟真。
> 因此本系统**不做拟真弹道学**，所有机制都必须满足"玩家一眼能看懂"。

三条由此推出的具体结论：

1. **弹道用射线（Hitscan）**，不做下坠、风偏、飞行时间。
2. **后坐力简化为散布锥**，随连发扩大、停火后恢复，不模拟枪口物理。
3. **命中部位粗粒度两段式**（普通 / 暴击），不做头胸四肢的精细倍率。

---

## 3. 架构：引擎能力的边界

战斗层是**第一个必须感知三维空间**的系统（射击要射线检测），
因此它是"逻辑不依赖场景"这条约束第一次受到真正考验的地方。

做法是**把引擎能力收敛到一个接口**（主文档 5.6 节约束三）：

```csharp
public interface IHitProbe
{
    bool TryRaycast(Vector3 origin, Vector3 direction, float maxDistance, out HitInfo hit);
}
```

| 层 | 职责 |
| --- | --- |
| `RaidDemo.Combat` | 定义接口与规则。除这个接口的实现之外，全部逻辑不触碰 UnityEngine 的物理系统 |
| `RaidDemo.Presentation` | 用 `Physics.Raycast` 实现 `PhysicsHitProbe` |
| `RaidDemo.Tests.EditMode` | 用假实现（构造好的命中结果）驱动全部战斗测试 |

这条边界让"射击规则的测试"与"场景里有没有东西"解耦：
测试可以精确指定"打中了谁、打在多高、距离多远"，而这些在真实场景里极难构造。

**纯逻辑部分是严格无引擎依赖的**：`DamageCalculator`、`WeaponRuntime`、`ArmorTiers`
都不引用 UnityEngine，因此伤害公式可以按纯数学测试。

---

## 4. 数据模型

战斗属性挂在物品定义上，复用 M2 建立的 `ItemBehavior` 扩展点
（`RaidDemo.Data.Content` 下的 ScriptableObject 子类）。一个物品一个行为，
因此不需要在 `ItemDefinition` 上加新字段。

| 类型 | 挂在 | 字段 |
| --- | --- | --- |
| `WeaponStats` | 武器物品 | 基础伤害、射速（发/分）、射击模式、连发数、弹匣容量、口径 ID、换弹时间、基础散布、每发散布增量、最大散布、散布恢复速度、射程 |
| `AmmoStats` | 弹药物品 | 口径 ID、穿透力 |
| `ArmorStats` | 护甲物品 | 防护等级（1~4）、最大耐久、磨损系数 |

### 4.1 为什么减伤率不放在护甲资产里

主文档 7.3 节的草案写的是"所有系数存放在 `AmmoDefinition` 与 `ArmorDefinition` 中"。
实现时收窄为：**护甲资产只放等级与耐久，等级到减伤率的映射是一张全局表**。

理由与"可读性优先"这条设计原则直接相关：

| 做法 | 玩家体验 |
| --- | --- |
| 每件护甲各自配置减伤率 | 两件同为 3 级的护甲可能一个 48% 一个 53%，玩家记不住，只能每次打开面板看 |
| 等级决定减伤（全局表） | "3 级甲就是 50%" 可以背下来，捡到装备时一眼能判断价值 |

等级表（`ArmorTiers`）：

| 护甲等级 | 护甲值 | 减伤率 | 玩家感知 |
| --- | --- | --- | --- |
| 无 | 0 | 0% | 完全不防 |
| 1 级 | 10 | 20% | 轻甲，聊胜于无 |
| 2 级 | 20 | 35% | 常规配置 |
| 3 级 | 30 | 50% | 重甲，明显抗打 |
| 4 级 | 40 | 65% | 顶级装备 |

---

## 5. 伤害公式（定稿）

```text
护甲值         = 等级 × 10
有效护甲值     = 护甲值 × (当前耐久 / 最大耐久)
穿透系数       = clamp((弹药穿透力 - 有效护甲值) / 护甲值, 0, 1)       // 无护甲时视为 1

最终伤害       = 基础伤害 × 部位倍率 × (1 - 减伤率 × (1 - 穿透系数))
护甲耐久扣减   = 基础伤害 × 磨损系数
```

### 5.1 公式的三个可解释点

1. **穿透系数是连续的**，不是"击穿/未击穿"的二值判定。
   高穿透弹药打重甲时伤害更高，但没有哪个数值是"够用就行"的门槛。
2. **护甲磨损降低减伤，而不是让护甲突然失效**。
   耐久打到 0 时有效护甲值为 0，此时穿透系数恒为 1，等于没有护甲——
   但它是连续衰减过去的，玩家能感觉到"越打越脆"。
3. **无护甲时穿透系数取 1**，公式退化为 `基础伤害 × 部位倍率`，
   不需要为"没有护甲"写一条独立分支。

### 5.2 部位倍率（粗粒度两段式）

| 情况 | 倍率 |
| --- | --- |
| 普通命中 | 1.0 |
| 暴击命中 | 由 `CombatTuning.CriticalMultiplier` 决定，初始值 2.0 |

**暴击的判定方式（实现时对草案做了修正）**：判定"命中点在暴击轴上的投影是否超过阈值"，
其中暴击轴是**相机朝向在地面上的投影**，也就是屏幕上"向上"对应的世界方向。

草案原本写的是"命中点与目标中心的高度差"。实现时发现它在斜俯视下不可用：
准星落在地面平面上，射线几乎是贴着水平方向过去的，打在任何目标身上高度都差不多，
暴击要么恒成立要么恒不成立。

改成沿暴击轴判定之后，几何含义反而更直白：**瞄准目标远离相机的那一侧就是打上半身**。
它同样不要求像素级瞄准——目标身上有一条相当宽的暴击带，阈值取 0.25 米，
而灰盒靶子的半径是 0.4 米。

---

## 6. 射击流程

### 6.1 三种射击模式

| 模式 | 行为 |
| --- | --- |
| 单发 | 每次按下射一发 |
| 连发 | 每次按下射固定发数（由数据决定），中途松开不影响已开始的连发 |
| 全自动 | 按住持续射击 |

### 6.2 每发的处理顺序

```text
1. 检查是否在换弹       是则拒绝
2. 检查射击间隔是否已到  未到则拒绝（连发/全自动时会自然触发）
3. 检查弹匣是否有弹药    空则拒绝，并提示需要换弹
4. 扣一发弹匣弹药
5. 计算散布后的实际方向（当前散布 × 随机偏移）
6. 调用 IHitProbe 做射线检测
7. 命中则结算伤害；未命中则只广播"开了一枪"
8. 累加散布，重置射击间隔计时
```

**顺序是关键**：先扣弹药再判定命中，保证"开了枪就消耗弹药"这一直觉成立；
而失败路径全部在扣弹药之前返回，因此不会出现"没打出去但子弹少了"。

### 6.3 散布模型

```text
当前散布 = clamp(基础散布 + 连发数 × 每发增量, 基础散布, 最大散布)
```

停火后按散布恢复速度逐步回落到基础值。
随机偏移用 M0 的 `DeterministicRandom`，因此**同一随机种子下射击结果可复现**——
这是让"射击"能被自动化测试的前提。

---

## 7. 换弹与弹药消耗

换弹是战斗系统与背包系统的**接缝**，也是 M3 唯一需要读取背包的地方。

```text
1. 检查当前武器是否已满弹匣   是则拒绝
2. 检查背包中是否有匹配口径的弹药堆（AmmoStats.CaliberId 相同）  没有则拒绝
3. 进入换弹状态，持续 WeaponStats.ReloadSeconds 秒，期间不能射击
4. 计时结束：从背包扣除弹药，把弹匣补满
```

**部分装填**：背包里的弹药不足一个弹匣时，把能装的都装上，而不是拒绝换弹。
搜打撤的节奏里，玩家经常在弹药见底时仍需应战，"至少让我装三发"比"一发都装不了"友好得多。
扣弹药与补给弹匣是**同一次规则调用**，不存在"扣了弹药但没装上"的中间态。

---

## 8. 命令与事件

### 8.1 命令

| 命令 | 状态 | 说明 |
| --- | --- | --- |
| `PlayerFireIntent` | M0 已定义 | 射击意图。只携带瞄准方向，命中判定在权威侧 |
| `PlayerReloadIntent` | **M3 新增** | 换弹意图。不携带任何参数——用哪把枪、装多少发都由权威侧决定 |

### 8.2 结果码

| 结果码 | 含义 |
| --- | --- |
| `combat_no_weapon` | 主武器槽是空的 |
| `combat_magazine_empty` | 弹匣没有弹药 |
| `combat_fire_rate_limited` | 射速限制，本次射击被忽略 |
| `combat_reloading` | 正在换弹 |
| `combat_magazine_full` | 弹匣已满，无需换弹 |
| `combat_no_ammo` | 背包里没有匹配口径的弹药 |

### 8.3 事件（`RaidDemo.Combat` 发布，表现层订阅）

| 事件 | 载荷 | 订阅方 |
| --- | --- | --- |
| `WeaponFiredEvent` | 射手、枪口位置、射击方向、是否命中、命中点 | 表现层画弹道与枪口火光 |
| `DamageAppliedEvent` | 目标、伤害值、是否暴击、剩余生命 | HUD、受击反馈、AI（M4） |
| `TargetDestroyedEvent` | 目标标识 | 战局统计（M5） |
| `ReloadStateChangedEvent` | 是否正在换弹、进度 | HUD、换弹动画 |

---

## 9. 接口清单

> 本节是实现的契约。签名一经确定，表现层与测试按此编写。

### 9.1 伤害结算（纯逻辑）

```csharp
public readonly struct DamageRequest
{
    public float BaseDamage;        // 武器基础伤害
    public float Penetration;       // 弹药穿透力
    public bool IsCritical;         // 是否暴击
    public ArmorSnapshot Armor;     // 目标的护甲状态（可为"无护甲"）
}

public readonly struct ArmorSnapshot
{
    public int Level;               // 0 表示无护甲
    public float Durability;        // 当前耐久
    public float MaxDurability;     // 最大耐久
    public float WearFactor;        // 磨损系数
}

public readonly struct DamageOutcome
{
    public float Damage;            // 最终伤害
    public float ArmorDamage;       // 本次造成的护甲耐久损失
    public float PenetrationFactor; // 穿透系数，供调试与提示
    public bool IgnoredArmor;       // 穿透系数是否为 1
}

public static class DamageCalculator
{
    public static DamageOutcome Resolve(in DamageRequest request, CombatTuning tuning);
}

public static class ArmorTiers
{
    public const int MaxLevel = 4;
    public static float GetArmorValue(int level);
    public static float GetReduction(int level);
}

public sealed class CombatTuning
{
    public float CriticalMultiplier;      // 暴击倍率
    public Vector3 CriticalAxis;          // 暴击轴：相机朝向在地面的投影
    public float CriticalOffsetMeters;    // 暴击偏移阈值（米）
    public float DefaultWearFactor;       // 默认磨损系数
    public float DefaultPenetration;      // 找不到匹配弹药时假定的穿透力
}
```

> 每级护甲值放在 `ArmorTiers` 而不是 `CombatTuning`：
> 等级表（等级 → 护甲值 + 减伤率）是一个整体，拆成两处会让"3 级甲是多少"这个问题
> 需要查两个文件才能回答。

### 9.2 武器运行时（纯逻辑）

```csharp
public enum FireMode { Single, Burst, Auto }

public sealed class WeaponRuntime
{
    public int MagazineAmmo { get; }
    public bool IsReloading { get; }
    public float CurrentSpread { get; }

    public FireAttempt TryFire(float aimDegrees, float deltaTime);
    public void Tick(float deltaTime);
    public bool TryBeginReload(out string failureCode);
    public void CompleteReload(int availableAmmo, out int consumed);
}
```

### 9.3 引擎能力边界

```csharp
public interface IHitProbe
{
    bool TryRaycast(Vector3 origin, Vector3 direction, float maxDistance, out HitInfo hit);
}

public readonly struct HitInfo
{
    public int TargetId;              // 0 表示没有命中可受击目标
    public Vector3 Point;             // 命中点世界坐标
    public float TargetCenterHeight;  // 目标中心高度，用于暴击判定
    public float Distance;
}
```

---

## 10. 边界情况枚举（测试来源）

| # | 场景 | 期望 |
| --- | --- | --- |
| 1 | 无护甲目标被命中 | 伤害等于基础伤害 × 部位倍率，护甲扣减为 0 |
| 2 | 穿透力远低于护甲值 | 穿透系数 0，减伤全额生效 |
| 3 | 穿透力远高于护甲值 | 穿透系数 1，伤害不被减免 |
| 4 | 穿透力恰好等于护甲值 | 穿透系数 0，与"完全打不穿"一致 |
| 5 | 护甲耐久只剩一半 | 有效护甲值减半，穿透系数上升，伤害提高 |
| 6 | 护甲耐久归零 | 有效护甲值 0，穿透系数 1，等同于无护甲 |
| 7 | 暴击命中 | 伤害为基础值 × 暴击倍率，再走护甲结算 |
| 8 | 极端数值（0 伤害、负耐久、空弹匣） | 不抛异常、不产生负数伤害 |
| 9 | 弹匣打空后继续开火 | 返回失败且不扣弹药 |
| 10 | 射速限制内的第二次开火 | 返回失败，弹匣数量不变 |
| 11 | 背包弹药不足一个弹匣 | 部分装填，扣掉全部可用弹药 |
| 12 | 背包没有匹配口径的弹药 | 换弹失败，弹匣数量不变 |
| 13 | 换弹过程中开火 | 被拒绝 |
| 14 | 散布随连发扩大并停火后回落 | 数值符合公式，且不超过上限 |

> **守恒断言同样适用于战斗**：任何失败路径都不得改变弹匣数量与背包弹药数。
> 与背包系统一样，"物品凭空消失"在界面上极难发现，但守恒断言能立刻抓到。

---

## 11. 与其它里程碑的接口

| 里程碑 | 依赖方式 |
| --- | --- |
| M2 背包 | 从 `EquipmentLoadout.Get(PrimaryWeapon)` 取武器定义；换弹从背包扣除弹药物品 |
| 表现层 | 订阅 `WeaponFiredEvent` 画弹道；`PhysicsHitProbe` 提供射线能力 |
| M4 AI | AI 复用同一套 `WeaponRuntime` 与伤害结算，只是意图来源不同 |
| M5 战局 | 命中与击杀计入结算数据 |
| M9 联机 | 命中判定全部在服务端；客户端只发 `PlayerFireIntent` 并播放表现 |

---

## 12. 未决与推迟

| 编号 | 事项 | 结论 |
| --- | --- | --- |
| P-13 | 武器音效与开火 VFX | M7 |
| P-14 | 多武器快捷切换（数字键 1 / 2） | 输入资产已存在 Previous / Next 动作，但 M3 不做，避免与背包操作争抢 |
| P-15 | 弹道下坠与飞行时间 | 明确不做（见第 2 节设计前提） |

---

## 13. 实现落位与偏离记录

> 与 M2 文档同一约定：记录"文档里写的"与"代码里做的"之间所有有意为之的差异。

### 13.1 文件清单

| 程序集 | 文件 | 职责 |
| --- | --- | --- |
| `RaidDemo.Data` | `Items/ICombatStats.cs` | 武器 / 弹药 / 护甲参数契约与射击模式枚举 |
| `RaidDemo.Data.Content` | `WeaponStats.cs`、`AmmoStats.cs`、`ArmorStats.cs` | 三类战斗参数资产（挂在物品定义的 `ItemBehavior` 上） |
| `RaidDemo.Shared` | `Commands/CombatCommands.cs` | 换弹意图（射击意图在 M0 已定义） |
| `RaidDemo.Combat` | `DamageTypes.cs`、`ArmorTiers.cs`、`CombatTuning.cs`、`DamageCalculator.cs` | 伤害结算（纯逻辑） |
| | `IHitProbe.cs` | 射线能力契约（引擎边界） |
| | `WeaponRuntime.cs`、`PlayerWeapon.cs` | 弹匣、射速、散布、换弹状态 |
| | `CombatantState.cs`、`CombatWorld.cs` | 可受击单位与注册表 |
| | `AmmoReserve.cs` | 背包弹药查找与取用（与 M2 的唯一接缝） |
| | `CombatEvents.cs` | 四个事件 |
| | `PlayerWeaponController.cs` | 每帧驱动 + 两个命令处理器 |
| `RaidDemo.Presentation` | `Combat/PhysicsHitProbe.cs` | 用物理系统实现射线 |
| | `Combat/CombatTargetView.cs` | 灰盒靶子与受击反馈 |
| | `Combat/TracerRenderer.cs` | 弹道表现（对象池） |
| `RaidDemo.Bootstrap` | `SceneBootstrap.Combat.cs` | 战斗装配、靶子生成、每帧驱动 |
| | `Bootstrap/Editor/ItemContentBuilder.Combat.cs` | 武器 / 弹药 / 护甲的参数表与资产生成 |

### 13.2 与第 9 节接口清单的差异

| 差异 | 原因 |
| --- | --- |
| 暴击判定改为沿暴击轴投影 | 见 5.2 节。斜俯视下高度差不可用 |
| `CombatTuning` 去掉 `CriticalHeightThreshold` 与 `ArmorValuePerLevel`，新增 `CriticalAxis`、`CriticalOffsetMeters`、`DefaultPenetration` | 前者随暴击判定方式改变；后者归入 `ArmorTiers` 以保持等级表单点定义 |
| `HitInfo` 的 `TargetCenterHeight` 改为 `TargetCenter`（三维坐标） | 暴击需要方向性，只给高度不够 |
| `IItemDefinition` 新增 `WeaponStats` / `AmmoStats` / `ArmorStats` 三个访问器 | 战斗层需要从物品定义读到战斗参数，而 `IItemDefinition` 是纯数据契约，不能暴露 `ItemBehavior` 资产类型 |
| 新增 `PlayerWeaponController` | 开火与换弹共用同一个武器运行时，时间只能在**一处**推进。若两个处理器各自 Tick 一次，武器的冷却与换弹会以两倍速度推进 |

### 13.3 换弹完成后按住扳机会立即续射

这是**刻意保留的行为**，不是缺陷：全自动武器在弹药到位后立刻恢复火力，
符合玩家按住扳机时的预期。测试里专门有一条用例锁定它
（`Reload_WithTriggerHeld_ResumesFireImmediately`），因为这类"看起来像 bug 的正确行为"
最容易被后来者当成缺陷改掉。

不想要这个效果的话，应当在换弹开始时松开扳机——但那需要额外的规则，
而目前的取舍是"少一条规则比多一条规则好"。

### 13.4 灰盒靶子与战利品箱的临时安排

- **靶子**在运行时由启动层生成，共 4 个：正前方、左右两侧各一个，外加一个更远的。
  正前方必须有一个，否则玩家站定试枪会全部打空，看起来像射击没生效。
- **战利品箱排除背包类物品**：背包占地 9 到 16 格，两件就能吃光整箱空间，
  把武器与弹药挤出去，而后者才是验证射击链路必需的东西。背包的获取途径属于 M6。
