using UnityEngine;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 撤离点的场景标记：把「哪里可以撤离」这件事写进地图，而不是写死在代码里。
    /// </summary>
    /// <remarks>
    /// <para>标记的圆心就是本组件所在节点的世界坐标（只取水平面），半径由生成器写入。
    /// 启动层读取全部标记后构造 <c>RaidDemo.Raid.ExtractionZone</c> 列表交给撤离读秒器。</para>
    ///
    /// <para>为什么判定用「圆心 + 半径」而不是触发器碰撞体：
    /// 判定属于纯逻辑，必须能在无头服务端与 EditMode 测试里跑；
    /// 触发器只能存在于客户端场景中，用它做权威判定会让逻辑绑死在引擎上。
    /// 因此场景里只放一个可视化的标记，判定完全由数学计算完成。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ExtractionZoneMarker : MonoBehaviour
    {
        /// <summary>撤离点编号。同一张地图内必须唯一。</summary>
        [SerializeField] private int m_ZoneId;

        /// <summary>撤离点显示名（界面提示用中文，例如「北门」）。</summary>
        [SerializeField] private string m_DisplayName = "撤离点";

        /// <summary>判定半径（米）。取 3.5 米：比角色体积大得多，站进去不会因为贴着边缘而反复进出。</summary>
        [SerializeField] private float m_Radius = 3.5f;

        /// <summary>撤离点编号。</summary>
        public int ZoneId
        {
            get { return m_ZoneId; }
        }

        /// <summary>撤离点显示名。</summary>
        public string DisplayName
        {
            get { return m_DisplayName; }
        }

        /// <summary>判定半径（米）。</summary>
        public float Radius
        {
            get { return m_Radius; }
        }
    }
}
