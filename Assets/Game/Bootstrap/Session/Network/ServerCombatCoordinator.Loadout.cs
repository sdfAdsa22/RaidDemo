using RaidDemo.Data;
using RaidDemo.Combat;
using RaidDemo.Inventory;
using RaidDemo.Kernel;
using RaidDemo.Shared;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器侧战斗权威的"装备配发"部分：决定一名玩家拿着什么进图。
    /// </summary>
    /// <remarks>
    /// <para>从主文件拆出来的原因：主文件已经承担"开火 / 命中 / 换弹 / 生死"的主体逻辑，
    /// 而"玩家该拿什么武器、备弹多少、背包多大"是内容规格而不是战斗流程，
    /// 改动原因完全不同（P5 起它还要接账号存档里的装备）。</para>
    ///
    /// <para><b>两条来源：</b>有账号进度时用玩家自己在安全屋准备好的装备
    /// （<see cref="ServerCombatCoordinator.TryAddPlayer(int, PlayerLoadout, out string)"/> 的 loadout 参数）；
    /// 没有时退回这里的默认配发——那是自动化验收里"从零开始"的路径，也是服务器没有存档时的兜底。</para>
    /// </remarks>
    public sealed partial class ServerCombatCoordinator
    {
        /// <summary>
        /// 参战的实现：决定拿什么武器、按需配发默认装备，并登记战斗单位与控制器。
        /// </summary>
        /// <param name="playerId">玩家标识。</param>
        /// <param name="loadout">随身装备；传 null 时按默认规格配发。</param>
        /// <param name="error">失败原因。</param>
        /// <remarks>
        /// 由 <c>ServerCombatCoordinator.TryAddPlayer</c> 的两个公开重载调用；
        /// 放在这里是因为它的一半内容是"装备从哪来"。
        /// </remarks>
        private bool TryAddPlayerCore(int playerId, PlayerLoadout loadout, out string error)
        {
            error = null;

            if (m_Participants.ContainsKey(playerId))
            {
                error = $"玩家 {playerId} 已参战。";
                return false;
            }

            if (m_Catalog == null)
            {
                error = "物品目录尚未就绪。";
                return false;
            }

            // 装备槽里的主武器决定弹道与弹药口径；没有武器时退回默认配发，
            // 否则玩家会"带着空手进图"且服务器不知道用什么武器结算（P5 起装备来自存档）。
            var weaponDefinition = ResolveWeaponDefinition(loadout, out var weaponError);
            if (weaponDefinition == null)
            {
                error = weaponError;
                return false;
            }

            loadout ??= BuildDefaultLoadout(weaponDefinition);

            var weapon = new PlayerWeapon(new DeterministicRandom((uint)(0x5EED0000 + playerId)));

            // 战斗单位先建：控制器的事件里带的是**战斗单位编号**，
            // 而它必须与 AI 的事件编号处在同一个空间里（否则玩家 1 的子弹会被记成敌人 0 开的火，
            // 因为两者的战斗世界编号恰好都是 1）。
            // isPlayer: true —— PVE 合作里"玩家之间不造成伤害"的判定依据（CombatRules）。
            var combatantId = m_World.Create(DefaultMaxHealth, armor: null, isPlayer: true);
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
            // 伤害规则用的射手编号必须和战斗单位一起绑（2026-09-16 修联机自伤）。
            // 只绑战斗单位时，"规则里的射手"会退回用玩家编号，而玩家编号与战斗单位编号
            // 是两个独立的编号空间（安全屋里靶子也占战斗单位编号）——
            // 于是"打中自己"与"打中靶子"在规则层分不出来，
            // 表现就是联机拿枪进安全屋一开火玩家倒地（负责人反馈的问题 6）。
            controller.BindCombatantForRules(combatantId);
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

        /// <summary>
        /// 取这名玩家该用哪件武器：优先装备槽里的主武器，其次副武器，最后退回默认配发武器。
        /// </summary>
        /// <param name="loadout">随身装备；可为 null。</param>
        /// <param name="error">失败原因。</param>
        /// <remarks>
        /// 武器决定弹道与弹药口径，因此"装备槽里有枪但服务器不知道用哪把"是必须避免的状态：
        /// 空手进图会让服务器按默认武器结算，两端对不上。
        /// </remarks>
        private IItemDefinition ResolveWeaponDefinition(PlayerLoadout loadout, out string error)
        {
            error = null;

            var equipment = loadout?.Equipment;
            if (equipment != null)
            {
                var primary = equipment.Get(EquipmentSlot.PrimaryWeapon);
                if (primary?.Definition != null && primary.Definition.WeaponStats != null)
                {
                    return primary.Definition;
                }

                var secondary = equipment.Get(EquipmentSlot.SecondaryWeapon);
                if (secondary?.Definition != null && secondary.Definition.WeaponStats != null)
                {
                    return secondary.Definition;
                }
            }

            if (m_Catalog.TryGet(ServerStarterKit.DefaultWeaponId, out var fallback))
            {
                return fallback;
            }

            error = $"物品目录里找不到配发武器 {ServerStarterKit.DefaultWeaponId}。";
            return null;
        }

        /// <summary>
        /// 构造配发装备：主武器槽一把 AK、弹药挂里一叠备弹、一个空背包。
        /// </summary>
        /// <remarks>
        /// 背包与弹药挂都是真实容器：换弹会真的从这里扣子弹，
        /// 因此"服务器说没子弹了"与"玩家背包里还显示有子弹"这两件事必然一致。
        /// 具体规格来自 <see cref="ServerStarterKit"/>——新账号建号时发的是同一套。
        /// </remarks>
        private PlayerLoadout BuildDefaultLoadout(IItemDefinition weaponDefinition)
        {
            var loadout = ServerStarterKit.Build(m_Catalog, m_Factory);

            // 目录里查不到默认武器时，至少让"玩家拿着的这件"进装备槽，
            // 否则会出现"参战成功但手上没有武器"的空转状态。
            if (weaponDefinition != null && loadout.Equipment.Get(EquipmentSlot.PrimaryWeapon) == null)
            {
                loadout.Equipment.Equip(m_Factory.Create(weaponDefinition), EquipmentSlot.PrimaryWeapon);
            }

            return loadout;
        }
    }
}
