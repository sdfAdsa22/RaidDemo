using RaidDemo.Shared;

namespace RaidDemo.Meta
{
    /// <summary>处理接取任务意图。</summary>
    public sealed class QuestAcceptCommandHandler : ICommandHandler<QuestAcceptIntent>
    {
        private readonly QuestSystem m_Quests;

        /// <summary>创建处理器。</summary>
        public QuestAcceptCommandHandler(QuestSystem quests)
        {
            m_Quests = quests;
        }

        /// <inheritdoc />
        public CommandResult Execute(in QuestAcceptIntent command)
        {
            if (m_Quests == null)
            {
                return CommandResult.Fail(CommandCodes.InternalError, "任务系统尚未完成装配。");
            }

            if (m_Quests.Get(command.QuestId) == null)
            {
                return CommandResult.Fail(CommandCodes.MetaQuestNotFound, "找不到这个任务。");
            }

            return m_Quests.TryAccept(command.QuestId, out var problem)
                ? CommandResult.Ok()
                : CommandResult.Fail(CommandCodes.MetaQuestStateInvalid, problem);
        }
    }

    /// <summary>处理追踪任务意图。</summary>
    public sealed class QuestTrackCommandHandler : ICommandHandler<QuestTrackIntent>
    {
        private readonly QuestSystem m_Quests;

        /// <summary>创建处理器。</summary>
        public QuestTrackCommandHandler(QuestSystem quests)
        {
            m_Quests = quests;
        }

        /// <inheritdoc />
        public CommandResult Execute(in QuestTrackIntent command)
        {
            if (m_Quests == null)
            {
                return CommandResult.Fail(CommandCodes.InternalError, "任务系统尚未完成装配。");
            }

            if (m_Quests.Get(command.QuestId) == null)
            {
                return CommandResult.Fail(CommandCodes.MetaQuestNotFound, "找不到这个任务。");
            }

            return m_Quests.TryTrack(command.QuestId, out var problem)
                ? CommandResult.Ok()
                : CommandResult.Fail(CommandCodes.MetaQuestStateInvalid, problem);
        }
    }

    /// <summary>处理上交任务物品意图。</summary>
    public sealed class QuestTurnInCommandHandler : ICommandHandler<QuestTurnInIntent>
    {
        private readonly QuestSystem m_Quests;

        /// <summary>创建处理器。</summary>
        public QuestTurnInCommandHandler(QuestSystem quests)
        {
            m_Quests = quests;
        }

        /// <inheritdoc />
        public CommandResult Execute(in QuestTurnInIntent command)
        {
            if (m_Quests == null)
            {
                return CommandResult.Fail(CommandCodes.InternalError, "任务系统尚未完成装配。");
            }

            var quest = m_Quests.Get(command.QuestId);
            if (quest == null)
            {
                return CommandResult.Fail(CommandCodes.MetaQuestNotFound, "找不到这个任务。");
            }

            if (quest.State != QuestState.Active)
            {
                return CommandResult.Fail(CommandCodes.MetaQuestStateInvalid, "任务当前不能上交物品。");
            }

            var required = quest.Definition.TargetCount;
            if (m_Quests.CountInStash(quest.Definition.TargetItemId) < required)
            {
                return CommandResult.Fail(
                    CommandCodes.MetaQuestItemsMissing,
                    "仓库里的任务物品还不够。");
            }

            return m_Quests.TryTurnIn(command.QuestId, out var problem)
                ? CommandResult.Ok()
                : CommandResult.Fail(CommandCodes.MetaQuestStateInvalid, problem);
        }
    }

    /// <summary>处理领取任务奖励意图。</summary>
    public sealed class QuestClaimCommandHandler : ICommandHandler<QuestClaimIntent>
    {
        private readonly QuestSystem m_Quests;

        /// <summary>创建处理器。</summary>
        public QuestClaimCommandHandler(QuestSystem quests)
        {
            m_Quests = quests;
        }

        /// <inheritdoc />
        public CommandResult Execute(in QuestClaimIntent command)
        {
            if (m_Quests == null)
            {
                return CommandResult.Fail(CommandCodes.InternalError, "任务系统尚未完成装配。");
            }

            var quest = m_Quests.Get(command.QuestId);
            if (quest == null)
            {
                return CommandResult.Fail(CommandCodes.MetaQuestNotFound, "找不到这个任务。");
            }

            if (quest.State != QuestState.Completed)
            {
                return CommandResult.Fail(
                    CommandCodes.MetaQuestStateInvalid,
                    "任务还没有完成，或者奖励已经领过了。");
            }

            return m_Quests.TryClaim(command.QuestId, out var problem)
                ? CommandResult.Ok()
                : CommandResult.Fail(CommandCodes.MetaQuestRewardBlocked, problem);
        }
    }
}
