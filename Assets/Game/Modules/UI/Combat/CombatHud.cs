using RaidDemo.Combat;
using RaidDemo.Inventory;
using TMPro;
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
    public sealed partial class CombatHud : MonoBehaviour
    {
        /// <summary>界面根节点。隐藏它需要拿到画布本身，而不是本组件所在的节点。</summary>
        private GameObject m_CanvasHost;

        /// <summary>界面参考分辨率。</summary>
        private const float ReferenceWidth = 1920f;

        /// <summary>界面参考高度。</summary>
        private const float ReferenceHeight = 1080f;

        /// <summary>面板到屏幕左下角的边距（像素）。</summary>
        /// <remarks>
        /// <para>取 56 而不是贴边的 20~30：HUD 压在最角落时容易被玩家忽略，也容易被屏幕边框切掉。</para>
        /// <para>情报放在**左下角**而不是右下角：右下角在 Game 视图"Play Maximized"等
        /// 显示模式下容易贴近窗口边缘被裁掉，而左下角在灰盒场景里也是空的
        /// （背包面板居中，不会覆盖到这里）。</para>
        /// </remarks>
        private const float Margin = 56f;

        /// <summary>
        /// 底板宽度与高度（像素）。
        /// </summary>
        /// <remarks>
        /// 宽度按最长的一行信息（护甲 / 头盔）加左右内边距反推为 240。
        /// 原先的 340 在只有一把 AK-74 时右侧会空出接近一半，视觉上不像一块信息卡，
        /// 更像一个没画完的方框。
        /// </remarks>
        private const float PanelWidth = 240f;

        /// <remarks>M8 批次 2 从 176 加到 196：底部多了一行"弹药口径提醒 / 换弹失败原因"，
        /// 那一行必须与换弹进度条各占各的位置，不能叠在一起。</remarks>
        private const float PanelHeight = 196f;

        /// <summary>换弹进度条高度（像素）。</summary>
        private const float BarHeight = 10f;

        /// <summary>
        /// HUD 压在世界上，因此用"纸面底板 + 深墨文字"。
        /// </summary>
        /// <remarks>与小样一致：白色圆角底板 + 深墨文字，描边保证它在亮绿草地与暗色厂房上都看得清。
        /// 相比"深底浅字"，这套更贴近卡通扁平的观感，也和背包、商人是同一套纸面语言。</remarks>
        private static readonly Color TextColor = UiPalette.Ink;
        private static readonly Color DimTextColor = UiPalette.InkSoft;
        private static readonly Color LowAmmoColor = UiPalette.Yellow;
        private static readonly Color LowHealthColor = UiPalette.Bad;
        private static readonly Color DeadColor = UiPalette.InkDisabled;
        private static readonly Color BarFillColor = UiPalette.Warn;

        private PlayerWeaponController m_Controller;
        private PlayerLoadout m_Loadout;

        private TextMeshProUGUI m_WeaponLabel;
        private TextMeshProUGUI m_AmmoLabel;
        private TextMeshProUGUI m_ReserveLabel;
        private TextMeshProUGUI m_HealthLabel;

        /// <summary>护甲显示（身体护甲与头盔的等级和耐久）。</summary>
        private TextMeshProUGUI m_ArmorLabel;
        /// <summary>底部提示行：弹药口径提醒与换弹失败原因。</summary>
        private TextMeshProUGUI m_HintLabel;
        private Image m_ReloadBarFill;
        private RectTransform m_ReloadBarRoot;
        private string m_WeaponName = "无武器";
        private string m_CaliberId;

        /// <summary>换弹失败等一次性提示的剩余显示时间（秒）。</summary>
        private float m_HintRemaining;

        /// <summary>一次性提示的显示时长。</summary>
        private const float HintSeconds = 3f;

        /// <summary>生命比例低于该值时生命数字变红。</summary>
        private const float LowHealthRatio = 0.3f;

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
        /// 显示或隐藏整个战斗界面。
        /// </summary>
        /// <param name="visible">是否显示。</param>
        /// <remarks>
        /// 主菜单与结算期间隐藏：那时战局尚未开始或已经结束，
        /// 「生命 --/--、无武器」这类信息没有意义，只会让菜单背后显得杂乱。
        /// </remarks>
        public void SetVisible(bool visible)
        {
            if (m_CanvasHost != null)
            {
                m_CanvasHost.SetActive(visible);
            }
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
                m_WeaponLabel.text = BuildWeaponText();
            }
        }

        /// <summary>
        /// 显示一条一次性提示（例如"弹药挂里没有 5.45 弹药"）。
        /// </summary>
        /// <param name="message">提示内容；为空时立即清空。</param>
        /// <param name="isWarning">true 用警告色，false 用普通文字色。</param>
        /// <remarks>提示会自动消失；显示期间优先于底部的常驻弹药提醒，
        /// 因为"刚刚按了 R 却没反应"的答案比背景提醒更紧急。</remarks>
        public void ShowHint(string message, bool isWarning = true)
        {
            if (m_HintLabel == null)
            {
                return;
            }

            m_HintLabel.text = message ?? string.Empty;
            m_HintLabel.color = isWarning ? LowAmmoColor : DimTextColor;
            m_HintRemaining = string.IsNullOrEmpty(message) ? 0f : HintSeconds;
        }

        /// <summary>
        /// 更新生命值显示。由启动层推送。
        /// </summary>
        /// <param name="current">当前生命值。</param>
        /// <param name="max">生命上限。</param>
        /// <param name="isAlive">是否存活。</param>
        /// <remarks>
        /// <para><b>生命值走"推送"，而弹药走"每帧拉取权威状态"</b>，这是刻意的差别：
        /// 弹药会因为开火、换弹、换枪、捡拾四条路径变化，任何一条漏掉就再也不会自愈，
        /// 因此每帧读取最稳；而生命值只由受击与治疗改变，来源是战斗层的单位状态，
        /// 界面若自己去取就需要反向依赖 AI 与启动层的数据结构。</para>
        /// <para>方法名与参数保持"界面只描述它需要什么"，不暴露任何战斗层类型。</para>
        /// </remarks>
        public void SetHealth(float current, float max, bool isAlive)
        {
            if (m_HealthLabel == null)
            {
                return;
            }

            if (!isAlive)
            {
                m_HealthLabel.text = "已阵亡";
                m_HealthLabel.color = DeadColor;
                return;
            }

            m_HealthLabel.text = $"生命 {Mathf.CeilToInt(current)} / {Mathf.CeilToInt(max)}";

            // 用向上取整的整数比较，避免"血量恰好 30%"时因为浮点误差而闪烁变色。
            m_HealthLabel.color = max > 0f && current <= max * LowHealthRatio
                ? LowHealthColor
                : TextColor;
        }

        /// <summary>
        /// 更新护甲显示。由启动层推送。
        /// </summary>
        /// <param name="bodyLevel">身体护甲等级，0 表示无甲。</param>
        /// <param name="bodyDurability">身体护甲当前耐久。</param>
        /// <param name="headLevel">头盔等级，0 表示无盔。</param>
        /// <param name="headDurability">头盔当前耐久。</param>
        /// <remarks>
        /// 耐久用整数显示：护甲耐久的实际意义是「还能挡几下」，
        /// 小数点后两位对玩家没有任何决策价值，只会让这一行更长。
        /// </remarks>
        public void SetArmor(int bodyLevel, float bodyDurability, int headLevel, float headDurability)
        {
            if (m_ArmorLabel == null)
            {
                return;
            }

            var body = bodyLevel > 0
                ? $"甲 {bodyLevel}级 {Mathf.CeilToInt(bodyDurability)}"
                : "甲 无";
            var head = headLevel > 0
                ? $"盔 {headLevel}级 {Mathf.CeilToInt(headDurability)}"
                : "盔 无";

            m_ArmorLabel.text = $"{body}  ｜  {head}";
            m_ArmorLabel.color = bodyLevel > 0 || headLevel > 0 ? TextColor : DimTextColor;
        }

        private void Update()
        {
            if (m_Controller == null)
            {
                return;
            }

            m_WeaponLabel.text = BuildWeaponText();

            var runtime = m_Controller.Runtime;
            if (runtime == null)
            {
                m_AmmoLabel.text = "-- / --";
                m_AmmoLabel.color = DimTextColor;
                m_ReserveLabel.text = "无弹匣";
                m_HintLabel.text = string.Empty;
                SetBarVisible(false);
                return;
            }

            var capacity = runtime.Weapon.MagazineCapacity;
            m_AmmoLabel.text = $"{runtime.MagazineAmmo} / {capacity}";

            // 余弹低于四分之一时变色：玩家不需要去读数字就能感到该换弹了。
            m_AmmoLabel.color = runtime.MagazineAmmo * 4 <= capacity ? LowAmmoColor : TextColor;

            // 换弹只从弹药挂取弹，因此这里必须显示**弹药挂**的余量而不是背包的。
            // 显示背包数量会让玩家看到一个换不了弹的"备弹 120"。
            var pouch = m_CaliberId == null ? 0 : AmmoReserve.CountAvailable(m_Loadout.AmmoPouch, m_CaliberId);
            var backpack = m_CaliberId == null ? 0 : AmmoReserve.CountAvailable(m_Loadout.Backpack, m_CaliberId);
            m_ReserveLabel.text = $"弹挂 {pouch}   背包 {backpack}";

            UpdateHint(runtime, pouch, backpack);
            UpdateReloadBar(runtime);
        }

        /// <summary>武器行文字：名字 + 按口径着色的口径徽标文字。</summary>
        private string BuildWeaponText()
        {
            if (string.IsNullOrEmpty(m_CaliberId))
            {
                return m_WeaponName;
            }

            var display = RaidDemo.Data.CaliberPalette.GetDisplayName(m_CaliberId);
            var hex = RaidDemo.Data.CaliberPalette.GetHex(m_CaliberId);
            return $"{m_WeaponName}  <color={hex}>· {display}</color>";
        }

        /// <summary>
        /// 维护底部提示行：一次性提示优先，否则显示常驻的弹药提醒。
        /// </summary>
        /// <param name="runtime">武器运行时。</param>
        /// <param name="pouch">弹药挂里当前口径的余量。</param>
        /// <param name="backpack">背包里当前口径的余量。</param>
        /// <remarks>
        /// <para>常驻提醒只覆盖两种会让人卡住的情况：</para>
        /// <list type="bullet">
        /// <item><description>弹匣没满、弹药挂是空的，但背包里有——提醒去搬弹药（换弹只从弹药挂取弹）；</description></item>
        /// <item><description>弹匣没满、两处都没有——明确写出"没有 xx 弹药"，而不是按 R 毫无反应。</description></item>
        /// </list>
        /// </remarks>
        private void UpdateHint(WeaponRuntime runtime, int pouch, int backpack)
        {
            if (m_HintLabel == null)
            {
                return;
            }

            if (m_HintRemaining > 0f)
            {
                m_HintRemaining -= Time.unscaledDeltaTime;
                if (m_HintRemaining <= 0f)
                {
                    m_HintLabel.text = string.Empty;
                }

                return;
            }

            var magazineNotFull = runtime.MagazineAmmo < runtime.Weapon.MagazineCapacity;
            var display = RaidDemo.Data.CaliberPalette.GetDisplayName(m_CaliberId);

            if (magazineNotFull && pouch <= 0 && backpack > 0)
            {
                m_HintLabel.text = $"弹药挂是空的：背包里有 {backpack} 发 {display}";
                m_HintLabel.color = LowAmmoColor;
            }
            else if (magazineNotFull && pouch <= 0 && backpack <= 0)
            {
                m_HintLabel.text = $"没有 {display} 弹药";
                m_HintLabel.color = LowHealthColor;
            }
            else
            {
                m_HintLabel.text = string.Empty;
            }
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

            UiFactory.SetBarProgress(
                m_ReloadBarFill,
                m_ReloadBarRoot.sizeDelta.x,
                runtime.ReloadProgress01);
        }

        /// <summary>显示或隐藏换弹进度条。</summary>
        private void SetBarVisible(bool visible)
        {
            if (m_ReloadBarRoot != null && m_ReloadBarRoot.gameObject.activeSelf != visible)
            {
                m_ReloadBarRoot.gameObject.SetActive(visible);
            }
        }
    }
}
