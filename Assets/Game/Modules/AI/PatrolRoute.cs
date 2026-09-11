using System.Collections.Generic;
using RaidDemo.Shared;

namespace RaidDemo.AI
{
    /// <summary>
    /// 一条巡逻路线：一串按顺序经过的路径点，走到头回到起点。
    /// </summary>
    /// <remarks>
    /// <para>路线是**每个 AI 自己的数据**，不是共享的全局配置：
    /// 同一队敌人如果共用一条路线，会像排队一样黏在一起，
    /// 玩家一眼就能看出它们是脚本驱动的。</para>
    ///
    /// <para>路线为空是合法状态：<see cref="Current"/> 返回自身位置，
    /// 巡逻状态会退化为原地观察。这样装配时少写一条路线不会导致崩溃或 NullReference。</para>
    /// </remarks>
    public sealed class PatrolRoute
    {
        private readonly List<Vector2F> m_Points;
        private int m_Index;

        /// <summary>创建一条巡逻路线。</summary>
        /// <param name="points">路径点，按巡逻顺序排列。允许为空。</param>
        public PatrolRoute(IEnumerable<Vector2F> points = null)
        {
            m_Points = new List<Vector2F>(8);
            if (points != null)
            {
                m_Points.AddRange(points);
            }
        }

        /// <summary>路径点数量。</summary>
        public int Count
        {
            get { return m_Points.Count; }
        }

        /// <summary>是否没有任何路径点。</summary>
        public bool IsEmpty
        {
            get { return m_Points.Count == 0; }
        }

        /// <summary>当前正在前往的路径点。</summary>
        /// <param name="fallback">路线为空时返回的替代位置，通常是 AI 当前位置。</param>
        public Vector2F GetCurrent(Vector2F fallback)
        {
            return m_Points.Count == 0 ? fallback : m_Points[m_Index];
        }

        /// <summary>把索引推进到下一个路径点，走到末尾后回到第一个。</summary>
        public void Advance()
        {
            if (m_Points.Count == 0)
            {
                return;
            }

            m_Index = (m_Index + 1) % m_Points.Count;
        }

        /// <summary>当前路径点索引。测试与调试用。</summary>
        public int CurrentIndex
        {
            get { return m_Index; }
        }
    }
}
