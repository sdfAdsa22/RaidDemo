# 02 背包与物品系统（M2）

> 本文是 M2「数据与背包」的详细设计文档，按 `Docs/README.md` 的约定在里程碑开始时创建。
> 上游依据：`Docs/00_项目工程规范与系统设计.md` 第 7.1 节（物品系统）、7.2 节（背包系统）、7.13 节（体力与负重的耦合）。
>
> 阶段：M2 ｜ 状态：**已完成并通过验收**（2026-09-11）

---

## 1. 范围与完成判据

| 项目 | 内容 |
| --- | --- |
| 交付物 | `ItemDefinition` / `ItemInstance` / `ItemCatalog`、`InventoryGrid`、装备槽、拖拽 UI、旋转 / 堆叠 / 整理 |
| 完成判据 | 背包 10 项测试全绿；UI 可拖拽与旋转 |
| 实际结果 | M2 完成时全工程 **176 项 EditMode 测试全绿**，其中 M2 新增 62 项（物品实例 14、网格规则 23、负重 14、命令链路 11）；另有负重对移动的耦合测试 7 项 |
| 后续追加 | M3 期间又为弹药挂（13 项）与切换武器（5 项）补了测试，**截至 M3 结束全工程 236 项**（后续里程碑继续增加，最新总数见 `04_AI.md` 第 1 节） |

**验收记录（2026-09-11，项目负责人实机操作）**

验收过程暴露了 4 个缺陷，全部修复后复测通过。逐条记录见 `Docs/12_用户反馈问题记录.md` 的 U-08 ~ U-11：

| 验收项 | 结果 |
| --- | --- |
| 战利品箱 ↔ 背包 双向拖拽 | 通过（修复 U-08 后） |
| 双击快速搬运 | 通过（修复 U-09 后） |
| 拖拽中旋转、右键拆分、F 整理 | 通过 |
| 装备槽：头盔与护甲各自归位 | 通过（修复 U-10 后） |
| 负重条随拾取上升，进入重装后无法奔跑 | 通过 |
| 背包打开时准星隐藏、不遮挡面板 | 通过（修复 U-11 后） |

**本阶段不做**（属于推迟项，不在此重复设计）：武器改装树（P-01）、保险（P-03）、钥匙与门锁（P-04）、真实战利品容器与掉落表（M5）。

---

## 2. 数据模型

### 2.1 静态定义与运行时状态严格分离

| 类型 | 性质 | 所在程序集 | 持久化 |
| --- | --- | --- | --- |
| `IItemDefinition` | 只读契约，纯 C# | `RaidDemo.Data` | 否 |
| `ItemDefinition` | ScriptableObject，实现上述契约 | `RaidDemo.Data.Content` | 否（随工程提交） |
| `ItemInstance` | 运行时状态，每份物品独立 | `RaidDemo.Data` | 是 |
| `ItemCatalog` | 全物品索引资产 | `RaidDemo.Data.Content` | 否 |

**为什么把契约与资产分开**：纯逻辑层（`Data`）不允许引用引擎类型，因此不能直接持有 `ScriptableObject`。
`IItemDefinition` 把「背包规则需要知道的字段」抽象出来，运行时代码只依赖该接口，
于是网格规则可以在 EditMode 测试中脱离场景直接构造与验证。

### 2.2 落位规则（新增类型时的判断依据）

| 问题 | 结论 |
| --- | --- |
| 需要 `ScriptableObject` 或 `UnityEngine` 类型？ | 放 `RaidDemo.Data.Content` |
| 是纯数据与纯规则？ | 放 `RaidDemo.Data` |
| 是「一局之内的容器与装备状态」？ | 放 `RaidDemo.Inventory` |
| 是客户端与服务端共用的契约（命令、事件载荷）？ | 放 `RaidDemo.Shared` |

### 2.3 稳定 ID 规则

- 形如 `category.name.variant`，例如 `weapon.rifle.ak74`、`medical.bandage.small`。
- 存档只记录 ID 字符串，**重命名显示名、换图标、改数值都不会破坏旧存档**。
- ID 一经发布不得修改；修改等同删除旧物品并新增一件新物品。

### 2.4 稀有度（RarityTier）

物品按**价值档位**分五级，枚举为 `RarityTier`，序列化时以字符串写盘
（不写整数序号，避免将来插入新档位导致旧存档整体错位）。

| 档位 | 枚举值 | 颜色 | 十六进制 | 定位 | `BaseValue` 区间 |
| --- | --- | --- | --- | --- | --- |
| 普通 | `Common` | 白 | `#E8E8E8` | 遍地都是：弹药、基础材料、杂物 | 500 ~ 2,000 |
| 精良 | `Uncommon` | 绿 | `#4CCE5A` | 常见可用：初级装备、常用消耗品 | 2,000 ~ 8,000 |
| 稀有 | `Rare` | 蓝 | `#3E8BFF` | 值得为它多跑一间房 | 8,000 ~ 25,000 |
| 史诗 | `Epic` | 紫 | `#A855F7` | 战局目标级，看见就起贪心 | 25,000 ~ 80,000 |
| 传说 | `Legendary` | 金 | `#FFB300` | 极稀有，考验你撤不撤 | 80,000 以上 |

