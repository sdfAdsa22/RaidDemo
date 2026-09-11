using RaidDemo.Combat;
using RaidDemo.Kernel;
using RaidDemo.Shared;
using UnityEngine;

namespace RaidDemo.AI
{
    /// <summary>
    /// AI 的武器运行时与开火结算。
    /// </summary>
    /// <remarks>
    /// <para><b>与玩家的 <c>PlayerWeaponController</c> 的差别只有"弹源"：</b>
    /// 玩家的弹药来自弹药挂，AI 的备弹是一个常数。除此之外——射速、散布扩散、
    /// 换弹耗时、部分装填、射线命中、护甲减伤——用的是同一套代码，
    /// 因此不会出现"AI 的枪规则和玩家不一样"这种两份实现必然带来的偏差。</para>
    ///
    /// <para><b>单独成类而不是塞进 <see cref="AiAgent"/>：</b>一是文件长度，
    /// 二是职责：Agent 管"走到哪、朝哪看、要不要打"，本类管"扣扳机之后发生什么"。
    /// 两者混在一起时，任何一处改动都要重新理解整段代码。</para>
    /// </remarks>
    public sealed class AiWeaponController
    {
        /// <summary>枪口相对脚底的高度（米）。与玩家一致，便于对照弹道。</summary>
        private const float MuzzleHeightMeters = 1.05f;

        private readonly AiDirector m_Director;
        private readonly int m_CombatantId;
        private readonly WeaponRuntime m_Weapon;

        private int m_ReserveAmmo;

        /// <summary>创建 AI 武器控制器。</summary>
        /// <param name="director">提供射线、战斗世界、调参与事件总线。</param>
        /// <param name="combatantId">射手的单位标识。</param>
        /// <param name="weapon">武器参数，可为 null（使用默认灰盒步枪）。</param>
        /// <param name="randomSeed">散布随机种子。</param>
        /// <param name="reserveAmmo">初始备弹。</param>
        public AiWeaponController(
            AiDirector director,
            int combatantId,
            AiWeaponProfile weapon,
            uint randomSeed,
            int reserveAmmo)
        {
            m_Director = director;
            m_CombatantId = combatantId;
            m_Weapon = new WeaponRuntime(
                weapon ?? AiWeaponProfile.CreateGreyboxRifle(),
                new DeterministicRandom(randomSeed));
            m_ReserveAmmo = reserveAmmo < 0 ? 0 : reserveAmmo;
        }

        /// <summary>当前弹匣余量。</summary>
        public int MagazineAmmo
        {
            get { return m_Weapon.MagazineAmmo; }
        }

        /// <summary>剩余备弹。</summary>
        public int ReserveAmmo
        {
            get { return m_ReserveAmmo; }
        }

        /// <summary>有效射程（米）。</summary>
        public float RangeMeters
        {
            get { return m_Weapon.Weapon.RangeMeters; }
        }

        /// <summary>是否正在换弹。</summary>
        public bool IsReloading
        {
            get { return m_Weapon.IsReloading; }
        }

        /// <summary>
        /// 推进一帧：先处理换弹，再推进扳机并结算命中。
        /// </summary>
        /// <param name="deltaTime">时间步长（秒）。</param>
        /// <param name="wantsToFire">本帧是否想要开火。</param>
        /// <param name="position">射手平面位置。</param>
        /// <param name="facingDegrees">射手当前朝向（度）。</param>
        public void Tick(float deltaTime, bool wantsToFire, Vector2F position, float facingDegrees)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            // 时间只在这一处推进：开火冷却与换弹计时共用同一个 WeaponRuntime，
            // 任何地方再调一次 Tick 都会让射速凭空翻倍。
            m_Weapon.Tick(deltaTime);

