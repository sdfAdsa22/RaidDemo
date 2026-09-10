using System;
using System.Collections.Generic;
using RaidDemo.Data;

namespace RaidDemo.Inventory
{
    /// <summary>
    /// <see cref="InventoryGrid"/> 的搬运相关实现：移动、快速转移、旋转、拆分、整理。
    /// </summary>
    /// <remarks>
    /// <para>拖拽与快捷键**共用这里的同一套代码**。快速转移只是"不指定落点"的调用方式，
    /// 因此不可能出现"拖拽被拦下、快捷键却塞进去了"这种分叉——
    /// 这类不一致在测试里极难覆盖，却会在玩家手里被立刻发现。</para>
    /// </remarks>
    public sealed partial class InventoryGrid
    {
        /// <summary>
        /// 把物品移动到另一个容器（或同一容器的另一处）。
        /// </summary>
        /// <param name="item">要移动的物品，必须在本容器内。</param>
        /// <param name="target">目标容器。</param>
        /// <param name="origin">目标左上角坐标。</param>
        /// <param name="rotated">是否旋转放置。</param>
        /// <returns>操作结果。<see cref="InventoryResult.MovedCount"/> 为实际合并进目标堆的数量。</returns>
        /// <remarks>
        /// 目标格上恰好是可堆叠的同类物品时走合并分支：能并多少并多少，
        /// **并剩下的部分留在原处**。这样只需要一次数据改动，原子性天然成立，
        /// 也不会出现"半个堆漂在目标容器里"这种需要玩家自己收拾的中间状态。
        /// </remarks>
        public InventoryResult Transfer(ItemInstance item, InventoryGrid target, GridPoint origin, bool rotated)
        {
            if (item == null)
            {
                return InventoryResult.Fail(InventoryFailure.NotFound, "物品不存在。");
            }

            if (target == null)
            {
                return InventoryResult.Fail(InventoryFailure.NotFound, "目标容器不存在。");
            }

            if (!Contains(item))
            {
                return InventoryResult.Fail(InventoryFailure.NotFound, "物品不在源容器内。");
            }

            var occupant = target.GetAt(origin);
            if (occupant != null && !ReferenceEquals(occupant, item) && occupant.CanStackWith(item))
            {
                return MergeInto(item, occupant);
            }

            // 同容器内的挪动要忽略物品自己的格子，否则向右挪一格会判定为压到自己。
            var ignore = ReferenceEquals(target, this) ? item : null;
            var check = target.EvaluatePlacement(item, origin, rotated, ignore);
            if (!check.Success)
            {
                return check;
            }

            if (ReferenceEquals(target, this))
            {
                ClearCells(item);
                item.Rotated = rotated;
                FillCells(item, origin, SizeFor(item.Definition, rotated));
                return InventoryResult.Ok();
            }

            ClearCells(item);
            m_Items.Remove(item);
            target.CommitPlacement(item, origin, rotated);
            return InventoryResult.Ok();
        }

        /// <summary>
        /// 一键转移：不指定落点，由规则寻找位置。
        /// </summary>
        /// <param name="item">要转移的物品，必须在本容器内。</param>
        /// <param name="target">目标容器，不能是本容器。</param>
        /// <returns>操作结果。放不下时失败原因为 <see cref="InventoryFailure.Full"/>。</returns>
        /// <remarks>
        /// 与 <see cref="Transfer"/> 的差别在于合并策略：这里要求整堆装得下才合并，
        /// 否则改为寻找空位。因为一键转移没有"落点"这个信息，
        /// 剩下半堆留在原处会让玩家以为转移失败了。
        /// </remarks>
        public InventoryResult QuickTransfer(ItemInstance item, InventoryGrid target)
        {
            if (item == null)
            {
                return InventoryResult.Fail(InventoryFailure.NotFound, "物品不存在。");
            }

            if (target == null)
            {
                return InventoryResult.Fail(InventoryFailure.NotFound, "目标容器不存在。");
            }

            if (ReferenceEquals(target, this))
            {
                return InventoryResult.Fail(InventoryFailure.NotFound, "源容器与目标容器相同。");
            }

            if (!Contains(item))
            {
                return InventoryResult.Fail(InventoryFailure.NotFound, "物品不在源容器内。");
            }

            // 规划阶段只读不写，因此这里失败时源容器一个字节都没动。
            if (!target.TryPlanAutoPlace(item, out var plan))
            {
                return InventoryResult.Fail(InventoryFailure.Full, $"{target.Label} 没有可用空间。");
            }

            ClearCells(item);
            m_Items.Remove(item);
            return target.CommitPlan(item, plan);
        }

