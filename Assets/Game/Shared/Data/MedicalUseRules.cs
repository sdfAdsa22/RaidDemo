namespace RaidDemo.Shared
{
    /// <summary>
    /// 医疗使用的生命判据：还差多少血才算"可以用药"。
    /// </summary>
    /// <remarks>
    /// <para><b>它防的是哪一类缺陷：</b>M13-21。联机客户端的"能不能用药"原来只读本地战斗世界，
    /// 而联机下根本没有本地战斗世界，于是恒等于"满血"——按 H 与右键使用都是静默返回，
    /// 读条不出现、请求不上行，服务器日志里也没有任何记录。</para>
    ///
    /// <para><b>优先级：</b>服务器权威镜像 &gt; 本地战斗世界 &gt; 未知（按满血处理，不消耗物品）。
    /// 第三档是刻意的：宁可"点了没反应"也不能在没有权威数据时凭空扣掉一件医疗品。</para>
    /// </remarks>
    public static class MedicalUseRules
    {
        /// <summary>
        /// 计算还差多少生命才满血。
        /// </summary>
        /// <param name="maxHealth">生命上限。</param>
        /// <param name="hasAuthoritativeHealth">是否已经拿到服务器权威生命值（联机时为 true）。</param>
        /// <param name="authoritativeHealth">服务器权威生命值。</param>
        /// <param name="hasLocalHealth">是否有本地战斗世界状态（单机时为 true）。</param>
        /// <param name="localHealth">本地战斗世界的生命值。</param>
        /// <returns>缺口；已满或没有任何可用数据时返回 0。</returns>
        public static float MissingHealth(
            float maxHealth,
            bool hasAuthoritativeHealth,
            float authoritativeHealth,
            bool hasLocalHealth,
            float localHealth)
        {
            var health = hasAuthoritativeHealth
                ? authoritativeHealth
                : (hasLocalHealth ? localHealth : maxHealth);

            var missing = maxHealth - health;
            return missing > 0f ? missing : 0f;
        }
    }
}