> **最高档用金色而不是红色**：射击游戏里红色被「敌人、危险、禁用」占用了语义，
> 再拿它表示「最值钱」会抢占视觉通道。金色是通用语感里的最高档。

**稀有度驱动什么**：UI 的格子边框与名称颜色（M2）、掉落表的抽取权重（M5）、
商人买价基准（M6）。**收购价不按稀有度浮动**，统一取基础价值的 75%——
否则稀有度会直接等于收益，玩家只需要看颜色，价值密度与稀有度的解耦随之失效。

**稀有度不驱动什么**（重要设计红线）：

- **不驱动武器伤害与护甲减伤**——那是 M3 的武器数值与 M6 的装备等级线，两条轴正交。
- **不做随机词条**（暗黑类那种"同型号随机属性"）——一旦稀有度等于强度，
  游戏就退化成刷装备，且搜索本身失去意义：玩家只捡紫色，白色物品变成垃圾。

> **稀有度与价值密度必须解耦。**一个占 1 格的白金表，可以比占 4 格的紫枪更值得拿——
> 正因为两者不同源，"要不要多拿一件"才是一个需要动脑的问题（见 7.12 节贪婪循环）。
> 若稀有度必然等于价值，背包决策就退化成"捡颜色"，玩法支柱随之瓦解。

**价值区间的校验范围**：只有 `ItemCategory.Loot`（值钱小物件）的 `BaseValue` 必须落在所属档位的区间内，
由 `ItemCatalog.Validate()` 在编辑器内提示。其余分类**豁免**——弹药单发 8 块、绷带一个 400 块，
它们按「每次使用的消耗量」定价，与整件物品的价值档位不是一个量纲，硬套区间只会逼着数值造假。
稀有度对它们表达的是「战场上多稀缺」，不是「值多少钱」。

与稀有度无关的另一条硬规则：**物品的占地格数独立于稀有度**，值钱小物件固定 1 格 1 件。

---

## 3. 网格规则

### 3.1 坐标与尺寸

| 类型 | 说明 |
| --- | --- |
| `GridPoint` | 整数二维坐标，原点 `(0,0)` 位于**左上角**，X 向右、Y 向下（与 UI 一致） |
| `GridSize` | 整数宽高，`Rotated()` 返回宽高互换的副本 |

> 用自建结构体而不是 `Vector2Int`，原因与 `Vector2F` 一致：`Data` 层不引用引擎类型，
> 且整数坐标在存取与网络传输时语义明确。

### 3.2 必须支持的规则

| 规则 | 说明 | 失败原因 |
| --- | --- | --- |
| 占用检测 | 物品按 `OccupiedSize` 占矩形区域，任一格被占用即冲突 | `Occupied` |
| 越界 | 放置区域超出容器边界 | `OutOfBounds` |
| 旋转 | 90° 旋转（宽高互换），旋转后重新做占用检测 | `RotationNotAllowed` |
| 堆叠合并 | 同 ID、可堆叠、未达 `MaxStack` 时合并，超出部分留在原处 | `StackLimit` |
| 拆分 | 拆分时指定数量，拆分出的新实例需自行放置 | `InvalidQuantity` |
| 快速转移 | 一键搬运，规则与拖拽**完全一致** | 同拖拽 |
| 自动整理 | 按尺寸从大到小重排，`O(格子数)` | — |
| 嵌套深度 | 容器中的容器，有限深度 ≤ 2 层 | `NestingTooDeep` |

> **拖拽与快速转移必须共用同一套规则代码**（见主文档 7.2 节）。
> 实现上只有一个入口 `InventoryGrid.TryTransfer`，快速转移只是「不指定落点」的调用方式，
> 因此不可能出现「拖拽被拦、快捷键却塞进去了」的分叉。

### 3.3 嵌套容器

背包中的背包按「深度」记账：直接放在角色背包里的容器为深度 1，容器内的容器为深度 2，
深度超过 `ContainerRules.MaxNestingDepth`（= 2）时拒绝放置。

> 限制深度的原因不是性能，而是**可理解性**：无限嵌套会让「我的东西在哪」变成一种负担，
> 而本项目的乐趣来自「要多拿一件还是现在就撤」，不是整理迷宫。
> 这条同样是防递归的保护：容器被放入自身时会形成环。

---

## 4. 装备槽

| 槽位 | 允许分类 | 说明 |
| --- | --- | --- |
| `PrimaryWeapon` | `Weapon` | 主武器，M3 的射击逻辑从此处取武器数据 |
| `SecondaryWeapon` | `Weapon` | 副武器 |
| `Head` | `Helmet` | 头盔 |
| `Body` | `BodyArmor` | 躯干护甲（防弹背心） |
| `Backpack` | `Backpack` | 背包，决定随身携带的网格容量 |

规则：

> **头盔与躯干护甲是两个分类，不是一个。**最初把两者合并为 `Armor`，
> 结果钢盔能穿在躯干上、防弹背心能戴在头上。分类是装备槽规则的唯一依据，
> 合并之后规则层就无从区分两者；拆开后槽位规则不需要任何额外字段即可表达正确语义。

