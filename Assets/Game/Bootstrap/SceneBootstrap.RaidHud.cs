using RaidDemo.Shared;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 战局 HUD 的刷新逻辑（从 <c>SceneBootstrap.Raid.cs</c> 拆出，控制单文件行数）。
    /// </summary>
    /// <remarks>
    /// <para>拆出来的原因：AR-03 要给"靠近撤离圈但还没进圈"加接近提示，
    /// 主文件的装配逻辑已经贴着 400 行上限；HUD 刷新的改动原因（界面文案/提示优先级）
    /// 与装配（一局开始时建什么）不同，分开后两边都还能一眼读完。</para>
    /// </remarks>
    public sealed partial class SceneBootstrap
    {
        /// <summary>靠近撤离点多少米内给出"踏入圆圈"提示（AR-03）。</summary>
        private const float ExtractionHintRangeMeters = 2.5f;

        /// <summary>把战局状态写进界面。</summary>
        private void UpdateRaidHud(bool playerAlive, Vector2F playerPosition)
        {
            if (m_RaidHud == null)
            {
                return;
            }

            m_RaidHud.SetTimer(m_RaidSession.RemainingSeconds, m_RaidSession.IsActive);
            m_RaidHud.SetKills(m_RaidSession.Kills);

            var searching = m_LootSearch != null && m_LootSearch.IsSearching;
            var searchingContainer = searching ? FindLootContainer(m_LootSearch.TargetContainerId) : null;
            m_RaidHud.SetSearchProgress(
                searching,
                m_LootSearch != null ? m_LootSearch.Progress01 : 0f,
                searchingContainer != null ? $"搜刮中：{searchingContainer.Definition.DisplayName}" : "搜刮中…");

            var usingItem = m_ItemUse != null && m_ItemUse.IsUsing;
            m_RaidHud.SetUseProgress(
                usingItem,
                usingItem ? m_ItemUse.Progress01 : 0f,
                usingItem ? $"使用中：{m_ItemUse.DisplayName}" : null);

            if (m_InventoryScreen != null && m_InventoryScreen.IsOpen)
            {
                // 背包打开时不需要交互提示与撤离提示：玩家的注意力在物品上，
                // 而且此时输入被屏蔽，提示会变成无法执行的噪声。
                m_RaidHud.SetInteractionPrompt(null);
                m_RaidHud.SetExtraction(false, null, 0f);
                return;
            }

            var zone = m_ExtractionTracker != null ? m_ExtractionTracker.ActiveZone : null;
            var extracting = zone != null && m_ExtractionTracker.ProgressSeconds > 0f;

            var prompt = !searching && playerAlive && m_NearbyLoot != null
                ? $"按 E 搜索 {m_NearbyLoot.Definition.DisplayName}"
                : null;

            // AR-03：站得离撤离圈很近但还没进判定圈时给一句明确提示，
            // 玩家据此知道要往哪挪，而不是怀疑读秒坏了。
            if (prompt == null && !extracting && playerAlive && m_ExtractionTracker != null)
            {
                var near = m_ExtractionTracker.FindNearest(playerPosition, ExtractionHintRangeMeters);
                if (near != null)
                {
                    prompt = $"踏入撤离圈开始读秒（{near.DisplayName}）";
                }
            }

            m_RaidHud.SetInteractionPrompt(prompt);
            m_RaidHud.SetExtraction(
                extracting,
                zone != null ? zone.DisplayName : string.Empty,
                m_ExtractionTracker != null ? m_ExtractionTracker.Progress01 : 0f);
        }
    }
}
