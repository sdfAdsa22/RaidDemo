using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Kernel;
using RaidDemo.Shared;
using UnityEngine;

namespace RaidDemo.Combat
{
    /// <summary>
    /// 玩家武器的每帧驱动：推进扳机与换弹，把结果翻译成射击与伤害事件。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么开火与换弹共用一个控制器而不是两个独立的处理器各管一半：</b>
    /// 两者都要推进同一个 <see cref="WeaponRuntime"/> 的时间。如果各自调一次 Tick，
    /// 武器的冷却与换弹计时会以两倍速度推进——这类 bug 在实机上表现为
    /// "射速比配置快一倍"，极难定位。时间只在一处推进，是唯一不容易错的写法。</para>
    /// <para>命令处理器保持极薄：它们只把意图写进控制器，真正的循环在这里。</para>
    /// </remarks>
    public sealed partial class PlayerWeaponController
    {
        private readonly PlayerWeapon m_Weapon;
        private readonly PlayerLoadout m_Loadout;
        private readonly IHitProbe m_Probe;
        private readonly CombatWorld m_World;
        private readonly CombatTuning m_Tuning;
        private readonly EventBus m_EventBus;
        private readonly int m_AmmoPouchContainerId;

        private Vector3 m_MuzzlePosition;
        private Vector2F m_AimWorldPoint;
        private float m_AimDegrees;
        private bool m_TriggerHeld;
        private int m_PlayerId;
        private uint m_Sequence;

        /// <summary>
        /// 持有者是否存活。
        /// </summary>
        /// <remarks>默认存活：安全屋与测试环境没有阵亡概念，由装配层在战局里每帧写入真实状态。</remarks>
        private bool m_IsAlive = true;

        /// <summary>创建控制器。</summary>
        /// <param name="weapon">手持武器状态。</param>
        /// <param name="loadout">角色携带物，换弹时从这里取弹药。</param>
        /// <param name="probe">射线检测能力。</param>
        /// <param name="world">战斗单位注册表。</param>
        /// <param name="tuning">全局调参。</param>
        /// <param name="eventBus">事件总线。</param>
        /// <param name="ammoPouchContainerId">
        /// 弹药挂的容器标识。换弹扣掉弹药之后要按这个标识广播容器变更事件，
        /// 否则界面不会刷新，玩家会以为子弹没被消耗。传 0 表示不广播。
        /// </param>
        public PlayerWeaponController(
            PlayerWeapon weapon,
            PlayerLoadout loadout,
            IHitProbe probe,
            CombatWorld world,
            CombatTuning tuning,
            EventBus eventBus,
            int ammoPouchContainerId = 0)
        {
            m_Weapon = weapon;
            m_Loadout = loadout;
            m_Probe = probe;
            m_World = world;
            m_Tuning = tuning ?? CombatTuning.Default;
            m_EventBus = eventBus;
            m_AmmoPouchContainerId = ammoPouchContainerId;
        }

        /// <summary>手持武器状态。</summary>
        public PlayerWeapon Weapon
        {
            get { return m_Weapon; }
        }

        /// <summary>当前武器运行时。未装备时为 null。</summary>
        public WeaponRuntime Runtime
        {
            get { return m_Weapon.Runtime; }
        }

        /// <summary>当前瞄准角度（度）。表现层用它摆放武器模型。</summary>
        public float AimDegrees
        {
            get { return m_AimDegrees; }
        }

        /// <summary>
        /// 按装备槽的当前内容同步武器。
        /// </summary>
        /// <param name="stats">主武器槽上的武器参数，没有武器时传 null。</param>
        /// <returns>武器确实发生了变化返回 true。</returns>
        /// <remarks>
        /// 换枪时把弹匣内的穿透力重置为"背包里现有同口径弹药的穿透力"，
        /// 因为一把刚拿出来的枪，弹匣里装的就是玩家带来的子弹。
        /// 找不到匹配弹药时退回调参里的默认值，避免出厂弹匣变成零穿透。
        /// </remarks>
        public bool SyncEquippedWeapon(IWeaponStats stats)
        {
            var changed = m_Weapon.Equip(stats);
            if (changed && stats != null)
            {
                var penetration = AmmoReserve.PeekPenetration(
                    m_Loadout.Backpack,
                    stats.CaliberId,
                    m_Tuning.DefaultPenetration);
                m_Weapon.SetLoadedPenetration(penetration);
            }

            return changed;
        }

        /// <summary>更新枪口位置。由表现层每帧写入角色手部所在的世界坐标。</summary>
        /// <param name="position">枪口世界坐标。</param>
        public void SetMuzzlePosition(Vector3 position)
        {
            m_MuzzlePosition = position;
        }

