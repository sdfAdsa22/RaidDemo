using System;
using System.Collections.Generic;
using RaidDemo.Combat;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Kernel;
using RaidDemo.Shared;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器侧的战斗权威：为每名玩家持有一套武器控制器，开火与命中都在这里判定。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么复用客户端的控制器：</b><see cref="PlayerWeaponController"/> 本来就是纯 C#——
    /// 它只认识武器参数、碰撞查询接口与战斗世界，不认识 MonoBehaviour、相机与输入设备。
    /// 服务器直接构造同一份实现，两端"武器怎么打"的规则就不可能不一致。</para>
    ///
    /// <para><b>权威点在哪：</b>是否还有子弹、能不能换弹、这一枪打中了谁、造成多少伤害、
    /// 目标死没死——全部由这里决定。客户端只上报"我想开枪""我想换弹"与瞄准方向，
    /// 既不能自己扣血，也不能宣布命中。</para>
    ///
    /// <para><b>P2-1 的配发规则：</b>每名玩家按统一规格发一把 AK 与 120 发备弹。
    /// 真实装备来自服务端存档（P5）；现在先让"武器权威"这件事本身成立并可验证。</para>
    ///
    /// <para>本类不依赖网络库：网络层只负责把输入搬进来、把事件搬出去。</para>
    /// </remarks>
    public sealed partial class ServerCombatCoordinator
    {
        /// <summary>默认生命上限。</summary>
        public const float DefaultMaxHealth = 100f;

        /// <summary>P2-1 的配发武器与弹药。</summary>
        private const string DefaultWeaponId = "weapon.rifle.ak74";
        private const string DefaultAmmoId = "ammo.5.45.standard";
        private const int DefaultReserveRounds = 120;
        private const int AmmoPouchWidth = 5;
        private const int AmmoPouchHeight = 1;
        private const int BackpackWidth = 5;
        private const int BackpackHeight = 5;

        /// <summary>取玩家世界坐标的回调：服务器用它决定子弹从哪发出。</summary>
        private readonly Func<int, Vector3> m_OriginProvider;

        /// <summary>一名玩家的战斗相关状态。</summary>
        private sealed class Participant
        {
            public int PlayerId;
            public PlayerLoadout Loadout;
            public PlayerWeapon Weapon;
            public PlayerWeaponController Controller;
            public int CombatantId;
            public Vector2F AimDirection = Vector2F.Right;
            public bool TriggerHeld;
            public bool IsAlive = true;
        }

        private readonly ItemCatalog m_Catalog;
        private readonly EventBus m_Events;
        private readonly CombatWorld m_World = new CombatWorld();
        private readonly CombatTuning m_Tuning;
        private readonly IHitProbe m_Probe;
        private readonly ItemFactory m_Factory = new ItemFactory();
        private readonly Dictionary<int, Participant> m_Participants = new Dictionary<int, Participant>();
        private readonly Dictionary<int, int> m_CombatantToPlayer = new Dictionary<int, int>();

        /// <summary>创建战斗协调器。</summary>
        /// <param name="catalog">物品目录（提供武器与弹药的真实参数）。</param>
        /// <param name="events">会话事件总线：战斗结果通过它发布给网络层。</param>
        /// <param name="originProvider">玩家世界坐标提供者。</param>
        /// <param name="probe">命中查询实现（服务器侧用 PhysX）。</param>
        /// <param name="tuning">战斗调参。</param>
        public ServerCombatCoordinator(
            ItemCatalog catalog,
            EventBus events,
            Func<int, Vector3> originProvider,
            IHitProbe probe,
            CombatTuning tuning = null)
        {
            m_Catalog = catalog;
            m_Events = events;
            m_OriginProvider = originProvider;
            m_Probe = probe;
            m_Tuning = tuning ?? CombatTuning.Default;
        }

        /// <summary>当前参战玩家数。</summary>
        public int PlayerCount => m_Participants.Count;

        /// <summary>战斗世界（单位血量等），供调试与测试读取。</summary>
        public CombatWorld World => m_World;

        /// <summary>物品目录是否可用。为 false 时无法配发武器。</summary>
        public bool HasContent => m_Catalog != null;

        /// <summary>
        /// 让一名玩家参战：配发武器、登记战斗单位。
        /// </summary>
        /// <param name="playerId">玩家标识。</param>
        /// <param name="error">失败原因。</param>
        public bool TryAddPlayer(int playerId, out string error)
        {
            error = null;

            if (m_Participants.ContainsKey(playerId))
            {
                error = $"玩家 {playerId} 已参战。";
                return false;
            }

            if (m_Catalog == null || !m_Catalog.TryGet(DefaultWeaponId, out var weaponDefinition))
            {
                error = $"物品目录里找不到配发武器 {DefaultWeaponId}。";
                return false;
            }

            var loadout = BuildDefaultLoadout(weaponDefinition);
            var weapon = new PlayerWeapon(new DeterministicRandom((uint)(0x5EED0000 + playerId)));

            // 战斗单位先建：控制器的事件里带的是**战斗单位编号**，
            // 而它必须与 AI 的事件编号处在同一个空间里（否则玩家 1 的子弹会被记成敌人 0 开的火，
            // 因为两者的战斗世界编号恰好都是 1）。
            var combatantId = m_World.Create(DefaultMaxHealth);
            m_CombatantToPlayer[combatantId] = playerId;

            var controller = new PlayerWeaponController(
                weapon,
                loadout,
                m_Probe,
                m_World,
                m_Tuning,
                m_Events,
                ammoPouchContainerId: 0,
                playerId: playerId);

            controller.BindCombatant(combatantId);
            controller.SyncEquippedWeapon(weaponDefinition.WeaponStats);

            m_Participants[playerId] = new Participant
            {
                PlayerId = playerId,
                Loadout = loadout,
                Weapon = weapon,
                Controller = controller,
                CombatantId = combatantId,
            };

            return true;
        }

        /// <summary>让一名玩家退出战斗。</summary>
        public bool RemovePlayer(int playerId)
        {
            if (!m_Participants.TryGetValue(playerId, out var participant))
            {
                return false;
            }

            m_CombatantToPlayer.Remove(participant.CombatantId);
            m_World.Remove(participant.CombatantId);
            m_Participants.Remove(playerId);
            return true;
        }

        /// <summary>
        /// 某个战斗单位编号是否属于玩家。
        /// </summary>
        /// <remarks>
        /// 不能用 <c>GetPlayerId(combatantId) != 0</c> 代替：玩家 0（第一个连接的客户端）是合法编号，
        /// 那会把玩家 0 误判成"不认识这个单位"。联机时翻译编号的下游（敌人编号）依赖这条判断。
        /// </remarks>
        public bool IsPlayerCombatant(int combatantId)
        {
            return m_CombatantToPlayer.ContainsKey(combatantId);
        }

        /// <summary>
        /// 取某名玩家的随身装备（背包 / 弹药挂 / 装备槽）。
        /// </summary>
        /// <param name="playerId">玩家标识。</param>
        /// <param name="loadout">该玩家的随身装备。</param>
        /// <returns>玩家已参战时返回 true。</returns>
        /// <remarks>
        /// 服务器侧的背包命令要用它：命令里的"1 号容器"是**玩家自己的背包**，
        /// 而服务器上一张注册表放着所有人的容器，靠这份 loadout 才能把编号落到具体对象上。
        /// </remarks>
        public bool TryGetLoadout(int playerId, out RaidDemo.Inventory.PlayerLoadout loadout)
        {
            if (m_Participants.TryGetValue(playerId, out var participant))
            {
                loadout = participant.Loadout;
                return true;
            }

            loadout = null;
            return false;
        }

        /// <summary>
        /// 按玩家当前手持的武器重新同步武器参数。
        /// </summary>
        /// <param name="playerId">玩家标识。</param>
        /// <returns>同步成功返回 true。</returns>
        /// <remarks>
        /// <para>玩家换枪之后必须调用：射程、伤害、口径、弹匣容量全部来自"当前武器"，
        /// 而权威侧的武器控制器是构造时按配发武器建好的。</para>
        ///
        /// <para>找不到装备或目录里没有对应条目时保持现状：宁可继续用手上那把，
        /// 也不要让服务器变成"没有武器"。</para>
        /// </remarks>
        public bool SyncWeaponFromLoadout(int playerId)
        {
            if (!m_Participants.TryGetValue(playerId, out var participant) || m_Catalog == null)
            {
                return false;
            }

            var item = participant.Loadout?.Equipment?.Get(EquipmentSlot.PrimaryWeapon)
                       ?? participant.Loadout?.Equipment?.Get(EquipmentSlot.SecondaryWeapon);

            var definition = item?.Definition;
            if (definition == null || definition.WeaponStats == null)
            {
                return false;
            }

            participant.Controller.SyncEquippedWeapon(definition.WeaponStats);
            return true;
        }

        /// <summary>
        /// 接收一条战斗输入：扳机状态与瞄准方向。
        /// </summary>
        /// <remarks>
        /// 瞄准方向来自每帧上行的移动输入里的朝向字段——玩家"看哪"与"打哪"在俯视角下是同一件事，
        /// 不需要为此再开一条消息。
        /// </remarks>
        public bool SubmitInput(int playerId, bool triggerHeld, Vector2F aimDirection)
        {
            if (!m_Participants.TryGetValue(playerId, out var participant))
            {
                return false;
            }

            participant.TriggerHeld = triggerHeld;
            if (!aimDirection.IsNearlyZero)
            {
                participant.AimDirection = aimDirection.Normalized;
            }

            return true;
        }

        /// <summary>请求换弹。</summary>
        public bool RequestReload(int playerId, uint sequence, out string failureCode)
        {
            failureCode = null;

            if (!m_Participants.TryGetValue(playerId, out var participant))
            {
                failureCode = "not-in-combat";
                return false;
            }

            return participant.Controller.TryRequestReload(playerId, sequence, out failureCode);
        }

        /// <summary>按固定步长推进所有参战者的武器状态。</summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            foreach (var participant in m_Participants.Values)
            {
                if (!participant.IsAlive)
                {
                    continue;
                }

                // 子弹从玩家当前位置发出：俯视角下枪口高度对命中判定的影响可以忽略，
                // 但位置必须跟得上，否则掩体边缘的判定会不对。
                participant.Controller.SetMuzzlePosition(m_OriginProvider(participant.PlayerId));
                participant.Controller.SetAimDirection(participant.AimDirection);
                participant.Controller.SetTriggerHeld(participant.TriggerHeld);
                participant.Controller.Tick(deltaTime);
            }
        }

        /// <summary>
        /// 处理一次伤害结果，维护存活状态。
        /// </summary>
        /// <remarks>
        /// 由网络层在收到 <see cref="DamageAppliedEvent"/> 后调用：玩家被打死之后
        /// 必须停止推进他的武器，否则尸体还会继续开枪。
        /// </remarks>
        public void OnDamageApplied(in DamageAppliedEvent damage)
        {
            if (!damage.WasKilled)
            {
                return;
            }

            if (m_CombatantToPlayer.TryGetValue(damage.TargetId, out var playerId)
                && m_Participants.TryGetValue(playerId, out var participant))
            {
                participant.IsAlive = false;
                participant.Controller.SetAlive(false);
                participant.TriggerHeld = false;
            }
        }

        /// <summary>
        /// 取某名玩家对应的战斗单位标识。
        /// </summary>
        /// <param name="playerId">玩家标识。</param>
        /// <returns>战斗单位标识；该玩家未参战时返回 0。</returns>
        /// <remarks>
        /// 网络层需要它把"伤害打在哪个单位"翻译回"哪个玩家"，
        /// 才能把结果发给对应的人而不是广播一个内部标识。
        /// </remarks>
        public int GetCombatantId(int playerId)
        {
            return m_Participants.TryGetValue(playerId, out var participant) ? participant.CombatantId : 0;
        }

        /// <summary>
        /// 把战斗单位标识翻译回玩家标识。
        /// </summary>
        /// <param name="combatantId">战斗单位标识。</param>
        /// <returns>玩家标识；该单位不是玩家（例如将来的 AI）或不存在时返回 0。</returns>
        public int GetPlayerId(int combatantId)
        {
            return m_CombatantToPlayer.TryGetValue(combatantId, out var playerId) ? playerId : 0;
        }

        /// <summary>取某名玩家的弹药信息（弹匣 / 备弹），供验收与调试使用。</summary>
        public bool TryGetAmmo(int playerId, out int magazine, out int reserve)
        {
            magazine = 0;
            reserve = 0;

            if (!m_Participants.TryGetValue(playerId, out var participant))
            {
                return false;
            }

            var runtime = participant.Controller.Runtime;
            if (runtime == null)
            {
                return false;
            }

            magazine = runtime.MagazineAmmo;
            var caliber = runtime.Weapon.CaliberId;
            reserve = AmmoReserve.CountAvailable(participant.Loadout.AmmoPouch, caliber);
            return true;
        }

        /// <summary>
        /// 构造配发装备：主武器槽一把 AK、弹药挂里一叠备弹、一个空背包。
        /// </summary>
        /// <remarks>
        /// 背包与弹药挂都是真实容器：换弹会真的从这里扣子弹，
        /// 因此"服务器说没子弹了"与"玩家背包里还显示有子弹"这两件事必然一致。
        /// </remarks>
        private PlayerLoadout BuildDefaultLoadout(ItemDefinition weaponDefinition)
        {
            var backpack = new InventoryGrid(BackpackWidth, BackpackHeight, "服务端配发背包");
            var ammoPouch = new InventoryGrid(
                AmmoPouchWidth,
                AmmoPouchHeight,
                "服务端配发弹药挂",
                acceptedCategory: ItemCategory.Ammo);

            var equipment = new EquipmentLoadout();
            var weaponItem = m_Factory.Create(weaponDefinition);
            equipment.Equip(weaponItem, EquipmentSlot.PrimaryWeapon);

            if (m_Catalog.TryGet(DefaultAmmoId, out var ammoDefinition))
            {
                var ammoItem = m_Factory.Create(ammoDefinition, DefaultReserveRounds);
                ammoPouch.Place(ammoItem, new GridPoint(0, 0), rotated: false);
            }

            return new PlayerLoadout(backpack, equipment, ammoPouch);
        }
    }
}
