using UnityEngine;

namespace RaidDemo.UI
{
    /// <summary>
    /// 「本帧的 Esc 已经被某个界面用掉了」的共享标记。
    /// </summary>
    /// <remarks>
    /// <para><b>它解决什么：</b>装配层（安全屋 / 战局）每帧都会读一次 Esc，用来决定
    /// "没有界面挡着时打开暂停菜单"。而各个界面自己也读 Esc 来关闭自己。
    /// 两处读的是同一个按键、同一个帧——只要界面的 <c>Update</c> 恰好排在装配层前面，
    /// 就会变成<b>按一次 Esc，界面关了，同时暂停菜单弹出来</b>
    /// （负责人反馈的现象：操作说明按 Esc 返回后出现暂停菜单）。</para>
    ///
    /// <para><b>为什么不用"谁先执行"来解决：</b>脚本执行顺序在 Unity 里是未定义的，
    /// 靠它等于把正确性寄托在运气上；而且改一次目录结构就可能变。</para>
    ///
    /// <para><b>为什么是"帧标记"而不是"队列"：</b>Esc 的语义是"处理一次就结束"，
    /// 界面消费掉之后，同一帧里装配层只需要知道"这帧别再拿它开暂停"。
    /// 用 <see cref="Time.frameCount"/> 比较即可，不需要任何队列或事件。</para>
    /// </remarks>
    public static class UiEscapeGuard
    {
        /// <summary>最近一次被消费的帧号；-1 表示从未消费过。</summary>
        private static int s_ConsumedFrame = -1;

        /// <summary>标记"本帧的 Esc 已被界面消费"。</summary>
        public static void Consume()
        {
            s_ConsumedFrame = Time.frameCount;
        }

        /// <summary>本帧的 Esc 是否已被消费。</summary>
        public static bool WasConsumedThisFrame
        {
            get { return s_ConsumedFrame == Time.frameCount; }
        }

        /// <summary>
        /// 清空标记。
        /// </summary>
        /// <remarks>
        /// 只给测试用：EditMode 下 <see cref="Time.frameCount"/> 不推进，
        /// 用"消费过"的旧状态会让用例互相污染。
        /// </remarks>
        public static void ResetForTests()
        {
            s_ConsumedFrame = -1;
        }
    }
}