- 槽位只接受对应分类，类型不符返回 `SlotTypeMismatch`；替换下来的物品需要有去处，放不下则拒绝整个操作。
- 装备中的物品同样计入总负重。
- `EquipmentLoadout` 不持有 UI，可在测试中直接构造。

---

## 5. 负重与体力的耦合

负重状态由「当前总负重 ÷ 承载上限」决定，三个档位对应主文档 7.13 节的体力耦合表：

| 负重状态 | 判定条件 | 能否奔跑 | 移动速度 | 体力回复 |
| --- | --- | --- | --- | --- |
| 轻装 | 负重比 < 0.70 | 可以 | 1.0× | 1.0× |
| 重装 | 0.70 ≤ 负重比 ≤ 1.00 | **不能** | 1.0× | 0.5× |
| 超重 | 负重比 > 1.00 | **不能** | 按下方公式衰减 | 0（完全不回复） |

超重的速度衰减公式（`ratio` 为负重比）：

```
speedMultiplier = Lerp(1.0, 0.4, Clamp01((ratio - 1.0) / 0.5))
```

即刚超过上限时几乎不减速，达到上限的 1.5 倍时降到 0.4× 并保持不再下降。

> **为什么是"先失去奔跑、再失去速度"的两段式，而不是一刀切**：
> 若只在 100% 处砍一刀，玩家在 99% 与 101% 之间的体验会断裂，感觉像踩到陷阱而不是自己做错了选择。
> 70% 处先拿走奔跑，是给玩家在装包阶段的预警信号；越过 100% 后再逐步拿速度，
> 让"越贪越慢"成为一个连续可感知的过程。这条与主文档 7.12 节的循环图一致——
> 贪婪的代价必须落在撤离能力上，否则玩家可以靠停停走走规避掉全部惩罚。
>
> **超重不禁止拾取**：负重是软约束，不是硬约束。若超重就禁止拾取，等于游戏替玩家做了
> "别贪了"的决定，而贪婪循环的全部乐趣恰恰来自这个决定由玩家自己做出。
> 游戏只负责让代价如实兑现。

实现上的耦合方式与 M1 保持一致：**负重只修改配置，不修改模拟逻辑**。

| 层 | 职责 |
| --- | --- |
| `RaidDemo.Shared` | `EncumbranceState` 枚举、`MovementModifiers` 结构（速度倍率 / 体力回复倍率 / 能否奔跑） |
| `RaidDemo.Inventory` | `EncumbranceProfile` 阈值、`EncumbranceRules` 判定与倍率表 |
| `RaidDemo.Simulation` | `PlayerMovementProfile.ApplyModifiers`，把倍率乘进体力上限 / 消耗 / 速度 |
| `RaidDemo.Bootstrap` | 组装：负重变化 → 计算倍率 → 应用到移动配置 |

> 这样 `Simulation` 不需要知道背包存在，`Inventory` 也不需要知道移动存在，
> 两者仅在 `Bootstrap` 这一处相遇。M9 时同一套判定的服务端版本无需改动逻辑。

---

## 6. 命令与事件

### 6.1 命令（`RaidDemo.Shared`）

所有背包操作都是**玩家意图**，经由 `CommandRouter` 执行，客户端不直接改容器状态。

| 命令 | 用途 |
| --- | --- |
| `InventoryMoveIntent` | 容器间移动，可携带旋转意图（拖拽的落点与旋转都由此表达） |
| `InventoryQuickTransferIntent` | 快速转移（不指定落点，由规则寻找位置） |
| `InventoryRotateIntent` | 原地旋转 |
| `InventorySplitIntent` | 拆分堆叠 |
| `InventorySortIntent` | 自动整理 |
| `InventoryEquipIntent` / `InventoryUnequipIntent` | 装备与卸下 |
| `InventoryDropIntent` | 丢弃到地面（M5 由战局接管生成掉落物） |
| `PlayerPickupIntent` | 已在 M0 定义，M2 起由背包处理器实现 |

### 6.2 结果码（追加到 `CommandCodes`）

| 结果码 | 含义 |
| --- | --- |
| `inventory_not_found` | 容器或格子不存在 |
| `inventory_full` | 目标容器没有可用空间 |
| `inventory_occupied` | 目标格被占用 |
| `inventory_out_of_bounds` | 目标格越界 |
| `inventory_stack_limit` | 堆叠已达上限 |
| `inventory_invalid_quantity` | 数量非法（≤0 或超过持有量） |
| `inventory_slot_mismatch` | 装备槽不接受该分类 |
| `inventory_nesting_too_deep` | 容器嵌套超过深度上限 |

### 6.3 事件（`RaidDemo.Inventory` 发布，UI 订阅）

| 事件 | 载荷 | 订阅方 |
| --- | --- | --- |
| `InventoryChangedEvent` | 容器 ID、变更类型 | UI 刷新 |
| `EncumbranceChangedEvent` | 负重状态、当前重量、上限 | HUD、AI（M4 的听觉） |

---

## 7. 接口清单

