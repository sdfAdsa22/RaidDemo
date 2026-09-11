using RaidDemo.Kernel;
using RaidDemo.Shared;

namespace RaidDemo.Inventory
{
    /// <summary>
    /// 背包命令处理器共享的运行时上下文。
    /// </summary>
    /// <remarks>
    /// <para>把"容器注册表、角色携带物、事件总线"三件东西打包传进来，
    /// 而不是让每个处理器各接三个构造参数。这样新增一种背包命令时，
    /// 处理器只需要认识这一个对象。</para>
    ///
    /// <para>它由启动层（Bootstrap）创建并注入，背包层自己不去查找全局服务——
    /// 这也是后续联机时能在服务端复用同一套处理器的前提。</para>
    /// </remarks>
    public sealed class InventoryContext
    {
        /// <summary>容器注册表。</summary>
        private readonly ContainerRegistry m_Registry;

        /// <summary>角色携带物（主背包加装备槽）。</summary>
        private readonly PlayerLoadout m_Loadout;

        /// <summary>事件总线，用于广播变更。</summary>
        private readonly EventBus m_EventBus;

        /// <summary>创建上下文。</summary>
        /// <param name="registry">容器注册表。</param>
        /// <param name="loadout">角色携带物。</param>
        /// <param name="eventBus">事件总线。</param>
        public InventoryContext(ContainerRegistry registry, PlayerLoadout loadout, EventBus eventBus)
        {
            m_Registry = registry;
            m_Loadout = loadout;
            m_EventBus = eventBus;
        }

        /// <summary>容器注册表。</summary>
        public ContainerRegistry Registry
        {
            get { return m_Registry; }
        }

        /// <summary>角色携带物。</summary>
        public PlayerLoadout Loadout
        {
            get { return m_Loadout; }
        }

        /// <summary>事件总线。</summary>
        public EventBus EventBus
        {
            get { return m_EventBus; }
        }

        /// <summary>按 ID 取容器网格。</summary>
        /// <param name="containerId">容器 ID。</param>
        /// <param name="grid">找到的网格。</param>
        /// <returns>存在返回 true。</returns>
        public bool TryResolve(int containerId, out InventoryGrid grid)
        {
            return m_Registry.TryGetGrid(containerId, out grid);
        }

        /// <summary>
        /// 广播容器变更事件。
        /// </summary>
        /// <param name="containerId">发生变更的容器 ID。</param>
        /// <param name="changeType">变更类型，取值见 <see cref="InventoryChangeTypes"/>。</param>
        /// <param name="playerId">发起变更的玩家。</param>
        /// <param name="sequence">来源命令序号。</param>
        public void PublishChanged(int containerId, string changeType, int playerId, uint sequence = 0u)
        {
            m_EventBus.Publish(new InventoryChangedEvent(containerId, changeType, playerId, 0d, sequence));
        }

        /// <summary>
        /// 把规则层的失败原因翻译成命令结果码。
        /// </summary>
        /// <param name="failure">规则层返回的失败原因。</param>
        /// <returns>命令结果码常量。</returns>
        /// <remarks>
        /// 翻译集中在一处，避免七个处理器各写一份 switch，
        /// 也保证同一个失败原因在不同命令里对客户端给出相同的代码。
        /// </remarks>
        public static string ToCode(InventoryFailure failure)
        {
            switch (failure)
            {
                case InventoryFailure.NotFound:
                    return CommandCodes.InventoryNotFound;
                case InventoryFailure.Full:
                    return CommandCodes.InventoryFull;
                case InventoryFailure.Occupied:
                    return CommandCodes.InventoryOccupied;
                case InventoryFailure.OutOfBounds:
                    return CommandCodes.InventoryOutOfBounds;
                case InventoryFailure.StackLimit:
                    return CommandCodes.InventoryStackLimit;
                case InventoryFailure.InvalidQuantity:
                    return CommandCodes.InventoryInvalidQuantity;
                case InventoryFailure.SlotTypeMismatch:
                    return CommandCodes.InventorySlotMismatch;
                case InventoryFailure.NestingTooDeep:
                    return CommandCodes.InventoryNestingTooDeep;
                case InventoryFailure.RotationNotAllowed:
                    return CommandCodes.InventoryRotationNotAllowed;
                case InventoryFailure.CategoryNotAllowed:
                    return CommandCodes.InventoryCategoryNotAllowed;
                default:
                    return CommandCodes.Rejected;
            }
        }

        /// <summary>把规则层结果转换成命令结果。</summary>
        /// <param name="result">规则层结果。</param>
        /// <returns>命令结果。成功时同样成功。</returns>
        public static CommandResult ToCommandResult(InventoryResult result)
        {
            return result.Success
                ? CommandResult.Ok()
                : CommandResult.Fail(ToCode(result.Failure), result.Message);
        }
    }
}
