namespace RaidDemo.Inventory
{
    /// <summary>
    /// 背包操作失败的原因。
    /// </summary>
    /// <remarks>
    /// <para>用枚举而不是异常表达失败，原因有三：</para>
    /// <list type="bullet">
    /// <item><description>失败是背包操作的**常态**——拖到非法位置每一天都会发生几百次，
    /// 用异常表达正常流程会让调试器淹没在异常里。</description></item>
    /// <item><description>失败原因要回传给 UI 用于高亮提示，也需要在联机时回传给客户端用于回滚预测，
    /// 它是一份数据，而不是一个错误。</description></item>
    /// <item><description>拖拽预览需要**预判**操作是否合法（拖动过程中就要变红变绿），
    /// 预判不能有副作用，用返回值天然满足这一点。</description></item>
    /// </list>
    /// </remarks>
    public enum InventoryFailure
    {
        /// <summary>没有失败。</summary>
        None = 0,

        /// <summary>目标区域超出容器边界。</summary>
        OutOfBounds,

        /// <summary>目标格已被其它物品占用。</summary>
        Occupied,

        /// <summary>目标容器整体没有可用空间。与 <see cref="Occupied"/> 的区别是：
        /// 前者指具体格子被占，本项指"哪里都放不下"。</summary>
        Full,

        /// <summary>堆叠数量已达上限。</summary>
        StackLimit,

        /// <summary>数量非法（小于等于 0，或超过当前持有量）。</summary>
        InvalidQuantity,

        /// <summary>物品不允许旋转，但请求了旋转放置。</summary>
        RotationNotAllowed,

        /// <summary>容器嵌套深度超过 <see cref="Data.ContainerRules.MaxNestingDepth"/>。</summary>
        NestingTooDeep,

        /// <summary>装备槽不接受该分类的物品。</summary>
        SlotTypeMismatch,

        /// <summary>容器不接受该分类的物品（例如弹药挂只收弹药）。</summary>
        CategoryNotAllowed,

        /// <summary>找不到指定物品、容器或格子。</summary>
        NotFound,
    }

    /// <summary>
    /// 背包操作的返回值。
    /// </summary>
    /// <remarks>
    /// 这是一个只读结构体而不是类：背包操作每帧可能发生多次（拖拽预览、快速转移连点），
    /// 用结构体可以完全避免堆分配，也不会给 GC 制造压力。
    /// </remarks>
    public readonly struct InventoryResult
    {
        private InventoryResult(bool success, InventoryFailure failure, string message, int movedCount)
        {
            Success = success;
            Failure = failure;
            Message = message;
            MovedCount = movedCount;
        }

        /// <summary>操作是否成功。</summary>
        public bool Success { get; }

        /// <summary>失败原因。成功时为 <see cref="InventoryFailure.None"/>。</summary>
        public InventoryFailure Failure { get; }

        /// <summary>便于开发者阅读的说明，仅用于日志与调试，不要用作文本判断依据。</summary>
        public string Message { get; }

        /// <summary>
        /// 实际被搬运或合并的数量。
        /// </summary>
        /// <remarks>
        /// 只有部分成功的操作才会让这个值与请求量不同——例如把 200 发子弹拖进一个
        /// 只能再容纳 40 发的容器时，操作成功但 <see cref="MovedCount"/> 是 40。
        /// 调用方据此决定源容器里是否还剩下东西。
        /// </remarks>
        public int MovedCount { get; }

        /// <summary>构造成功结果。</summary>
        /// <param name="movedCount">实际搬运或合并的数量，纯放置类操作为 0。</param>
        public static InventoryResult Ok(int movedCount = 0)
        {
            return new InventoryResult(true, InventoryFailure.None, null, movedCount);
        }

        /// <summary>构造失败结果。</summary>
        /// <param name="failure">失败原因。</param>
        /// <param name="message">说明文字，用于日志与调试。</param>
        public static InventoryResult Fail(InventoryFailure failure, string message = null)
        {
            return new InventoryResult(false, failure, message, 0);
        }

        public override string ToString()
        {
            return Success
                ? $"InventoryResult(成功, moved={MovedCount})"
                : $"InventoryResult(失败, {Failure}{(string.IsNullOrEmpty(Message) ? string.Empty : ", " + Message)})";
        }
    }
}