> 本节是实现的契约，签名一经确定，UI 与测试按此编写；改签名需要同步改本文档。

### 7.1 `RaidDemo.Data`

```csharp
public readonly struct GridPoint { public int X; public int Y; }
public readonly struct GridSize { public int Width; public int Height; public GridSize Rotated(); }

public enum ItemCategory { Weapon, Ammo, Medical, Armor, Backpack, Loot, Key, Quest }
public enum RarityTier { Common, Uncommon, Rare, Epic, Legendary }

public interface IItemDefinition
{
    string Id { get; }
    string DisplayName { get; }
    ItemCategory Category { get; }
    RarityTier Rarity { get; }
    GridSize GridSize { get; }
    float WeightKg { get; }
    int BaseValue { get; }
    int MaxStack { get; }
    bool CanRotate { get; }
    bool IsContainer { get; }
    GridSize ContainerGridSize { get; }
}

public sealed class ItemInstance
{
    public IItemDefinition Definition { get; }
    public int InstanceId { get; }             // 工厂分配的自增号，仅用于区分个体与事件寻址
    public int StackCount { get; }
    public ItemState State { get; }            // 独立状态载荷，M2 恒为 null（见下方说明）
    public bool Rotated { get; set; }          // 由容器在放置时写入
    public GridSize OccupiedSize { get; }      // 已按 Rotated 换算
    public float WeightKg { get; }
    public int TotalValue { get; }
    public bool CanStackWith(ItemInstance other);
    public int AddToStack(int count);          // 返回实际加入的数量
    public ItemInstance Split(int count);      // 返回新实例，放置由调用方负责
}

/// <summary>独立状态的载荷基类。M2 不实现任何具体子类。</summary>
public abstract class ItemState { public abstract bool ContentEquals(ItemState other); }

public sealed class ItemFactory { public ItemInstance Create(IItemDefinition definition, int count = 1); }
public static class ContainerRules { public const int MaxNestingDepth = 2; }
```

#### 关于独立状态（`ItemState`）的取舍

**独立状态**指同一型号的两件物品在数据上可以不同——例如"打了 300 发的 AK"与"全新的 AK"。
本项目采用**内联状态**：状态载荷挂在 `ItemInstance` 上，**不引入全局实例注册表**。

理由是两者的成本差别：状态字段本身很便宜，贵的是"用 ID 去别处查表"所带来的悬垂引用、
ID 分配与持久化问题。而格子坐标本身就是物品位置的唯一标识——"把 3 号格的枪移到 7 号格"
用坐标即可定位，不需要物品身份证。因此 `InstanceId` 只用于**同一帧内的个体区分与事件寻址**，
它不是任何字典的键。

**堆叠规则从第一天就写成"状态不相同则不可堆叠"**（`CanStackWith` 内比较 `State`）。
M2 阶段所有物品的 `State` 都是 `null`，该条件恒为真、等价于"同类全能堆"；
等 M3 接入武器装弹数与护甲耐久时，只需填充该字段，**容器、拖拽、存档与同步层一行都不用改**。

> 这等于花 M2 阶段约 50 行代码，买 M3 阶段的零重构。

### 7.2 `RaidDemo.Data.Content`

```csharp
public sealed class ItemDefinition : ScriptableObject, IItemDefinition { /* 序列化字段见 7.1 节主文档 */ }
public sealed class ItemCatalog : ScriptableObject
{
    public IReadOnlyList<ItemDefinition> All { get; }
    public bool TryGet(string id, out ItemDefinition definition);
    public IReadOnlyList<string> Validate();   // 空 ID、重复 ID、尺寸非正等
}
public abstract class ItemBehavior : ScriptableObject { }   // 扩展点，M2 不实现具体行为
```

### 7.3 `RaidDemo.Inventory`

