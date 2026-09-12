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
        /// <summary>
        /// 换背包后重建整套界面布局。
        /// </summary>
        /// <param name="backpack">新的背包网格。</param>
        /// <param name="containerId">背包的容器 ID。</param>
        /// <remarks>
        /// <para>换包会改变网格尺寸，而弹药挂与战利品面板的位置是按背包高度推算出来的，
        /// 因此不能只替换一个视图——必须整块重建，否则面板会互相压住。</para>
        ///
        /// <para>重建只销毁自己创建的画布，玩家数据（网格与装备）完全不动。</para>
        /// </remarks>
        public void RebuildLayout(InventoryGrid backpack, int containerId)
        {
            // 先记住当前打开的战利品容器，并把状态清成「没有打开」。
            // 重建会销毁它的视图；若状态还留着这个 ID，OpenLootContainer 会以为
            // 「这个容器已经展示过了」而跳过重建——表现就是面板消失、重搜同一个箱子也不回来。
            var openLootId = m_LootContainerId;
            m_LootContainerId = 0;

            if (m_CanvasHost != null)
            {
                Destroy(m_CanvasHost);
            }

            m_CanvasHost = null;
            m_Root = null;
            m_MenuRoot = null;
            m_BackpackView = null;
            m_AmmoPouchView = null;
            m_LootView = null;
            m_Slots.Clear();
            m_MenuRows.Clear();

            m_BackpackContainerId = containerId;
            m_Loadout.ReplaceBackpack(backpack);

            var wasOpen = m_IsOpen;
            BuildLayout();
            RefreshAll();
            SetVisible(wasOpen);

            // 把打开着的战利品面板按原样恢复。标题用网格自己的标签，
            // 它就是创建容器时写进网格的显示名，不必再去问容器定义。
            if (wasOpen && openLootId > 0 && m_Registry.TryGetGrid(openLootId, out var loot))
            {
                OpenLootContainer(openLootId, loot.Label);
            }
        }

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

            // 弹药挂紧贴在背包下方：它是随身物品的一部分，放在一起符合「背包里的东西」这个分组。
            var pouchTopLeft = new Vector2(280f, 56f + (backpack.Height * InventoryGridView.CellSize) + 20f);
            if (m_Registry.TryGetGrid(m_AmmoPouchContainerId, out var pouch))
            {
                m_AmmoPouchView = CreateGridView(rootRect, pouch, m_AmmoPouchContainerId, "弹药挂", pouchTopLeft);
            }

            // 战利品与仓库放在**右侧独立一列**：它们与随身物品是两处东西，
            // 竖着叠在背包下方既挤又容易压出面板边界（仓库是 10 列宽）。
            //
            // 横坐标必须**按背包实际宽度推算**，不能写死：背包由装备决定（5x5 / 6x6 / 7x7），
            // 写死 600 时，换成 6x6 的背包就会与这一列压在一起（宽度变成 336，超出预留的 320）。
            var backpackColumnWidth = backpack.Width * InventoryGridView.CellSize;
            m_LootAnchorTopLeft = new Vector2(280f + backpackColumnWidth + 40f, 56f);
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

            // 打开界面时**如果什么容器都没指定**，就默认显示仓库（出击准备）。
            //
            // 条件必须是「当前没有打开任何容器」而不是「当前不是仓库」：
            // 后者会把刚被显式打开的容器顶掉——在安全屋里按 E 开测试箱，
            // 面板会立刻被替换成仓库，看起来就像「按 E 只会开仓库」。
            if (visible
                && m_PrepStashContainerId > 0
                && m_LootContainerId == 0)
            {
                OpenStash(m_PrepStashContainerId);
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
