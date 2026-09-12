using System;
using RaidDemo.Data;

namespace RaidDemo.Meta
{
    /// <summary>
    /// 商人定价规则：买价、卖价与差价都集中在这里。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么定价不写在物品定义里：</b>物品定义回答"这东西值多少"，
    /// 商人规则回答"商人愿意出多少、卖多少"。两者分开之后，
    /// 调整经济曲线只需要改本类的一个比率，不必逐个物品改资产。</para>
    ///
    /// <para>规则如下：商人卖给玩家时按基础价值收费；商人收购时只付基础价值的
    /// <see cref="SellRate"/>。买卖之间存在差价，因此不会出现"买进来再卖出去刷钱"。
    /// 收购价按整堆结算后再四舍五入，避免 30 发子弹出现小数余额。</para>
    /// </remarks>
    public static class TraderPricing
    {
        /// <summary>
        /// 商人收购价占基础价值的比例。
        /// </summary>
        /// <remarks>
        /// 0.75 是当前经济曲线的支点：一套中档装备约 22,500，
        /// 一次成功撤离卖掉落约 12,000 ~ 25,000，因此"死一套要打两局才回本"成立。
        /// 如果实机测试觉得太紧或太松，只需要改这一个数字。
        /// </remarks>
        public const float SellRate = 0.75f;

        /// <summary>计算玩家向商人购买时的总价。</summary>
        /// <param name="definition">物品定义，不允许为 null。</param>
        /// <param name="count">数量，必须大于 0。</param>
        /// <returns>总价（金币）。</returns>
        public static int GetBuyPrice(IItemDefinition definition, int count = 1)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "数量必须大于 0。");
            }

            return definition.BaseValue * count;
        }

        /// <summary>计算商人收购玩家物品时的总价。</summary>
        /// <param name="definition">物品定义，不允许为 null。</param>
        /// <param name="count">数量，必须大于 0。</param>
        /// <returns>总价（金币）。</returns>
        public static int GetSellPrice(IItemDefinition definition, int count = 1)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "数量必须大于 0。");
            }

            var raw = definition.BaseValue * (double)count * SellRate;
            return (int)Math.Round(raw, MidpointRounding.AwayFromZero);
        }
    }
}
