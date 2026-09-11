using System;
using System.Collections.Generic;

namespace RaidDemo.AI
{
    /// <summary>
    /// 一个可被状态机驱动的状态。
    /// </summary>
    /// <typeparam name="TContext">状态共享的上下文（本项目中是 <see cref="AiContext"/>）。</typeparam>
    /// <remarks>
    /// <para><b>状态是普通 C# 类，不是 MonoBehaviour。</b>这是本项目 AI 可验证性的基础：
    /// 状态机可以在 EditMode 测试里"喂入一串感知输入、断言状态迁移序列"，
    /// 完全不需要启动游戏、加载场景或等待若干帧。</para>
    ///
    /// <para>泛型而不是写死 <c>AiContext</c>：状态机本身是与 AI 无关的通用设施，
    /// 将来战局流程、任务系统都可能需要同一套东西。真正与 AI 绑定的只有上下文类型。</para>
    /// </remarks>
    public interface IState<TContext>
    {
        /// <summary>本状态对应的标识。</summary>
        AiStateId Id { get; }

        /// <summary>进入状态时调用一次。适合重置计时器与临时变量。</summary>
        /// <param name="context">共享上下文。</param>
        void Enter(TContext context);

        /// <summary>离开状态时调用一次。适合清理只在本状态有效的意图。</summary>
        /// <param name="context">共享上下文。</param>
        void Exit(TContext context);

        /// <summary>
        /// 推进一帧。
        /// </summary>
        /// <param name="context">共享上下文。</param>
        /// <param name="deltaTime">时间步长（秒），由调用方显式传入。</param>
        /// <returns>请求迁移时返回目标状态，否则返回 <see cref="AiTransition.None"/>。</returns>
        AiTransition Tick(TContext context, float deltaTime);
    }

    /// <summary>
    /// 通用状态机：持有当前状态、推进时间、执行迁移并广播变化。
    /// </summary>
    /// <typeparam name="TContext">状态共享的上下文类型。</typeparam>
    /// <remarks>
    /// <para><b>三条刻意的设计决定：</b></para>
    /// <list type="number">
    /// <item><description><b>状态只能返回"下一个状态"，不能自己执行迁移。</b>
    /// 迁移的时机与副作用因此只有一处，不可能出现两个状态互相调用导致的重入。</description></item>
    /// <item><description><b>目标与当前状态相同时忽略。</b>状态返回自身是常见的写法笔误，
    /// 若照做就会反复触发"退出 → 进入"，把状态内的计时器一直清零——
    /// 症状是 AI 卡在原地无限重新进入同一个状态，而且日志会刷屏。</description></item>
    /// <item><description><b>时间由调用方提供。</b>与移动模拟一致，便于在联机时按固定步长重放，
    /// 也便于测试里"一次推进 5 秒"地验证超时逻辑。</description></item>
    /// </list>
    /// </remarks>
    public sealed class StateMachine<TContext>
    {
        private readonly TContext m_Context;
        private readonly Dictionary<AiStateId, IState<TContext>> m_States;
        private readonly Action<AiStateId, string> m_OnStateChanged;

        private IState<TContext> m_Current;

        /// <summary>
        /// 创建状态机。
        /// </summary>
        /// <param name="context">共享上下文。</param>
        /// <param name="states">全部可用状态，不能为空。</param>
        /// <param name="onStateChanged">
        /// 状态变化回调，参数为新的状态与迁移理由。用于广播事件与写日志。
        /// 刻意用回调而不是让状态机直接依赖事件总线：状态机是通用设施，
        /// 不应当被钉在某个项目的通信方式上。
        /// </param>
        public StateMachine(
            TContext context,
            IEnumerable<IState<TContext>> states,
            Action<AiStateId, string> onStateChanged = null)
        {
            m_Context = context;
            m_States = new Dictionary<AiStateId, IState<TContext>>(8);
            m_OnStateChanged = onStateChanged;

            if (states == null)
            {
                throw new ArgumentNullException(nameof(states));
            }

            foreach (var state in states)
            {
                if (state == null)
                {
                    continue;
                }

                m_States[state.Id] = state;
            }
        }

        /// <summary>当前状态标识。尚未启动时返回巡逻。</summary>
        public AiStateId CurrentId
        {
            get { return m_Current != null ? m_Current.Id : AiStateId.Patrol; }
        }

        /// <summary>是否已经启动。</summary>
        public bool HasStarted
        {
            get { return m_Current != null; }
        }

        /// <summary>在当前状态中已经停留的时长（秒）。</summary>
        public float TimeInState { get; private set; }

        /// <summary>累计迁移次数。用于测试断言"确实发生了迁移"。</summary>
        public int TransitionCount { get; private set; }

        /// <summary>最近一次迁移的理由，便于把调试信息直接打印出来。</summary>
        public string LastTransitionReason { get; private set; }

        /// <summary>已注册的状态数量。</summary>
        public int StateCount
        {
            get { return m_States.Count; }
        }

        /// <summary>
        /// 启动状态机，进入初始状态。
        /// </summary>
        /// <param name="initial">初始状态。</param>
        /// <remarks>重复调用会重新进入初始状态并把停留时间清零，仅用于重置。</remarks>
        public void Start(AiStateId initial)
        {
            m_Current = null;
            TimeInState = 0f;
            Change(initial, "初始化");

            // 初始进入不计入迁移次数：这个计数要回答的是"运行期间换了多少次状态"，
            // 把开幕也算进去会让所有基于该计数的断言都偏移一次。
            TransitionCount = 0;
            LastTransitionReason = null;
        }

        /// <summary>
        /// 外部请求迁移。
        /// </summary>
        /// <param name="target">目标状态。</param>
        /// <param name="reason">理由。</param>
        /// <returns>确实发生了迁移返回 true。</returns>
        /// <remarks>
        /// 供状态机之外的事件使用，例如"挨了一枪"这种与当前行为无关的强制打断。
        /// 它和状态内部的迁移走同一条路径，因此日志与回调不会漏。
        /// </remarks>
        public bool TryChange(AiStateId target, string reason)
        {
            if (m_Current != null && m_Current.Id == target)
            {
                return false;
            }

            return Change(target, reason);
        }

        /// <summary>
        /// 推进一帧。
        /// </summary>
        /// <param name="deltaTime">时间步长（秒）。非正值会被忽略。</param>
        public void Tick(float deltaTime)
        {
            if (m_Current == null || deltaTime <= 0f)
            {
                return;
            }

            TimeInState += deltaTime;

            var transition = m_Current.Tick(m_Context, deltaTime);
            if (!transition.HasTarget)
            {
                return;
            }

            if (transition.Target.Value == m_Current.Id)
            {
                // 目标就是当前状态：忽略，理由见类型注释。
                return;
            }

            Change(transition.Target.Value, transition.Reason);
        }

        /// <summary>执行一次迁移：先退出旧状态，再进入新状态，最后广播。</summary>
        private bool Change(AiStateId target, string reason)
        {
            if (!m_States.TryGetValue(target, out var next))
            {
                // 未注册的状态属于装配错误。这里不抛异常：
                // 灰盒阶段宁可让 AI 卡在旧状态并留下一条明确日志，也不要让整局崩掉。
                LastTransitionReason = $"未注册的状态：{target}（保持原状态）";
                return false;
            }

            m_Current?.Exit(m_Context);

            m_Current = next;
            TimeInState = 0f;
            TransitionCount++;
            LastTransitionReason = reason;

            m_Current.Enter(m_Context);
            m_OnStateChanged?.Invoke(m_Current.Id, reason);
            return true;
        }
    }
}
