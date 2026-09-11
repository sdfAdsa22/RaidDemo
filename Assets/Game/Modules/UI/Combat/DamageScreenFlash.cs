using System;
using RaidDemo.Combat;
using RaidDemo.Kernel;
using UnityEngine;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 玩家受击时的屏幕红闪反馈。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么用屏幕闪烁，而不是给玩家模型换色：</b>角色的颜色在这个项目里已经被三种信息占用——
    /// 敌人用颜色表达状态、命中反馈用闪白、玩家自身有固定配色。再加上一层"受伤"的换色，
    /// 玩家就无法判断自己看到的红色到底是敌人进入交战，还是自己正在掉血。
    /// 屏幕反馈还有第二个好处：斜俯视下玩家的注意力集中在准星与战场，
    /// 屏幕边缘的变化不需要盯着角色也能察觉到。</para>
    ///
    /// <para>叠加层用一张全屏半透明图，靠 alpha 衰减实现闪烁。它不接受射线（<c>raycastTarget = false</c>），
    /// 因此不会挡住背包界面的拖拽操作。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class DamageScreenFlash : MonoBehaviour
    {
        /// <summary>普通受击的闪烁时长（秒）。</summary>
        private const float HitFlashSeconds = 0.35f;

        /// <summary>普通受击的起始不透明度。</summary>
        private const float HitAlpha = 0.28f;

        /// <summary>致命一击的闪烁时长（秒）。明显更长，用于区分"掉血"与"死了"。</summary>
        private const float KillFlashSeconds = 0.9f;

        /// <summary>致命一击的起始不透明度。</summary>
        private const float KillAlpha = 0.55f;

        /// <summary>界面层级。高于背包面板（200）与战斗信息（150），低于调试面板。</summary>
        private const int SortingOrder = 400;

        private static readonly Color FlashColor = new Color(0.78f, 0.06f, 0.06f);

        private Image m_Overlay;
        private IDisposable m_Subscription;
        private GameObject m_OverlayHost;
        private int m_PlayerCombatantId;
        private float m_Remaining;
        private float m_Duration;

        /// <summary>
        /// 绑定事件总线并构建叠加层。
        /// </summary>
        /// <param name="eventBus">事件总线。</param>
        /// <param name="playerCombatantId">玩家在战斗层中的单位标识。</param>
        public void Bind(EventBus eventBus, int playerCombatantId)
        {
            m_PlayerCombatantId = playerCombatantId;
            BuildOverlay();

            m_Subscription?.Dispose();
            m_Subscription = eventBus?.Subscribe<DamageAppliedEvent>(OnDamageApplied);
        }

        /// <summary>当前是否正在闪烁。供测试与调试读取。</summary>
        public bool IsFlashing
        {
            get { return m_Remaining > 0f; }
        }

        private void OnDestroy()
        {
            m_Subscription?.Dispose();
            m_Subscription = null;
        }

        private void Update()
        {
            if (m_Remaining <= 0f || m_Overlay == null)
            {
                return;
            }

            m_Remaining -= Time.deltaTime;
            if (m_Remaining <= 0f)
            {
                m_Remaining = 0f;
                m_OverlayHost.SetActive(false);
                return;
            }

            var color = FlashColor;
            color.a = StartAlpha() * (m_Remaining / m_Duration);
            m_Overlay.color = color;
        }

        /// <summary>只响应玩家自己挨打的事件。</summary>
        private void OnDamageApplied(DamageAppliedEvent evt)
        {
            if (evt.TargetId != m_PlayerCombatantId || m_Overlay == null)
            {
                return;
            }

            m_Duration = evt.WasKilled ? KillFlashSeconds : HitFlashSeconds;
            m_Remaining = m_Duration;

            var color = FlashColor;
            color.a = StartAlpha();
            m_Overlay.color = color;
            m_OverlayHost.SetActive(true);
        }

        /// <summary>本次闪烁的起始不透明度。</summary>
        private float StartAlpha()
        {
            return m_Duration >= KillFlashSeconds ? KillAlpha : HitAlpha;
        }

        /// <summary>
        /// 构建全屏叠加层。
        /// </summary>
        /// <remarks>
        /// 独立创建自己的画布而不是复用战斗信息面板的画布：那张画布的排序值较低，
        /// 而受击反馈必须盖在背包之上。多一个画布几乎没有开销，却省掉了一整类层级冲突。
        /// </remarks>
        private void BuildOverlay()
        {
            if (m_Overlay != null)
            {
                return;
            }

            m_OverlayHost = new GameObject("DamageFlashCanvas", typeof(Canvas), typeof(CanvasScaler));
            m_OverlayHost.transform.SetParent(transform, worldPositionStays: false);

            var canvas = m_OverlayHost.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            var imageHost = new GameObject("Overlay", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)imageHost.transform;
            rect.SetParent(m_OverlayHost.transform, worldPositionStays: false);

            // 直接铺满父级画布：不写死分辨率，因此任何窗口尺寸下都能盖满屏幕。
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            m_Overlay = imageHost.GetComponent<Image>();
            m_Overlay.color = new Color(FlashColor.r, FlashColor.g, FlashColor.b, 0f);
            m_Overlay.raycastTarget = false;

            m_OverlayHost.SetActive(false);
        }
    }
}
