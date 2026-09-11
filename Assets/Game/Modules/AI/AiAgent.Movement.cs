using System.Collections.Generic;
using RaidDemo.Shared;

namespace RaidDemo.AI
{
    /// <summary>
    /// AiAgent 的移动执行部分：把状态写下的意图变成位置与朝向。
    /// </summary>
    /// <remarks>
    /// <para>按职责拆成 partial 文件的直接原因是文件长度上限（400 行），
    /// 但拆分的边界本身也是合理的：主文件管"感知与决策"，这里管"怎么动"。
    /// 两者改动的理由完全不同——调感知参数不会碰这里的代码。</para>
    /// </remarks>
    public sealed partial class AiAgent
    {
        /// <summary>丢弃当前移动路径。状态切换时由状态调用。</summary>
        public void ResetPath()
        {
            m_Movement.ResetPath();
        }

        /// <summary>
        /// 复制当前寻路路径点，供调试可视化绘制。
        /// </summary>
        /// <param name="destination">目标列表，会先被清空。</param>
        /// <returns>路径点数量。</returns>
        /// <remarks>
        /// 逻辑层不使用这个方法。它的存在只为让开发者模式能画出"AI 打算怎么绕过去"，
        /// 因此刻意保持只读（复制而非暴露内部集合，理由见 <see cref="AiMovement.CopyWaypoints"/>）。
        /// </remarks>
        public int CopyPathWaypoints(List<Vector2F> destination)
        {
            return m_Movement.CopyWaypoints(destination);
        }

        /// <summary>执行本帧意图：移动与转向。</summary>
        private void ApplyIntent(float deltaTime)
        {
            var profile = m_Director.Profile;
            var moved = false;
            var moveDirection = Vector2F.Zero;

            if (m_Intent.HasMoveGoal)
            {
                var result = m_Movement.Step(m_Position, m_Intent.MoveGoal, ResolveSpeed(), deltaTime);
                m_Position = result.Position;
                moved = result.Moved;
                moveDirection = result.Direction;
            }

            var desiredFacing = m_Intent.Facing;
            if (desiredFacing.IsNearlyZero && moved)
            {
                // 状态没有指定朝向时面向移动方向，这是最不需要解释的默认行为。
                desiredFacing = moveDirection;
            }

            if (desiredFacing.IsNearlyZero)
            {
                return;
            }

            m_FacingDegrees = AiAngles.StepTowards(
                m_FacingDegrees,
                AiAngles.ToDegrees(desiredFacing),
                profile.TurnSpeedDegreesPerSecond * deltaTime);
        }

        /// <summary>按当前状态取移动速度。</summary>
        private float ResolveSpeed()
        {
            var profile = m_Director.Profile;
            switch (m_Machine.CurrentId)
            {
                case AiStateId.Investigate:
                    return profile.InvestigateSpeed;
                case AiStateId.Alert:
                    return profile.AlertSpeed;
                case AiStateId.Engage:
                    return profile.EngageSpeed;
                case AiStateId.Retreat:
                    return profile.RetreatSpeed;
                default:
                    return profile.PatrolSpeed;
            }
        }
    }
}
