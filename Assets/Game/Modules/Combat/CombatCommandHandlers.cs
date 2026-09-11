using RaidDemo.Shared;

namespace RaidDemo.Combat
{
    /// <summary>
    /// 战斗命令处理器：射击与换弹。
    /// </summary>
    /// <remarks>
    /// <para>两个处理器都刻意保持极薄：它们只把意图写进 <see cref="PlayerWeaponController"/>，
    /// 真正的每帧循环在控制器里，由启动层统一驱动。</para>
    /// <para>这样做是为了保证"武器的时间每帧只推进一次"。如果两个处理器各自推进一次，
    /// 射速与换弹计时会以两倍速度前进——这类 bug 在实机上表现为"射速比配置快一倍"，
    /// 极难定位。</para>
    /// </remarks>
    public sealed class FireCommandHandler : ICommandHandler<PlayerFireIntent>
    {
        private readonly PlayerWeaponController m_Controller;

        /// <summary>创建处理器。</summary>
        /// <param name="controller">武器控制器。</param>
        public FireCommandHandler(PlayerWeaponController controller)
        {
            m_Controller = controller;
        }

        /// <inheritdoc />
        public CommandResult Execute(in PlayerFireIntent command)
        {
            if (!m_Controller.Weapon.IsEquipped)
            {
                // 没有武器时立刻拒绝，而不是默默地什么都不做：
                // 玩家按住扳机却毫无反馈时，会以为游戏卡住了。
                return CommandResult.Fail(CommandCodes.CombatNoWeapon, "主武器槽是空的。");
            }

            m_Controller.SetAimDirection(command.AimDirection);
            m_Controller.SetTriggerHeld(true);
            return CommandResult.Ok();
        }
    }

    /// <summary>
    /// 处理换弹意图。
    /// </summary>
    /// <remarks>
    /// 这是少数会**同步失败**的命令之一：弹匣满、没有匹配弹药、正在换弹，
    /// 这三种情况都不需要等待，立刻就能给出结果，因此直接在处理器里返回失败码，
    /// 而不是等到下一帧再通过事件通知。
    /// </remarks>
    public sealed class ReloadCommandHandler : ICommandHandler<PlayerReloadIntent>
    {
        private readonly PlayerWeaponController m_Controller;

        /// <summary>创建处理器。</summary>
        /// <param name="controller">武器控制器。</param>
        public ReloadCommandHandler(PlayerWeaponController controller)
        {
            m_Controller = controller;
        }

        /// <inheritdoc />
        public CommandResult Execute(in PlayerReloadIntent command)
        {
            if (m_Controller.TryRequestReload(command.PlayerId, command.Sequence, out var failureCode))
            {
                return CommandResult.Ok();
            }

            return CommandResult.Fail(failureCode, "换弹请求被拒绝。");
        }
    }
}