```csharp
public enum InventoryFailure { None, OutOfBounds, Occupied, StackLimit, InvalidQuantity,
                               RotationNotAllowed, NestingTooDeep, SlotTypeMismatch, NotFound }

public readonly struct InventoryResult
{
    public bool Success { get; }
    public InventoryFailure Failure { get; }
    public string Message { get; }
    public int MovedCount { get; }      // 实际搬运 / 合并的数量
}

public sealed class InventoryGrid
{
    public InventoryGrid(int width, int height, string label = null);
    public int Width { get; } public int Height { get; }
    public string Label { get; }
    public IReadOnlyList<ItemInstance> Items { get; }
    public float TotalWeightKg { get; }
    public ItemInstance GetAt(GridPoint cell);
    public bool TryGetOrigin(ItemInstance item, out GridPoint origin);
    public InventoryResult CanPlace(ItemInstance item, GridPoint origin, bool rotated);
    public InventoryResult Place(ItemInstance item, GridPoint origin, bool rotated);
    public InventoryResult AutoPlace(ItemInstance item);              // 先尝试合并，再寻找空位
    public InventoryResult Transfer(ItemInstance item, InventoryGrid target, GridPoint origin, bool rotated);
    public InventoryResult QuickTransfer(ItemInstance item, InventoryGrid target);
    public InventoryResult Rotate(ItemInstance item);
    public InventoryResult Split(ItemInstance item, int count, InventoryGrid target, GridPoint origin, bool rotated);
    public InventoryResult Remove(ItemInstance item);
    public void Sort();                                              // 按尺寸降序整理
}

public enum EquipmentSlot { PrimaryWeapon, SecondaryWeapon, Head, Body, Backpack }
public sealed class EquipmentLoadout { public InventoryResult Equip(ItemInstance item, EquipmentSlot slot);
                                       public ItemInstance Get(EquipmentSlot slot);
                                       public InventoryResult Unequip(EquipmentSlot slot, InventoryGrid target);
                                       public float TotalWeightKg { get; } }

public sealed class PlayerLoadout   // 角色随身：主背包网格 + 装备槽
{
    public InventoryGrid Backpack { get; }
    public EquipmentLoadout Equipment { get; }
    public float TotalWeightKg { get; }
    public EncumbranceState EvaluateState(EncumbranceProfile profile);
}

public sealed class EncumbranceProfile
{
    public float CapacityKg = 50f;           // 承载上限（千克）；见 11.5 节的三次调整
    public float OverloadSpanRatio = 0.5f;   // 超重衰减区间：1.0 倍上限衰减到 1.5 倍上限触底
}

public static class EncumbranceRules
{
    public const float HeavyRatio = 0.70f;      // 重装阈值：失去奔跑能力
    public const float OverloadSpeedFloor = 0.4f;
    public static EncumbranceState Evaluate(float weightKg, EncumbranceProfile profile);
    public static MovementModifiers ResolveModifiers(EncumbranceState state, float overRatio);
}

// RaidDemo.Shared：客户端与服务端共用
public enum EncumbranceState { Light, Heavy, Overloaded }

public readonly struct MovementModifiers
{
    public float SpeedMultiplier { get; }          // 乘进步行与奔跑速度
    public float StaminaRegenMultiplier { get; }   // 乘进体力恢复速度，0 表示不回复
    public bool CanSprint { get; }                 // false 时忽略奔跑意图
    public static MovementModifiers Default { get; }
}

public sealed class ContainerRegistry     // containerId → 网格，命令按 ID 定位容器
{
    public int Register(InventoryGrid grid, ContainerKind kind);
    public bool TryGetGrid(int containerId, out InventoryGrid grid);
}
```

### 7.4 命令处理器

每个命令一个处理器类，注册到 `CommandRouter`；处理器只做「解析命令 → 调用规则 → 发布事件」，
不承载规则本身（规则在 `InventoryGrid` 等类型中，保证拖拽与快捷键走同一条路径）。

---

## 8. 边界情况枚举（测试用例来源）

| # | 场景 | 期望 |
| --- | --- | --- |
| 1 | 物品尺寸超出容器 | `OutOfBounds`，容器状态不变 |
| 2 | 目标格已被占用 | `Occupied`，双方位置不变 |
| 3 | 部分重叠 | 判定失败，不允许「压一半」 |
| 4 | 旋转后由放不下变为放得下 | 成功，`OccupiedSize` 宽高互换 |
| 5 | `CanRotate = false` 的物品请求旋转 | `RotationNotAllowed` |
| 6 | 堆叠合并未超上限 | 合并到已有实例，源实例消失 |
| 7 | 堆叠合并超过上限 | 新实例保留剩余数量，不丢物品 |
| 8 | 拆分数量为 0 或超过持有量 | `InvalidQuantity` |
| 9 | 拆分后分别占位 | 两个实例各自可被查询到 |
| 10 | 快速转移空间不足 | `inventory_full`，**原容器状态完全不变**（原子性） |
| 11 | 容器放入自身 | 拒绝，不产生环 |
| 12 | 嵌套深度超过 2 | `NestingTooDeep` |
| 13 | 自动整理后总占位不变 | 物品数量与总重量守恒 |
| 14 | 装备槽类型不符 | `SlotTypeMismatch`，槽位不变 |
| 15 | 负重比恰为 0.70 | 判定为重装（阈值取闭区间下界），奔跑被拒绝 |
| 16 | 负重比恰为 1.00 与 1.01 | 分别在重装与超重两侧，1.00 倍不减速 |
| 17 | 负重比达到 1.5 倍上限 | 速度倍率触底 0.4×，继续增重不再下降 |
| 18 | 重装与超重的体力回复 | 分别为 0.5× 与 0（完全不回复） |
| 19 | 物品售价超出所属稀有度区间 | `ItemCatalog.Validate()` 报出该条，但不阻止运行 |

> **守恒断言是本系统的核心测试思路**：无论操作成功还是失败，物品数量与总重量都必须守恒。
> 一个「物品凭空消失」的 bug 在 UI 上极难发现，但在守恒断言下会立刻暴露。

---

## 9. 与其它里程碑的接口

