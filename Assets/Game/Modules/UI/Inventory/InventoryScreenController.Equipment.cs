using RaidDemo.Inventory;
using RaidDemo.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 背包界面左栏：装备槽、负重条与价值显示。
    /// </summary>
    /// <remarks>
    /// <para>从 Layout 部分拆出来的原因很直接：整个布局文件已经超过项目规定的 400 行上限。
    /// 按职责切分之后，这一部分只回答"左栏长什么样、显示什么数字"，
    /// 面板骨架与网格摆放留在 Layout 里。</para>
    /// <para><b>装备槽是五行状态条，不是拟物化的人体轮廓。</b>这是刻意的取舍：
    /// 在只有 5 个槽位、又是纯代码构建的前提下，人形轮廓并不比一行文字更好读，
    /// 却要多维护一套坐标；现在用颜色区分空槽（灰字）与已装备（墨字），一眼能扫完。</para>
    /// </remarks>
    public sealed partial class InventoryScreenController
    {
        /// <summary>左栏宽度（像素）。</summary>
        private const float LeftColumnWidth = 300f;

        /// <summary>装备槽尺寸与间距（像素）。</summary>
        private const float SlotWidth = 300f;

        private const float SlotHeight = 58f;

        private const float SlotGap = 14f;

        /// <summary>负重条尺寸（像素）。</summary>
        private const float BarWidth = 300f;

        private const float BarHeight = 20f;

        /// <summary>背包价值 / 携带总值显示。</summary>
        private TextMeshProUGUI m_ValueLabel;

        /// <summary>创建设备槽一列。</summary>
        private void BuildEquipmentColumn(RectTransform parent)
        {
            var slots = new[]
            {
                EquipmentSlot.PrimaryWeapon,
                EquipmentSlot.SecondaryWeapon,
                EquipmentSlot.Head,
                EquipmentSlot.Body,
                EquipmentSlot.Backpack,
            };

            UiFactory.CreateLabel(
                parent,
                "装备",
                new Vector2(m_ContentLeftX + 4f, m_ContentTopY - 26f),
                new Vector2(LeftColumnWidth, 22f),
                UiPalette.SmallSize,
                TextAlignmentOptions.Left,
                UiPalette.InkSoft);

            for (var i = 0; i < slots.Length; i++)
            {
                var rect = UiFactory.CreatePanel(
                    parent,
                    $"Slot_{slots[i]}",
                    new Vector2(SlotWidth, SlotHeight),
                    UiSprites.Slot,
                    new Vector2(m_ContentLeftX, m_ContentTopY + (i * (SlotHeight + SlotGap))));

                var image = rect.GetComponent<Image>();
                var label = CreateLabel(
                    rect,
                    SlotDisplayName(slots[i]),
                    new Vector2(12f, 0f),
                    SlotWidth - 24f,
                    SlotHeight,
                    (int)UiPalette.BodySize);
                label.alignment = TextAlignmentOptions.Left;

                m_Slots.Add(new SlotWidget
                {
                    Slot = slots[i],
                    Rect = rect,
                    Background = image,
                    Label = label,
                });
            }
        }

        /// <summary>创建负重条与价值显示。</summary>
        private void BuildWeightBar(RectTransform parent)
        {
            var top = m_ContentTopY + (5f * (SlotHeight + SlotGap)) + 22f;

            m_BarLabel = CreateLabel(
                parent,
                string.Empty,
                new Vector2(m_ContentLeftX, top),
                LeftColumnWidth,
                24f,
                (int)UiPalette.SmallSize);

            UiFactory.CreateBar(
                parent,
                new Vector2(m_ContentLeftX, top + 28f),
                new Vector2(BarWidth, BarHeight),
                UiPalette.Ok,
                out m_BarFill);

            m_ValueLabel = CreateLabel(
                parent,
                string.Empty,
                new Vector2(m_ContentLeftX, top + 60f),
                LeftColumnWidth + 60f,
                46f,
                (int)UiPalette.SmallSize);
            m_ValueLabel.color = UiPalette.InkSoft;
            m_ValueLabel.textWrappingMode = TextWrappingModes.Normal;
        }

        /// <summary>刷新装备槽显示。</summary>
        private void RefreshEquipmentSlots()
        {
            for (var i = 0; i < m_Slots.Count; i++)
            {
                var widget = m_Slots[i];
                var item = m_Loadout.Equipment.Get(widget.Slot);
                widget.Item = item;
                widget.Background.color = Color.white;
                widget.Label.text = item == null
                    ? $"{SlotDisplayName(widget.Slot)}：空"
                    : $"{SlotDisplayName(widget.Slot)}：{item.Definition.DisplayName}";
                widget.Label.color = item == null ? UiPalette.InkDisabled : UiPalette.Ink;
            }
        }

        /// <summary>刷新负重条。</summary>
        private void RefreshWeightBar()
        {
            if (m_BarFill == null)
            {
                return;
            }

            var weight = m_Loadout.TotalWeightKg;
            var capacity = m_EncumbranceProfile != null ? m_EncumbranceProfile.CapacityKg : 0f;
            var ratio = capacity > 0f ? weight / capacity : 0f;

            UiFactory.SetBarProgress(m_BarFill, BarWidth, ratio);
            m_BarFill.color = ratio > 1f ? UiPalette.Bad : ratio >= 0.7f ? UiPalette.Warn : UiPalette.Ok;
            m_BarLabel.text = $"负重 {weight:F1} / {capacity:F0} kg";
        }

        /// <summary>
        /// 刷新背包价值与携带总值。
        /// </summary>
        /// <remarks>背包价值只统计主背包网格；携带总值与战局结算的带入价值同口径，
        /// 包含装备槽、弹药挂与背包。两个数字同时显示，玩家既能看到背包里装了什么，
        /// 也能看到这一趟输了会亏多少。</remarks>
        private void RefreshValueLabel()
        {
            if (m_ValueLabel == null || m_Loadout == null)
            {
                return;
            }

            var backpackValue = 0;
            var backpackItems = m_Loadout.Backpack != null ? m_Loadout.Backpack.Items : null;
            if (backpackItems != null)
            {
                for (var i = 0; i < backpackItems.Count; i++)
                {
                    backpackValue += backpackItems[i].TotalValue;
                }
            }

            var carriedValue = RaidDemo.Raid.RaidResult.ComputeCarriedValue(m_Loadout);
            m_ValueLabel.text = $"背包价值 {backpackValue:N0}\n携带总值 {carriedValue:N0}";
        }
    }
}
