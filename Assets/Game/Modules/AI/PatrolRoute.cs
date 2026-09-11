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

        /// <summary>
        /// 把全部路径点复制到调用方提供的列表里。
        /// </summary>
        /// <param name="destination">目标列表，会先被清空。</param>
        /// <returns>复制的路径点数量。</returns>
        /// <remarks>
        /// 仅供调试可视化绘制巡逻路线使用，逻辑层自己不调用它。
        /// 采用"复制到传入的列表"而不是暴露内部集合，理由见 <see cref="AiMovement.CopyWaypoints"/>。
        /// </remarks>
        public int CopyPoints(List<Vector2F> destination)
        {
            if (destination == null)
            {
                return 0;
            }

            destination.Clear();
            for (var i = 0; i < m_Points.Count; i++)
            {
                destination.Add(m_Points[i]);
            }

            return destination.Count;
        }
    }
}