| 里程碑 | 依赖方式 |
| --- | --- |
| M3 战斗 | 从 `EquipmentLoadout.Get(PrimaryWeapon)` 取武器定义；弹药从背包中的 `Ammo` 物品扣除 |
| M5 战局 | 战利品容器复用 `InventoryGrid`，掉落表产出 `ItemDefinition`，`PlayerPickupIntent` 由容器处理器实现 |
| M6 局外 | 仓库是一个 10×8 的 `InventoryGrid`；商人买价 = `BaseValue`，收购价 = `BaseValue` × 75% |
| M9 联机 | 容器与装备槽状态在服务端，客户端只发命令；`InstanceId` 用于同步与回滚 |

---

## 10. 未决与推迟

| 编号 | 事项 | 结论 |
| --- | --- | --- |
| P-09 | 负重的移动速度惩罚数值（超重时 1.0× 线性降至 0.4×） | 初始值，待 M5 战局闭环后按体感调整 |
| P-10 | `ItemInstance` 的弹药余量 / 已装配件列表 | 随 M3、改装树（P-01）一并补齐；字段预留但不实现 |
| P-11 | 战利品容器实体（箱子）与掉落表 | M5 |
| — | 背包 UI 的最终视觉与动画 | M7 打磨阶段 |
| P-12 | 界面文本改用 TextMeshPro | M7。当前工程未导入 TMP 基础资源，也没有中文字体资产；M2 用旧版 Text 加系统字体（见 11.6） |

---

## 11. 实现落位与偏离记录

> 本节在 M2 实现完成后补写，记录"文档里写的"与"代码里做的"之间所有有意为之的差异。
> 目的是让后来者（包括几个月后的自己）不必靠读 diff 才能理解某处为什么与设计不同。

### 11.1 文件清单

| 程序集 | 文件 | 职责 |
| --- | --- | --- |
| `RaidDemo.Data` | `Items/GridGeometry.cs` | `GridPoint`、`GridSize` |
| | `Items/ItemEnums.cs` | `ItemCategory`、`RarityTier`、`RarityValueBands` |
| | `Items/IItemDefinition.cs` | 物品定义契约 |
| | `Items/ItemState.cs` | 独立状态载荷基类（M2 无子类） |
| | `Items/ItemInstance.cs` | 运行时实例：堆叠、拆分、重量、价值 |
| | `Items/ItemFactory.cs` | 实例工厂与实例号分配 |
| | `Items/ContainerRules.cs` | 嵌套深度上限 |
| `RaidDemo.Shared` | `Data/EncumbranceTypes.cs` | `EncumbranceState`、`MovementModifiers` |
| | `Data/EquipmentSlots.cs` | `EquipmentSlot` |
| | `Commands/InventoryCommands.cs` | 移动、快速转移、旋转、拆分意图 |
| | `Commands/InventoryEquipmentCommands.cs` | 整理、装备、卸下意图 |
| `RaidDemo.Data.Content` | `ItemDefinition.cs` | 物品定义资产与自校验 |
| | `ItemCatalog.cs` | 全物品索引与目录级校验 |
| | `ItemBehavior.cs` | 特殊行为扩展点（M2 无实现） |
| | `RarityPalette.cs` | 稀有度配色与中文名 |
| `RaidDemo.Inventory` | `InventoryGrid.cs` 等三个 partial 文件 | 网格容器规则核心 |
| | `InventoryResult.cs` | 失败原因枚举与结果结构 |
| | `EquipmentLoadout.cs`、`PlayerLoadout.cs` | 装备槽与角色携带物 |
| | `EncumbranceProfile.cs`、`EncumbranceRules.cs` | 负重判定与倍率 |
| | `ContainerRegistry.cs` | 容器 ID 索引与嵌套网格 |
| | `InventoryEvents.cs` | 两个变更事件 |
| | `InventoryContext.cs` | 处理器共享上下文 |
| | `InventoryCommandHandlers.cs`、`InventoryEquipCommandHandlers.cs` | 七个命令处理器 |
| `RaidDemo.UI` | `Inventory/InventoryGridView.cs` | 单个网格容器的视图 |
| | `Inventory/ItemCellView.cs` | 单件物品的色块与文本 |
| | `Inventory/InventoryScreenController.cs` 等三个 partial 文件 | 装配、布局、拖拽 |
| | `Inventory/UiFontProvider.cs` | 运行时字体 |
| `RaidDemo.Bootstrap` | `SceneBootstrap.Inventory.cs` | 背包装配（partial 拆分） |
| | `Bootstrap/Editor/ItemContentBuilder.cs` | 初始物品资产生成器 |

### 11.2 与第 7 节接口清单的差异

| 差异 | 原因 |
| --- | --- |
| `InventoryFailure` 增加了 `Full` | 7.3 节漏了这一项，而 8.3 节又要求区分"格子被占"与"整体放不下"，两者必须分开 |
| `InventoryGrid` 增加了 `CanAutoPlace`、`Split(item, count)`、`RemoveAt`、`Depth`、`HostItem` | 装备替换需要"先确认退路再动手"；不指定落点的拆分需要在界面里一键完成；后两项用于嵌套检测 |
| `EquipmentLoadout.Equip` 增加可选参数 `returnTo` | 换装时旧装备必须有去处。原签名无法表达这件事，会导致旧装备凭空消失 |
| `ItemCategory.Armor` 拆成 `Helmet` 与 `BodyArmor` | 4 节的槽位表原本让头盔与躯干护甲共用 `Armor`，导致两者可以互相装备。分类是槽位规则的唯一依据，必须做精确 |
| `PlayerLoadout` 增加 `EncumbranceRatio`、`EvaluateModifiers` | 装配层每帧要用，放在这里避免调用处重复写两步流程 |
| `MovementModifiers` 需要三参构造函数 | 测试与装配都要构造具体倍率，只给 `Default` 无法覆盖 |

