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
                TryIssueAutoLootCommand();
                TryEquipmentSelfTest();
                return;
            }

            // 先找射程内最近的敌人；找不到再退回"盯着最近的队友"。
            // 打中谁不重要，重要的是"打中"这件事必须真的发生：只有命中了，
            // 才会走到"伤害结算 → 广播 → 客户端更新"这条链路上。
            var aim = TryResolveEnemyAim(out var enemyAim)
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

        /// <summary>验收模式下开始前往撤离点的时间（秒）。</summary>
        private const float AutoExtractSeekSeconds = 25f;

        private ExtractionZoneMarker[] m_ExtractionMarkerCache;

        private const float StuckCheckInterval = 1.5f;
        private const float StuckDistanceMeters = 0.5f;
        private const float UnstickDuration = 1.2f;

        private Vector2 m_LastStuckPosition;
        private double m_NextStuckCheck;
        private double m_UnstickUntil;
        private float m_UnstickSign = 1f;

        /// <summary>卡住就往侧向偏一下（验收脚本的"蹭过去"，不是寻路；细节见排障手册 P-22）。</summary>
        private Vector2F ApplyUnstick(Vector2F desired)
        {
            if (m_PlayerMotor == null)
            {
                return desired;
            }

            var now = Time.timeAsDouble;
            var position = m_PlayerMotor.SimulatedPosition;

            if (now >= m_NextStuckCheck)
            {
                // SimulatedPosition 是 Unity 的 Vector2（x=世界 x，y=世界 z）。
                var dx = position.x - m_LastStuckPosition.x;
                var dy = position.y - m_LastStuckPosition.y;
                var moved = Mathf.Sqrt((dx * dx) + (dy * dy));

                if (moved < StuckDistanceMeters)
                {
                    m_UnstickUntil = now + UnstickDuration;
                    // 左右交替，避免每次都往同一侧蹭、结果在同一处反复卡住。
                    m_UnstickSign = -m_UnstickSign;
                }

                m_LastStuckPosition = position;
                m_NextStuckCheck = now + StuckCheckInterval;
            }

            if (now >= m_UnstickUntil)
            {
                return desired;
            }

            // 顺时针 / 逆时针转 90°。
            return m_UnstickSign > 0f
                ? new Vector2F(-desired.Y, desired.X)
                : new Vector2F(desired.Y, -desired.X);
        }

        /// <summary>找最近的撤离点方向（验收模式用；与服务器判定读的是同一批标记）。</summary>
        private bool TryResolveExtractionAim(out Vector2F aim)
        {
            aim = Vector2F.Zero;

            if (!m_AutoWalk || Time.realtimeSinceStartup < AutoExtractSeekSeconds || m_PlayerMotor == null)
            {
                return false;
            }

            if (m_ExtractionMarkerCache == null)
            {
                // 显式写 UnityEngine.Object：本文件引入了 System，Object 会有二义性。
                m_ExtractionMarkerCache =
                    UnityEngine.Object.FindObjectsByType<ExtractionZoneMarker>(FindObjectsSortMode.None);
            }

            if (m_ExtractionMarkerCache.Length == 0)
            {
                return false;
            }

            var self = m_PlayerMotor.SimulatedPosition;
            var bestSqrDistance = float.MaxValue;
            var found = false;

            // 先去北侧上坡道的入口：谷底到塬面（撤离点所在）之间隔着 6 米高的环状坡体，
            // 直线走会被坡面挡住（实测两名客户端一起卡在 (2.1, 9.3)）。
            // 通道位置来自地图剖面：北侧坡道在 x=0、向外为 +Z 方向。
            var waypointX = 0f;
            var waypointZ = 24f;
            var toWaypointX = waypointX - self.x;
            var toWaypointZ = waypointZ - self.y;
            if ((toWaypointX * toWaypointX) + (toWaypointZ * toWaypointZ) > 4f)
            {
                aim = new Vector2F(toWaypointX, toWaypointZ).Normalized;
                return true;
            }

            for (var i = 0; i < m_ExtractionMarkerCache.Length; i++)
            {
                var marker = m_ExtractionMarkerCache[i];
                if (marker == null)
                {
                    continue;
                }

                var position = marker.transform.position;
                var dx = position.x - self.x;
                var dz = position.z - self.y;
                var sqrDistance = (dx * dx) + (dz * dz);
                if (sqrDistance >= bestSqrDistance || sqrDistance < 0.25f)
                {
                    continue;
                }

                bestSqrDistance = sqrDistance;
                aim = new Vector2F(dx, dz).Normalized;
                found = true;
            }

            return found;
        }

        private double m_NextAutoLootTime;

        /// <summary>验收模式下周期性从场景容器里拿一件东西（走真实命令路径）。</summary>
        private void TryIssueAutoLootCommand()
        {
            if (m_CommandRouter == null || m_ContainerRegistry == null
                || Time.timeAsDouble < m_NextAutoLootTime)
            {
                return;
            }

            m_NextAutoLootTime = Time.timeAsDouble + AutoLootIntervalSeconds;

            // 轮着拿不同的箱子：固定拿第一个的话，箱子里恰好没有武器时
            // "装备"那条路径就永远走不到（实测踩过）。两个人仍然会抢同一个——
            // 他们的轮转节奏相近，先到先得照样能被验证。
            var lootContainerId = ContainerIds.SceneContainer(m_AutoLootContainerOffset % AutoLootContainerCount);
            m_AutoLootContainerOffset++;

            if (!m_ContainerRegistry.TryGetGrid(lootContainerId, out var grid) || grid.Items.Count == 0)
            {
                return;
            }

            // 优先拿武器：随手拿的第一件多半是弹药或药品，把口袋塞满就再也放不下枪了。
            var item = FindWeaponIn(grid) ?? grid.Items[0];
            if (!grid.TryGetOrigin(item, out var origin))
            {
                return;
            }

            var result = m_CommandRouter.Dispatch(new InventoryMoveIntent(
                LocalNetworkPlayerId,
                lootContainerId,
                ContainerIds.PlayerBackpack,
                origin.X,
                origin.Y,
                0,
                0));

            Debug.Log($"[联机] 自动拾取：容器 {lootContainerId} 第 ({origin.X},{origin.Y}) 格 → 背包，结果={result.Success}");
        }

        /// <summary>在网格里找第一件武器；没有返回 null。</summary>
        private static ItemInstance FindWeaponIn(InventoryGrid grid)
        {
            var items = grid.Items;
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i]?.Definition?.WeaponStats != null)
                {
                    return items[i];
                }
            }

            return null;
        }

        private const int AutoLootContainerCount = 15;
        private int m_AutoLootContainerOffset;
        private bool m_EquipmentSelfTestLogged;

    }
}
