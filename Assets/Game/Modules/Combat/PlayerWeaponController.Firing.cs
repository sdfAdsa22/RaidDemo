using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Kernel;
using RaidDemo.Shared;
using UnityEngine;

namespace RaidDemo.Combat
{
    /// <summary>
    /// 玩家武器控制器的发射部分：把一次扳机变成一颗或多颗弹丸的射线与伤害。
    /// </summary>
    /// <remarks>
    /// <para>从主文件拆出来的原因有两个：主文件触及 400 行上限；"推进时间与换弹"
    /// 和"打出一条弹道"本来就是两条独立的链路，分开之后各自都能单独阅读。</para>
    /// <para>多弹丸（霰弹枪）的规则集中在这里：一发子弹消耗一颗弹药，
    /// 但会按 <c>PelletCount</c> 打出多条射线，每条独立结算命中与伤害。</para>
    /// </remarks>
    public sealed partial class PlayerWeaponController
    {
        /// <summary>
        /// 发射一发：按弹丸数逐颗算散布、投射射线、结算伤害、广播事件。
        /// </summary>
        /// <param name="runtime">武器运行时。</param>
        /// <param name="spreadOffsetDegrees">这一发整体的散布偏移（度）。</param>
        /// <remarks>
        /// <para><b>一发子弹 ≠ 一条射线。</b>霰弹枪一发打出多颗弹丸，每颗独立命中、独立结算伤害；
        /// 但弹药只消耗一颗（在 <see cref="WeaponRuntime.UpdateTrigger"/> 里扣，
        /// 这里只负责把这一发打成几条弹道）。</para>
        /// <para>弹丸在扇面内**均匀排开**而不是纯随机：六颗弹丸随机落在锥角里时
        /// 经常出现两颗重叠、另一侧空一大块；均匀分布保证每次的弹幕形状都可读，
        /// 也让"近距离一发全中"成为可预期的设计而不是运气。</para>
        /// </remarks>
        private void FireOnce(WeaponRuntime runtime, float spreadOffsetDegrees)
        {
            var pellets = runtime.Weapon.PelletCount < 1 ? 1 : runtime.Weapon.PelletCount;
            for (var i = 0; i < pellets; i++)
            {
                var pelletOffset = spreadOffsetDegrees
                    + ResolvePelletOffsetDegrees(i, pellets, runtime.Weapon.PelletSpreadDegrees);
                FirePellet(runtime, pelletOffset, i);
            }
        }

        /// <summary>
        /// 扇面内第 <paramref name="index"/> 颗弹丸相对中心的偏移（度）。
        /// </summary>
        /// <param name="index">弹丸序号（从 0 开始）。</param>
        /// <param name="count">弹丸总数。</param>
        /// <param name="fanDegrees">扇面总宽度（度）。</param>
        /// <returns>在 [-fan/2, +fan/2] 内均匀分布的角度偏移；单弹丸或零宽度时返回 0。</returns>
        private static float ResolvePelletOffsetDegrees(int index, int count, float fanDegrees)
        {
            if (count <= 1 || fanDegrees <= 0f)
            {
                return 0f;
            }

            var t = index / (float)(count - 1);
            return (t - 0.5f) * fanDegrees;
        }

        /// <summary>发射一颗弹丸：算方向、投射射线、结算伤害、广播带弹丸序号的事件。</summary>
        private void FirePellet(WeaponRuntime runtime, float spreadOffsetDegrees, int pelletIndex)
        {
            var origin = m_MuzzlePosition;
            var range = runtime.Weapon.RangeMeters;
            var direction = ResolveShotDirection(spreadOffsetDegrees);

            var didHit = m_Probe.TryRaycast(origin, direction, range, out var hit);
            var endPoint = didHit ? hit.Point : origin + (direction * range);
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
                // 枪声半径 = 武器射程：本项目里"打得到多远"与"多远处能听见"是同一个数字，
                // 短射程的手枪因此天然比步枪安静。将来加消音器时换成另一个值即可。
                runtime.Weapon.RangeMeters,
                0d,
                m_Sequence,
                pelletIndex));
        }

        /// <summary>
        /// 计算这一发的三维方向：枪口水平指向"地面瞄准点按散布旋转后的方位"。
        /// </summary>
        /// <param name="spreadOffsetDegrees">散布造成的角度偏移（度）。</param>
        /// <returns>单位方向向量。</returns>
        /// <remarks>
        /// <para><b>方向取水平，而不是"枪口指向瞄准点"。</b>
        /// 后者会让射线在瞄准点处落到地面，看起来就是"子弹到准星为止"。</para>
        /// <para>水平射线的长度由武器射程限制，因此子弹会**穿过准星继续飞**，
        /// 直到命中目标或打满射程——手枪 25 米、步枪 40 米，射程差异体现在这里。</para>
        /// <para>与准星的对齐改由表现层解决：弹道画在地面上（见 <c>TracerRenderer</c>），
        /// 因此它仍然穿过准星，不会出现"子弹和准星不在一条线"的问题。</para>
        /// </remarks>
        private Vector3 ResolveShotDirection(float spreadOffsetDegrees)
        {
            var groundMuzzle = new Vector3(m_MuzzlePosition.x, 0f, m_MuzzlePosition.z);
            var toAim = m_AimWorldPoint.IsNearlyZero
                ? Vector2F.FromDegrees(m_AimDegrees)
                : new Vector2F(m_AimWorldPoint.X - groundMuzzle.x, m_AimWorldPoint.Y - groundMuzzle.z);

            if (toAim.IsNearlyZero)
            {
                // 瞄准点与角色重合时没有方向可言，退回当前朝向。
                toAim = Vector2F.FromDegrees(m_AimDegrees);
            }

            // 散布绕竖直轴旋转瞄准点，因此弹着点的距离不变、只改变方位。
            var rotated = Quaternion.AngleAxis(spreadOffsetDegrees, Vector3.up)
                          * new Vector3(toAim.X, 0f, toAim.Y);

            // 只取水平分量：射线因此不会落到地面，能一直飞到射程末端。
            return rotated.sqrMagnitude > 1e-6f ? rotated.normalized : Vector3.forward;
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
}
