namespace RaidDemo.Meta
{
    /// <summary>
    /// 局外进度的"钱包"部分：金币的增减与两种写入方式。
    /// </summary>
    /// <remarks>
    /// <para>从主文件拆出来的原因：主文件同时管仓库、随身物、任务与图鉴，
    /// 加上 P5 的服务器同步之后顶到了 400 行上限；而"钱怎么变"本来就是可以单独读的一小块。</para>
    ///
    /// <para><b>三条规则：</b></para>
    /// <list type="number">
    /// <item><description>单机：交易与任务奖励用 <see cref="AddMoney"/> / <see cref="TrySpend"/>，
    /// 它们只改数值、<b>不广播</b>——一次交易可能同时改余额与物品，广播要等两件事都完成，
    /// 否则自动存档可能写进"钱已扣、物品还没放进去"的中间状态；</description></item>
    /// <item><description>读档：<see cref="RestoreMoney"/> 直接写入，同样不广播；</description></item>
    /// <item><description>联机（P5）：金币的权威在服务器，客户端用
    /// <see cref="ApplyServerMoney"/> 贴服务器下发的余额，并广播一次让界面刷新。</description></item>
    /// </list>
    /// </remarks>
    public sealed partial class MetaProgress
    {
        /// <summary>增加金币。</summary>
        /// <remarks>只改数值，不广播变化（见类型注释里的第 1 条规则）。</remarks>
        public void AddMoney(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            Money += amount;
        }

        /// <summary>尝试扣除金币。余额不足时不做任何改变。</summary>
        public bool TrySpend(int amount)
        {
            if (amount <= 0 || Money < amount)
            {
                return false;
            }

            Money -= amount;
            return true;
        }

        /// <summary>
        /// 存档还原专用：直接写入余额，不触发"变化"事件。
        /// </summary>
        internal void RestoreMoney(int money)
        {
            Money = money > 0 ? money : 0;
        }

        /// <summary>
        /// 采用服务器下发的金币（P5：联机时金币由服务端结算并落库）。
        /// </summary>
        /// <param name="money">服务器权威余额。</param>
        /// <remarks>
        /// <para>只改数值并广播一次变化，不做任何本地经济判断：联机时"我买不买得起"
        /// 由服务器回答，客户端这一份只是用来画界面的镜像。</para>
        ///
        /// <para>与 <see cref="RestoreMoney"/> 的区别是可见性与用途：那个是读档内部用的
        /// （不广播），这个是联机下行通道调用的公开入口。</para>
        /// </remarks>
        public void ApplyServerMoney(int money)
        {
            var clamped = money > 0 ? money : 0;
            if (Money == clamped)
            {
                return;
            }

            Money = clamped;
            NotifyChanged();
        }
    }
}
