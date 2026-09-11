using RaidDemo.Kernel;
using UnityEngine;

namespace RaidDemo.Combat
{
    /// <summary>
    /// 开火事件：一发子弹已经打出，附带弹道信息。
    /// </summary>
    /// <remarks>
    /// <para>无论是否命中都会发布——表现层需要知道"枪响了但打空了"，
    /// 否则玩家会觉得枪没开。</para>
    /// <para>载荷带上命中点而不是命中对象，是因为表现层只需要画一条从枪口到命中点的线，
    /// 不需要知道打中了谁。需要知道的是 HUD 与统计，它们订阅的是伤害事件。</para>
    /// </remarks>
    public readonly struct WeaponFiredEvent : IEventEnvelope
    {
        /// <summary>创建开火事件。</summary>
        /// <param name="shooterId">射手标识。</param>
        /// <param name="origin">枪口世界坐标。</param>
        /// <param name="endPoint">弹道终点世界坐标（命中点，或未命中时的射程末端）。</param>
        /// <param name="didHit">是否命中任何碰撞体。</param>
        /// <param name="hitTargetId">命中的可受击单位标识，0 表示没有。</param>
        /// <param name="noiseRadiusMeters">
        /// 这一枪的可听半径（米）。由开火方按自己的武器提供——枪声是"暴露位置"的主要来源，
        /// 而不同武器的吵法不同（短管手枪比步枪安静）。
        /// </param>
        /// <param name="timestamp">事件时间戳（秒）。</param>
        /// <param name="sequence">来源命令序号。</param>
        public WeaponFiredEvent(
            int shooterId,
            Vector3 origin,
            Vector3 endPoint,
            bool didHit,
            int hitTargetId,
            float noiseRadiusMeters,
            double timestamp = 0d,
            uint sequence = 0u)
        {
            ShooterId = shooterId;
            Origin = origin;
            EndPoint = endPoint;
            DidHit = didHit;
            HitTargetId = hitTargetId;
            NoiseRadiusMeters = noiseRadiusMeters;
            Timestamp = timestamp;
            Sequence = sequence;
        }

        /// <summary>射手标识。</summary>
        public int ShooterId { get; }

        /// <summary>枪口世界坐标。</summary>
        public Vector3 Origin { get; }

        /// <summary>弹道终点世界坐标。</summary>
        public Vector3 EndPoint { get; }

        /// <summary>是否命中任何碰撞体。</summary>
        public bool DidHit { get; }

        /// <summary>命中的可受击单位标识，0 表示没有。</summary>
        public int HitTargetId { get; }

        /// <summary>
        /// 这一枪的可听半径（米）。
        /// </summary>
        /// <remarks>
        /// 放在事件里而不是让听者去查"是谁开的枪、用的什么武器"：
        /// 枪声的影响范围是**开火那一刻**的属性，事后再去反查既绕远又容易查错。
        /// </remarks>
        public float NoiseRadiusMeters { get; }

        /// <inheritdoc />
        public double Timestamp { get; }

        /// <inheritdoc />
        public string Source
        {
            get { return "combat.weapon"; }
        }

        /// <inheritdoc />
        public uint Sequence { get; }

        public override string ToString()
        {
            return $"WeaponFiredEvent(射手={ShooterId}, 命中={DidHit}, 目标={HitTargetId})";
        }
    }

