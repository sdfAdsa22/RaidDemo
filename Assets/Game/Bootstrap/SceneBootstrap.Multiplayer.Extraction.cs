using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Presentation;
using RaidDemo.Shared;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 联机验收脚本的"撤离导航"部分：选择撤离点、卡住脱困与进度日志。
    /// </summary>
    /// <remarks>
    /// 从 SceneBootstrap.Multiplayer.Diagnostics.cs 拆出（该文件在加入"同层优先撤离点"
    /// 与多级脱困后超过 400 行上限）。这里的逻辑只服务验收机器人（-autowalk），
    /// 不参与真实玩家的任何判定。
    /// </remarks>
    public sealed partial class SceneBootstrap
    {
        /// <summary>验收模式下开始前往撤离点的时间（秒）。</summary>
        private const float AutoExtractSeekSeconds = 25f;

        private ExtractionZoneMarker[] m_ExtractionMarkerCache;

        /// <summary>
        /// "同一层"的高度容差（米）：与自己在同一高度带内的撤离点优先。
        /// </summary>
        /// <remarks>
        /// 谷底与塬面之间隔着 6 米的环状坡体，"跨层直达"是走不通的。
        /// 优先同层之后：谷底出生的机器人会直接走南谷口（谷底出口，全程平地），
        /// 而不是硬啃北侧坡道——U-87 复验里它正是卡在爬坡路线上。
        /// </remarks>
        private const float ExtractSameLevelToleranceMeters = 2f;

        /// <summary>撤离进度日志的间隔（秒）。</summary>
        private const float ExtractReportIntervalSeconds = 5f;

        private double m_NextExtractReportTime;

        private const float StuckCheckInterval = 1.5f;
        private const float StuckDistanceMeters = 0.5f;
        private const float UnstickDuration = 1.2f;

        private Vector2 m_LastStuckPosition;
        private double m_NextStuckCheck;
        private double m_UnstickUntil;
        private float m_UnstickSign = 1f;

        /// <summary>连续判定"卡住"的次数（移动恢复时清零）。</summary>
        private int m_StuckStreak;

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
                    m_StuckStreak++;
                    m_UnstickUntil = now + UnstickDuration;
                    // 左右交替，避免每次都往同一侧蹭、结果在同一处反复卡住。
                    m_UnstickSign = -m_UnstickSign;
                }
                else
                {
                    m_StuckStreak = 0;
                }

                m_LastStuckPosition = position;
                m_NextStuckCheck = now + StuckCheckInterval;
            }

            if (now >= m_UnstickUntil)
            {
                return desired;
            }

            // 每连续卡住三次里挑一次"倒退"：侧蹭脱不开的直角与台阶，先离开接触面再绕。
            // （U-87 复验里机器人卡在坡道路径上，只有 90° 侧蹭时会横着贴住坡体走不动。）
            if (m_StuckStreak > 0 && m_StuckStreak % 3 == 0)
            {
                return new Vector2F(-desired.X, -desired.Y);
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
            var selfHeight = m_PlayerMotor.transform.position.y;
            var bestSameLevelSqr = float.MaxValue;
            var bestSameLevelAim = Vector2F.Zero;
            var hasSameLevel = false;
            var bestAnySqr = float.MaxValue;
            var bestAnyAim = Vector2F.Zero;
            var hasAny = false;

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
                if (sqrDistance < 0.25f)
                {
                    // 已经站在撤离区里：继续朝标记中心挪一点点，剩下的交给服务器读秒。
                    var insideAim = new Vector2F(dx, dz).Normalized;
                    aim = insideAim.IsNearlyZero ? Vector2F.Right : insideAim;
                    return true;
                }

                var direction = new Vector2F(dx, dz).Normalized;

                if (sqrDistance < bestAnySqr)
                {
                    bestAnySqr = sqrDistance;
                    bestAnyAim = direction;
                    hasAny = true;
                }

                if (Mathf.Abs(position.y - selfHeight) <= ExtractSameLevelToleranceMeters
                    && sqrDistance < bestSameLevelSqr)
                {
                    bestSameLevelSqr = sqrDistance;
                    bestSameLevelAim = direction;
                    hasSameLevel = true;
                }
            }

            // 同层优先：谷底直接去南谷口（平地直达），塬面上直接去三个坡顶之一；
            // 只有"所有撤离点都在别层"时才退回全局最近（爬坡兜底）。
            if (hasSameLevel)
            {
                aim = bestSameLevelAim;
                return true;
            }

            if (hasAny)
            {
                aim = bestAnyAim;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 验收机器人：每 5 秒报一次"正在前往撤离点"与自己的位置。
        /// </summary>
        /// <remarks>
        /// 没有这行日志时，"机器人卡住"与"机器人还在路上"从外部看起来完全一样
        /// （U-87 复验的教训）——位置与高度的低频读数让两种状态一眼可辨。
        /// </remarks>
        private void ReportExtractionProgress()
        {
            var now = Time.timeAsDouble;
            if (now < m_NextExtractReportTime || m_PlayerMotor == null)
            {
                return;
            }

            m_NextExtractReportTime = now + ExtractReportIntervalSeconds;

            var position = m_PlayerMotor.SimulatedPosition;
            m_Session?.Log.Info(
                $"[联机] 验收机器人：前往撤离点，当前位置 ({position.x:F1}, {position.y:F1})，"
                + $"高度 {m_PlayerMotor.transform.position.y:F1} 米。");
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
