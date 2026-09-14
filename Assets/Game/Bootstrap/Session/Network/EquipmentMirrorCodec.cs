using System.Collections.Generic;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Shared;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 装备槽镜像的编解码：把装备槽打包成"1×N 伪容器"，以及从镜像还原装备槽。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么是独立静态类：</b>服务器发送前与客户端接收后做的是同一套映射
    /// （列固定 0、行号即槽位序号、空槽不发条目）。两边各写一遍的代价不是"多几行"，
    /// 而是两端约定会在后续改动里悄悄分叉——那种错位表现为"装备了却看不见"，
    /// 从现象几乎不可能反推。集中成一份纯函数之后，EditMode 测试直接钉住约定。</para>
    ///
    /// <para><b>为什么放在 Bootstrap 层：</b>消息类型（<see cref="ContainerContentsMessage"/>）
    /// 属于本层，而 <c>RaidDemo.Inventory</c> 不允许反向依赖它。编解码依赖两个方向，
    /// 只能落在共同的上一层。</para>
    /// </remarks>
    public static class EquipmentMirrorCodec
    {
        /// <summary>镜像容器的列数。固定为 1：一列从上到下就是各个装备槽。</summary>
        public const int MirrorWidth = 1;

        /// <summary>
        /// 把装备槽打包成镜像容器。
        /// </summary>
        /// <param name="equipment">要打包的装备槽；不允许为 null。</param>
        /// <remarks>空槽不发条目——镜像的语义是"服务器有什么"，由接收方先清空再重建。</remarks>
        public static ContainerContentsMessage Build(EquipmentLoadout equipment)
        {
            if (equipment == null)
            {
                throw new System.ArgumentNullException(nameof(equipment));
            }

            var items = new List<ContainerItemMessage>(EquipmentLoadout.SlotCount);
            for (var slot = 0; slot < EquipmentLoadout.SlotCount; slot++)
            {
                var item = equipment.Get((EquipmentSlot)slot);
                if (item == null || item.Definition == null)
                {
                    continue;
                }

                items.Add(new ContainerItemMessage
                {
                    ItemId = item.Definition.Id,
                    Count = item.StackCount,
                    // 装备槽没有旋转概念：装备永远是"正着"躺在槽里。
                    Rotated = false,
                    CellX = 0,
                    CellY = slot,
                });
            }

            return new ContainerContentsMessage
            {
                ContainerId = ContainerIds.EquipmentMirror,
                Width = MirrorWidth,
                Height = EquipmentLoadout.SlotCount,
                Items = items.ToArray(),
            };
        }

        /// <summary>
        /// 把镜像内容应用到装备槽（先清空再重建）。
        /// </summary>
        /// <param name="equipment">目标装备槽。</param>
        /// <param name="mirror">服务器下发的镜像条目。</param>
        /// <param name="catalog">物品定义目录，用于按 Id 还原实例。</param>
        /// <param name="factory">物品工厂；省略时内部新建一个。</param>
        /// <returns>实际放回槽位的件数。</returns>
        /// <remarks>
        /// 先清空是语义要求而不是实现细节：服务器取下的装备必须真的从客户端消失。
        /// 查不到的定义与越界的行号一律跳过——与 <c>LootContainerSync</c> 对未知物品的处理一致，
        /// 一个坏条目不该让整批同步停下。
        /// </remarks>
        public static int Apply(
            EquipmentLoadout equipment,
            in ContainerContentsMessage mirror,
            IItemDefinitionLookup catalog,
            ItemFactory factory = null)
        {
            if (equipment == null || catalog == null)
            {
                return 0;
            }

            factory ??= new ItemFactory();
            var applied = 0;

            equipment.Clear();

            if (mirror.Items == null)
            {
                return applied;
            }

            for (var i = 0; i < mirror.Items.Length; i++)
            {
                var entry = mirror.Items[i];
                if (string.IsNullOrEmpty(entry.ItemId)
                    || !catalog.TryGet(entry.ItemId, out var definition))
                {
                    continue;
                }

                var slotIndex = entry.CellY;
                if (slotIndex < 0 || slotIndex >= EquipmentLoadout.SlotCount)
                {
                    continue;
                }

                equipment.Equip(factory.Create(definition), (EquipmentSlot)slotIndex);
                applied++;
            }

            return applied;
        }
    }
}
