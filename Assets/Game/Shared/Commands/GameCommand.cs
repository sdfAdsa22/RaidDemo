using System;
using System.Collections.Generic;

namespace RaidDemo.Shared
{
    /// <summary>
    /// 游戏命令：玩家意图的统一表达形式。
    /// </summary>
    /// <remarks>
    /// <para><b>本项目最重要的架构约束之一。</b>所有会改变游戏状态的玩家行为——移动、开火、拾取、
    /// 换弹、丢弃——都必须先表达为一个实现本接口的命令对象，再经由唯一的执行入口处理。</para>
    ///
    /// <para>这样做换来的是单机与联机共用同一套逻辑：</para>
    /// <list type="bullet">
    /// <item><description>单机模式：输入 → CommandRouter → 本地立即执行。</description></item>
    /// <item><description>联机模式：输入 → CommandRouter → 发送到服务端 → 服务端执行 → 状态回传。</description></item>
    /// </list>
    ///
    /// <para>若绕过这一层直接在输入回调里改 transform 或血量，那么到联机阶段这部分代码必须重写；
    /// 而本项目要求 M9 只是<b>接入</b>网络层，而不是重写逻辑层。这是该约束存在的全部理由。</para>
    /// </remarks>
    public interface IGameCommand
    {
        /// <summary>命令类型标识，用于注册与查找处理器。同一种命令的所有实例返回相同的值。</summary>
        string CommandType { get; }

        /// <summary>
        /// 发起该命令的玩家标识。单机模式下为本地玩家 ID，联机模式下由服务端校验归属。
        /// </summary>
        int PlayerId { get; }

        /// <summary>
        /// 命令序号，由发起方单调递增。
        /// </summary>
        /// <remarks>
        /// 序号有两个用途：其一是网络层的丢包与乱序检测（服务端只接受比已处理序号更新的命令）；
        /// 其二是客户端预测失败时的回滚重放——重放需要按原始顺序重新执行一串命令。
        /// 单机模式下可以填 0，但建议从 M1 起就正确赋值，避免 M9 时遗漏。
        /// </remarks>
        uint Sequence { get; }

        /// <summary>
        /// 客户端发起该命令时的时间戳（秒）。服务端据此做延迟补偿。
        /// 单机模式下可填 0。
        /// </summary>
        double Timestamp { get; }
    }

    /// <summary>
    /// 命令执行结果。
    /// </summary>
    /// <remarks>
    /// 命令可能失败——目标格已被占用、弹药不足、距离过远。失败必须有明确的失败原因，
    /// 因为联机环境下服务端需要把拒绝理由回传给客户端，让客户端能回滚预测并给出提示。
    /// </remarks>
    public readonly struct CommandResult
    {
        private CommandResult(bool success, string code, string message)
        {
            Success = success;
            Code = code;
            Message = message;
        }

        /// <summary>是否执行成功。</summary>
        public bool Success { get; }

        /// <summary>
        /// 机器可读的结果码。成功时为 <see cref="CommandCodes.Success"/>。
        /// 使用字符串而不是枚举，是为了让结果码可以跨程序集扩展，而不必修改共享层的枚举定义。
        /// </summary>
        public string Code { get; }

        /// <summary>便于开发者阅读的说明。仅供日志与调试使用，不要用于程序判断。</summary>
        public string Message { get; }

        /// <summary>构造一个成功结果。</summary>
        public static CommandResult Ok()
        {
            return new CommandResult(true, CommandCodes.Success, null);
        }

        /// <summary>构造一个失败结果。</summary>
        /// <param name="code">机器可读的结果码，建议取自 <see cref="CommandCodes"/>。</param>
        /// <param name="message">说明文字，用于日志与调试。</param>
        public static CommandResult Fail(string code, string message)
        {
            return new CommandResult(false, code, message);
        }
    }

    /// <summary>内置的结果码集合。业务模块可自行定义额外的结果码。</summary>
    public static class CommandCodes
    {
        /// <summary>执行成功。</summary>
        public const string Success = "success";

        /// <summary>命令格式或参数非法。</summary>
        public const string InvalidCommand = "invalid_command";

        /// <summary>找不到对应的处理器——通常意味着模块没有完成注册，属于开发期错误。</summary>
        public const string HandlerNotFound = "handler_not_found";

        /// <summary>执行过程中抛出了未预期的异常。</summary>
        public const string InternalError = "internal_error";

        /// <summary>命令被拒绝：当前状态不允许该操作（例如死亡后仍尝试开火）。</summary>
        public const string Rejected = "rejected";

        /// <summary>命令序号过期，已被处理过（联机时的重复包）。</summary>
        public const string StaleSequence = "stale_sequence";

        /// <summary>找不到指定的容器或格子。</summary>
        public const string InventoryNotFound = "inventory_not_found";

        /// <summary>目标容器没有可用空间。</summary>
        public const string InventoryFull = "inventory_full";

        /// <summary>目标格已被占用。</summary>
        public const string InventoryOccupied = "inventory_occupied";

        /// <summary>目标格越界。</summary>
        public const string InventoryOutOfBounds = "inventory_out_of_bounds";

