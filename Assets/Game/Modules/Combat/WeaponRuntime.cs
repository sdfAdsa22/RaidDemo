using RaidDemo.Data;
using RaidDemo.Kernel;

namespace RaidDemo.Combat
{
    /// <summary>
    /// 扳机状态的一次推进结果。
    /// </summary>
    public enum TriggerState
    {
        /// <summary>本帧没有发射。可能是扳机没按，也可能是射速间隔还没到。</summary>
        Idle = 0,

        /// <summary>本帧发射了一发。</summary>
        Fired,

        /// <summary>玩家想开枪，但弹匣是空的。</summary>
        BlockedEmpty,

        /// <summary>玩家想开枪，但正在换弹。</summary>
        BlockedReloading,
    }

    /// <summary>
    /// 一把武器的运行时状态：弹匣、冷却、散布、换弹进度。
    /// </summary>
    /// <remarks>
    /// <para>纯 C# 类，不依赖场景与 MonoBehaviour，也不直接做射线检测：
    /// 它只回答"这一帧该不该发射、往哪个角度偏"，命中与伤害由命令处理器完成。</para>
    /// <para>随机数由外部注入。散布偏移需要随机，但随机必须可复现，
    /// 否则射击逻辑没法自动化测试，联机时客户端预测与服务端判定也会对不上。</para>
    /// </remarks>
    public sealed class WeaponRuntime
    {
        /// <summary>武器参数。</summary>
        private readonly IWeaponStats m_Weapon;

        /// <summary>散布用的确定性随机数。</summary>
        private readonly DeterministicRandom m_Random;

        /// <summary>弹匣内剩余弹药。</summary>
        private int m_MagazineAmmo;

        /// <summary>距离下一次可以射击的剩余时间（秒）。</summary>
        private float m_Cooldown;

        /// <summary>当前散布（度）。以锥角全宽表示，实际偏移是它的一半以内。</summary>
        private float m_Spread;

        /// <summary>换弹剩余时间（秒）。大于 0 表示正在换弹。</summary>
        private float m_ReloadRemaining;

        /// <summary>换弹计时是否已经走完、等待补弹。补弹需要背包数据，因此由外部调用完成。</summary>
        private bool m_ReloadReady;

        /// <summary>排队等待发射的弹数。单发与连发靠它实现，全自动每帧补充。</summary>
        private int m_PendingShots;

        /// <summary>上一帧扳机是否按住，用于识别按下的那一瞬间。</summary>
        private bool m_TriggerHeld;

        /// <summary>
        /// 创建武器运行时。
        /// </summary>
        /// <param name="weapon">武器参数，不允许为 null。</param>
        /// <param name="random">确定性随机数，不允许为 null。</param>
        /// <param name="startLoaded">是否以满弹匣开始。测试里常需要从空弹匣开始。</param>
        public WeaponRuntime(IWeaponStats weapon, DeterministicRandom random, bool startLoaded = true)
        {
            m_Weapon = weapon ?? throw new System.ArgumentNullException(nameof(weapon));
            m_Random = random ?? throw new System.ArgumentNullException(nameof(random));
            m_MagazineAmmo = startLoaded ? weapon.MagazineCapacity : 0;
            m_Spread = weapon.BaseSpreadDegrees;
        }

        /// <summary>武器参数。</summary>
        public IWeaponStats Weapon
        {
            get { return m_Weapon; }
        }

        /// <summary>弹匣内剩余弹药。</summary>
        public int MagazineAmmo
        {
            get { return m_MagazineAmmo; }
        }

        /// <summary>弹匣是否已满。</summary>
        public bool IsMagazineFull
        {
            get { return m_MagazineAmmo >= m_Weapon.MagazineCapacity; }
        }

        /// <summary>是否正在换弹。</summary>
        public bool IsReloading
        {
            get { return m_ReloadRemaining > 0f; }
        }

        /// <summary>换弹计时是否已走完、等待外部补弹。</summary>
        public bool IsReloadReady
        {
            get { return m_ReloadReady; }
        }

