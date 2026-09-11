using RaidDemo.Combat;
using RaidDemo.Inventory;
using UnityEngine;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 战斗信息面板：当前武器、弹匣余量、备用弹药与换弹进度。
    /// </summary>
    /// <remarks>
    /// <para>没有这块界面，玩家就无法回答"我现在还有几发"这个每几秒都要问一次的问题，
    /// 射击系统再正确也玩不下去。</para>
    /// <para>数据全部**每帧从权威状态读取**，而不是靠事件维护一份副本：
    /// 弹匣数量会因为开火、换弹、换枪三条路径变化，任何一条漏发事件，
    /// 界面就会显示错误且不会再自愈。每帧读取的开销只是几次属性访问。</para>
    /// </remarks>
    public sealed class CombatHud : MonoBehaviour
    {
        /// <summary>界面参考分辨率。</summary>
        private const float ReferenceWidth = 1920f;

        /// <summary>界面参考高度。</summary>
        private const float ReferenceHeight = 1080f;

        /// <summary>面板到屏幕右下角的边距（像素）。</summary>
        /// <remarks>取 56 而不是贴边的 20~30：HUD 压在最角落时容易被玩家忽略，也容易被屏幕边框切掉。</remarks>
        private const float Margin = 56f;

        /// <summary>面板宽度（像素）。</summary>
        private const float PanelWidth = 260f;

        /// <summary>换弹进度条高度（像素）。</summary>
        private const float BarHeight = 10f;

        private static readonly Color TextColor = new Color(0.94f, 0.94f, 0.96f);
        private static readonly Color DimTextColor = new Color(0.72f, 0.72f, 0.78f);
        private static readonly Color LowAmmoColor = new Color(1f, 0.55f, 0.35f);
        private static readonly Color BarBackColor = new Color(0.18f, 0.18f, 0.20f, 0.8f);
        private static readonly Color BarFillColor = new Color(0.95f, 0.75f, 0.25f);

        private PlayerWeaponController m_Controller;
        private PlayerLoadout m_Loadout;

        private Text m_WeaponLabel;
        private Text m_AmmoLabel;
        private Text m_ReserveLabel;
        private Image m_ReloadBarFill;
        private RectTransform m_ReloadBarRoot;
        private string m_WeaponName = "无武器";
        private string m_CaliberId;

        /// <summary>
        /// 初始化并构建界面。
        /// </summary>
        /// <param name="controller">武器控制器，弹匣与换弹状态从它读取。</param>
        /// <param name="loadout">角色携带物，备用弹药从它的背包里统计。</param>
        public void Initialize(PlayerWeaponController controller, PlayerLoadout loadout)
        {
            m_Controller = controller;
            m_Loadout = loadout;
            BuildLayout();
        }

        /// <summary>
        /// 设置当前武器的显示信息。换枪时由启动层调用。
        /// </summary>
        /// <param name="displayName">武器显示名，没有武器时传 null。</param>
        /// <param name="caliberId">口径标识，没有武器时传 null。</param>
        public void SetWeapon(string displayName, string caliberId)
        {
            m_WeaponName = string.IsNullOrEmpty(displayName) ? "无武器" : displayName;
            m_CaliberId = caliberId;

            // 立刻写一次文本，而不是等下一帧的 Update。
            // 换枪是个离散事件，界面上晚一帧才变虽然看不出差别，
            // 但"设置之后立刻可读"让这套状态在调试与自动化验证中都更可靠。
            if (m_WeaponLabel != null)
            {
                m_WeaponLabel.text = m_WeaponName;
            }
        }

        private void Update()
        {
            if (m_Controller == null)
            {
                return;
            }

            m_WeaponLabel.text = m_WeaponName;

            var runtime = m_Controller.Runtime;
            if (runtime == null)
            {
                m_AmmoLabel.text = "-- / --";
                m_AmmoLabel.color = DimTextColor;
                m_ReserveLabel.text = "无弹匣";
                SetBarVisible(false);
                return;
            }

            var capacity = runtime.Weapon.MagazineCapacity;
            m_AmmoLabel.text = $"{runtime.MagazineAmmo} / {capacity}";

            // 余弹低于四分之一时变色：玩家不需要去读数字就能感到该换弹了。
            m_AmmoLabel.color = runtime.MagazineAmmo * 4 <= capacity ? LowAmmoColor : TextColor;

            var reserve = m_CaliberId == null
                ? 0
                : AmmoReserve.CountAvailable(m_Loadout.Backpack, m_CaliberId);
            m_ReserveLabel.text = $"备弹 {reserve}";

            UpdateReloadBar(runtime);
        }

        /// <summary>刷新换弹进度条。</summary>
        private void UpdateReloadBar(WeaponRuntime runtime)
        {
            var reloading = runtime.IsReloading;
            SetBarVisible(reloading);
            if (!reloading)
            {
                return;
            }

            m_ReloadBarFill.rectTransform.sizeDelta = new Vector2(
                PanelWidth * Mathf.Clamp01(runtime.ReloadProgress01),
                0f);
        }

        /// <summary>显示或隐藏换弹进度条。</summary>
        private void SetBarVisible(bool visible)
        {
            if (m_ReloadBarRoot != null && m_ReloadBarRoot.gameObject.activeSelf != visible)
            {
                m_ReloadBarRoot.gameObject.SetActive(visible);
            }
        }

        /// <summary>构建整套界面。</summary>
        private void BuildLayout()
        {
            var canvasHost = new GameObject("CombatHudCanvas", typeof(Canvas), typeof(CanvasScaler));
            canvasHost.transform.SetParent(transform, worldPositionStays: false);

            var canvas = canvasHost.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // 高于准星（100），低于背包面板（200）：
            // 背包打开时面板会盖住屏幕中央，而弹药信息在右下角，两者不冲突。
            canvas.sortingOrder = 150;

            var scaler = canvasHost.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);

            var panel = new GameObject("Panel", typeof(RectTransform));
            var panelRect = (RectTransform)panel.transform;
            panelRect.SetParent(canvasHost.transform, worldPositionStays: false);
            panelRect.anchorMin = new Vector2(1f, 0f);
            panelRect.anchorMax = new Vector2(1f, 0f);
            panelRect.pivot = new Vector2(1f, 0f);
            panelRect.anchoredPosition = new Vector2(-Margin, Margin);
            panelRect.sizeDelta = new Vector2(PanelWidth, 120f);

            m_WeaponLabel = CreateLabel(panelRect, "无武器", 96f, 28f, 15, DimTextColor);
            m_AmmoLabel = CreateLabel(panelRect, "-- / --", 58f, 40f, 30, TextColor);
            m_ReserveLabel = CreateLabel(panelRect, "备弹 0", 32f, 22f, 14, DimTextColor);
            BuildReloadBar(panelRect);
        }

        /// <summary>创建换弹进度条。</summary>
        private void BuildReloadBar(RectTransform parent)
        {
            var backHost = new GameObject("ReloadBarBack", typeof(RectTransform), typeof(Image));
            var backRect = (RectTransform)backHost.transform;
            backRect.SetParent(parent, worldPositionStays: false);
            backRect.anchorMin = new Vector2(0f, 0f);
            backRect.anchorMax = new Vector2(0f, 0f);
            backRect.pivot = new Vector2(0f, 0f);
            backRect.anchoredPosition = new Vector2(0f, 14f);
            backRect.sizeDelta = new Vector2(PanelWidth, BarHeight);

            var backImage = backHost.GetComponent<Image>();
            backImage.color = BarBackColor;
            backImage.raycastTarget = false;
            m_ReloadBarRoot = backRect;

            var fillHost = new GameObject("ReloadBarFill", typeof(RectTransform), typeof(Image));
            var fillRect = (RectTransform)fillHost.transform;
            fillRect.SetParent(backRect, worldPositionStays: false);
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.anchoredPosition = Vector2.zero;
            fillRect.sizeDelta = Vector2.zero;

            m_ReloadBarFill = fillHost.GetComponent<Image>();
            m_ReloadBarFill.color = BarFillColor;
            m_ReloadBarFill.raycastTarget = false;

            backHost.SetActive(false);
        }

        /// <summary>创建一个右对齐的文本标签。</summary>
        private static Text CreateLabel(
            RectTransform parent,
            string content,
            float bottom,
            float height,
            int fontSize,
            Color color)
        {
            var host = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var rect = (RectTransform)host.transform;
            rect.SetParent(parent, worldPositionStays: false);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, bottom);
            rect.sizeDelta = new Vector2(0f, height);

            var text = host.GetComponent<Text>();
            text.font = UiFontProvider.Get(fontSize);
            text.fontSize = fontSize;
            text.text = content;
            text.alignment = TextAnchor.LowerRight;
            text.color = color;

            // 溢出显示而不是换行：文字一旦略宽于矩形，默认的自动换行会把后半段
            // 折到第二行，而矩形高度只有几十像素，第二行随即被垂直裁掉——
            // 表现就是"界面上只显示了一部分文字"。
            // 这里宁可让文字略微超出矩形，也不要它被无声地裁掉。
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            text.raycastTarget = false;
            return text;
        }
    }
}
