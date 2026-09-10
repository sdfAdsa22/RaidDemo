using RaidDemo.Data;

namespace RaidDemo.Inventory
{
    /// <summary>
    /// <see cref="InventoryGrid"/> 的放置相关实现。
    /// </summary>
    /// <remarks>
    /// 拆成 partial 文件的原因有两个：一是主文件已经接近项目规定的 400 行上限；
    /// 二是"能不能放"与"怎么搬运"是两类不同的问题，分开后每部分都能独立阅读。
    /// </remarks>
    public sealed partial class InventoryGrid
    {
        /// <summary>
        /// 一次放置的完整计划。规划阶段产出它，提交阶段消费它。
        /// </summary>
        /// <remarks>
        /// 引入"计划"这一层的唯一目的是原子性：先把要发生的每一件事算清楚，
        /// 确认全部可行之后再一次性改数据，于是失败路径上根本没有需要回滚的中间状态。
        /// </remarks>
        private readonly struct PlacementPlan
        {
            public PlacementPlan(GridPoint origin, bool rotated, ItemInstance mergeTarget, int mergeCount)
            {
                Origin = origin;
                Rotated = rotated;
                MergeTarget = mergeTarget;
                MergeCount = mergeCount;
            }

            /// <summary>放置的左上角坐标。合并时无意义。</summary>
            public GridPoint Origin { get; }

            /// <summary>是否旋转放置。合并时无意义。</summary>
            public bool Rotated { get; }

            /// <summary>要并入的目标堆；为 null 表示放入空格。</summary>
            public ItemInstance MergeTarget { get; }

            /// <summary>并入目标堆的数量。</summary>
            public int MergeCount { get; }

            /// <summary>是否为合并操作。</summary>
            public bool IsMerge
            {
                get { return MergeTarget != null; }
            }
        }

        /// <summary>
        /// 预判物品能否放到指定位置。**不产生任何副作用**，供拖拽预览每帧调用。
        /// </summary>
        /// <param name="item">要放置的物品。</param>
        /// <param name="origin">目标左上角坐标。</param>
        /// <param name="rotated">是否旋转 90 度放置。</param>
        /// <returns>可以放置时返回成功。</returns>
        /// <remarks>
        /// 若物品已经在本容器内（即同容器内的挪动），它自己的格子会被忽略，
        /// 否则玩家把一个 2x2 的物品向右挪一格时会因为"压到自己"而被拒绝。
        /// </remarks>
        public InventoryResult CanPlace(ItemInstance item, GridPoint origin, bool rotated)
        {
            return EvaluatePlacement(item, origin, rotated, Contains(item) ? item : null);
        }

        /// <summary>
        /// 把物品放到指定位置。
        /// </summary>
        /// <param name="item">要放置的物品，必须不在本容器内。</param>
        /// <param name="origin">目标左上角坐标。</param>
        /// <param name="rotated">是否旋转 90 度放置。</param>
        /// <returns>操作结果。</returns>
        public InventoryResult Place(ItemInstance item, GridPoint origin, bool rotated)
        {
            if (item == null)
            {
                return InventoryResult.Fail(InventoryFailure.NotFound, "物品不存在。");
            }

            if (Contains(item))
            {
                // 同一件物品在容器里出现两次会让"它在哪一格"这个问题没有唯一答案，
                // 后续的移动、移除与存档都会出错，因此在入口处直接拒绝。
                return InventoryResult.Fail(InventoryFailure.Occupied, "该物品已经在容器内，请使用 Transfer 移动。");
            }

            var check = EvaluatePlacement(item, origin, rotated, null);
            if (!check.Success)
            {
                return check;
            }

            CommitPlacement(item, origin, rotated);
            return InventoryResult.Ok();
        }

