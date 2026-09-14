using System.Collections.Generic;
using RaidDemo.UI;

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
    }
}