        /// <summary>堆叠已达上限。</summary>
        public const string InventoryStackLimit = "inventory_stack_limit";

        /// <summary>数量非法（小于等于 0 或超过持有量）。</summary>
        public const string InventoryInvalidQuantity = "inventory_invalid_quantity";

        /// <summary>装备槽不接受该分类的物品。</summary>
        public const string InventorySlotMismatch = "inventory_slot_mismatch";

        /// <summary>容器嵌套深度超过上限。</summary>
        public const string InventoryNestingTooDeep = "inventory_nesting_too_deep";

        /// <summary>物品不允许旋转，但请求了旋转。</summary>
        public const string InventoryRotationNotAllowed = "inventory_rotation_not_allowed";

        /// <summary>容器不接受该分类的物品（例如弹药挂只收弹药）。</summary>
        public const string InventoryCategoryNotAllowed = "inventory_category_not_allowed";

        /// <summary>主武器槽是空的。</summary>
        public const string CombatNoWeapon = "combat_no_weapon";

        /// <summary>弹匣里没有弹药。</summary>
        public const string CombatMagazineEmpty = "combat_magazine_empty";

        /// <summary>射速限制，本次射击被忽略——这是正常的节奏控制，不是错误。</summary>
        public const string CombatFireRateLimited = "combat_fire_rate_limited";

        /// <summary>正在换弹，无法射击。</summary>
        public const string CombatReloading = "combat_reloading";

        /// <summary>弹匣已满，无需换弹。</summary>
        public const string CombatMagazineFull = "combat_magazine_full";

        /// <summary>背包里没有匹配口径的弹药。</summary>
        public const string CombatNoAmmo = "combat_no_ammo";

        /// <summary>
        /// 阵亡状态下尝试使用武器。
        /// </summary>
        /// <remarks>与"没有武器"分开：把阵亡报成"没有武器"会让排查战局问题时
        /// 往装备丢失的方向查，而真正的原因在别处。</remarks>
        public const string CombatIncapacitated = "combat_incapacitated";

        /// <summary>金币余额不足，交易被拒绝。</summary>
        public const string MetaInsufficientFunds = "meta_insufficient_funds";

        /// <summary>交易数量非法（小于等于 0，或超过一次可购买的上限）。</summary>
        public const string MetaInvalidQuantity = "meta_invalid_quantity";

        /// <summary>商人货架上没有这件商品。</summary>
        public const string MetaItemNotSold = "meta_item_not_sold";

        /// <summary>找不到指定任务。</summary>
        public const string MetaQuestNotFound = "meta_quest_not_found";

        /// <summary>任务当前状态不允许该操作。</summary>
        public const string MetaQuestStateInvalid = "meta_quest_state_invalid";

        /// <summary>上交任务所需的物品数量不足。</summary>
        public const string MetaQuestItemsMissing = "meta_quest_items_missing";

        /// <summary>任务奖励没有空间发放。</summary>
        public const string MetaQuestRewardBlocked = "meta_quest_reward_blocked";
    }

    /// <summary>
    /// 命令处理器。
    /// </summary>
    /// <typeparam name="TCommand">要处理的命令类型，必须是结构体以避免每次命令产生堆分配。</typeparam>
    /// <remarks>
    /// 处理器实现为普通 C# 类而非 MonoBehaviour，原因是命令处理属于逻辑层，
    /// 需要能够在 EditMode 单元测试中脱离场景直接构造与调用。
    /// </remarks>
    public interface ICommandHandler<TCommand>
        where TCommand : struct, IGameCommand
    {
        /// <summary>执行命令并返回结果。实现不应抛出异常，失败请通过 <see cref="CommandResult.Fail"/> 返回。</summary>
        CommandResult Execute(in TCommand command);
    }

    /// <summary>
    /// 命令路由：所有游戏命令的唯一执行入口。
    /// </summary>
    /// <remarks>
    /// <para>路由器按 <see cref="IGameCommand.CommandType"/> 把命令分派给对应的处理器。
    /// 注册发生在启动阶段（见 Bootstrap 程序集），运行期间只做查找与调用。</para>
    ///
    /// <para>本类不引用 UnityEngine，因此同一份实现既能在客户端使用，也能在服务端使用。</para>
    /// </remarks>
    public sealed class CommandRouter
    {
        /// <summary>命令类型标识到处理器的映射。</summary>
        private readonly Dictionary<string, object> m_Handlers = new Dictionary<string, object>(32, StringComparer.Ordinal);

        /// <summary>最近执行的命令记录，用于调试与测试断言。</summary>
        private readonly Queue<CommandRecord> m_History;

        private readonly int m_HistoryCapacity;
        private int m_DispatchedCount;
        private int m_RejectedCount;

        /// <summary>
        /// 创建命令路由器。
        /// </summary>
        /// <param name="historyCapacity">
        /// 保留的命令记录条数上限。默认 64 条足够覆盖一次排查所需的上下文，
        /// 同时避免长期运行时的内存增长。
        /// </param>
        public CommandRouter(int historyCapacity = 64)
        {
            m_HistoryCapacity = Math.Max(8, historyCapacity);
            m_History = new Queue<CommandRecord>(m_HistoryCapacity);
        }