            // 顺序至关重要：必须先结算"已经到点的换弹"，再判断要不要开始新的换弹。
            // 反过来的话，计时刚走完的那一帧会立刻被重新开始一次换弹，
            // m_ReloadReady 被覆盖成 false，弹匣永远补不上——表现为 AI 打空一个弹匣后
            // 就一直空着枪，而且日志里没有任何异常。这个顺序问题在实机上极难定位。
            CompleteReloadIfReady();
            BeginReloadIfEmpty();

            var state = m_Weapon.UpdateTrigger(wantsToFire, out var spreadOffsetDegrees);
            if (state == TriggerState.Fired)
            {
                FireOnce(position, facingDegrees, spreadOffsetDegrees);
            }
        }

        /// <summary>弹匣打空时自动开始换弹。</summary>
        private void BeginReloadIfEmpty()
        {
            if (m_Weapon.MagazineAmmo > 0
                || m_Weapon.IsReloading
                || m_Weapon.IsReloadReady
                || m_ReserveAmmo <= 0)
            {
                return;
            }

            m_Weapon.TryBeginReload(out _);
        }

        /// <summary>换弹计时走完后从备弹补满弹匣。</summary>
        private void CompleteReloadIfReady()
        {
            if (!m_Weapon.IsReloadReady)
            {
                return;
            }

            // CompleteReload 返回实际装入量，扣备弹与补弹匣因此是同一次决策，
            // 不会出现"扣了 30 发却只装上 12 发"的错账。
            var loaded = m_Weapon.CompleteReload(m_ReserveAmmo);
            m_ReserveAmmo -= loaded;
        }

        /// <summary>发射一发：算散布、投射射线、结算伤害、广播事件。</summary>
        private void FireOnce(Vector2F position, float facingDegrees, float spreadOffsetDegrees)
        {
            var weapon = m_Weapon.Weapon;
            var origin = new Vector3(position.X, MuzzleHeightMeters, position.Y);

            // 方向取水平方向并叠加散布：与玩家完全一致，因此弹道在俯视角下与准星对得上。
            var direction2D = Vector2F.FromDegrees(facingDegrees + spreadOffsetDegrees);
            var direction = new Vector3(direction2D.X, 0f, direction2D.Y);

            // 先声明再传入 out：若写成 `probe != null && probe.TryRaycast(..., out var hit)`，
            // 因为短路求值的存在，编译器无法证明后续读取 hit 时它一定被赋值。
            var hit = default(HitInfo);
            var didHit = m_Director.Probe != null
                         && m_Director.Probe.TryRaycast(origin, direction, weapon.RangeMeters, out hit);

            var endPoint = didHit ? hit.Point : origin + (direction * weapon.RangeMeters);
            var targetId = didHit ? hit.TargetId : 0;

            if (targetId != 0)
            {
                ResolveDamage(targetId, hit, weapon.BaseDamage);
            }

            m_Director.EventBus?.Publish(new WeaponFiredEvent(
                m_CombatantId,
                origin,
                endPoint,
                didHit,
                targetId));
        }

        /// <summary>对命中目标结算伤害。与玩家走同一条结算路径。</summary>
        private void ResolveDamage(int targetId, in HitInfo hit, float baseDamage)
        {
            if (!m_Director.World.TryGet(targetId, out var target) || !target.IsAlive)
            {
                return;
            }

            var tuning = m_Director.Tuning;
            var isCritical = DamageCalculator.IsCriticalHit(hit.Point, hit.TargetCenter, tuning);
            var outcome = target.ApplyDamage(baseDamage, m_Director.WeaponPenetration, isCritical, tuning);
            var wasKilled = !target.IsAlive;

            m_Director.EventBus?.Publish(new DamageAppliedEvent(
                m_CombatantId,
                targetId,
                outcome.Damage,
                outcome.ArmorDamage,
                isCritical,
                outcome.PenetrationFactor,
                target.Health,
                wasKilled));

            if (wasKilled)
            {
                m_Director.EventBus?.Publish(new TargetDestroyedEvent(targetId, m_CombatantId));
            }
        }
    }
}
