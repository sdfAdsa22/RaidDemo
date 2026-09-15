using RaidDemo.Combat;
using RaidDemo.Data;
using RaidDemo.Inventory;
using RaidDemo.Raid;
using RaidDemo.Shared;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// SceneBootstrap 的局外结算部分：这一局的东西到底留不留得下来。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么从战局主循环里拆出来：</b>主循环回答"每帧推进什么"，这里回答"结束那一刻写什么账"。
    /// 两种运行形态在这一步的分歧最大——单机要改本地进度（入库 / 丢装备 / 任务），
    /// 联机一个都不许改（那全是服务端的账）。挤在主循环里会让同一个文件同时承担
    /// "帧循环"与"经济规则"两件事（工程规范：单文件 ≤ 400 行）。</para>
    /// </remarks>
    public sealed partial class SceneBootstrap
    {
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

            // 联机时展示数据一律取服务器下发的那一份（RD-AUD-049）：
            // 击杀数、用时、带出价值都由服务器裁定，本地这几项只是镜像。
            var kills = evt.Kills;
            var elapsedSeconds = evt.ElapsedSeconds;
            int? authoritativeValue = null;
            var serverOutcome = LocalOutcome;
            if (IsMultiplayerProcess && serverOutcome.HasValue)
            {
                kills = serverOutcome.Value.Kills;
                elapsedSeconds = serverOutcome.Value.ElapsedSeconds;
                authoritativeValue = serverOutcome.Value.CarriedValue;
            }

            var result = RaidResult.Create(
                evt.Outcome,
                kills,
                elapsedSeconds,
                m_BroughtInValue,
                m_Loadout,
                authoritativeValue);

            var progress = RaidFlowController.Ensure().Progress;

            // 联机：局外结算全在服务器（仓库 / 金币 / 任务都是服务端那一份，见 P5 / P5.5）。
            // 在这里改本地进度等于"用联机的账覆盖单机的账"——玩家下一次单机开局会发现
            // 仓库被联机内容替换了（RD-AUD-043）。本地这份镜像由服务器的下发覆盖。
            var questSummary = IsMultiplayerProcess
                ? "联机模式：仓库、金币与任务进度由服务器结算。"
                : progress.Quests.BuildRaidSummary();

            if (IsMultiplayerProcess)
            {
                Debug.Log(
                    $"[联机] 本局按服务器结算：{result.Outcome}，带出价值 {result.ExtractedValue}，"
                    + $"击杀 {result.Kills}，用时 {result.ElapsedSeconds:F0} 秒（本地进度不做改动）。");
            }
            else if (evt.Outcome == RaidOutcome.Extracted)
            {
                // 局外结算（单机）：撤离成功 → 全部搬进仓库；阵亡或超时 → 随身携带物直接丢弃
                // （仓库不受影响）。这是「装备真的会丢」的唯一实现点，也是 M6 批次 2 的核心。
                var deposited = progress.DepositLoadoutToStash();
                if (progress.LastDepositFailures > 0)
                {
                    Debug.LogWarning(
                        $"[RaidDemo] 仓库放不下，{progress.LastDepositFailures} 件物品未能入库。"
                        + "它们仍留在你的背包 / 装备槽里——清理仓库后手动放入即可，不会被销毁。");
                }

                Debug.Log($"[RaidDemo] 撤离成功，{deposited} 件物品已入库。");

                // 只有活着带出来才算任务进度；阵亡与超时不会推进任何撤离类目标。
                progress.Quests.ReportExtraction(
                    result.ExtractedValue,
                    m_PlayerTookDamageThisRaid);
            }
            else
            {
                // 阵亡与超时同档处理：随身的东西没了，仓库绝对安全。
                progress.ClearLoadout();
            }

            var flow = RaidFlowController.Ensure();
            flow.MarkRaidFinished();
            flow.ShowResult(result, questSummary);
        }
    }
}
