using System.Collections.Generic;
using RaidDemo.UI;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 流程控制器的局域网发现部分：扫描、列出房间、把选定地址交给界面。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么与主流程分开：</b>发现是"锦上添花"的路径——它失败不影响联机
    /// （手输地址永远可用）。把它单独成文件，出问题时可以整块关掉而不动主流程。</para>
    ///
    /// <para>扫描是异步的：<c>StartLanScan</c> 发出广播探测，收包由每帧的 <c>TickScanWindow</c>
    /// 驱动，窗口结束时 <c>FinishLanScan</c> 把结果交给界面。</para>
    /// </remarks>
    public sealed partial class RaidFlowController
    {
        /// <summary>扫描结果缓存（界面按行展示）。</summary>
        private readonly List<MultiplayerMenuRoom> m_ScanRows = new List<MultiplayerMenuRoom>(4);

        /// <summary>扫描器（首次扫描时创建；进程内复用，避免每轮重开 socket）。</summary>
        private LanDiscoveryScanner m_LanScanner;

        /// <summary>启动底层扫描器。</summary>
        private void LanScanStarted()
        {
            m_LanScanner ??= new LanDiscoveryScanner(LanDiscoveryConstants.DefaultPort);
            m_LanScanner.StartScan();
        }

        /// <summary>取本轮扫描结果。</summary>
        private IReadOnlyList<LanRoomInfo> LanScanResults()
        {
            return m_LanScanner != null ? m_LanScanner.Results : (IReadOnlyList<LanRoomInfo>)System.Array.Empty<LanRoomInfo>();
        }

        /// <summary>收尾底层扫描器。</summary>
        private void LanScanFinished()
        {
            // 保留扫描器（下一轮直接复用 socket），这里只把最后一批回包收干净。
            m_LanScanner?.Poll();
        }

        /// <summary>开始一轮扫描。</summary>
        private void StartLanScan()
        {
            m_ScanRows.Clear();
            m_MultiplayerScreen.SetScanResults(m_ScanRows);
            LanScanStarted();
        }

        /// <summary>扫描窗口结束：把结果交给界面（没有结果时说清楚下一步）。</summary>
        private void FinishLanScan()
        {
            m_ScanRows.Clear();

            var discovered = LanScanResults();
            for (var i = 0; i < discovered.Count; i++)
            {
                var room = discovered[i];
                m_ScanRows.Add(new MultiplayerMenuRoom
                {
                    Address = room.Address,
                    Port = room.GamePort,
                    Description = room.Describe(),
                    Joinable = room.IsJoinable,
                });
            }

            LanScanFinished();

            m_MultiplayerScreen.SetScanResults(m_ScanRows);
            m_MultiplayerScreen.SetStatus(
                m_ScanRows.Count > 0
                    ? $"发现 {m_ScanRows.Count} 个局域网房间，点一行即可填入地址。"
                    : "没有发现局域网房间：可以手输地址（本机填 127.0.0.1）。",
                false);
        }
        /// <summary>点「局域网扫描」或点某一行扫描结果：把地址填进界面。</summary>
        private void OnMultiplayerJoinFound(string address, int port)
        {
            m_MultiplayerScreen.SetDefaults(null, null, address, port);
            m_MultiplayerScreen.SetStatus($"已选择 {address}:{port}，点「连接」加入。", false);
        }

        /// <summary>点「扫描」：开始一轮局域网发现。</summary>
        private void OnMultiplayerScan()
        {
            m_ScanDeadline = Time.realtimeSinceStartup + ScanWindowSeconds;
            m_MultiplayerScreen.SetScanning(true);
            m_MultiplayerScreen.SetStatus("正在搜索局域网房间…", false);
            StartLanScan();
        }

        /// <summary>扫描窗口结束：收起"扫描中"状态。</summary>
        private void TickScanWindow()
        {
            if (m_ScanDeadline < 0f)
            {
                return;
            }

            // 扫描期间每帧收包：回包可能随时到达（服务器是被动应答），
            // 只在窗口结束时收一次会把这 1.2 秒里的包堆在系统缓冲里，容易丢。
            m_LanScanner?.Poll();

            if (Time.realtimeSinceStartup < m_ScanDeadline)
            {
                return;
            }

            m_ScanDeadline = -1f;
            m_MultiplayerScreen.SetScanning(false);
            FinishLanScan();
        }

    }
}