### 11.3 合并策略的最终语义

文档 3.2 节写的是"超出部分留在原处"，实现遵循了这一点，并把一键转移与之区分开：

| 路径 | 落到已有同类堆上时 |
| --- | --- |
| 拖拽（`Transfer`） | 能并多少并多少，**并剩下的留在原处** |
| 一键转移（`QuickTransfer`） | 只有整堆装得下才合并，否则改为寻找空位；两者都不可行才算失败 |

之所以不同：一键转移没有落点信息，让半个堆留在原处会让玩家以为操作失败。

### 11.4 界面的输入方案

**没有使用 uGUI 的 EventSystem 与 DragHandler**，而是直接用指针位置换算格子坐标。

这样做有三个好处：不引入 `InputSystemUIInputModule` 的额外配置；拖拽预览天然拿到"落在哪一格"这种格子级信息；
界面可以完全脱离 EventSystem 运行，减少一个在灰盒阶段完全不必要的依赖。

界面对游戏数据**只读**：所有操作都翻译成命令经由 `CommandRouter` 执行，
再由 `InventoryChangedEvent` 触发重画。界面里没有任何一处直接修改容器。

### 11.5 负重上限的三次调整（30 → 20 → 50 千克）

| 时间 | 上限 | 依据 |
| --- | --- | --- |
| 设计初稿 | 30 kg | 拍脑袋估的 |
| M2 实现 | **20 kg** | 按当时的物品重量回算：护甲 8.5 + 头盔 2.0 + 步枪 4.2 + 突击背包 3.0 已是 17.7 kg，再拿一件值钱小物件就越线。当时的目标是「让负重在对局里看得出效果」 |
| M5.6（2026-09-12） | **50 kg** | 那时还没有真正的战局，副作用看不出来；M5 闭环跑通后实测发现**光穿一套基础装备就进入重装**，玩家还没开始搜刮就跑不动了。同时把装备减重（背心 5.0 / 头盔 1.2 / 步枪 3.2 / 突击背包 2.5），一套基础装约 11.9 kg |

> **教训**：M2 那次的推理在当时是对的——「上限必须落在稍微贪一点就会超的位置」。
> 错的是它拿**灰盒阶段的物品重量**当成了长期数值，而那时没有真实的对局压力来校验。
> 数值结论要标注它依赖的前提，否则下一个里程碑会照着过期的前提继续做决定。

### 11.6 弹药挂与分类过滤容器

M3 阶段按负责人要求加入了**弹药挂**：一行五格，只收弹药。

实现方式是给 `InventoryGrid` 增加一个可选的**分类过滤**（`acceptedCategory`）：

```csharp
var pouch = new InventoryGrid(5, 1, "弹药挂", acceptedCategory: ItemCategory.Ammo);
```

**这是 M2 架构的一次兑现**：弹药挂没有专属代码——它是网格规则的一个实例，
拖拽、堆叠、拆分、整理、快速转移全部自动可用，只多了一条"分类不对就拒收"的规则。

**分类检查必须放在寻位之前。**第一版把它放在放置校验里，而 `AutoPlace` 与
`QuickTransfer` 在寻位失败时统一报"没空间"，把"这个容器根本不收这类东西"
这个真正的原因吞掉了。现在 `AutoPlace`、`QuickTransfer` 都会先做分类检查，
并回报专职的结果码 `inventory_category_not_allowed`。

### 11.7 双击的目标决策（`QuickActionResolver`）

双击从"移入另一个容器"升级为一套有优先级的规则，抽成独立的
`QuickActionResolver`（`RaidDemo.Inventory`），**不写在界面里**——
它有多条分支与回退路径，值得被单元测试逐条锁死。

| 物品 | 优先动作 | 回退 |
| --- | --- | --- |
| 弹药 | 进弹药挂 | 弹药挂满 → 进背包 |
| 武器 | 主武器槽 | 主武器槽被占 → 副武器槽；两个都满 → **替换主武器** |
| 头盔 / 护甲 / 背包 | 对应槽位 | 槽位被占 → 替换（旧装备回背包） |
| 其它 | 在背包与战利品箱之间搬运 | —— |

> 武器两个槽都满时选择替换**主武器**，因为双击表达的意图是"我要用这个"，
> 替换当前手持的那把最符合直觉。

### 11.8 界面字体

工程未导入 TextMeshPro 的基础资源，也没有任何中文字体资产，而界面需要显示中文。
因此 M2 用旧版 `UnityEngine.UI.Text`，字体通过 `Font.CreateDynamicFontFromOSFont` 从操作系统获取。
本项目只发布 Windows 版（决策 D-15），系统字体必然存在，同时避免了往仓库里提交几十兆的字体文件。
**这属于灰盒阶段的临时方案**，M7 换美术时统一迁移到 TMP 与正式字体资源（见 P-12）。

