using RaidDemo.Kernel;
using RaidDemo.Shared;

namespace RaidDemo.Simulation
{
    /// <summary>
    /// 处理玩家移动意图：推进移动模拟，并把新状态广播给表现层。
    /// </summary>
    /// <remarks>
    /// <para>这是「输入 - 命令 - 路由 - 模拟 - 事件 - 表现」完整链路中的中间环节。
    /// 它把 <see cref="PlayerMoveIntent"/> 转换为一次模拟推进，再通过
    /// <see cref="PlayerMovementChanged"/> 把结果交给表现层。</para>
    ///
    /// <para>为什么奔跑状态不在命令里直接生效，而要由模拟层判定：
    /// 命令只描述玩家意图（朝哪走、要不要跑），而是否真的跑得起来取决于体力是否耗尽。
    /// 后者属于模拟状态而非意图，若由客户端决定，客户端就能绕过体力限制。</para>
    /// </remarks>
    public sealed class PlayerMoveCommandHandler : ICommandHandler<PlayerMoveIntent>
    {
        private readonly PlayerMovementSimulator m_Simulator;
        private readonly EventBus m_EventBus;

        /// <summary>最近的移动意图。移动命令每帧重新提交，保存在此供无命令的帧继续使用。</summary>
        private Vector2F m_LastMoveDirection;

        private Vector2F m_LastLookDirection;
        private bool m_LastWantsToSprint;
        private int m_LastPlayerId;

        /// <summary>创建处理器。</summary>
        /// <param name="simulator">移动模拟器。</param>
        /// <param name="eventBus">事件总线。</param>
        public PlayerMoveCommandHandler(PlayerMovementSimulator simulator, EventBus eventBus)
        {
            m_Simulator = simulator;
            m_EventBus = eventBus;
        }

        /// <summary>当前模拟器实例，供测试与调试读取状态。</summary>
        public PlayerMovementSimulator Simulator => m_Simulator;

        /// <summary>
        /// 接收移动意图。本方法只记录意图，不推进模拟。
        /// </summary>
        /// <remarks>
        /// 命令不携带 deltaTime，因为时间是模拟的输入而非命令的内容。
        /// 联机时服务端以固定步长推进，客户端预测使用自己的步长；
        /// 若把时间写进命令，客户端就能通过伪造时间加速移动。
        /// </remarks>
        public CommandResult Execute(in PlayerMoveIntent command)
        {
            m_LastMoveDirection = command.MoveDirection;
            m_LastLookDirection = command.LookDirection;
            m_LastWantsToSprint = command.WantsToSprint;
            m_LastPlayerId = command.PlayerId;

            return CommandResult.Ok();
        }

        /// <summary>
        /// 按当前意图推进一帧模拟并广播结果。
        /// </summary>
        /// <param name="deltaTime">时间步长（秒）。</param>
        /// <remarks>
        /// 由场景层（单机）或服务端循环（联机）每帧调用。
        /// 命令负责更新意图，本方法负责按意图持续推进，
        /// 因此即使某帧没有收到新命令，角色也不会突然停下。
        /// </remarks>
        public void Tick(float deltaTime)
        {
            m_Simulator.Step(m_LastMoveDirection, m_LastLookDirection, deltaTime, m_LastWantsToSprint);
            PublishState();
        }

        /// <summary>清空输入意图。用于暂停、打开界面或玩家死亡时立刻停下角色。</summary>
        public void ClearIntent()
        {
            m_LastMoveDirection = Vector2F.Zero;
            m_LastWantsToSprint = false;
        }

        private void PublishState()
        {
            var state = m_Simulator.State;
            m_EventBus.Publish(new PlayerMovementChanged(
                m_LastPlayerId,
                state.Position,
                state.Facing,
                state.CurrentSpeed,
                state.Stamina,
                state.IsSprinting,
                state.IsExhausted));
        }
    }
}