        /// <summary>
        /// 自动寻找位置放置：优先并入已有的同类堆，其次找一个能容纳的空位。
        /// </summary>
        /// <param name="item">要放置的物品，必须不在本容器内。</param>
        /// <returns>操作结果。放不下时失败原因为 <see cref="InventoryFailure.Full"/>。</returns>
        public InventoryResult AutoPlace(ItemInstance item)
        {
            if (item == null)
            {
                return InventoryResult.Fail(InventoryFailure.NotFound, "物品不存在。");
            }

            if (Contains(item))
            {
                return InventoryResult.Fail(InventoryFailure.Occupied, "该物品已经在容器内。");
            }

            if (!TryPlanAutoPlace(item, out var plan))
            {
                return InventoryResult.Fail(InventoryFailure.Full, $"{m_Label} 没有可用空间。");
            }

            return CommitPlan(item, plan);
        }

        /// <summary>
        /// 预判物品能否被自动放下。**不产生任何副作用**。
        /// </summary>
        /// <param name="item">要放置的物品。</param>
        /// <returns>能放下返回 true。</returns>
        /// <remarks>
        /// 交换装备、卸下装备这类"先确认退路再动手"的操作依赖它：
        /// 必须先把换下来的东西的容身之处确认好，否则玩家会丢东西。
        /// </remarks>
        public bool CanAutoPlace(ItemInstance item)
        {
            return item != null && !Contains(item) && TryPlanAutoPlace(item, out _);
        }

        /// <summary>
        /// 从容器中取出物品。
        /// </summary>
        /// <param name="item">要取出的物品。</param>
        /// <returns>操作结果。</returns>
        /// <remarks>
        /// 不重置物品的 <see cref="ItemInstance.Rotated"/>：旋转是"这件物品在容器里怎么摆"的属性，
        /// 由下一次放置决定。在这里顺手清掉会让调用方无法预判，也会让"取出再放回"意外改变朝向。
        /// </remarks>
        public InventoryResult Remove(ItemInstance item)
        {
            if (item == null || !m_Items.Contains(item))
            {
                return InventoryResult.Fail(InventoryFailure.NotFound, "该物品不在容器内。");
            }

            ClearCells(item);
            m_Items.Remove(item);
            return InventoryResult.Ok();
        }

        /// <summary>取出指定格子上的物品并返回它。</summary>
        /// <param name="cell">格子坐标。</param>
        /// <returns>被取出的物品；空格返回 null。</returns>
        public ItemInstance RemoveAt(GridPoint cell)
        {
            var item = GetAt(cell);
            if (item == null)
            {
                return null;
            }

            Remove(item);
            return item;
        }

        /// <summary>
        /// 校验一次放置是否合法。
        /// </summary>
        /// <param name="item">要放置的物品。</param>
        /// <param name="origin">目标左上角坐标。</param>
        /// <param name="rotated">是否旋转放置。</param>
        /// <param name="ignore">占用检测时要忽略的物品（同容器内挪动时是物品自己）。</param>
        private InventoryResult EvaluatePlacement(
            ItemInstance item,
            GridPoint origin,
            bool rotated,
            ItemInstance ignore)
        {
            if (item == null)
            {
                return InventoryResult.Fail(InventoryFailure.NotFound, "物品不存在。");
            }

            var definition = item.Definition;
            if (rotated && !definition.CanRotate)
            {
                return InventoryResult.Fail(
                    InventoryFailure.RotationNotAllowed,
                    $"{definition.DisplayName} 不允许旋转。");
            }

            var size = SizeFor(definition, rotated);
            if (!IsInside(origin, size))
            {
                return InventoryResult.Fail(
                    InventoryFailure.OutOfBounds,
                    $"{definition.DisplayName} 放到 {origin} 会超出 {m_Label} 的边界。");
            }

            if (definition.IsContainer)
            {
                if (m_Depth >= ContainerRules.MaxNestingDepth)
                {
                    return InventoryResult.Fail(
                        InventoryFailure.NestingTooDeep,
                        $"{m_Label} 的嵌套深度已达上限 {ContainerRules.MaxNestingDepth}。");
                }

                if (ReferenceEquals(m_HostItem, item))
                {
                    return InventoryResult.Fail(
                        InventoryFailure.NestingTooDeep,
                        "容器不能放进它自己的内部。");
                }
            }

            for (var y = 0; y < size.Height; y++)
            {
                for (var x = 0; x < size.Width; x++)
                {
                    var occupant = GetOccupant(new GridPoint(origin.X + x, origin.Y + y));
                    if (occupant != null && !ReferenceEquals(occupant, ignore))
                    {
                        return InventoryResult.Fail(
                            InventoryFailure.Occupied,
                            $"目标位置已被 {occupant.Definition.DisplayName} 占用。");
                    }
                }
            }

            return InventoryResult.Ok();
        }

