using RaidDemo.Meta;
using RaidDemo.Shared;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 安全屋的交易自检（<c>-autotrade</c>，P5.5 验收辅助）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要它：</b>验证"商店在联机下可用"需要有人在安全屋里真的买一件、
    /// 卖一件、接一个任务。无人值守的验收脚本没法操作鼠标，而"只测到登录成功"
    /// 覆盖不到 P5.5 的任何一条新链路。它按固定节拍派发三个**与玩家点击完全相同**的命令，
    /// 由服务器执行并把结果写进日志——脚本据此判定。</para>
    ///
    /// <para><b>它不走后门：</b>用的就是 <c>CommandRouter</c>（联机时已被换成上行版本），
    /// 因此它验证的是真实链路，不是"绕过 UI 的特例"。</para>
    ///
    /// <para><b>为什么出售坐标是 (0,0)：</b>验收用全新的存档目录，共享仓库是空的，
    /// 刚买进的 30 发弹药会被 <c>AutoPlace</c> 放到左上角第一个格。脚本与自检都依赖
    /// "买的是 1×1 的弹药"这一条。</para>
    /// </remarks>
    public sealed partial class SafeHouseBootstrap
    {
        /// <summary>每个交易动作之间的间隔（秒）：给服务器执行与结果下行留时间。</summary>
        private const float AutoTradeStepSeconds = 3f;

        /// <summary>自检购买的商品（1×1、一组 30 发、价格便宜）。</summary>
        private const string AutoTradeBuyItemId = "ammo.9x19.standard";

        /// <summary>自检购买的数量（与货架分组一致）。</summary>
        private const int AutoTradeBuyCount = 30;

        /// <summary>自检出售的格子（空仓库时购买物落在这里）。</summary>
        private const int AutoTradeSellCellX = 0;

        private const int AutoTradeSellCellY = 0;

        /// <summary>自检阶段；-1 表示尚未开始。</summary>
        private int m_AutoTradeStage = -1;

        /// <summary>下一个动作的触发时刻（未缩放时间）。</summary>
        private float m_AutoTradeNextAt;

        /// <summary>
        /// 每帧推进交易自检（由联机安全屋的 Tick 调用）。
        /// </summary>
        /// <remarks>
        /// 触发条件比"联机"更严：必须是**已经在房间里**且商人链路已挂上——
        /// 早于这个时点派发的命令会撞上"尚未连接到服务器"，脚本会误判成失败。
        /// </remarks>
        private void TickAutoTrade()
        {
            var options = ClientMode.Options;
            if (options == null || !options.AutoTrade || !IsMultiplayerSafeHouse)
            {
                return;
            }

            var session = MultiplayerClientSession.Current;
            if (session == null || !session.SelfInRoom || m_MerchantLink == null)
            {
                return;
            }

            var now = Time.unscaledTime;

            if (m_AutoTradeStage < 0)
            {
                m_AutoTradeStage = 0;
                m_AutoTradeNextAt = now + AutoTradeStepSeconds;
                Debug.Log("[联机] -autotrade：交易自检已排程（购买 → 出售 → 接任务）。");
                return;
            }

            if (now < m_AutoTradeNextAt)
            {
                return;
            }

            switch (m_AutoTradeStage)
            {
                case 0:
                {
                    var result = m_CommandRouter.Dispatch(
                        new BuyItemIntent(0, AutoTradeBuyItemId, AutoTradeBuyCount));
                    LogAutoTradeStep("购买", result);
                    break;
                }

                case 1:
                {
                    var result = m_CommandRouter.Dispatch(new SellItemIntent(
                        0, m_StashContainerId, AutoTradeSellCellX, AutoTradeSellCellY));
                    LogAutoTradeStep("出售", result);
                    break;
                }

                case 2:
                {
                    var result = m_CommandRouter.Dispatch(
                        new QuestAcceptIntent(0, QuestCatalog.FirstExtractId));
                    LogAutoTradeStep("接取任务", result);
                    break;
                }

                default:
                    m_AutoTradeStage = int.MaxValue;
                    Debug.Log("[联机] -autotrade：交易自检动作已全部派发。");
                    return;
            }

            m_AutoTradeStage++;
            m_AutoTradeNextAt = now + AutoTradeStepSeconds;
        }

        /// <summary>把一步自检的结果写进日志（脚本按这些行判定）。</summary>
        private static void LogAutoTradeStep(string label, in CommandResult result)
        {
            Debug.Log(result.Success
                ? $"[联机] -autotrade：{label}已上行（结果由服务器回发）。"
                : $"[联机] -autotrade：{label}失败：{result.Message}");
        }
    }
}
