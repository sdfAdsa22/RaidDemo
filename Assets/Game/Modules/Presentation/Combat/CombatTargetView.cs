using RaidDemo.Combat;
using UnityEngine;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 可被击中的靶子：把场景物体与战斗层的单位标识对应起来。
    /// </summary>
    /// <remarks>
    /// <para>它只做三件事：告诉射线我是几号目标、我的中心在哪、被击中时变个颜色。
    /// 生命值与护甲在战斗层的 CombatantState 里，这里不保存第二份——
    /// 双份状态一定会有一天对不上。</para>
    /// <para>命中变色是本阶段唯一的受击反馈，真正的受击动画与特效属于 M7。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class CombatTargetView : MonoBehaviour
    {
        /// <summary>命中后闪色的持续时长（秒）。</summary>
        private const float HitFlashSeconds = 0.12f;

        /// <summary>普通状态下的颜色。</summary>
        private static readonly Color NormalColor = new Color(0.85f, 0.45f, 0.25f);

        /// <summary>被击中时的颜色。</summary>
        private static readonly Color HitColor = new Color(1f, 0.95f, 0.6f);

        /// <summary>被摧毁后的颜色。</summary>
        private static readonly Color DestroyedColor = new Color(0.25f, 0.25f, 0.28f);

        private Renderer m_Renderer;
        private MaterialPropertyBlock m_PropertyBlock;
        private float m_FlashRemaining;

        /// <summary>战斗层分配给本靶子的单位标识。</summary>
        public int TargetId { get; private set; }

        /// <summary>靶子中心的世界坐标，用于暴击判定。</summary>
        public Vector3 CenterWorldPosition { get; private set; }

        /// <summary>
        /// 初始化靶子。
        /// </summary>
        /// <param name="targetId">战斗层分配的单位标识。</param>
        public void Initialize(int targetId)
        {
            TargetId = targetId;
            m_Renderer = GetComponentInChildren<Renderer>();
            m_PropertyBlock = new MaterialPropertyBlock();

            // 中心取碰撞体包围盒的中心而不是变换原点：
            // 灰盒靶子的胶囊图元原点恰好在几何中心，换成别的模型后就不一定了。
            var targetCollider = GetComponentInChildren<Collider>();
            CenterWorldPosition = targetCollider != null ? targetCollider.bounds.center : transform.position;

            ApplyColor(NormalColor);
        }

        /// <summary>触发一次受击闪烁。</summary>
        public void FlashHit()
        {
            m_FlashRemaining = HitFlashSeconds;
            ApplyColor(HitColor);
        }

        /// <summary>把靶子标记为已摧毁。</summary>
        public void MarkDestroyed()
        {
            m_FlashRemaining = 0f;
            ApplyColor(DestroyedColor);
        }

        private void Update()
        {
            if (m_FlashRemaining <= 0f)
            {
                return;
            }

            m_FlashRemaining -= Time.deltaTime;
            if (m_FlashRemaining <= 0f)
            {
                ApplyColor(NormalColor);
            }
        }

        /// <summary>用属性块改颜色，避免为每个靶子生成材质实例。</summary>
        private void ApplyColor(Color color)
        {
            if (m_Renderer == null)
            {
                return;
            }

            if (m_PropertyBlock == null)
            {
                m_PropertyBlock = new MaterialPropertyBlock();
            }

            m_Renderer.GetPropertyBlock(m_PropertyBlock);
            m_PropertyBlock.SetColor("_BaseColor", color);
            m_Renderer.SetPropertyBlock(m_PropertyBlock);
        }
    }
}
