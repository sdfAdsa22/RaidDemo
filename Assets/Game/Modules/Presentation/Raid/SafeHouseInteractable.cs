using UnityEngine;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 安全屋里的一个可交互设施（仓库、商人、出口、任务板）。
    /// </summary>
    /// <remarks>
    /// <para>只承载「这里有个什么东西」这一条事实：类型、显示名、交互距离。
    /// 具体行为（打开仓库界面、弹出地图选择、打开交易面板）由安全屋的装配层分派，
    /// 与战局里的战利品箱是一样的分工——标记只描述位置与身份，行为不写在标记上。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class SafeHouseInteractable : MonoBehaviour
    {
        /// <summary>设施类型。</summary>
        public enum Kind
        {
            /// <summary>仓库：存取装备与战利品。</summary>
            Stash = 0,

            /// <summary>商人：买卖（批次 3 才有实际交易）。</summary>
            Merchant = 1,

            /// <summary>出口：选择地图并出击。</summary>
            Exit = 2,

            /// <summary>任务板（批次 4）。</summary>
            QuestBoard = 3,

            /// <summary>
            /// 开发期测试箱：固定产出武器、护甲、头盔、背包。
            /// </summary>
            /// <remarks>交付前连同 <c>crate.debug</c> 定义一起删除。</remarks>
            DebugCrate = 4,
        }

        /// <summary>设施类型。</summary>
        [SerializeField] private Kind m_Kind = Kind.Stash;

        /// <summary>交互提示里显示的名字，例如「仓库」「商人」「出口」。</summary>
        [SerializeField] private string m_DisplayName = "设施";

        /// <summary>可交互距离（米）。</summary>
        [SerializeField] private float m_RangeMeters = 2.5f;

        /// <summary>设施类型。</summary>
        public Kind Type
        {
            get { return m_Kind; }
        }

        /// <summary>显示名。</summary>
        public string DisplayName
        {
            get { return m_DisplayName; }
        }

        /// <summary>可交互距离（米）。</summary>
        public float RangeMeters
        {
            get { return m_RangeMeters; }
        }
    }
}