        /// <summary>累计分派的命令数量（含失败的命令）。</summary>
        public int DispatchedCount => m_DispatchedCount;

        /// <summary>累计被拒绝的命令数量。</summary>
        public int RejectedCount => m_RejectedCount;

        /// <summary>
        /// 注册命令处理器。
        /// </summary>
        /// <typeparam name="TCommand">命令类型。</typeparam>
        /// <param name="handler">处理器实例。</param>
        /// <param name="overwrite">
        /// 是否允许覆盖已注册的处理器。默认 false：
        /// 同一命令出现两个处理器几乎总是模块初始化重复或命名冲突，应当立即暴露。
        /// </param>
        public void Register<TCommand>(ICommandHandler<TCommand> handler, bool overwrite = false)
            where TCommand : struct, IGameCommand
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler), "命令处理器不能为 null。");
            }

            // 用默认值实例取得类型标识：命令的结构体字段不影响 CommandType，
            // 因此无需构造完整实例即可拿到用于注册的键。
            var key = default(TCommand).CommandType;
            if (string.IsNullOrEmpty(key))
            {
                throw new InvalidOperationException(
                    $"命令类型 {typeof(TCommand).Name} 未提供 CommandType 标识，无法注册。");
            }

            if (!overwrite && m_Handlers.ContainsKey(key))
            {
                throw new InvalidOperationException(
                    $"命令 {key} 已注册处理器。若确实需要替换（例如测试注入），请显式传入 overwrite: true。");
            }

            m_Handlers[key] = handler;
        }

        /// <summary>查询指定命令类型是否已注册处理器。</summary>
        public bool IsRegistered(string commandType)
        {
            return !string.IsNullOrEmpty(commandType) && m_Handlers.ContainsKey(commandType);
        }

        /// <summary>
        /// 分派命令。
        /// </summary>
        /// <typeparam name="TCommand">命令类型。</typeparam>
        /// <param name="command">命令内容，按只读引用传递以避免结构体拷贝。</param>
        /// <returns>执行结果。找不到处理器时返回失败，而不是抛出异常。</returns>
        /// <remarks>
        /// 这里不抛出异常是刻意的：联机环境下未注册的命令往往来自版本不一致的客户端，
        /// 服务端需要拒绝并记录，而不是让整个战局崩溃。
        /// </remarks>
        public CommandResult Dispatch<TCommand>(in TCommand command)
            where TCommand : struct, IGameCommand
        {
            m_DispatchedCount++;

            var key = command.CommandType;
            if (string.IsNullOrEmpty(key))
            {
                m_RejectedCount++;
                var invalid = CommandResult.Fail(
                    CommandCodes.InvalidCommand,
                    $"命令 {typeof(TCommand).Name} 的 CommandType 为空。");
                Record(key, invalid);
                return invalid;
            }

            if (!m_Handlers.TryGetValue(key, out var handler))
            {
                m_RejectedCount++;
                var missing = CommandResult.Fail(
                    CommandCodes.HandlerNotFound,
                    $"命令 {key} 没有注册处理器。请检查 Bootstrap 阶段的注册流程。");
                Record(key, missing);
                return missing;
            }

            var typed = (ICommandHandler<TCommand>)handler;
            CommandResult result;
            try
            {
                result = typed.Execute(in command);
            }
            catch (Exception exception)
            {
                // 处理器抛出的异常在此统一转换，保证调用方只需处理 CommandResult 一种结果形式。
                m_RejectedCount++;
                result = CommandResult.Fail(
                    CommandCodes.InternalError,
                    $"命令 {key} 执行时抛出异常：{exception.GetType().Name} — {exception.Message}");
            }

            if (!result.Success)
            {
                m_RejectedCount++;
            }

            Record(key, result);
            return result;
        }

        /// <summary>读取最近的命令记录（时间顺序）。</summary>
        public IReadOnlyList<CommandRecord> GetHistory()
        {
            return new List<CommandRecord>(m_History);
        }

        /// <summary>清空注册与历史记录，供测试用例之间隔离。</summary>
        public void Clear()
        {
            m_Handlers.Clear();
            m_History.Clear();
            m_DispatchedCount = 0;
            m_RejectedCount = 0;
        }

        private void Record(string commandType, CommandResult result)
        {
            if (m_History.Count >= m_HistoryCapacity)
            {
                m_History.Dequeue();
            }

            m_History.Enqueue(new CommandRecord(commandType, result.Success, result.Code));
        }
    }

    /// <summary>一条命令的执行记录。</summary>
    public readonly struct CommandRecord
    {
        public CommandRecord(string commandType, bool success, string code)
        {
            CommandType = commandType;
            Success = success;
            Code = code;
        }

        /// <summary>命令类型标识。</summary>
        public string CommandType { get; }

        /// <summary>是否执行成功。</summary>
        public bool Success { get; }

        /// <summary>结果码。</summary>
        public string Code { get; }

        public override string ToString()
        {
            return Success ? $"{CommandType} → 成功" : $"{CommandType} → 失败({Code})";
        }
    }
}
