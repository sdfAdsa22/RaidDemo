using RaidDemo.Raid;
using RaidDemo.Shared;
using RaidDemo.Combat;

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
            UpdateRaidHud(playerAlive);
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

        /// <summary>玩家当前是否存活。</summary>
        private bool IsPlayerAlive()
        {
            if (m_CombatWorld == null || m_PlayerCombatantId == 0)
            {
                return false;
            }

            return m_CombatWorld.TryGet(m_PlayerCombatantId, out var state) && state.IsAlive;
        }

        /// <summary>统计玩家造成的击杀。</summary>
        /// <remarks>
        /// 只认「攻击方是玩家」且「本次确实打死」的伤害事件。
        /// 让 AI 之间互相误伤也计入击杀会让结算数字变得不可信。
        /// </remarks>
        private void OnKillCounted(DamageAppliedEvent evt)
        {
            if (!evt.WasKilled || evt.AttackerId != m_PlayerCombatantId)
            {
                return;
            }

            m_RaidSession?.NotifyKill(evt.AttackerId);
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

            RaidFlowController.Ensure().ShowResult(result);
        }
    }
}
