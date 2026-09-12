using UnityEngine;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 安全屋的靶子：用来试枪，不还手、不会死。
    /// </summary>
    /// <remarks>
    /// <para>它不是敌人，因此**不进战斗层的单位表**——否则靶子会出现在击杀统计里，
    /// 也会被 AI 的感知与寻路当成目标。命中反馈由安全屋的表现层自己处理。</para>
    ///
    /// <para>靶子存在的意义有两个：给玩家一个不冒风险试枪的地方，
    /// 以及给演示视频一个「武器手感」的展示位。它同时是开发期验证弹道与伤害的最快路径。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ShootingTarget : MonoBehaviour
    {
        /// <summary>靶环半径（米）。命中越靠中心，显示的数字越醒目。</summary>
        [SerializeField] private float m_BullseyeRadius = 0.18f;

        /// <summary>累计承受的伤害，用于在面板上显示「打了多少」。</summary>
        public float TotalDamage { get; private set; }

        /// <summary>累计命中次数。</summary>
        public int HitCount { get; private set; }

        /// <summary>靶环半径（米）。</summary>
        public float BullseyeRadius
        {
            get { return m_BullseyeRadius; }
        }

        /// <summary>登记一次命中。</summary>
        /// <param name="damage">本次伤害。</param>
        /// <param name="localHitPoint">命中点（靶子的本地坐标），用于判断是否打在靶心。</param>
        /// <returns>是否命中靶心。</returns>
        public bool RegisterHit(float damage, Vector3 localHitPoint)
        {
            TotalDamage += damage;
            HitCount++;

            var offset = new Vector2(localHitPoint.x, localHitPoint.y);
            return offset.magnitude <= m_BullseyeRadius;
        }

        /// <summary>清空统计。</summary>
        public void ResetStats()
        {
            TotalDamage = 0f;
            HitCount = 0;
        }
    }
}