---

## 12. M5.5 增量：右键上下文菜单与物品使用

> 这两项是在 M5 战局闭环之后追加的：前者补上了「物品能做哪些事」的入口，
> 后者让医疗品从「占格子的钱」变成真正的资源。

### 12.1 右键菜单

**旧行为**：右键即执行——可堆叠就拆一半，装备槽就卸下。动作少的时候很快，但没地方放新动作。

| 入口 | 行为 |
| --- | --- |
| 右键 | 弹出菜单：**使用 / 装备 / 卸下 / 拆分** |
| `Shift + 右键` | 直接拆一半（保留快捷路径：拆分是整理背包的高频动作） |
| 点击空白 / `Esc` / 再次右键 | 关闭菜单 |

**可用性规则**（不可用时**灰显而不是隐藏**）：

| 选项 | 可用条件 |
| --- | --- |
| 使用 | 物品是医疗品，且位于**自己的**背包或弹药挂（战利品箱里不行） |
| 装备 | 分类是武器 / 头盔 / 护甲 / 背包（具体装到哪个槽由快速操作规则决定，与双击一致） |
| 卸下 | 目标是一个装备槽 |
| 拆分 | 可堆叠且数量大于 1 |

> **为什么战利品箱里不能用**：站在别人箱子上给自己打针是很荒唐的行为；
> 而「必须先拿进包里」这条约束顺带给搜刮加了一点决策成本。

> **为什么灰显而不是隐藏**：隐藏会让玩家以为功能不存在，灰显等于告诉他
> 「这里有这个动作，只是现在不能用」。

### 12.2 物品使用（医疗）

数据用 M2 就预留好的 `ItemBehavior` 扩展点描述：`MedicalBehavior` 只需两个数值——
回血量与读条时长。加一种止痛药 = 建一个资产，逻辑代码一行不用改。

| 物品 | 回血量 | 读条时长 |
| --- | --- | --- |
| 小绷带 | 25 | 1.5 秒 |
| 野战医疗包 | 70 | 3 秒 |

**规则**（`RaidDemo.Raid.ItemUseInteraction`，纯逻辑可测）：

| 规则 | 内容 | 理由 |
| --- | --- | --- |
| 入口 | `H` 键（不打开背包也能用），或右键菜单的「使用」 | 打起来没时间翻包 |
| 移动 | **允许边走边用** | 只剩一丝血还要站住包扎，等于把救命变成自杀 |
| 打断 | **受伤即打断，且不消耗物品** | 扣了东西又没回血是最让人恼火的失败，受伤本身已是代价 |
| 取消 | 读条中再按一次 `H` | —— |
| 选药 | **优先用刚好补满缺口的最小那件**，都不够时用回血最多的 | 常态省下医疗包，急救时才自动切大件 |

**两侧入口共用一条路径**：菜单的「使用」由装配层注入回调，最终与 `H` 键调用同一个
`ItemUseInteraction.TryBegin`——否则迟早出现「快捷键能用、菜单点了没反应」这种分叉。

> **一个实现上的坑**：`IItemDefinition` 接口刻意不认识 `ItemBehavior`
> （行为是 Unity 资产类型，挂进接口会让数据层反向依赖内容层），
> 因此取行为必须走物品目录 `ItemCatalog.Get(id).Behavior`，不能从物品实例上直接拿。

### 12.3 背包决定随身容量（M5.6）

在 M5.6 之前，随身网格是开战时按固定 5×5 创建的常量，**装备背包不改变任何东西**——
小包与突击包都只是躺在装备槽里的装饰品。

| 状态 | 随身网格 |
| --- | --- |
| 没有装备背包 | **5×5 口袋（25 格）** |
| 小型背包 | 6×6（36 格） |
| 突击背包 | 7×7（49 格） |

**换包规则**：先把现有物品搬进新网格，**任何一件放不下就放弃这次调整**并保留原网格。
项目里没有地面掉落系统，让物品凭空消失是不可接受的失败方式；
因此缩容只会在「东西刚好放得下」时发生。

> **已知放宽**：背包塞满时把它卸下来，容量会暂时保持不变（自愈：等你腾出空间后，
> 下一次变化会重新尝试缩容）。正式版应当改为「拒绝卸下并给出提示」，
> 那需要一条界面提示通路，留待后续。

**实现要点**：

| 要点 | 原因 |
| --- | --- |
| 容器 ID 保持不变（`ContainerRegistry.Replace`） | 命令里携带的是容器 ID，ID 一变全部失效 |
| `PlayerLoadout.ReplaceBackpack` 只替换、不搬运 | 「装不下怎么办」属于规则，不该藏在 setter 里 |
| 容量变化后**整块重建界面** | 弹药挂与战利品面板的位置是按背包高度推算的，只换一个视图会互相压住 |
| 重建时要恢复「打开着的战利品面板」 | 否则面板消失，而且状态里还留着旧 ID，重搜同一个箱子也不会重建（见排障 M5-P-14） |
