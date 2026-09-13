using System.Text;
using RaidDemo.Data;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RaidDemo.UI
{
    /// <summary>
    /// 图鉴界面的动作部分：按页签筛选、选中条目、刷新右侧详情。
    /// </summary>
    /// <remarks>与商人界面同一套分工：Layout 管"长什么样"，这里管"点哪里、显示什么"。</remarks>
    public sealed partial class CodexScreenController
    {
        /// <summary>重画全部内容：标题进度、按页签筛选卡片、刷新详情。</summary>
        private void RefreshAll()
        {
            RefreshProgressLabel();
            ApplyFilter();
            RefreshDetail();
        }

        /// <summary>刷新标题里的收集进度。</summary>
        private void RefreshProgressLabel()
        {
            if (m_ProgressLabel == null)
            {
                return;
            }

            var total = m_Catalog != null ? m_Catalog.All.Count : 0;
            var discovered = CountDiscovered(m_Catalog, m_Codex);
            m_ProgressLabel.text = $"已收集 {discovered} / {total}";
        }

        /// <summary>
        /// 打开界面时把选中项挪到第一件已获得的物品上。
        /// </summary>
        /// <remarks>选中项本身已经是已获得时就保持不动：玩家上次翻到哪一件，这次打开还看哪一件。
        /// 只有在"当前什么都没选、或选中的是一件没拿到的东西"时才做这次纠偏，
        /// 否则第一眼看到的就是一片问号，容易让人误以为图鉴是空的。</remarks>
        private void PreferDiscoveredSelection()
        {
            if (m_Selected != null && m_Selected.Discovered)
            {
                return;
            }

            for (var i = 0; i < m_Cards.Count; i++)
            {
                var card = m_Cards[i];
                if (!card.Rect.gameObject.activeSelf || !card.Discovered)
                {
                    continue;
                }

                m_Selected = card;
                RefreshCards();
                RefreshDetail();
                return;
            }
        }

        /// <summary>按当前页签显示 / 隐藏卡片，并保证始终有一张卡片处于选中态。</summary>
        private void ApplyFilter()
        {
            CardWidget firstVisible = null;
            for (var i = 0; i < m_Cards.Count; i++)
            {
                var card = m_Cards[i];
                var visible = MatchesFilter(card.Definition, m_Filter);
                card.Rect.gameObject.SetActive(visible);
                if (visible && firstVisible == null)
                {
                    firstVisible = card;
                }
            }

            if (m_Selected == null || !m_Selected.Rect.gameObject.activeSelf)
            {
                m_Selected = firstVisible;
            }

            RefreshCards();
        }

        /// <summary>某件物品是否属于当前页签。</summary>
        private static bool MatchesFilter(ItemDefinition definition, CodexFilter filter)
        {
            if (definition == null)
            {
                return false;
            }

            switch (filter)
            {
                case CodexFilter.Weapon:
                    return definition.Category == ItemCategory.Weapon;
                case CodexFilter.Ammo:
                    return definition.Category == ItemCategory.Ammo;
                case CodexFilter.Armor:
                    return definition.Category == ItemCategory.Helmet
                        || definition.Category == ItemCategory.BodyArmor;
                case CodexFilter.Medical:
                    return definition.Category == ItemCategory.Medical;
                case CodexFilter.Backpack:
                    return definition.Category == ItemCategory.Backpack;
                case CodexFilter.Misc:
                    return definition.Category == ItemCategory.Loot
                        || definition.Category == ItemCategory.Key
                        || definition.Category == ItemCategory.Quest;
                default:
                    return true;
            }
        }

        /// <summary>按"是否已点亮"重画每张卡片，并标出当前选中。</summary>
        private void RefreshCards()
        {
            for (var i = 0; i < m_Cards.Count; i++)
            {
                var card = m_Cards[i];
                card.Discovered = IsDiscovered(card.Definition);
                ApplyCardVisual(card, card == m_Selected);
            }
        }

        /// <summary>页签的悬停与点击。</summary>
        private void UpdateTabInput(Vector2 pointer)
        {
            for (var i = 0; i < m_Tabs.Count; i++)
            {
                var tab = m_Tabs[i];
                var hovered = tab.Button.Contains(pointer);
                tab.Button.SetVariant(tab.Filter == m_Filter ? UiButtonKind.Primary : UiButtonKind.Normal);
                tab.Button.SetHovered(hovered);

                if (!hovered || !Mouse.current.leftButton.wasPressedThisFrame)
                {
                    continue;
                }

                if (m_Filter == tab.Filter)
                {
                    return;
                }

                m_Filter = tab.Filter;
                ApplyFilter();
                RefreshDetail();
                UiAudio.Play(UiCue.TabSwitch);
                return;
            }
        }

        /// <summary>卡片的悬停与点击。点选已获得条目播放轻音，点未获得条目播放锁定音。</summary>
        private void UpdateCardInput(Vector2 pointer)
        {
            for (var i = 0; i < m_Cards.Count; i++)
            {
                var card = m_Cards[i];
                if (!card.Rect.gameObject.activeSelf)
                {
                    continue;
                }

                var hovered = RectTransformUtility.RectangleContainsScreenPoint(card.Rect, pointer, null);
                if (hovered && card != m_Selected && Mouse.current.leftButton.wasPressedThisFrame)
                {
                    m_Selected = card;
                    RefreshCards();
                    RefreshDetail();
                    UiAudio.Play(card.Discovered ? UiCue.Click : UiCue.Locked);
                    return;
                }
            }
        }

        /// <summary>关闭按钮。</summary>
        private void UpdateCloseInput(Vector2 pointer)
        {
            if (m_CloseButton == null)
            {
                return;
            }

            var hovered = m_CloseButton.Contains(pointer);
            m_CloseButton.SetHovered(hovered);
            if (hovered && Mouse.current.leftButton.wasPressedThisFrame)
            {
                Close();
            }
        }

        /// <summary>刷新右侧详情栏。未获得条目只显示线索，不显示任何数值。</summary>
        private void RefreshDetail()
        {
            if (m_DetailName == null)
            {
                return;
            }

            var definition = m_Selected != null ? m_Selected.Definition : null;
            if (definition == null)
            {
                m_DetailIcon.color = Color.clear;
                m_DetailName.text = "——";
                m_DetailMeta.text = "目录为空";
                m_DetailBaseTitle.gameObject.SetActive(false);
                m_DetailBase.text = string.Empty;
                m_DetailStatsDivider.gameObject.SetActive(false);
                m_DetailStatsTitle.gameObject.SetActive(false);
                m_DetailStats.text = string.Empty;
                m_DetailDescriptionDivider.gameObject.SetActive(false);
                m_DetailDescription.text = string.Empty;
                m_DetailDescriptionTitle.gameObject.SetActive(false);
                return;
            }

            var discovered = IsDiscovered(definition);
            var rarityColor = RarityPalette.GetColorOnPaper(definition.Rarity);

            if (discovered)
            {
                m_DetailIcon.sprite = definition.Icon;
                m_DetailIcon.color = definition.Icon != null ? Color.white : Color.clear;
                m_DetailName.text = definition.DisplayName;
                m_DetailMeta.text =
                    $"{RarityPalette.GetDisplayName(definition.Rarity)} · {ResolveCategoryName(definition.Category)}";
                m_DetailBaseTitle.gameObject.SetActive(true);
                m_DetailBase.text = BuildBaseInfoText(definition);
                m_DetailStatsDivider.gameObject.SetActive(true);
                m_DetailStatsTitle.gameObject.SetActive(true);
                m_DetailStats.text = BuildStatsText(definition);
                m_DetailDescriptionDivider.gameObject.SetActive(true);
                m_DetailDescription.text = definition.Description;
                m_DetailDescriptionTitle.gameObject.SetActive(true);
            }
            else
            {
                m_DetailIcon.sprite = definition.Icon;
                m_DetailIcon.color = definition.Icon != null
                    ? new Color(0.1f, 0.1f, 0.1f, 0.85f)
                    : Color.clear;
                m_DetailName.text = UnknownName;
                m_DetailMeta.text = $"{RarityPalette.GetDisplayName(definition.Rarity)} · 未获得";
                // 未获得条目没有基础信息与参数可言：两节标题一起收起，把线索直接放在正文位置，
                // 避免出现"标题下面一片空白"的排版。
                m_DetailBaseTitle.gameObject.SetActive(false);
                m_DetailBase.text = "在战局中搜刮容器、从商人处购买，\n或完成任务奖励，拿到手的那一刻\n这条记录就会点亮。";
                m_DetailStatsDivider.gameObject.SetActive(false);
                m_DetailStatsTitle.gameObject.SetActive(false);
                m_DetailStats.text = string.Empty;
                m_DetailDescriptionDivider.gameObject.SetActive(false);
                m_DetailDescription.text = string.Empty;
                m_DetailDescriptionTitle.gameObject.SetActive(false);
            }

            // 未获得时也给描边一点稀有度颜色，保留"还差哪一件"的线索。
            m_DetailName.color = discovered ? UiPalette.Ink : UiPalette.InkDisabled;
            m_DetailMeta.color = discovered ? rarityColor : UiPalette.InkSoft;
        }

        /// <summary>基础信息：占格、重量、价值。两类物品都必须显示这三项。</summary>
        private static string BuildBaseInfoText(ItemDefinition definition)
        {
            var size = definition.GridSize;
            return $"占格 {size.Width} × {size.Height}\n"
                + $"重量 {definition.WeightKg:0.##} 千克\n"
                + $"价值 {definition.BaseValue:N0} 金币";
        }

        /// <summary>
        /// 参数区的文字：按物品的行为类型生成。
        /// </summary>
        /// <remarks>取值全部来自定义资产，界面不硬编码任何数值；
        /// 新增一件同行为类型的物品时这里不需要改动。</remarks>
        private static string BuildStatsText(ItemDefinition definition)
        {
            var builder = new StringBuilder(160);

            var weapon = definition.WeaponStats;
            if (weapon != null)
            {
                builder.AppendLine($"伤害 {weapon.BaseDamage:0.#} ｜ 射速 {weapon.RoundsPerMinute:0} 发/分");
                builder.AppendLine($"弹匣 {weapon.MagazineCapacity} 发 ｜ 口径 {weapon.CaliberId}");
                builder.AppendLine($"射程 {weapon.RangeMeters:0.#} 米 ｜ 换弹 {weapon.ReloadSeconds:0.#} 秒");
                builder.Append($"射击模式 {ResolveFireModeName(weapon)}");
                return builder.ToString();
            }

            var ammo = definition.AmmoStats;
            if (ammo != null)
            {
                builder.AppendLine($"口径 {ammo.CaliberId}");
                builder.Append($"穿透力 {ammo.Penetration:0.##}");
                return builder.ToString();
            }

            var armor = definition.ArmorStats;
            if (armor != null)
            {
                builder.AppendLine($"防护等级 {armor.ProtectionLevel} 级");
                builder.AppendLine($"耐久 {armor.MaxDurability:0.#}");
                builder.Append($"损耗系数 {armor.WearFactor:0.##}");
                return builder.ToString();
            }

            if (definition.Behavior is MedicalBehavior medical)
            {
                builder.AppendLine($"回复 {medical.HealAmount} 点生命");
                builder.Append($"使用时长 {medical.UseDurationSeconds:0.#} 秒");
                return builder.ToString();
            }

            if (definition.IsContainer)
            {
                var inner = definition.ContainerGridSize;
                builder.Append($"内部空间 {inner.Width} × {inner.Height} 格");
                return builder.ToString();
            }

            builder.Append("这件物品没有特殊参数，主要价值来自出售。");
            return builder.ToString();
        }

        /// <summary>射击模式的中文名，含连发发数。</summary>
        private static string ResolveFireModeName(IWeaponStats weapon)
        {
            switch (weapon.FireMode)
            {
                case WeaponFireMode.Burst:
                    return $"连发（{weapon.BurstCount} 发）";
                case WeaponFireMode.Auto:
                    return "全自动";
                default:
                    return "单发";
            }
        }

        /// <summary>物品类别的中文名。</summary>
        private static string ResolveCategoryName(ItemCategory category)
        {
            switch (category)
            {
                case ItemCategory.Weapon:
                    return "武器";
                case ItemCategory.Ammo:
                    return "弹药";
                case ItemCategory.Medical:
                    return "医疗";
                case ItemCategory.Helmet:
                    return "头盔";
                case ItemCategory.BodyArmor:
                    return "护甲";
                case ItemCategory.Backpack:
                    return "背包";
                case ItemCategory.Key:
                    return "钥匙";
                case ItemCategory.Quest:
                    return "任务物品";
                default:
                    return "杂物";
            }
        }
    }
}