        /// <summary>
        /// 规划一次自动放置。先找能装下整堆的同类堆，再找第一个能放下的空位。
        /// </summary>
        /// <param name="item">要放置的物品。</param>
        /// <param name="plan">规划结果。</param>
        /// <returns>存在可行方案返回 true。</returns>
        /// <remarks>
        /// 合并要求"整堆装得下"而不是"能塞多少塞多少"：
        /// 一键转移的语义是"把东西搬过去"，如果只能部分搬走，剩下半堆留在原处
        /// 反而比直接放不下更让人困惑。部分合并在拖拽路径上另有规则（见 <c>Transfer</c>）。
        /// </remarks>
        private bool TryPlanAutoPlace(ItemInstance item, out PlacementPlan plan)
        {
            plan = default;

            for (var i = 0; i < m_Items.Count; i++)
            {
                var candidate = m_Items[i];
                if (candidate.CanStackWith(item) && candidate.RemainingStackRoom >= item.StackCount)
                {
                    plan = new PlacementPlan(default, false, candidate, item.StackCount);
                    return true;
                }
            }

            // 先按不旋转找，再按旋转找。顺序固定，因此同一份背包在两次整理之间不会来回跳。
            if (TryFindFreeOrigin(item, false, out var origin))
            {
                plan = new PlacementPlan(origin, false, null, 0);
                return true;
            }

            if (item.Definition.CanRotate && TryFindFreeOrigin(item, true, out var rotatedOrigin))
            {
                plan = new PlacementPlan(rotatedOrigin, true, null, 0);
                return true;
            }

            return false;
        }

        /// <summary>按从上到下、从左到右的顺序找第一个能容纳物品的空位。</summary>
        private bool TryFindFreeOrigin(ItemInstance item, bool rotated, out GridPoint origin)
        {
            origin = default;
            var size = SizeFor(item.Definition, rotated);

            for (var y = 0; y + size.Height <= m_Height; y++)
            {
                for (var x = 0; x + size.Width <= m_Width; x++)
                {
                    var candidate = new GridPoint(x, y);
                    if (EvaluatePlacement(item, candidate, rotated, null).Success)
                    {
                        origin = candidate;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 提交一次规划好的放置。
        /// </summary>
        /// <remarks>
        /// 进入本方法意味着校验已经全部通过，因此这里不再失败、不需要回滚。
        /// </remarks>
        private InventoryResult CommitPlan(ItemInstance item, in PlacementPlan plan)
        {
            if (plan.IsMerge)
            {
                var merged = plan.MergeTarget.AddToStack(plan.MergeCount);
                return InventoryResult.Ok(merged);
            }

            CommitPlacement(item, plan.Origin, plan.Rotated);
            return InventoryResult.Ok();
        }

        /// <summary>把物品登记进容器并写入占用表。</summary>
        private void CommitPlacement(ItemInstance item, GridPoint origin, bool rotated)
        {
            item.Rotated = rotated;
            m_Items.Add(item);
            FillCells(item, origin, SizeFor(item.Definition, rotated));
        }
    }
}
