using System;
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