        /// <summary>
        /// 就地旋转物品 90 度。
        /// </summary>
        /// <param name="item">要旋转的物品，必须在本容器内。</param>
        /// <returns>操作结果。旋转后放不下时失败，且物品保持原朝向。</returns>
        public InventoryResult Rotate(ItemInstance item)
        {
            if (item == null || !m_Items.Contains(item))
            {
                return InventoryResult.Fail(InventoryFailure.NotFound, "物品不在容器内。");
            }

            if (!item.Definition.CanRotate)
            {
                return InventoryResult.Fail(
                    InventoryFailure.RotationNotAllowed,
                    $"{item.Definition.DisplayName} 不允许旋转。");
            }

            if (!TryGetOrigin(item, out var origin))
            {
                return InventoryResult.Fail(InventoryFailure.NotFound, "找不到物品在容器中的位置。");
            }

            var target = !item.Rotated;
            var check = EvaluatePlacement(item, origin, target, item);
            if (!check.Success)
            {
                return check;
            }

            ClearCells(item);
            item.Rotated = target;
            FillCells(item, origin, SizeFor(item.Definition, target));
            return InventoryResult.Ok();
        }

        /// <summary>
        /// 从一堆中拆出指定数量，自动放进本容器的空位。
        /// </summary>
        /// <param name="item">源堆，必须在本容器内。</param>
        /// <param name="count">拆出的数量，必须大于 0 且小于源堆数量。</param>
        /// <returns>操作结果。容器没有空位时失败，且源堆数量保持不变。</returns>
        /// <remarks>
        /// 这是"按 R 拆分"这类不指定落点的界面操作入口。
        /// 它先找位置再拆分，因此不会出现拆完了却没地方放、物品凭空少一半的情况。
        /// </remarks>
        public InventoryResult Split(ItemInstance item, int count)
        {
            if (item == null || !m_Items.Contains(item))
            {
                return InventoryResult.Fail(InventoryFailure.NotFound, "源物品不在容器内。");
            }

            if (count <= 0 || count >= item.StackCount)
            {
                return InventoryResult.Fail(
                    InventoryFailure.InvalidQuantity,
                    $"拆分数量必须大于 0 且小于 {item.StackCount}。");
            }

            if (TryFindFreeOrigin(item, false, out var origin))
            {
                return Split(item, count, this, origin, false);
            }

            if (item.Definition.CanRotate && TryFindFreeOrigin(item, true, out var rotatedOrigin))
            {
                return Split(item, count, this, rotatedOrigin, true);
            }

            return InventoryResult.Fail(
                InventoryFailure.Full,
                $"{m_Label} 没有空位放下拆分出的物品。");
        }

        /// <summary>
        /// 从一堆中拆出指定数量，放到目标位置。
        /// </summary>
        /// <param name="item">源堆，必须在本容器内。</param>
        /// <param name="count">拆出的数量，必须大于 0 且小于源堆数量。</param>
        /// <param name="target">接收拆分结果的目标容器。</param>
        /// <param name="origin">拆分结果的左上角坐标。</param>
        /// <param name="rotated">拆分结果是否旋转放置。</param>
        /// <returns>操作结果。</returns>
        /// <remarks>
        /// 先校验后拆分：目标放不下时源堆数量必须保持不变，
        /// 否则玩家会看到"拆失败了但子弹少了一半"。
        /// </remarks>
        public InventoryResult Split(
            ItemInstance item,
            int count,
            InventoryGrid target,
            GridPoint origin,
            bool rotated)
        {
            if (item == null || !m_Items.Contains(item))
            {
                return InventoryResult.Fail(InventoryFailure.NotFound, "源物品不在容器内。");
            }

            if (count <= 0 || count >= item.StackCount)
            {
                return InventoryResult.Fail(
                    InventoryFailure.InvalidQuantity,
                    $"拆分数量必须大于 0 且小于 {item.StackCount}。");
            }

            if (target == null)
            {
                return InventoryResult.Fail(InventoryFailure.NotFound, "目标容器不存在。");
            }

            // 拆分出的是一个全新的堆，因此它不能与自己重叠——忽略项传 null。
            var check = target.EvaluatePlacement(item, origin, rotated, null);
            if (!check.Success)
            {
                return check;
            }

            var piece = item.Split(count);
            var placed = target.Place(piece, origin, rotated);
            if (!placed.Success)
            {
                // 理论上不会走到这里：上面的校验与 Place 用的是同一套规则。
                // 保留这条兜底是为了让"拆分出半个物品凭空消失"永远不会发生。
                item.AddToStack(piece.StackCount);
                return placed;
            }

            return InventoryResult.Ok(count);
        }