        /// <summary>换弹进度，取值 0 到 1，供界面显示。</summary>
        public float ReloadProgress01
        {
            get
            {
                var total = m_Weapon.ReloadSeconds;
                if (total <= 0f)
                {
                    return 1f;
                }

                var progress = 1f - (m_ReloadRemaining / total);
                return progress < 0f ? 0f : progress > 1f ? 1f : progress;
            }
        }

        /// <summary>当前散布（度）。</summary>
        public float CurrentSpreadDegrees
        {
            get { return m_Spread; }
        }

        /// <summary>
        /// 推进时间：冷却、散布恢复、换弹计时。
        /// </summary>
        /// <param name="deltaTime">时间步长（秒）。非正值会被忽略。</param>
        /// <remarks>
        /// 时间由调用方显式传入，与移动模拟保持一致——
        /// 这样同一段逻辑在联机时能按固定步长在服务端重放。
        /// </remarks>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            if (m_Cooldown > 0f)
            {
                m_Cooldown -= deltaTime;
                if (m_Cooldown < 0f)
                {
                    m_Cooldown = 0f;
                }
            }

            if (m_ReloadRemaining > 0f)
            {
                m_ReloadRemaining -= deltaTime;
                if (m_ReloadRemaining <= 0f)
                {
                    m_ReloadRemaining = 0f;
                    m_ReloadReady = true;
                }

                return;
            }

            // 只有在没有待发射弹药时才收回散布：
            // 连发过程中每发都会重新抬升散布，此时恢复没有意义。
            if (!m_TriggerHeld && m_PendingShots <= 0)
            {
                RecoverSpread(deltaTime);
            }
        }

        /// <summary>
        /// 推进扳机状态，返回本帧应当发生的一次射击。
        /// </summary>
        /// <param name="held">本帧扳机是否按住。</param>
        /// <param name="spreadOffsetDegrees">本发的角度偏移（度）。调用方把它加到瞄准角上。</param>
        /// <returns>本帧的扳机状态。只有为 Fired 时才真正消耗弹药。</returns>
        /// <remarks>
        /// <b>按住扳机不放时，换弹结束的那一帧会立刻继续射击。</b>
        /// 这是刻意保留的行为：全自动武器在弹药到位后立刻恢复火力，
        /// 符合玩家按住扳机时的预期，也让"打空了先找掩体换弹"这件事保持紧张感。
        /// 不想要这个效果的话，应当在换弹开始时松开扳机。
        /// </remarks>
        public TriggerState UpdateTrigger(bool held, out float spreadOffsetDegrees)
        {
            spreadOffsetDegrees = 0f;

            if (IsReloading)
            {
                m_TriggerHeld = held;
                return held ? TriggerState.BlockedReloading : TriggerState.Idle;
            }

            var pressedThisFrame = held && !m_TriggerHeld;
            m_TriggerHeld = held;

            if (pressedThisFrame)
            {
                QueueOnPress();
            }
            else if (held && m_Weapon.FireMode == WeaponFireMode.Auto && m_PendingShots <= 0)
            {
                // 全自动：按住期间每当冷却结束就补一发。
                m_PendingShots = 1;
            }

            if (m_PendingShots <= 0 || m_Cooldown > 0f)
            {
                return TriggerState.Idle;
            }

            if (m_MagazineAmmo <= 0)
            {
                // 没子弹时清空队列。留着队列会让玩家在换弹结束后莫名其妙地自动打出一发。
                m_PendingShots = 0;
                return TriggerState.BlockedEmpty;
            }

            m_PendingShots--;
            m_MagazineAmmo--;
            m_Cooldown = m_Weapon.RoundsPerMinute > 0f ? 60f / m_Weapon.RoundsPerMinute : 0f;

            // 先取本发的偏移，再抬升散布：第一发应当是按基础散布飞出去的。
            spreadOffsetDegrees = RollSpreadOffset();
            IncreaseSpread();
            return TriggerState.Fired;
        }

