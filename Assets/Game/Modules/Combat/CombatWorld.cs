using System.Collections.Generic;
using RaidDemo.Data;

namespace RaidDemo.Combat
{
    /// <summary>
    /// 战斗单位注册表：按运行时标识查找到具体单位。
    /// </summary>
    /// <remarks>
    /// <para>射线检测只能返回一个整数标识（碰撞体上挂的编号），战斗层据此找到对应的
    /// <see cref="CombatantState"/>。这样命中判定就不需要传递任何 Unity 对象引用，
    /// 服务端与客户端可以各自维护自己的注册表。</para>
    /// <para>标识从 1 开始，0 保留为"没有命中任何单位"。</para>
    /// </remarks>
    public sealed class CombatWorld
    {
        private readonly Dictionary<int, CombatantState> m_Combatants;
        private int m_NextId = 1;

        /// <summary>创建一个空的战斗世界。</summary>
        public CombatWorld()
        {
            m_Combatants = new Dictionary<int, CombatantState>(16);
        }

        /// <summary>当前登记的单位数量。</summary>
        public int Count
        {
            get { return m_Combatants.Count; }
        }

        /// <summary>当前登记的全部单位。</summary>
        public IReadOnlyCollection<CombatantState> All
        {
            get { return m_Combatants.Values; }
        }

        /// <summary>
        /// 创建一个单位并登记。
        /// </summary>
        /// <param name="maxHealth">最大生命值。</param>
        /// <param name="armor">护甲参数，可为 null。</param>
        /// <returns>新单位的运行时标识。</returns>
        public int Create(float maxHealth, IArmorStats armor = null)
        {
            var id = m_NextId++;
            m_Combatants[id] = new CombatantState(id, maxHealth, armor);
            return id;
        }

        /// <summary>按标识查找单位。</summary>
        /// <param name="id">运行时标识。</param>
        /// <param name="combatant">找到的单位。</param>
        /// <returns>存在返回 true。</returns>
        public bool TryGet(int id, out CombatantState combatant)
        {
            return m_Combatants.TryGetValue(id, out combatant);
        }

        /// <summary>移除一个单位。</summary>
        /// <param name="id">运行时标识。</param>
        /// <returns>确实移除了返回 true。</returns>
        public bool Remove(int id)
        {
            return m_Combatants.Remove(id);
        }

        /// <summary>清空全部单位。仅供测试用例之间隔离使用。</summary>
        public void Clear()
        {
            m_Combatants.Clear();
            m_NextId = 1;
        }
    }
}