        /// <summary>
        /// 自动整理：按占格面积从大到小重新摆放。
        /// </summary>
        /// <remarks>
        /// <para>排序键是"面积降序、其次按 ID 字典序"。面积优先是因为大件更难安排，
        /// 先放它们能显著减少后续放不下的概率；ID 作为第二关键字保证结果稳定可复现——
        /// 同一份背包连续整理两次必须得到完全相同的布局，否则玩家会以为东西被动了。</para>
        ///
        /// <para><b>守恒保证</b>：整个重排是"全部取出再放回"，一旦中途有物品放不下，
        /// 就整体回滚到操作前的布局。回滚一定成功，因为原布局本来就是合法的，
        /// 因此物品数量与总重量在任何路径上都不会变化。</para>
        /// </remarks>
        public void Sort()
        {
            if (m_Items.Count <= 1)
            {
                return;
            }

            var original = new List<PlacementSnapshot>(m_Items.Count);
            for (var i = 0; i < m_Items.Count; i++)
            {
                if (TryGetOrigin(m_Items[i], out var origin))
                {
                    original.Add(new PlacementSnapshot(m_Items[i], origin, m_Items[i].Rotated));
                }
            }

            var ordered = new List<PlacementSnapshot>(original);
            ordered.Sort(CompareByAreaThenId);

            ClearAll();

            for (var i = 0; i < ordered.Count; i++)
            {
                var entry = ordered[i];
                if (!TryFindAnyFreeOrigin(entry.Item, out var origin, out var rotated))
                {
                    RestoreLayout(original);
                    return;
                }

                CommitPlacement(entry.Item, origin, rotated);
            }
        }

        /// <summary>按不旋转优先的顺序找一个能放下物品的位置。</summary>
        private bool TryFindAnyFreeOrigin(ItemInstance item, out GridPoint origin, out bool rotated)
        {
            if (TryFindFreeOrigin(item, false, out origin))
            {
                rotated = false;
                return true;
            }

            if (item.Definition.CanRotate && TryFindFreeOrigin(item, true, out origin))
            {
                rotated = true;
                return true;
            }

            rotated = false;
            return false;
        }

        /// <summary>把布局恢复到指定快照。</summary>
        private void RestoreLayout(List<PlacementSnapshot> snapshot)
        {
            ClearAll();
            for (var i = 0; i < snapshot.Count; i++)
            {
                var entry = snapshot[i];
                CommitPlacement(entry.Item, entry.Origin, entry.Rotated);
            }
        }

        /// <summary>把源物品的一部分并入目标堆。并剩下的留在源物品里。</summary>
        private InventoryResult MergeInto(ItemInstance source, ItemInstance target)
        {
            var request = source.StackCount;
            var merged = target.AddToStack(request);
            if (merged <= 0)
            {
                return InventoryResult.Fail(
                    InventoryFailure.StackLimit,
                    $"{target.Definition.DisplayName} 已达到堆叠上限。");
            }

            if (merged >= request)
            {
                ClearCells(source);
                m_Items.Remove(source);
                return InventoryResult.Ok(merged);
            }

            // 拆出已经并入目标堆的那部分并丢弃对象：它的数量已经在目标堆里了，
            // 源物品自然只剩没并进去的余量。
            source.Split(merged);
            return InventoryResult.Ok(merged);
        }

        /// <summary>排序比较：面积降序，面积相同则按 ID 字典序升序。</summary>
        private static int CompareByAreaThenId(PlacementSnapshot left, PlacementSnapshot right)
        {
            var leftArea = left.Item.Definition.GridSize.CellCount;
            var rightArea = right.Item.Definition.GridSize.CellCount;
            if (leftArea != rightArea)
            {
                return rightArea.CompareTo(leftArea);
            }

            return string.CompareOrdinal(left.Item.Definition.Id, right.Item.Definition.Id);
        }

        /// <summary>整理前后用于还原布局的一条记录。</summary>
        private readonly struct PlacementSnapshot
        {
            public PlacementSnapshot(ItemInstance item, GridPoint origin, bool rotated)
            {
                Item = item;
                Origin = origin;
                Rotated = rotated;
            }

            public ItemInstance Item { get; }

            public GridPoint Origin { get; }

            public bool Rotated { get; }
        }
    }
}
