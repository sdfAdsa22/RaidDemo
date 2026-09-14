using System;
using RaidDemo.Inventory;
using RaidDemo.Presentation;
using RaidDemo.Shared;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 战局装配根的联机验收辅助：状态上报与自动行走。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单独一个文件：</b>这些代码只服务于"无头验收"（<c>-autowalk</c>），
    /// 与联机的实际功能（预测、对账、插值）关注点不同——前者是"怎么看到发生了什么"，
    /// 后者是"发生了什么"。混在一起会让联机主文件同时承担两件事。</para>
    ///
    /// <para><b>它们为什么重要：</b>M9 的两次排障都卡在"没有线索"上——NGO 在发行版里不打日志、
    /// 本项目的 Verbose 在发行版里被编译掉（M9-P-10 / P-12），
    /// 于是"连不上"与"连上了但没数据"从外部看起来一模一样。
    /// 这两段代码就是那条始终存在的线索：状态每 2 秒打一次，自动行走让被测路径真的被走到（P-14）。</para>
    /// </remarks>
    public sealed partial class SceneBootstrap
    {
        /// <summary>
        /// 周期性上报联机状态。
        /// </summary>
        /// <remarks>
        /// 只在 <c>-autowalk</c>（验收模式）下输出。连接没建起来时，界面与日志都没有任何线索，
        /// 这行状态是唯一能区分"没连上"与"连上了但没数据"的证据。
        /// </remarks>
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

        /// <summary>
        /// 自动行走的输入：沿圆周匀速转向，同时改变朝向。
        /// </summary>
        /// <remarks>
        /// <para>刻意让朝向与移动方向一起转：远端视图的朝向、走路动画与播放倍率都会被验证到，
        /// 只朝一个方向走的话，朝向同步出了问题也看不出来。</para>
        ///
        /// <para><b>优先瞄敌人：</b>验收必须走到"玩家打死敌人"这条分支，而瞄队友永远走不到——
        /// 上一轮验收里敌人一枪没挨，就是因为这个。</para>
        /// </remarks>
        private void UpdateAutoWalkInput()
        {
            if (m_InputCollector == null)
            {
                return;
            }

            var angle = Time.timeAsDouble * 1.2d;
            var x = (float)Math.Cos(angle);
            var y = (float)Math.Sin(angle);

            // 验收的最后一段是撤离：让客户端在打了一阵之后自己往撤离点走，
            // 否则"撤离读秒由服务器裁定"这条路径永远走不到（出生点离撤离点三十多米）。
            if (TryResolveExtractionAim(out var extractAim))
            {
                extractAim = ApplyUnstick(extractAim);
                m_InputCollector.ScriptedLookDirection = extractAim;
                m_InputCollector.ScriptedMoveDirection = new Vector2(extractAim.X, extractAim.Y);
                TryIssueAutoLootCommand();
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
        }

        /// <summary>验收模式下自动拾取的间隔（秒）。</summary>
        private const float AutoLootIntervalSeconds = 4f;

        /// <summary>验收模式下开始前往撤离点的时间（秒）。</summary>
        private const float AutoExtractSeekSeconds = 25f;

        private ExtractionZoneMarker[] m_ExtractionMarkerCache;

        /// <summary>卡住判定的采样间隔（秒）。</summary>
        private const float StuckCheckInterval = 1.5f;

        /// <summary>判定"没动"的位移阈值（米）。</summary>
        private const float StuckDistanceMeters = 0.5f;

        /// <summary>侧移持续时间（秒）。</summary>
        private const float UnstickDuration = 1.2f;

        private Vector2 m_LastStuckPosition;
        private double m_NextStuckCheck;
        private double m_UnstickUntil;
        private float m_UnstickSign = 1f;

        /// <summary>
        /// 卡住就往侧向偏一下。
        /// </summary>
        /// <param name="desired">本来想走的方向。</param>
        /// <returns>实际要发的方向。</returns>
        /// <remarks>
        /// <para><b>为什么需要它：</b>验收模式是"朝目标直线走"，撞到集装箱或墙就再也过不去——
        /// 实测两名客户端一起卡在 (2.1, 9.3) 一分钟，于是"撤离读秒"这条路径永远走不到。</para>
        ///
        /// <para>做法是最朴素的：每 1.5 秒检查一次位移，几乎没动就把方向转 90°（左右交替）1.2 秒。
        /// 它不是寻路，只是让验收脚本能从障碍上"蹭"过去；真正的寻路在 AI 那一侧，
        /// 客户端联机时不跑 AI、也没有导航网格。</para>
        /// </remarks>
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

        /// <summary>
        /// 找最近的撤离点方向（验收模式用）。
        /// </summary>
        /// <param name="aim">指向撤离点的单位方向。</param>
        /// <returns>到了该去撤离的时间、且地图上有撤离点时返回 true。</returns>
        /// <remarks>
        /// 用的是场景里的 <see cref="ExtractionZoneMarker"/>——与服务器判定读的是同一批对象，
        /// 因此"客户端往哪走"与"服务器认不认"不会错位。
        /// </remarks>
        private bool TryResolveExtractionAim(out Vector2F aim)
        {
            aim = Vector2F.Zero;

            if (!m_AutoWalk || Time.timeSinceLevelLoad < AutoExtractSeekSeconds || m_PlayerMotor == null)
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

        /// <summary>
        /// 验收模式下周期性从场景容器里拿一件东西。
        /// </summary>
        /// <remarks>
        /// <para>它走的是与玩家拖拽**完全相同**的命令路径（<c>InventoryMoveIntent</c> →
        /// 命令路由 → 联机时上行给服务器），因此验证的是真实链路，而不是一条测试专用旁路。</para>
        ///
        /// <para>两名客户端都会去拿同一个箱子：这正是"先到先得"要验收的场景——
        /// 先发的那个拿到物品，后发的那个拿到失败，而两边的箱子内容最终一致。</para>
        /// </remarks>
        private void TryIssueAutoLootCommand()
        {
            if (m_CommandRouter == null || m_ContainerRegistry == null
                || Time.timeAsDouble < m_NextAutoLootTime)
            {
                return;
            }

            m_NextAutoLootTime = Time.timeAsDouble + AutoLootIntervalSeconds;

            // 固定拿 100 号容器（地图上的第一个箱子）：两个人抢同一个箱子才有意义。
            const int lootContainerId = ContainerIds.SceneBase;
            if (!m_ContainerRegistry.TryGetGrid(lootContainerId, out var grid) || grid.Items.Count == 0)
            {
                return;
            }

            var item = grid.Items[0];
            if (!grid.TryGetOrigin(item, out var origin))
            {
                return;
            }

            var result = m_CommandRouter.Dispatch(new InventoryMoveIntent(
                m_LocalPlayerId,
                lootContainerId,
                ContainerIds.PlayerBackpack,
                origin.X,
                origin.Y,
                0,
                0));

            Debug.Log($"[联机] 自动拾取：容器 {lootContainerId} 第 ({origin.X},{origin.Y}) 格 → 背包，结果={result.Success}");
        }

        /// <summary>验收模式下瞄准的最大距离（米）。比步枪射程略小，留一点余量。</summary>
        private const float AutoAimEnemyRangeMeters = 11f;

        /// <summary>
        /// 找射程内最近的远端敌人作为瞄准方向。
        /// </summary>
        /// <remarks>
        /// 只认还没阵亡的敌人：尸体不可被命中（服务器已经关掉了它的碰撞体），
        /// 继续朝它开枪会让验收一直停在"开了枪但没命中"。
        /// </remarks>
        /// <param name="aim">找到的瞄准方向。</param>
        /// <returns>射程内存在存活敌人时返回 true。</returns>
        private bool TryResolveEnemyAim(out Vector2F aim)
        {
            aim = Vector2F.Zero;

            if (m_RemoteEnemyViews.Count == 0 || m_PlayerMotor == null)
            {
                return false;
            }

            var self = m_PlayerMotor.SimulatedPosition;
            var bestSqrDistance = AutoAimEnemyRangeMeters * AutoAimEnemyRangeMeters;
            var found = false;

            foreach (var pair in m_RemoteEnemyViews)
            {
                var view = pair.Value;
                if (view == null || view.IsDestroyed)
                {
                    continue;
                }

                var position = view.transform.position;
                var dx = position.x - self.x;
                var dz = position.z - self.y;
                var sqrDistance = (dx * dx) + (dz * dz);
                if (sqrDistance >= bestSqrDistance || sqrDistance < 0.01f)
                {
                    continue;
                }

                bestSqrDistance = sqrDistance;
                aim = new Vector2F(dx, dz).Normalized;
                found = true;
            }

            return found;
        }

        /// <summary>验收模式下把朝向对准最近的远端玩家；没有目标时保持原方向。</summary>
        private Vector2F ResolveAutoAimDirection(Vector2F fallback)
        {
            if (m_RemoteViews.Count == 0 || m_PlayerMotor == null)
            {
                return fallback;
            }

            var self = m_PlayerMotor.SimulatedPosition;
            var bestSqrDistance = float.MaxValue;
            var aim = fallback;

            foreach (var view in m_RemoteViews.Values)
            {
                if (view == null)
                {
                    continue;
                }

                var position = view.transform.position;
                var dx = position.x - self.x;
                var dz = position.z - self.y;
                var sqrDistance = (dx * dx) + (dz * dz);
                if (sqrDistance >= bestSqrDistance || sqrDistance < 0.01f)
                {
                    continue;
                }

                bestSqrDistance = sqrDistance;
                aim = new Vector2F(dx, dz).Normalized;
            }

            return aim;
        }
    }
}
