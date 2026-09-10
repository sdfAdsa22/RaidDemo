using System;
using NUnit.Framework;
using RaidDemo.Shared;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>命令路由的行为测试。</summary>
    /// <remarks>
    /// 命令路由是单机与联机共用逻辑的入口，因此这里覆盖的重点是
    /// 分派正确性、失败路径的返回值，以及异常不会击穿到调用方。
    /// </remarks>
    [TestFixture]
    public sealed class CommandRouterTests
    {
        /// <summary>测试用命令：把值加到累加器上。</summary>
        private readonly struct AddCommand : IGameCommand
        {
            public const string TypeId = "test.add";

            public AddCommand(int value, int playerId = 1)
            {
                Value = value;
                PlayerId = playerId;
            }

            public string CommandType => TypeId;

            public int PlayerId { get; }

            public uint Sequence => 0u;

            public double Timestamp => 0d;

            public int Value { get; }
        }

        /// <summary>测试用命令：总是失败。</summary>
        private readonly struct FailingCommand : IGameCommand
        {
            public const string TypeId = "test.fail";

            public string CommandType => TypeId;

            public int PlayerId => 1;

            public uint Sequence => 0u;

            public double Timestamp => 0d;
        }

        /// <summary>测试用命令：执行时抛异常。</summary>
        private readonly struct ThrowingCommand : IGameCommand
        {
            public const string TypeId = "test.throw";

            public string CommandType => TypeId;

            public int PlayerId => 1;

            public uint Sequence => 0u;

            public double Timestamp => 0d;
        }

        private sealed class AddCommandHandler : ICommandHandler<AddCommand>
        {
            public int Total { get; private set; }

            public CommandResult Execute(in AddCommand command)
            {
                Total += command.Value;
                return CommandResult.Ok();
            }
        }

        private sealed class FailingCommandHandler : ICommandHandler<FailingCommand>
        {
            public CommandResult Execute(in FailingCommand command)
            {
                return CommandResult.Fail(CommandCodes.Rejected, "当前状态不允许该操作。");
            }
        }

        private sealed class ThrowingCommandHandler : ICommandHandler<ThrowingCommand>
        {
            public CommandResult Execute(in ThrowingCommand command)
            {
                throw new InvalidOperationException("模拟处理器内部错误");
            }
        }

        private CommandRouter m_Router;

        [SetUp]
        public void SetUp()
        {
            m_Router = new CommandRouter();
        }

        [Test]
        public void Dispatch_RoutesCommandToHandler()
        {
            var handler = new AddCommandHandler();
            m_Router.Register(handler);

            var result = m_Router.Dispatch(new AddCommand(5));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(CommandCodes.Success, result.Code);
            Assert.AreEqual(5, handler.Total);
        }

        [Test]
        public void Dispatch_MultipleCommands_AccumulateInOrder()
        {
            var handler = new AddCommandHandler();
            m_Router.Register(handler);

            m_Router.Dispatch(new AddCommand(1));
            m_Router.Dispatch(new AddCommand(2));
            m_Router.Dispatch(new AddCommand(3));

            Assert.AreEqual(6, handler.Total, "命令应按提交顺序依次执行。");
            Assert.AreEqual(3, m_Router.DispatchedCount);
        }

        [Test]
        public void Dispatch_WithoutHandler_ReturnsFailureInsteadOfThrowing()
        {
            var result = m_Router.Dispatch(new AddCommand(1));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(CommandCodes.HandlerNotFound, result.Code);
            Assert.AreEqual(1, m_Router.RejectedCount);
        }

        [Test]
        public void Dispatch_HandlerReturnsFailure_PropagatesReason()
        {
            m_Router.Register(new FailingCommandHandler());

            var result = m_Router.Dispatch(new FailingCommand());

            Assert.IsFalse(result.Success);
            Assert.AreEqual(CommandCodes.Rejected, result.Code);
        }

        /// <summary>
        /// 处理器内部抛出异常时，路由应把它转换为失败结果而不是让异常扩散。
        /// </summary>
        /// <remarks>
        /// 联机环境下未处理的异常会导致服务端断开与该客户端的连接甚至影响整局；
        /// 转换为失败结果后，服务端可以拒绝该命令并继续处理其他玩家。
        /// </remarks>
        [Test]
        public void Dispatch_HandlerThrows_ReturnsInternalErrorResult()
        {
            m_Router.Register(new ThrowingCommandHandler());

            var result = m_Router.Dispatch(new ThrowingCommand());

            Assert.IsFalse(result.Success);
            Assert.AreEqual(CommandCodes.InternalError, result.Code);
            StringAssert.Contains("模拟处理器内部错误", result.Message, "失败信息应保留原始异常内容，便于定位。");
        }

        [Test]
        public void Register_DuplicateWithoutOverwrite_Throws()
        {
            m_Router.Register(new AddCommandHandler());

            var exception = Assert.Throws<InvalidOperationException>(() => m_Router.Register(new AddCommandHandler()));
            StringAssert.Contains(AddCommand.TypeId, exception.Message, "错误信息应指出是哪个命令重复注册。");
        }

        [Test]
        public void Register_WithOverwrite_ReplacesHandler()
        {
            var first = new AddCommandHandler();
            var second = new AddCommandHandler();

            m_Router.Register(first);
            m_Router.Register(second, overwrite: true);
            m_Router.Dispatch(new AddCommand(3));

            Assert.AreEqual(0, first.Total, "被替换的处理器不应再收到命令。");
            Assert.AreEqual(3, second.Total);
        }

        [Test]
        public void Register_NullHandler_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => m_Router.Register<AddCommand>(null));
        }

        [Test]
        public void IsRegistered_ReflectsRegistrationState()
        {
            Assert.IsFalse(m_Router.IsRegistered(AddCommand.TypeId));

            m_Router.Register(new AddCommandHandler());

            Assert.IsTrue(m_Router.IsRegistered(AddCommand.TypeId));
        }

        [Test]
        public void GetHistory_RecordsDispatchedCommands()
        {
            m_Router.Register(new AddCommandHandler());
            m_Router.Dispatch(new AddCommand(1));
            m_Router.Dispatch(new FailingCommand());

            var history = m_Router.GetHistory();

            Assert.AreEqual(2, history.Count);
            Assert.AreEqual(AddCommand.TypeId, history[0].CommandType);
            Assert.IsTrue(history[0].Success);
            Assert.IsFalse(history[1].Success, "失败的命令也应留下记录，便于排查被拒原因。");
        }

        [Test]
        public void GetHistory_IsBounded()
        {
            m_Router.Register(new AddCommandHandler());

            for (var i = 0; i < 500; i++)
            {
                m_Router.Dispatch(new AddCommand(i));
            }

            Assert.LessOrEqual(m_Router.GetHistory().Count, 64, "命令历史必须有上限，避免长时间运行持续占用内存。");
        }

        [Test]
        public void Clear_ResetsHandlersAndCounters()
        {
            m_Router.Register(new AddCommandHandler());
            m_Router.Dispatch(new AddCommand(1));

            m_Router.Clear();

            Assert.AreEqual(0, m_Router.DispatchedCount);
            Assert.AreEqual(0, m_Router.RejectedCount);
            Assert.IsFalse(m_Router.IsRegistered(AddCommand.TypeId), "Clear 应同时移除已注册的处理器。");
        }
    }
}
