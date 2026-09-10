using System;

namespace RaidDemo.Data
{
    /// <summary>
    /// 物品的运行实例：一件（或一堆）具体的物品。
    /// </summary>
    /// <remarks>
    /// <para><see cref="ItemInstance"/> 同时承担两个角色：它既是"数量为 N 的一堆"，
    /// 也是"数量恒为 1 的装备"——装备槽里的武器同样是 <see cref="ItemInstance"/>。
    /// 这样地面掉落、背包格子、装备槽、商人库存、战利品箱全部共用同一个数据类型，
    /// 序列化只需要写一次。</para>
    ///
    /// <para>放置在容器里的物品，其 <see cref="Rotated"/> 由容器在放置时写入，
    /// 因此同一件物品被旋转后放进不同容器不会被"记住"，这符合直觉。</para>
    /// </remarks>
    public sealed class ItemInstance
    {
        /// <summary>静态定义。创建后不再改变——物品不会变成另一种物品。</summary>
        private readonly IItemDefinition m_Definition;

        /// <summary>当前堆叠数量。</summary>
        private int m_StackCount;

        /// <summary>
        /// 创建实例。仅供 <see cref="ItemFactory"/> 与本程序集内的拆分逻辑调用。
        /// </summary>
        /// <param name="definition">静态定义，不允许为 null。</param>
        /// <param name="instanceId">会话内唯一的实例号。</param>
        /// <param name="stackCount">初始数量，必须在 1 到 <c>MaxStack</c> 之间。</param>
        internal ItemInstance(IItemDefinition definition, int instanceId, int stackCount)
        {
            m_Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            InstanceId = instanceId;
            m_StackCount = stackCount;
        }

        /// <summary>静态定义。</summary>
        public IItemDefinition Definition
        {
            get { return m_Definition; }
        }

        /// <summary>
        /// 会话内唯一的实例号。
        /// </summary>
        /// <remarks>
        /// 用途仅限于"区分个体"与"事件载荷里指代某一件物品"。
        /// 本项目不使用全局实例注册表，因此它不是任何字典的键，也就不存在悬垂引用的风险。
        /// 联机时由服务端分配，客户端不可自行决定。
        /// </remarks>
        public int InstanceId { get; }

        /// <summary>当前堆叠数量。装备类物品恒为 1。</summary>
        public int StackCount
        {
            get { return m_StackCount; }
        }

        /// <summary>
        /// 独立状态载荷。M2 阶段恒为 null，字段为 M3 的装弹数与护甲耐久预留。
        /// </summary>
        public ItemState State { get; private set; }

        /// <summary>
        /// 在容器中是否已旋转 90 度。由容器在放置时写入。
        /// </summary>
        public bool Rotated { get; set; }

        /// <summary>按当前旋转状态换算出的实际占地尺寸。</summary>
        public GridSize OccupiedSize
        {
            get { return Rotated ? m_Definition.GridSize.Rotated() : m_Definition.GridSize; }
        }

        /// <summary>整堆重量（千克）= 单位重量 × 数量。</summary>
        public float WeightKg
        {
            get { return m_Definition.WeightKg * m_StackCount; }
        }

        /// <summary>整堆价值（游戏币）= 单价 × 数量。</summary>
        public int TotalValue
        {
            get { return m_Definition.BaseValue * m_StackCount; }
        }

        /// <summary>是否还能再堆入物品（未达上限）。</summary>
        public bool HasStackRoom
        {
            get { return m_StackCount < m_Definition.MaxStack; }
        }

        /// <summary>剩余可堆叠空间。</summary>
        public int RemainingStackRoom
        {
            get { return m_Definition.MaxStack - m_StackCount; }
        }

        /// <summary>
        /// 判断能否与另一件物品合并为一堆。
        /// </summary>
        /// <param name="other">另一件物品，可为 null。</param>
        /// <returns>可合并返回 true。</returns>
        /// <remarks>
        /// 四个条件缺一不可：同一定义、本身可堆叠、本堆未满、**状态内容相等**。
        /// 最后一条在 M2 恒为真（所有物品的状态都是 null），
        /// 但从第一天就写上它，M3 接入装弹数与耐久时合并逻辑一行都不用改。
        /// </remarks>
        public bool CanStackWith(ItemInstance other)
        {
            if (other == null || ReferenceEquals(this, other))
            {
                return false;
            }

            if (m_Definition.MaxStack <= 1 || !HasStackRoom)
            {
                return false;
            }

            if (!string.Equals(m_Definition.Id, other.m_Definition.Id, StringComparison.Ordinal))
            {
                return false;
            }

            return StateEquals(State, other.State);
        }

        /// <summary>
        /// 尝试向本堆加入指定数量。
        /// </summary>
        /// <param name="count">希望加入的数量，必须大于 0。</param>
        /// <returns>实际加入的数量。达到堆叠上限时小于请求量，为 0 表示一点也放不下。</returns>
        /// <remarks>
        /// 返回值而不是抛异常或静默截断：调用方（例如"把 200 发子弹拖到一个只剩 40 发空间的格子"）
        /// 需要知道还剩多少没放进去，才能决定是继续找空位还是留在原地。
        /// </remarks>
        public int AddToStack(int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            var added = Math.Min(count, RemainingStackRoom);
            m_StackCount += added;
            return added;
        }

        /// <summary>
        /// 从本堆拆出一部分，返回一个**尚未放入任何容器**的新实例。
        /// </summary>
        /// <param name="count">拆出的数量，必须大于 0 且严格小于当前数量。</param>
        /// <returns>新实例，其放置由调用方负责。</returns>
        /// <remarks>
        /// <para>数量校验用抛异常而不是返回 null：调用方传了非法数量属于程序缺陷，
        /// 而不是游戏中的正常失败。正常失败（目标格放不下）由规则层的返回值表达。</para>
        ///
        /// <para>新实例的 <see cref="Rotated"/> 不继承原实例——拆分出来的东西是独立的一堆，
        /// 摆在哪里、转不转由它自己的放置过程决定。</para>
        /// </remarks>
        public ItemInstance Split(int count)
        {
            if (count <= 0 || count >= m_StackCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(count),
                    count,
                    $"拆分数量必须大于 0 且小于当前数量 {m_StackCount}。");
            }

            m_StackCount -= count;
            return new ItemInstance(m_Definition, ItemInstanceIdAllocator.Next(), count);
        }

        /// <summary>
        /// 写入独立状态载荷。M2 没有调用方，供 M3 接入装弹数与耐久时使用。
        /// </summary>
        /// <param name="state">新的状态，可为 null。</param>
        public void SetState(ItemState state)
        {
            State = state;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            var suffix = m_StackCount > 1 ? $" x{m_StackCount}" : string.Empty;
            return $"{m_Definition.DisplayName}{suffix}";
        }

        /// <summary>
        /// 状态相等判定：都为 null 视为相等。
        /// </summary>
        /// <remarks>
        /// M2 阶段两边永远是 null，因此恒为 true。这个分支不是"暂时用不上的代码"，
        /// 而是堆叠语义的一部分——将来只要有一边带状态，就必须走内容比较。
        /// </remarks>
        private static bool StateEquals(ItemState left, ItemState right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            return left.ContentEquals(right);
        }
    }
}