        /// <summary>
        /// 尝试开始换弹。
        /// </summary>
        /// <param name="failureCode">失败时的结果码，成功时为 null。</param>
        /// <returns>成功进入换弹状态返回 true。</returns>
        /// <remarks>
        /// 本方法只负责能不能开始，**不检查背包里有没有弹药**——
        /// 那是背包层的信息，由命令处理器在调用之前确认。
        /// 把两件事分开，是为了让武器运行时保持与背包无关。
        /// </remarks>
        public bool TryBeginReload(out string failureCode)
        {
            if (IsReloading)
            {
                failureCode = "combat_reloading";
                return false;
            }

            if (IsMagazineFull)
            {
                failureCode = "combat_magazine_full";
                return false;
            }

            m_ReloadRemaining = m_Weapon.ReloadSeconds;
            m_ReloadReady = false;
            m_PendingShots = 0;
            failureCode = null;
            return true;
        }

        /// <summary>
        /// 完成换弹：把弹匣补满，返回实际消耗的弹药数。
        /// </summary>
        /// <param name="availableAmmo">背包中可用的弹药总数。</param>
        /// <returns>实际装入弹匣的弹药数。</returns>
        /// <remarks>
        /// <para><b>部分装填</b>：可用弹药不足一个弹匣时把能装的都装上，而不是拒绝换弹。
        /// 搜打撤里玩家经常在弹药见底时仍需应战，至少让我装三发比一发都装不了友好得多。</para>
        /// <para>返回实际装入量而不是让调用方自己算：扣弹药与补弹匣必须是同一次决策，
        /// 否则迟早出现扣了 30 发、只装上 12 发的错账。</para>
        /// </remarks>
        public int CompleteReload(int availableAmmo)
        {
            if (!m_ReloadReady)
            {
                return 0;
            }

            m_ReloadReady = false;

            var room = m_Weapon.MagazineCapacity - m_MagazineAmmo;
            if (room <= 0 || availableAmmo <= 0)
            {
                return 0;
            }

            var taken = availableAmmo < room ? availableAmmo : room;
            m_MagazineAmmo += taken;

            // 换弹后重新握枪，散布回到基础值。这让打空了换弹同时也是一次喘息机会。
            m_Spread = m_Weapon.BaseSpreadDegrees;
            return taken;
        }

        /// <summary>把弹匣直接补满。仅供初始化与调试使用。</summary>
        public void RefillMagazine()
        {
            m_MagazineAmmo = m_Weapon.MagazineCapacity;
            m_ReloadRemaining = 0f;
            m_ReloadReady = false;
            m_PendingShots = 0;
            m_Spread = m_Weapon.BaseSpreadDegrees;
        }

        /// <summary>按射击模式安排按下扳机时的排队弹数。</summary>
        private void QueueOnPress()
        {
            if (m_Weapon.FireMode == WeaponFireMode.Burst)
            {
                m_PendingShots = m_Weapon.BurstCount > 1 ? m_Weapon.BurstCount : 1;
                return;
            }

            m_PendingShots = 1;
        }

        /// <summary>依当前散布取一个随机角度偏移。散布是锥角全宽，因此偏移范围是正负一半。</summary>
        private float RollSpreadOffset()
        {
            var half = m_Spread * 0.5f;
            if (half <= 0f)
            {
                return 0f;
            }

            return m_Random.NextFloat(-half, half);
        }

        /// <summary>每开一枪抬高散布，不超过上限。</summary>
        private void IncreaseSpread()
        {
            m_Spread += m_Weapon.SpreadPerShotDegrees;
            if (m_Spread > m_Weapon.MaxSpreadDegrees)
            {
                m_Spread = m_Weapon.MaxSpreadDegrees;
            }
        }

        /// <summary>停火后把散布收回基础值。</summary>
        private void RecoverSpread(float deltaTime)
        {
            var baseSpread = m_Weapon.BaseSpreadDegrees;
            if (m_Spread <= baseSpread)
            {
                m_Spread = baseSpread;
                return;
            }

            m_Spread -= m_Weapon.SpreadRecoveryPerSecond * deltaTime;
            if (m_Spread < baseSpread)
            {
                m_Spread = baseSpread;
            }
        }
    }
}