        /// <summary>更新瞄准方向。由射击命令每帧写入。</summary>
        /// <param name="aimDirection">地面平面上的瞄准方向。</param>
        public void SetAimDirection(Vector2F aimDirection)
        {
            if (aimDirection.IsNearlyZero)
            {
                return;
            }

            m_AimDegrees = Mathf.Atan2(aimDirection.Y, aimDirection.X) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// 更新瞄准点在地面上的世界坐标。
        /// </summary>
        /// <param name="worldPoint">准星所在的地面位置。</param>
        /// <remarks>
        /// 弹道从这个点反推方向，而不是简单地把射线放平——放平的射线在斜俯视下
        /// 与准星不在同一条视线上，玩家会看到"子弹和准星对不上"。
        /// </remarks>
        public void SetAimWorldPoint(Vector2F worldPoint)
        {
            m_AimWorldPoint = worldPoint;
        }

        /// <summary>设置扳机是否按住。</summary>
        /// <param name="held">是否按住。</param>
        public void SetTriggerHeld(bool held)
        {
            m_TriggerHeld = held;
        }

        /// <summary>
        /// 设置持有者是否存活。
        /// </summary>
        /// <param name="alive">是否存活。</param>
        /// <remarks>
        /// <para><b>为什么存活状态要由外部写入，而不是控制器自己查：</b>控制器不认识战斗世界里的
        /// "哪个单位是我"——它只负责把武器打出去。让装配层每帧告诉它"玩家还在不在"，
        /// 控制器就不必为了这一条规则去持有单位标识，职责边界保持不变。</para>
        /// <para>阵亡时顺手松开扳机：否则"阵亡时正按着左键"的状态会被保留下来，
        /// 下一局或重新装备武器时会莫名其妙地立刻打出一发。</para>
        /// </remarks>
        public void SetAlive(bool alive)
        {
            m_IsAlive = alive;
            if (!alive)
            {
                m_TriggerHeld = false;
            }
        }

        /// <summary>持有者当前是否存活。</summary>
        public bool IsAlive => m_IsAlive;

        /// <summary>
        /// 请求换弹。
        /// </summary>
        /// <param name="playerId">发起玩家。</param>
        /// <param name="sequence">命令序号。</param>
        /// <param name="failureCode">失败时的结果码。</param>
        /// <returns>成功进入换弹状态返回 true。</returns>
        /// <remarks>
        /// 弹药是否充足由这里检查，而不是由武器运行时检查——
        /// 武器运行时不知道背包的存在，这条边界在 M2 就已经划好了。
        /// </remarks>
        public bool TryRequestReload(int playerId, uint sequence, out string failureCode)
        {
            failureCode = null;
            if (!m_IsAlive)
            {
                failureCode = CommandCodes.CombatIncapacitated;
                return false;
            }

            if (!m_Weapon.IsEquipped)
            {
                failureCode = CommandCodes.CombatNoWeapon;
                return false;
            }

            var runtime = m_Weapon.Runtime;
            if (runtime.IsMagazineFull)
            {
                failureCode = CommandCodes.CombatMagazineFull;
                return false;
            }

            var caliberId = runtime.Weapon.CaliberId;
            if (AmmoReserve.CountAvailable(m_Loadout.AmmoPouch, caliberId) <= 0)
            {
                // 弹药挂空的就打不了。背包里的弹药必须先搬进弹药挂——
                // 这条规则让"弹挂里装多少"成为出击前的决策，代价是战斗中要开背包补弹。
                failureCode = CommandCodes.CombatNoAmmo;
                return false;
            }

            if (!runtime.TryBeginReload(out var weaponFailure))
            {
                failureCode = weaponFailure;
                return false;
            }

            m_PlayerId = playerId;
            m_Sequence = sequence;
            m_EventBus.Publish(new ReloadStateChangedEvent(playerId, true, runtime.MagazineAmmo));
            return true;
        }

        /// <summary>
        /// 每帧推进一次。由启动层调用，且**每帧只能调用一次**。
        /// </summary>
        /// <param name="deltaTime">时间步长（秒）。</param>
        public void Tick(float deltaTime)
        {
            // 阵亡后武器彻底停摆：不再扣扳机、不再推进换弹计时。
            // 少了这一道闸门时，玩家阵亡动画已经在播"倒地"，人还能继续扫射。
            if (!m_IsAlive || !m_Weapon.IsEquipped || deltaTime <= 0f)
            {
                return;
            }

            var runtime = m_Weapon.Runtime;
            runtime.Tick(deltaTime);

            CompleteReloadIfReady(runtime);

            var state = runtime.UpdateTrigger(m_TriggerHeld, out var spreadOffsetDegrees);
            if (state == TriggerState.Fired)
            {
                FireOnce(runtime, spreadOffsetDegrees);
            }
        }

        /// <summary>换弹计时走完后扣弹药并把弹匣补满。</summary>
        private void CompleteReloadIfReady(WeaponRuntime runtime)
        {
            if (!runtime.IsReloadReady)
            {
                return;
            }

            var caliberId = runtime.Weapon.CaliberId;
            var room = runtime.Weapon.MagazineCapacity - runtime.MagazineAmmo;
            var available = AmmoReserve.CountAvailable(m_Loadout.AmmoPouch, caliberId);
            var want = available < room ? available : room;

            // 先取弹药再补弹匣：取出量一定不超过剩余空间，因此不会有取了却装不下的部分。
            var withdrawal = AmmoReserve.Consume(m_Loadout.AmmoPouch, caliberId, want);
            var loaded = runtime.CompleteReload(withdrawal.Amount);

            if (withdrawal.Amount > 0 && m_AmmoPouchContainerId != 0)
            {
                // 取走弹药是在弹药挂上做的真实改动，必须广播出去。
                // 少了这一步，界面会一直显示换弹前的数量，
                // 玩家会以为换弹没有消耗子弹——而实际上消耗了，只是界面没刷新。
                m_EventBus.Publish(new InventoryChangedEvent(
                    m_AmmoPouchContainerId,
                    InventoryChangeTypes.Remove,
                    m_PlayerId));
            }

            if (!withdrawal.IsEmpty)
            {
                m_Weapon.SetLoadedPenetration(withdrawal.Penetration);
            }

            m_EventBus.Publish(new ReloadStateChangedEvent(
                m_PlayerId,
                false,
                runtime.MagazineAmmo,
                loaded));
        }
    }
}

