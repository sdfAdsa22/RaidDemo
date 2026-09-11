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
    public sealed class PlayerWeaponController
    {
        private readonly PlayerWeapon m_Weapon;
        private readonly PlayerLoadout m_Loadout;
        private readonly IHitProbe m_Probe;
        private readonly CombatWorld m_World;
        private readonly CombatTuning m_Tuning;
        private readonly EventBus m_EventBus;

        private Vector3 m_MuzzlePosition;
        private float m_AimDegrees;
        private bool m_TriggerHeld;
        private int m_PlayerId;
        private uint m_Sequence;

        /// <summary>创建控制器。</summary>
        /// <param name="weapon">手持武器状态。</param>
        /// <param name="loadout">角色携带物，换弹时从这里取弹药。</param>
        /// <param name="probe">射线检测能力。</param>
        /// <param name="world">战斗单位注册表。</param>
        /// <param name="tuning">全局调参。</param>
        /// <param name="eventBus">事件总线。</param>
        public PlayerWeaponController(
            PlayerWeapon weapon,
            PlayerLoadout loadout,
            IHitProbe probe,
            CombatWorld world,
            CombatTuning tuning,
            EventBus eventBus)
        {
            m_Weapon = weapon;
            m_Loadout = loadout;
            m_Probe = probe;
            m_World = world;
            m_Tuning = tuning ?? CombatTuning.Default;
            m_EventBus = eventBus;
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

        /// <summary>设置扳机是否按住。</summary>
        /// <param name="held">是否按住。</param>
        public void SetTriggerHeld(bool held)
        {
            m_TriggerHeld = held;
        }

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
            if (AmmoReserve.CountAvailable(m_Loadout.Backpack, caliberId) <= 0)
            {
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
            if (!m_Weapon.IsEquipped || deltaTime <= 0f)
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
            var available = AmmoReserve.CountAvailable(m_Loadout.Backpack, caliberId);
            var want = available < room ? available : room;

            // 先取弹药再补弹匣：取出量一定不超过剩余空间，因此不会有取了却装不下的部分。
            var withdrawal = AmmoReserve.Consume(m_Loadout.Backpack, caliberId, want);
            var loaded = runtime.CompleteReload(withdrawal.Amount);

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

        /// <summary>发射一发：算散布、投射射线、结算伤害、广播事件。</summary>
        private void FireOnce(WeaponRuntime runtime, float spreadOffsetDegrees)
        {
            var shotDegrees = m_AimDegrees + spreadOffsetDegrees;
            var planar = Vector2F.FromDegrees(shotDegrees);
            var direction = new Vector3(planar.X, 0f, planar.Y);
            var origin = m_MuzzlePosition;
            var range = runtime.Weapon.RangeMeters;

            var didHit = m_Probe.TryRaycast(origin, direction, range, out var hit);
            var endPoint = didHit ? hit.Point : origin + (direction.normalized * range);
            var targetId = didHit ? hit.TargetId : 0;

            if (targetId != 0)
            {
                ResolveDamage(targetId, hit, runtime);
            }

            m_EventBus.Publish(new WeaponFiredEvent(
                m_PlayerId,
                origin,
                endPoint,
                didHit,
                targetId,
                0d,
                m_Sequence));
        }

        /// <summary>对命中目标结算伤害。</summary>
        private void ResolveDamage(int targetId, in HitInfo hit, WeaponRuntime runtime)
        {
            if (!m_World.TryGet(targetId, out var combatant) || !combatant.IsAlive)
            {
                return;
            }

            var isCritical = DamageCalculator.IsCriticalHit(hit.Point, hit.TargetCenter, m_Tuning);
            var outcome = combatant.ApplyDamage(
                runtime.Weapon.BaseDamage,
                m_Weapon.LoadedPenetration,
                isCritical,
                m_Tuning);

            var wasKilled = !combatant.IsAlive;
            m_EventBus.Publish(new DamageAppliedEvent(
                m_PlayerId,
                targetId,
                outcome.Damage,
                outcome.ArmorDamage,
                isCritical,
                outcome.PenetrationFactor,
                combatant.Health,
                wasKilled,
                0d,
                m_Sequence));

            if (wasKilled)
            {
                m_EventBus.Publish(new TargetDestroyedEvent(targetId, m_PlayerId));
            }
        }
    }

    /// <summary>
    /// 处理射击意图：记录瞄准方向并压住扳机。
    /// </summary>
    /// <remarks>
    /// 处理器刻意保持极薄。真正的射击循环在 <see cref="PlayerWeaponController.Tick"/>，
    /// 由启动层每帧调用一次——这样"每帧只推进一次武器时间"这件事由结构保证，
    /// 而不是靠每个处理器都记得别多调一次。
    /// </remarks>
    public sealed class FireCommandHandler : ICommandHandler<PlayerFireIntent>
    {
        private readonly PlayerWeaponController m_Controller;

        /// <summary>创建处理器。</summary>
        /// <param name="controller">武器控制器。</param>
        public FireCommandHandler(PlayerWeaponController controller)
        {
            m_Controller = controller;
        }

        /// <inheritdoc />
        public CommandResult Execute(in PlayerFireIntent command)
        {
            if (!m_Controller.Weapon.IsEquipped)
            {
                // 没有武器时立刻拒绝，而不是默默地什么都不做：
                // 玩家按住扳机却毫无反馈时，会以为游戏卡住了。
                return CommandResult.Fail(CommandCodes.CombatNoWeapon, "主武器槽是空的。");
            }

            m_Controller.SetAimDirection(command.AimDirection);
            m_Controller.SetTriggerHeld(true);
            return CommandResult.Ok();
        }
    }

    /// <summary>
    /// 处理换弹意图。
    /// </summary>
    /// <remarks>
    /// 这是少数会**同步失败**的命令之一：弹匣满、没有匹配弹药、正在换弹，
    /// 这三种情况都不需要等待，立刻就能给出结果，因此直接在处理器里返回失败码，
    /// 而不是等到下一帧再通过事件通知。
    /// </remarks>
    public sealed class ReloadCommandHandler : ICommandHandler<PlayerReloadIntent>
    {
        private readonly PlayerWeaponController m_Controller;

        /// <summary>创建处理器。</summary>
        /// <param name="controller">武器控制器。</param>
        public ReloadCommandHandler(PlayerWeaponController controller)
        {
            m_Controller = controller;
        }

        /// <inheritdoc />
        public CommandResult Execute(in PlayerReloadIntent command)
        {
            if (m_Controller.TryRequestReload(command.PlayerId, command.Sequence, out var failureCode))
            {
                return CommandResult.Ok();
            }

            return CommandResult.Fail(failureCode, "换弹请求被拒绝。");
        }
    }
}