    /// <summary>
    /// 伤害结算完成事件。
    /// </summary>
    public readonly struct DamageAppliedEvent : IEventEnvelope
    {
        /// <summary>创建伤害事件。</summary>
        /// <param name="attackerId">攻击方标识。</param>
        /// <param name="targetId">受击单位标识。</param>
        /// <param name="damage">最终伤害。</param>
        /// <param name="armorDamage">造成的护甲耐久损失。</param>
        /// <param name="isCritical">是否暴击命中。</param>
        /// <param name="penetrationFactor">穿透系数。</param>
        /// <param name="remainingHealth">受击后剩余生命。</param>
        /// <param name="wasKilled">本次是否造成击杀。</param>
        /// <param name="timestamp">事件时间戳（秒）。</param>
        /// <param name="sequence">来源命令序号。</param>
        public DamageAppliedEvent(
            int attackerId,
            int targetId,
            float damage,
            float armorDamage,
            bool isCritical,
            float penetrationFactor,
            float remainingHealth,
            bool wasKilled,
            double timestamp = 0d,
            uint sequence = 0u)
        {
            AttackerId = attackerId;
            TargetId = targetId;
            Damage = damage;
            ArmorDamage = armorDamage;
            IsCritical = isCritical;
            PenetrationFactor = penetrationFactor;
            RemainingHealth = remainingHealth;
            WasKilled = wasKilled;
            Timestamp = timestamp;
            Sequence = sequence;
        }

        /// <summary>攻击方标识。</summary>
        public int AttackerId { get; }

        /// <summary>受击单位标识。</summary>
        public int TargetId { get; }

        /// <summary>最终伤害。</summary>
        public float Damage { get; }

        /// <summary>造成的护甲耐久损失。</summary>
        public float ArmorDamage { get; }

        /// <summary>是否暴击命中。</summary>
        public bool IsCritical { get; }

        /// <summary>穿透系数。</summary>
        public float PenetrationFactor { get; }

        /// <summary>受击后剩余生命。</summary>
        public float RemainingHealth { get; }

        /// <summary>本次是否造成击杀。</summary>
        public bool WasKilled { get; }

        /// <inheritdoc />
        public double Timestamp { get; }

        /// <inheritdoc />
        public string Source
        {
            get { return "combat.damage"; }
        }

        /// <inheritdoc />
        public uint Sequence { get; }

        public override string ToString()
        {
            return $"DamageAppliedEvent(目标={TargetId}, 伤害={Damage:F1}, 暴击={IsCritical}, 剩余={RemainingHealth:F0})";
        }
    }

    /// <summary>
    /// 单位被击毁事件。
    /// </summary>
    public readonly struct TargetDestroyedEvent : IEventEnvelope
    {
        /// <summary>创建击毁事件。</summary>
        /// <param name="targetId">被击毁的单位标识。</param>
        /// <param name="killerId">击杀者标识。</param>
        /// <param name="timestamp">事件时间戳（秒）。</param>
        public TargetDestroyedEvent(int targetId, int killerId, double timestamp = 0d)
        {
            TargetId = targetId;
            KillerId = killerId;
            Timestamp = timestamp;
        }

        /// <summary>被击毁的单位标识。</summary>
        public int TargetId { get; }

        /// <summary>击杀者标识。</summary>
        public int KillerId { get; }

        /// <inheritdoc />
        public double Timestamp { get; }

        /// <inheritdoc />
        public string Source
        {
            get { return "combat.destroyed"; }
        }

        /// <inheritdoc />
        public uint Sequence
        {
            get { return 0u; }
        }

        public override string ToString()
        {
            return $"TargetDestroyedEvent(目标={TargetId}, 击杀者={KillerId})";
        }
    }

    /// <summary>
    /// 换弹状态变化事件。开始换弹、换弹完成时各发布一次。
    /// </summary>
    public readonly struct ReloadStateChangedEvent : IEventEnvelope
    {
        /// <summary>创建换弹事件。</summary>
        /// <param name="ownerId">持枪者标识。</param>
        /// <param name="isReloading">是否正在换弹。</param>
        /// <param name="magazineAmmo">当前弹匣弹药数。</param>
        /// <param name="loadedAmmo">本次实际装入的弹药数，非完成事件时为 0。</param>
        /// <param name="timestamp">事件时间戳（秒）。</param>
        public ReloadStateChangedEvent(
            int ownerId,
            bool isReloading,
            int magazineAmmo,
            int loadedAmmo = 0,
            double timestamp = 0d)
        {
            OwnerId = ownerId;
            IsReloading = isReloading;
            MagazineAmmo = magazineAmmo;
            LoadedAmmo = loadedAmmo;
            Timestamp = timestamp;
        }

        /// <summary>持枪者标识。</summary>
        public int OwnerId { get; }

        /// <summary>是否正在换弹。</summary>
        public bool IsReloading { get; }

        /// <summary>当前弹匣弹药数。</summary>
        public int MagazineAmmo { get; }

        /// <summary>本次实际装入的弹药数。</summary>
        public int LoadedAmmo { get; }

        /// <inheritdoc />
        public double Timestamp { get; }

        /// <inheritdoc />
        public string Source
        {
            get { return "combat.reload"; }
        }

        /// <inheritdoc />
        public uint Sequence
        {
            get { return 0u; }
        }

        public override string ToString()
        {
            return IsReloading
                ? $"ReloadStateChangedEvent(开始换弹, 弹匣={MagazineAmmo})"
                : $"ReloadStateChangedEvent(换弹完成, 弹匣={MagazineAmmo}, 装入={LoadedAmmo})";
        }
    }
}
