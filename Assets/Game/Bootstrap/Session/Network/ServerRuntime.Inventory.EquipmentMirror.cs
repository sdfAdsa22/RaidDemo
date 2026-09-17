using RaidDemo.Inventory;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 服务器运行时的装备槽镜像部分：把玩家的装备状态打包成可下行的"伪容器"。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单独成文件：</b>单文件超过 400 行会触发工程规范检查（见 PathRulesTests）。
    /// 拆分的切法是"按内容"而不是按行数硬切——这一段只读战斗协调者里的玩家载荷，
    /// 与容器注册表本身没有共享状态，正好是一个独立职责。</para>
    ///
    /// <para><b>为什么是"伪容器"而不是新协议：</b>容器内容批次已经具备完整的
    /// 序列化、下行通道、增量重发（每次背包 / 装备命令执行后都会重发）与客户端重建路径。
    /// 装备槽的形状（若干固定槽位、每槽一件）可以无损表达成"每槽一格"的网格，
    /// 因此复用它只需要两端各加一处转换，不需要新增消息类型、通道与重发时机。</para>
    /// </remarks>
    public sealed partial class ServerRuntime
    {
        /// <summary>
        /// 取玩家的装备槽镜像；映射约定在 <see cref="EquipmentMirrorCodec"/> 里。
        /// </summary>
        /// <param name="playerId">目标玩家编号。</param>
        /// <param name="message">打包结果；该玩家没有载荷时返回 false。</param>
        /// <remarks>空槽位不发条目——镜像的语义是"服务器有什么"，由接收方先清空再重建。</remarks>
        private bool TryBuildEquipmentMirror(int playerId, out ContainerContentsMessage message)
        {
            message = default;

            // 与背包命令通道取同一份随身装备：安全屋里玩家没有参战，这里同样回退到
            // 账号档案（否则安全屋下发的批次里没有装备镜像，客户端切换场景后装备显示会缺失）。
            var loadout = ResolveCommandLoadout(playerId);
            if (loadout == null || loadout.Equipment == null)
            {
                return false;
            }

            message = EquipmentMirrorCodec.Build(loadout.Equipment);
            return true;
        }
    }
}
