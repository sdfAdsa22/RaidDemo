using RaidDemo.Kernel;
using RaidDemo.Shared;

namespace RaidDemo.Inventory
{
    /// <summary>
    /// 处理切换武器意图：交换主武器槽与副武器槽。
    /// </summary>
    /// <remarks>
    /// <para>本项目只区分"当前手持的"（主武器槽）与"备用的"（副武器槽），
    /// 因此切换武器就是交换这两个槽位。不需要额外维护一个"当前武器编号"——
    /// 那会引入第二份状态，而两份状态迟早会不一致。</para>
    /// <para>交换不会失败：两边都是已经装备好的物品，交换前后占用完全相同。</para>
    /// </remarks>
    public sealed class WeaponSwitchCommandHandler : ICommandHandler<PlayerSwitchWeaponIntent>
    {
        private readonly EquipmentLoadout m_Equipment;
        private readonly EventBus m_EventBus;

        /// <summary>创建处理器。</summary>
        /// <param name="equipment">装备栏。</param>
        /// <param name="eventBus">事件总线。</param>
        public WeaponSwitchCommandHandler(EquipmentLoadout equipment, EventBus eventBus)
        {
            m_Equipment = equipment;
            m_EventBus = eventBus;
        }

        /// <inheritdoc />
        public CommandResult Execute(in PlayerSwitchWeaponIntent command)
        {
            var primary = m_Equipment.Get(EquipmentSlot.PrimaryWeapon);
            var secondary = m_Equipment.Get(EquipmentSlot.SecondaryWeapon);

            if (primary == null && secondary == null)
            {
                return CommandResult.Fail(CommandCodes.CombatNoWeapon, "两个武器槽都是空的。");
            }

            // 只有一把武器时也允许切换：把空槽换到手上等于收起武器，
            // 这是玩家的明确意图，没有理由拒绝。
            if (!m_Equipment.Swap(EquipmentSlot.PrimaryWeapon, EquipmentSlot.SecondaryWeapon))
            {
                return CommandResult.Fail(CommandCodes.Rejected, "武器槽没有变化。");
            }

            // 装备栏不是容器，因此容器标识填 0。
            // 界面只把这个事件当作"刷新一下"的信号，不关心是哪一件。
            m_EventBus.Publish(new InventoryChangedEvent(
                0,
                InventoryChangeTypes.Equip,
                command.PlayerId,
                sequence: command.Sequence));

            return CommandResult.Ok();
        }
    }
}
