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
        private static readonly Color DefaultNormalColor = new Color(0.85f, 0.45f, 0.25f);

        /// <summary>被击中时的颜色。</summary>
        private static readonly Color HitColor = new Color(1f, 0.95f, 0.6f);

        /// <summary>被摧毁后的颜色。</summary>
        private static readonly Color DestroyedColor = new Color(0.25f, 0.25f, 0.28f);

        private Renderer m_Renderer;
        private Collider m_Collider;
        private MaterialPropertyBlock m_PropertyBlock;
        private float m_FlashRemaining;

        /// <summary>当前的基础色。敌人会按状态改它，靶子则一直用默认值。</summary>
        private Color m_NormalColor = DefaultNormalColor;

        /// <summary>是否已被摧毁。摧毁后不再接受基础色变更。</summary>
        private bool m_IsDestroyed;

        /// <summary>战斗层分配给本靶子的单位标识。</summary>
        public int TargetId { get; private set; }

        /// <summary>
        /// 目标中心的世界坐标，用于视线判定与暴击判定。
        /// </summary>
        /// <remarks>
        /// <para><b>每次读取时重算，而不是初始化时算一次。</b>靶子是静止的，那样做没问题；
        /// 但 M4 的敌人会移动，缓存下来的中心点会永远停在出生位置，
        /// 表现为"敌人跑起来之后 AI 看不见它、暴击判定也失效"。</para>
        /// <para>代价是一次包围盒查询。它只在被射线命中的那一帧发生，可以忽略。</para>
        /// </remarks>
        public Vector3 CenterWorldPosition
        {
            get
            {
                if (m_Collider == null)
                {
                    m_Collider = GetComponentInChildren<Collider>();
                }

                return m_Collider != null ? m_Collider.bounds.center : transform.position;
            }
        }

        /// <summary>
        /// 初始化靶子。
        /// </summary>
        /// <param name="targetId">战斗层分配的单位标识。</param>
        public void Initialize(int targetId)
        {
            Initialize(targetId, colorFeedback: true);
        }

        /// <summary>
        /// 初始化靶子，并指定是否需要换色反馈。
        /// </summary>
        /// <param name="targetId">战斗层分配的单位标识。</param>
        /// <param name="colorFeedback">
        /// 为 false 时不接管渲染器颜色。
        /// </param>
        /// <remarks>
        /// 玩家也需要被射线命中（AI 要能打到玩家），但玩家的配色由角色本身与受击界面负责：
        /// 若让本组件去改玩家模型的颜色，命中一次就会把角色染成靶子的橙色，
        /// 而且闪烁结束后还会把颜色重置成靶子色而不是角色色。因此这里提供"只要标识、
        /// 不要换色"的模式，让同一种身份登记方式服务于两种不同的表现需求。
        /// </remarks>
        public void Initialize(int targetId, bool colorFeedback)
        {
            TargetId = targetId;
            m_Renderer = colorFeedback ? GetComponentInChildren<Renderer>() : null;
            m_PropertyBlock = new MaterialPropertyBlock();

            // 中心取碰撞体包围盒的中心而不是变换原点：
            // 灰盒靶子的胶囊图元原点恰好在几何中心，换成别的模型后就不一定了。
            m_Collider = GetComponentInChildren<Collider>();

            m_IsDestroyed = false;
            m_NormalColor = DefaultNormalColor;
            ApplyColor(m_NormalColor);
        }

        /// <summary>
        /// 设置基础色。敌人用它表达当前状态（巡逻/调查/交战/撤退）。
        /// </summary>
        /// <param name="color">新的基础色。</param>
        /// <remarks>正在闪烁或已被摧毁时只记录，不立即改色，避免闪烁被打断或被覆盖。</remarks>
        public void SetNormalColor(Color color)
        {
            m_NormalColor = color;
            if (m_FlashRemaining > 0f || m_IsDestroyed)
            {
                return;
            }

            ApplyColor(m_NormalColor);
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
            m_IsDestroyed = true;
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
                ApplyColor(m_NormalColor);
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
