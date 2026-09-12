using RaidDemo.Raid;
using RaidDemo.Shared;
using RaidDemo.Combat;
using RaidDemo.Data;
using RaidDemo.Inventory;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// SceneBootstrap 的战局主循环部分：推进计时、处理搜刮交互、刷新战局界面。
    /// </summary>
    /// <remarks>
    /// <para>与 <c>SceneBootstrap.Raid.cs</c>（装配）分开，是因为两者的职责不同：
    /// 那边回答「一局开始时要把哪些东西建起来」，这边回答「每帧要推进什么」。</para>
    /// </remarks>
    public sealed partial class SceneBootstrap
    {
        /// <summary>推进战局闭环。每帧由主循环调用一次。</summary>
        /// <param name="deltaTime">时间步长（秒）。</param>
        /// <param name="inputBlocked">玩家输入是否被屏蔽（背包打开或已阵亡）。</param>
        private void UpdateRaid(float deltaTime, bool inputBlocked)
        {
            if (m_RaidSession == null)
            {
                return;
            }

            var playerAlive = IsPlayerAlive();
            var playerPosition = m_MoveHandler != null
                ? m_MoveHandler.Simulator.State.Position
                : Vector2F.Zero;

            if (m_RaidSession.IsActive)
            {
                m_RaidSession.Tick(deltaTime);
                m_ExtractionTracker.Tick(deltaTime, playerPosition, playerAlive);
            }

            UpdateLootSearch(deltaTime, playerPosition, playerAlive, inputBlocked);
            UpdateItemUse(deltaTime, playerAlive, inputBlocked);
            UpdateRaidHud(playerAlive);
        }

        /// <summary>
        /// 主菜单阶段的「出击准备」：把仓库面板接到背包界面上，并让菜单让位。
        /// </summary>
        /// <remarks>
        /// <para>复用背包界面而不是新做一个准备界面：它已经有装备槽、背包、弹药挂与拖拽规则，
        /// 而「仓库」对界面来说只是一个容器。唯一要做的就是把它绑到右侧那块面板上——
        /// 战局里那块显示战利品，主菜单里显示仓库。</para>
        ///
        /// <para>菜单必须让开：它的遮罩层级（300）高于背包（200），
        /// 不让位的话玩家会看到「按了 Tab 但什么都没发生」。</para>
        /// </remarks>
        private void UpdatePreparation()
        {
            if (m_InventoryScreen == null || m_StashContainerId == 0)
            {
                return;
            }

            var open = m_InventoryScreen.IsOpen;
            if (open && m_InventoryScreen.LootContainerId != m_StashContainerId)
            {
                // 把右侧面板绑到仓库；标题直接用「仓库」，与战局里的「战利品：XXX」区分开。
                m_InventoryScreen.OpenLootContainer(m_StashContainerId, "仓库");
            }

            RaidFlowController.Ensure().SetMenuVisible(!open);
        }

        /// <summary>
        /// 处理医疗品的使用：按键、读条、完成后的回血与消耗。
        /// </summary>
        /// <remarks>
        /// <para>读条期间**允许移动**：被打到只剩一丝血时还要站住包扎，
        /// 会把这条功能从「救命」变成「自杀」。它唯一的打断条件是受伤。</para>
        ///
        /// <para>读条中再按一次 H 表示主动取消。</para>
        /// </remarks>
        private void UpdateItemUse(float deltaTime, bool playerAlive, bool inputBlocked)
        {
            if (m_ItemUse == null)
            {
                return;
            }

            if (!inputBlocked && playerAlive && m_InputCollector != null && m_InputCollector.ReadUseMedicalIntent())
            {
                if (m_ItemUse.IsUsing)
                {
                    m_ItemUse.Cancel("主动取消");
                }
                else
                {
                    TryBeginMedicalUse();
                }
            }

            // 受伤标记在这一帧用完即清：它表示「刚刚这一帧挨了打」，
            // 留到下一帧就会把「读完的瞬间受伤」误判成打断。
            m_ItemUse.Tick(deltaTime, playerAlive, m_PlayerDamagedThisFrame);
            m_PlayerDamagedThisFrame = false;
        }

        /// <summary>从随身携带物里挑一件医疗品开始使用。</summary>
        /// <remarks>
        /// 挑选策略：**优先用刚好够补满缺口的最小那件**，都不够时用回血最多的那件。
        /// 这样常态下省下医疗包留给硬仗，急救时才自动切到大件。
        /// </remarks>
        private void TryBeginMedicalUse()
        {
            var missing = ResolveMissingHealth();
            if (missing <= 0f)
            {
                return;
            }

            var best = SelectMedicalItem(missing, out var bestHeal, out var bestDuration);
            if (best != null)
            {
                m_ItemUse.TryBegin(best, best.Definition.DisplayName, bestDuration);
            }
        }

        /// <summary>取物品定义上的医疗行为，取不到返回 null。</summary>
        /// <remarks>
        /// 走物品目录而不是 <c>ItemInstance.Definition</c>：后者是 IItemDefinition 接口，
        /// 而接口刻意不认识 Behavior——行为是 Unity 资产类型，
        /// 把它挂进接口会让数据层反向依赖内容层。目录返回的是具体定义，认识它。
        /// </remarks>
        private MedicalBehavior ResolveMedical(ItemInstance item)
        {
            if (item == null || m_ItemCatalog == null)
            {
                return null;
            }

            var definition = m_ItemCatalog.Get(item.Definition.Id);
            return definition != null ? definition.Behavior as MedicalBehavior : null;
        }

        /// <summary>还差多少生命才满血。已经满血或读不到状态时返回 0。</summary>
        private float ResolveMissingHealth()
        {
            if (m_CombatWorld == null || !m_CombatWorld.TryGet(m_PlayerCombatantId, out var state))
            {
                return 0f;
            }

            var missing = PlayerMaxHealth - state.Health;
            return missing > 0f ? missing : 0f;
        }

        /// <summary>从背包与弹药挂里挑一件最合适的医疗品。</summary>
        private ItemInstance SelectMedicalItem(float missingHealth, out int healAmount, out float durationSeconds)
        {
            healAmount = 0;
            durationSeconds = 0f;

            ItemInstance smallestSufficient = null;
            var smallestSurplus = float.MaxValue;
            var smallestHeal = 0;
            var smallestDuration = 0f;
            ItemInstance largestFallback = null;
            var largestHeal = 0;
            var largestDuration = 0f;

            for (var pass = 0; pass < 2; pass++)
            {
                var grid = pass == 0 ? m_Loadout?.Backpack : m_Loadout?.AmmoPouch;
                if (grid == null)
                {
                    continue;
                }

                var items = grid.Items;
                for (var i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    var medical = ResolveMedical(item);
                    if (medical == null)
                    {
                        continue;
                    }

                    if (medical.HealAmount >= missingHealth)
                    {
                        var surplus = medical.HealAmount - missingHealth;
                        if (surplus < smallestSurplus)
                        {
                            smallestSurplus = surplus;
                            smallestSufficient = item;
                            smallestHeal = medical.HealAmount;
                            smallestDuration = medical.UseDurationSeconds;
                        }

                        continue;
                    }

                    if (medical.HealAmount > largestHeal)
                    {
                        largestHeal = medical.HealAmount;
                        largestDuration = medical.UseDurationSeconds;
                        largestFallback = item;
                    }
                }
            }

            var picked = smallestSufficient ?? largestFallback;
            if (picked == null)
            {
                return null;
            }

            healAmount = smallestSufficient != null ? smallestHeal : largestHeal;
            durationSeconds = smallestSufficient != null ? smallestDuration : largestDuration;
            return picked;
        }

        /// <summary>处理搜刮交互：寻找附近容器、接收交互键、推进读条。</summary>
        private void UpdateLootSearch(
            float deltaTime,
            Vector2F playerPosition,
            bool playerAlive,
            bool inputBlocked)
        {
            m_NearbyLoot = playerAlive
                ? FindNearestLootContainer(playerPosition)
                : null;

            // 读条期间玩家的移动输入会立刻打断搜刮：这是「搜刮有成本」的核心，
            // 也是防止玩家边跑边搜的唯一手段。
            var wantsToMove = !inputBlocked && !m_PendingMoveIntent.IsNearlyZero;
            var inventoryOpen = m_InventoryScreen != null && m_InventoryScreen.IsOpen;

            if (!inputBlocked
                && playerAlive
                && m_NearbyLoot != null
                && !inventoryOpen
                && m_InputCollector != null
                && m_InputCollector.ReadInteractIntent())
            {
                m_LootSearch.TryBegin(m_NearbyLoot.ContainerId, playerPosition);
            }

            m_LootSearch.Tick(deltaTime, playerPosition, playerAlive, wantsToMove);
        }

        /// <summary>找出距离玩家最近、且在交互范围内的容器。</summary>
        /// <param name="playerPosition">玩家位置。</param>
        /// <returns>最近的容器；都不在范围内时返回 null。</returns>
        private LootContainerRuntime FindNearestLootContainer(Vector2F playerPosition)
        {
            if (m_LootContainers == null)
            {
                return null;
            }

            LootContainerRuntime nearest = null;
            var nearestSquared = float.MaxValue;
            for (var i = 0; i < m_LootContainers.Count; i++)
            {
                var container = m_LootContainers[i];
                var dx = container.WorldPosition.x - playerPosition.X;
                var dz = container.WorldPosition.z - playerPosition.Y;
                var squared = (dx * dx) + (dz * dz);
                if (squared > container.RangeMeters * container.RangeMeters)
                {
                    continue;
                }

                if (squared < nearestSquared)
                {
                    nearestSquared = squared;
                    nearest = container;
                }
            }

            return nearest;
        }

        /// <summary>搜刮读条完成：打开对应容器。</summary>
        private void OnLootSearchCompleted(LootSearchCompletedEvent evt)
        {
            if (m_InventoryScreen == null)
            {
                return;
            }

            var container = FindLootContainer(evt.ContainerId);
            var displayName = container != null ? container.Definition.DisplayName : "战利品";
            m_InventoryScreen.OpenLootContainer(evt.ContainerId, displayName);
        }

        /// <summary>撤离读秒完成：本局以成功结束。</summary>
        /// <remarks>
        /// 这一根接线是整条闭环的最后一环，漏掉它的症状非常隐蔽：
        /// 读秒条会正常走满，玩家站在撤离点里什么也没发生，而日志里没有任何报错。
        /// </remarks>
        private void OnExtractionCompleted(ExtractionCompletedEvent evt)
        {
            m_RaidSession?.NotifyExtracted();
        }

        /// <summary>战局结束：生成结算数据并交给流程控制器展示。</summary>
        private void OnRaidEnded(RaidEndedEvent evt)
        {
            if (m_RaidResultShown)
            {
                return;
            }

            m_RaidResultShown = true;

            // 结算时强制关掉背包：否则面板会夹在结算界面与战局世界之间，
            // 而且它解锁的光标状态会与结算界面打架。
            if (m_InventoryScreen != null)
            {
                m_InventoryScreen.Close();
            }

            // 同时清掉受击红屏：结算会把世界冻结，红屏若还留在画面上，
            // 整张结算界面都会被染成红色。
            if (m_DamageFlash != null)
            {
                m_DamageFlash.ClearImmediate();
            }

            // 战局界面与战斗界面一并收起：结算画面要给出一个干净的结论，
            // 而不是让未走完的倒计时继续在标题上方跳。
            if (m_RaidHud != null)
            {
                m_RaidHud.gameObject.SetActive(false);
            }

            if (m_CombatHud != null)
            {
                m_CombatHud.SetVisible(false);
            }

            var result = RaidResult.Create(
                evt.Outcome,
                evt.Kills,
                evt.ElapsedSeconds,
                m_BroughtInValue,
                m_Loadout);

            // 局外结算：这一局的东西到底留不留得下来。
            // 撤离成功 → 全部搬进仓库；阵亡或超时 → 随身携带物**直接丢弃**（仓库不受影响）。
            // 这是「装备真的会丢」的唯一实现点，也是 M6 批次 2 的核心。
            var progress = RaidFlowController.Ensure().Progress;
            if (evt.Outcome == RaidOutcome.Extracted)
            {
                var deposited = progress.DepositLoadoutToStash();
                if (progress.LastDepositFailures > 0)
                {
                    Debug.LogWarning(
                        $"[RaidDemo] 仓库放不下，{progress.LastDepositFailures} 件物品未能入库。"
                        + "请先清理仓库再出击。");
                }

                Debug.Log($"[RaidDemo] 撤离成功，{deposited} 件物品已入库。");
            }
            else
            {
                // 阵亡与超时同档处理：随身的东西没了，仓库绝对安全。
                progress.ClearLoadout();
            }

            RaidFlowController.Ensure().ShowResult(result);
        }
    }
}
