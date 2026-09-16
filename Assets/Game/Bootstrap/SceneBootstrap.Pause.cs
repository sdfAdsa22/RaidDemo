using RaidDemo.UI;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 战局里"按 Esc 打开暂停菜单"的判定部分。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单独成文件：</b>这段判断串了三个条件（有没有界面挡着、Esc 是否已被界面消费、
    /// 当前是不是结算界面），混在 <c>Update</c> 的大流程里既难读，也容易在改动时漏掉其中一个
    /// ——而漏掉的后果是"关掉背包的同时弹出暂停菜单"这种一眼看不出原因的怪现象。
    /// 主文件也已接近单文件 400 行上限。</para>
    /// </remarks>
    public sealed partial class SceneBootstrap
    {
        /// <summary>
        /// 战局里处理"Esc 打开暂停菜单"。
        /// </summary>
        /// <param name="flow">流程控制器。</param>
        /// <param name="escapePressed">本帧是否按了 Esc。</param>
        /// <param name="inventoryOpen">背包是否打开。</param>
        /// <param name="resultOpen">结算界面是否打开。</param>
        /// <returns>本帧的 Esc 已经被这里处理（调用方应直接 return）时返回 true。</returns>
        /// <remarks>
        /// 背包/结算会先吃掉 Esc；<see cref="UiEscapeGuard"/> 则负责挡住"界面刚在同一帧关掉自己"
        /// 那种竞态——否则一次按键会既关界面又弹暂停菜单。
        /// </remarks>
        private bool TryHandleRaidPauseInput(
            RaidFlowController flow,
            bool escapePressed,
            bool inventoryOpen,
            bool resultOpen)
        {
            if (inventoryOpen || resultOpen || !escapePressed)
            {
                return false;
            }

            // 背包可能在同一帧用 Esc 关掉了自己（并标记为已消费）：
            // 那样这一帧就只应该关界面，不该再弹出暂停菜单。
            if (UiEscapeGuard.WasConsumedThisFrame)
            {
                return true;
            }

            flow.ShowPauseMenu(warnAbandon: true);
            return true;
        }
    }
}
