using System;
using RaidDemo.Inventory;
using RaidDemo.Data;
using RaidDemo.Presentation;
using RaidDemo.Shared;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>战局装配根的联机验收辅助：状态上报、自动行走与验收脚本操作。</summary>
    /// <remarks>
    /// <para>只服务于无头验收（<c>-autowalk</c>）：状态每 2 秒打一次，替身操作让被测路径真的被走到
    /// （M9-P-10 / P-12 / P-14 的教训都在这份文件里落成了代码）。</para>
    /// </remarks>
    public sealed partial class SceneBootstrap
    {
        /// <summary>周期性上报联机状态（连不上与连上但没数据，只有这行能区分）。</summary>
        private void ReportNetworkState()
        {
            if (!m_AutoWalk || Time.timeAsDouble < m_NextNetworkReportTime)
            {
                return;
            }

            m_NextNetworkReportTime = Time.timeAsDouble + 2d;

            if (m_NetworkClient == null)
            {
                Debug.Log("[联机] 状态：NetworkManager 尚未创建。");
                return;
            }

            var transport = m_NetworkClient.NetworkConfig != null
                ? m_NetworkClient.NetworkConfig.NetworkTransport as UnityTransport
                : null;

            var target = "无传输层";
            if (transport != null)
            {
                var data = transport.ConnectionData;
                target = $"{data.Address}:{data.Port}（本地绑定端口 {data.ClientBindPort}）";
            }

            Debug.Log(
                $"[联机] 状态：IsClient={m_NetworkClient.IsClient} 监听中={m_NetworkClient.IsListening} " +
                $"已连接={m_NetworkClient.IsConnectedClient} 本机Id={m_NetworkClient.LocalClientId} 目标={target}");
        }

        /// <summary>自动行走的输入（朝向与移动方向一起变，同时优先瞄敌人）。</summary>
        private void UpdateAutoWalkInput()
        {
            if (m_InputCollector == null)
            {
                return;
            }

            var angle = Time.timeAsDouble * 1.2d;
            var x = (float)Math.Cos(angle);
            var y = (float)Math.Sin(angle);

            // 队友倒地优先于一切：验收要走到"扶起队友"这条路径，
            // 而它只在有队友躺着的时候才会发生。
            if (TryResolveRescueAim(out var rescueAim, out var withinRange))
            {
                m_InputCollector.ScriptedLookDirection = rescueAim;
                m_InputCollector.ScriptedMoveDirection = withinRange
                    ? Vector2.zero
                    : new Vector2(rescueAim.X, rescueAim.Y);

                // 走到身边就按住救援键（走的是与玩家按 F 完全相同的上报路径）。
                m_InputCollector.ScriptedWantsToRevive = withinRange;
                return;
            }

            m_InputCollector.ScriptedWantsToRevive = false;

            // 只救人的验收模式：不搜刮、不撤离、不主动交火，站在原地等队友倒地。
            // 它让"扶起队友"这条路径不再依赖脚本能不能走到某个位置（见排障手册 P-22/R-1）。
            if (ClientMode.IsActive && ClientMode.Options != null && ClientMode.Options.RescueOnly)
            {
                m_InputCollector.ScriptedLookDirection = new Vector2F(x, y);
                m_InputCollector.ScriptedMoveDirection = Vector2.zero;
                return;
            }

            // 验收的最后一段是撤离：让客户端在打了一阵之后自己往撤离点走，
            // 否则"撤离读秒由服务器裁定"这条路径永远走不到（出生点离撤离点三十多米）。
            if (TryResolveExtractionAim(out var extractAim))
            {
                extractAim = ApplyUnstick(extractAim);
                m_InputCollector.ScriptedLookDirection = extractAim;
                m_InputCollector.ScriptedMoveDirection = new Vector2(extractAim.X, extractAim.Y);
                ReportExtractionProgress();
                TryIssueAutoLootCommand();
                TryEquipmentSelfTest();
                return;
            }

            // 先找射程内最近的敌人；找不到再退回"盯着最近的队友"。
            // 打中谁不重要，重要的是"打中"这件事必须真的发生：只有命中了，
            // 才会走到"伤害结算 → 广播 → 客户端更新"这条链路上。
            var hasEnemyAim = TryResolveEnemyAim(out var enemyAim);
            m_AutoWalkHasEnemyTarget = hasEnemyAim;
            var aim = hasEnemyAim
                ? enemyAim
                : ResolveAutoAimDirection(new Vector2F(x, y));
            m_InputCollector.ScriptedLookDirection = aim;

            // 有队友时朝他走过去，而不是各绕各的圈：
            // 两人相距几十米时连射程都够不着，验收永远等不到命中。
            var hasTarget = !aim.Equals(new Vector2F(x, y));
            m_InputCollector.ScriptedMoveDirection = hasTarget
                ? new Vector2(aim.X, aim.Y)
                : new Vector2(x, y);

            TryIssueAutoLootCommand();
            TryEquipmentSelfTest();
        }

        private bool m_EquipmentSelfTestDone;

        /// <summary>验收模式下把捡到的武器装备上（走真实命令路径）。</summary>
        private void TryEquipmentSelfTest()
        {
            if (!m_AutoWalk || m_EquipmentSelfTestDone || Time.realtimeSinceStartup < 20f
                || m_CommandRouter == null || m_ContainerRegistry == null || m_Loadout == null)
            {
                return;
            }

            if (!m_ContainerRegistry.TryGetGrid(ContainerIds.PlayerBackpack, out var backpack))
            {
                Debug.Log("[联机] 装备自检：找不到背包容器，跳过。");
                m_EquipmentSelfTestDone = true;
                return;
            }

            // 找背包里的第一件武器（自动拾取会把箱子里的枪捡进来）：
            // 装备它 = 一步"真实玩家会做"的操作，同时能把服务器的手持武器真正换掉。
            var items = backpack.Items;
            if (!m_EquipmentSelfTestLogged)
            {
                m_EquipmentSelfTestLogged = true;
                Debug.Log($"[联机] 装备自检：背包 {items.Count} 件，开始找武器。");
            }

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item?.Definition?.WeaponStats == null)
                {
                    continue;
                }

                if (backpack.TryGetOrigin(item, out var origin))
                {
                    m_EquipmentSelfTestDone = true;
                    var equip = m_CommandRouter.Dispatch(new InventoryEquipIntent(
                        LocalNetworkPlayerId,
                        ContainerIds.PlayerBackpack,
                        origin.X,
                        origin.Y,
                        EquipmentSlot.PrimaryWeapon));
                    Debug.Log($"[联机] 装备自检：装备背包里的武器，结果={equip.Success}（{equip.Code}）");
                }

                return;
            }

            // 等太久还没捡到枪就放弃这次自检，避免每帧重试。
            if (Time.realtimeSinceStartup > 60f)
            {
                m_EquipmentSelfTestDone = true;
                Debug.Log("[联机] 装备自检：背包里没有武器，跳过。");
            }
        }

        /// <summary>验收模式下自动拾取的间隔（秒）。</summary>
        private const float AutoLootIntervalSeconds = 4f;

    }
}
