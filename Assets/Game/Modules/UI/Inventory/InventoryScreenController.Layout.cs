using System.Collections.Generic;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Kernel;
using RaidDemo.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 背包界面的布局与刷新部分。
    /// </summary>
    /// <remarks>
    /// <para>拆成 partial 文件的原因：整个界面类已经超过项目规定的单文件 400 行上限。
    /// 这里按职责切分，布局负责"长什么样"，拖拽部分负责"鼠标做了什么"。</para>
    /// <para>界面在运行时用代码构建，不依赖预制体。灰盒阶段这样最省事，
    /// 等 M7 做美术时再把布局换成预制体，届时逻辑部分不需要改动。</para>
    /// </remarks>
    public sealed partial class InventoryScreenController
    {
        /// <summary>构建整套界面。</summary>
        private void BuildLayout()
        {
            var canvasHost = new GameObject("InventoryCanvas", typeof(Canvas), typeof(CanvasScaler));
            canvasHost.transform.SetParent(transform, worldPositionStays: false);
            var canvas = canvasHost.GetComponent<Canvas>();
            m_CanvasHost = canvasHost;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // 必须高于准星画布的 100：两者相同时，后创建的画布会盖在上面，
            // 而准星是启动过程中后建的，于是准星会压在背包面板上。
            canvas.sortingOrder = 200;

            var scaler = canvasHost.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);

            var rootHost = new GameObject("Root", typeof(RectTransform), typeof(Image));
            var rootRect = (RectTransform)rootHost.transform;
            rootRect.SetParent(canvasHost.transform, worldPositionStays: false);
            rootRect.anchorMin = new Vector2(0.5f, 0.5f);
            rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.anchoredPosition = new Vector2(0f, 0f);
            rootRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            var background = rootHost.GetComponent<Image>();
            background.color = PanelColor;
            background.raycastTarget = false;
            m_Root = rootHost;

            CreateLabel(rootRect, "背包 (Tab 关闭  F 整理  双击快速搬运  R 拖拽中旋转  右键拆分 / 卸下)",
                new Vector2(24f, 16f), PanelWidth - 48f, 24f, 16);

            BuildEquipmentColumn(rootRect);
            BuildWeightBar(rootRect);

            var backpack = m_Loadout.Backpack;
            m_BackpackView = CreateGridView(rootRect, backpack, m_BackpackContainerId, "主背包", new Vector2(280f, 56f));

            // 弹药挂夹在背包与战利品面板之间：它是随身物品的一部分，
            // 放在背包正下方符合「背包里的东西」这个心理分组。
            //
            // 战利品面板在这里**只算位置、不创建**：M5 的容器散布在地图上，
            // 只有玩家搜刮读条完成后才需要它出现（见 OpenLootContainer）。
            // 固定创建一块空面板会让界面在没开箱子时也占着半个屏幕。
            m_LootAnchorTopLeft = new Vector2(280f, 56f + (backpack.Height * InventoryGridView.CellSize) + 20f);
            if (m_Registry.TryGetGrid(m_AmmoPouchContainerId, out var pouch))
            {
                m_AmmoPouchView = CreateGridView(rootRect, pouch, m_AmmoPouchContainerId, "弹药挂", m_LootAnchorTopLeft);
                m_LootAnchorTopLeft += new Vector2(0f, (pouch.Height * InventoryGridView.CellSize) + 22f + 24f);
            }
        }

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

            for (var i = 0; i < slots.Length; i++)
            {
                var host = new GameObject($"Slot_{slots[i]}", typeof(RectTransform), typeof(Image));
                var rect = (RectTransform)host.transform;
                rect.SetParent(parent, worldPositionStays: false);
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(40f, -56f - (i * SlotGap));
                rect.sizeDelta = new Vector2(SlotWidth, SlotHeight);

                var image = host.GetComponent<Image>();
                image.color = SlotColor;
                image.raycastTarget = false;

                var label = CreateLabel(rect, SlotDisplayName(slots[i]), new Vector2(8f, 0f), SlotWidth - 16f, SlotHeight, 13);

                m_Slots.Add(new SlotWidget
                {
                    Slot = slots[i],
                    Rect = rect,
                    Background = image,
                    Label = label,
                });
            }
        }

        /// <summary>创建负重条。</summary>
        private void BuildWeightBar(RectTransform parent)
        {
            var top = 56f + (5f * SlotGap) + 24f;

            var backHost = new GameObject("WeightBarBack", typeof(RectTransform), typeof(Image));
            var backRect = (RectTransform)backHost.transform;
            backRect.SetParent(parent, worldPositionStays: false);
            backRect.anchorMin = new Vector2(0f, 1f);
            backRect.anchorMax = new Vector2(0f, 1f);
            backRect.pivot = new Vector2(0f, 1f);
            backRect.anchoredPosition = new Vector2(40f, -top);
            backRect.sizeDelta = new Vector2(BarWidth, BarHeight);
            var backImage = backHost.GetComponent<Image>();
            backImage.color = BarBackColor;
            backImage.raycastTarget = false;

            var fillHost = new GameObject("WeightBarFill", typeof(RectTransform), typeof(Image));
            var fillRect = (RectTransform)fillHost.transform;
            fillRect.SetParent(backRect, worldPositionStays: false);
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.anchoredPosition = Vector2.zero;
            fillRect.sizeDelta = new Vector2(0f, 0f);
            m_BarFill = fillHost.GetComponent<Image>();
            m_BarFill.color = LightColor;
            m_BarFill.raycastTarget = false;

            m_BarLabel = CreateLabel(parent, string.Empty, new Vector2(40f, top + BarHeight + 4f), BarWidth, 20f, 13);
        }

        /// <summary>创建一个网格视图。</summary>
        private InventoryGridView CreateGridView(
            RectTransform parent,
            InventoryGrid grid,
            int containerId,
            string title,
            Vector2 topLeft)
        {
            var host = new GameObject($"Grid_{containerId}");
            host.transform.SetParent(parent, worldPositionStays: false);
            var view = host.AddComponent<InventoryGridView>();
            view.Build(parent, grid, containerId, title, topLeft);
            view.Refresh();
            return view;
        }

        /// <summary>重画全部内容。</summary>
        private void RefreshAll()
        {
            m_BackpackView?.Refresh();
            m_AmmoPouchView?.Refresh();
            m_LootView?.Refresh();
            RefreshEquipmentSlots();
            RefreshWeightBar();
        }

        /// <summary>刷新装备槽显示。</summary>
        private void RefreshEquipmentSlots()
        {
            for (var i = 0; i < m_Slots.Count; i++)
            {
                var widget = m_Slots[i];
                var item = m_Loadout.Equipment.Get(widget.Slot);
                widget.Item = item;
                widget.Background.color = SlotColor;
                widget.Label.text = item == null
                    ? $"{SlotDisplayName(widget.Slot)}：空"
                    : $"{SlotDisplayName(widget.Slot)}：{item.Definition.DisplayName}";
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

            m_BarFill.rectTransform.sizeDelta = new Vector2(BarWidth * Mathf.Clamp01(ratio), 0f);
            m_BarFill.color = ratio > 1f ? OverloadedColor : ratio >= 0.7f ? HeavyColor : LightColor;
            m_BarLabel.text = $"{weight:F1} / {capacity:F0} kg";
        }

        /// <summary>显示或隐藏整个界面。</summary>
        private void SetVisible(bool visible)
        {
            m_IsOpen = visible;
            if (m_Root != null)
            {
                m_Root.SetActive(visible);
            }

            m_SetCursorLock?.Invoke(!visible);

            if (!visible)
            {
                CancelDrag();
                CloseContextMenu();

                // 关闭界面即结束这次搜刮：战利品面板随之销毁，
                // 想再搬东西就得重新走到箱子前读条。这样「开箱成本」对每次搜刮都成立，
                // 而不是开一次之后就能无限次免费取用。
                CloseLootContainer();
            }
        }
        /// <summary>装备槽的中文名。</summary>
        private static string SlotDisplayName(EquipmentSlot slot)
        {
            switch (slot)
            {
                case EquipmentSlot.PrimaryWeapon:
                    return "主武器";
                case EquipmentSlot.SecondaryWeapon:
                    return "副武器";
                case EquipmentSlot.Head:
                    return "头盔";
                case EquipmentSlot.Body:
                    return "护甲";
                default:
                    return "背包";
            }
        }

        /// <summary>创建一个文本标签。</summary>
        private static Text CreateLabel(
            RectTransform parent,
            string content,
            Vector2 topLeft,
            float width,
            float height,
            int fontSize)
        {
            var host = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var rect = (RectTransform)host.transform;
            rect.SetParent(parent, worldPositionStays: false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
            rect.sizeDelta = new Vector2(width, height);

            var text = host.GetComponent<Text>();
            text.font = UiFontProvider.Get(fontSize);
            text.fontSize = fontSize;
            text.text = content;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = new Color(0.92f, 0.92f, 0.95f);
            text.raycastTarget = false;
            return text;
        }
    }
}
