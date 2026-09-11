namespace RaidDemo.Raid
{
    /// <summary>
    /// 一局战局的节奏参数。
    /// </summary>
    /// <remarks>
    /// 这些数值决定「一局玩起来是什么感觉」，因此都做成可在 Inspector 里改的字段，
    /// 由装配层从场景读取后填进来。逻辑层只负责判断合法性，不负责决定具体数值。
    /// </remarks>
    public sealed class RaidSettings
    {
        /// <summary>一局的总时长（秒）。默认 8 分钟。</summary>
        public float RaidDurationSeconds { get; set; } = 480f;

        /// <summary>撤离读秒时长（秒）。进入撤离区后必须站满这么久。</summary>
        public float ExtractionDurationSeconds { get; set; } = 10f;

        /// <summary>
        /// 校验参数是否合法。
        /// </summary>
        /// <returns>合法返回 null，否则返回中文问题描述。</returns>
        /// <remarks>
        /// 返回字符串而不是抛异常或返回 bool：装配层需要把具体原因写进日志，
        /// 而「时长非法」这种信息对排查问题毫无帮助——必须说清是哪一个数值不合法。
        /// </remarks>
        public string Validate()
        {
            if (RaidDurationSeconds <= 0f)
            {
                return $"战局时长必须大于 0，当前为 {RaidDurationSeconds}。";
            }

            if (ExtractionDurationSeconds <= 0f)
            {
                return $"撤离读秒时长必须大于 0，当前为 {ExtractionDurationSeconds}。";
            }

            if (ExtractionDurationSeconds > RaidDurationSeconds)
            {
                return $"撤离读秒时长（{ExtractionDurationSeconds}）不能超过战局时长（{RaidDurationSeconds}），"
                    + "否则永远不可能完成撤离。";
            }

            return null;
        }
    }
}
