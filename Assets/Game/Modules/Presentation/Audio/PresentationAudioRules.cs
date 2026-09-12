using System;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 武器在表现层的类别：决定用哪套枪声、枪口火焰与武器模型。
    /// </summary>
    /// <remarks>
    /// 刻意不叫「武器类型」：战斗层的武器参数（射程、射速、弹匣）与表现无关，
    /// 这里只回答「看起来、听起来像长枪还是短枪」这一个问题。
    /// </remarks>
    public enum WeaponPresentationKind
    {
        /// <summary>短枪：手枪类。占 2 格及以下。</summary>
        Pistol = 0,

        /// <summary>长枪：步枪类。占 3 格及以上。</summary>
        Rifle = 1,
    }

    /// <summary>脚步的路面类别。</summary>
    public enum FootstepSurface
    {
        /// <summary>草地：谷底与塬面。</summary>
        Grass = 0,

        /// <summary>硬质地面：水泥、金属平台、木栈板。</summary>
        Hard = 1,
    }

    /// <summary>
    /// 音效播放的纯逻辑规则。
    /// </summary>
    /// <remarks>
    /// <para>把「什么时候该响、该响哪一条」从 MonoBehaviour 里拆出来，
    /// 是为了让这部分能进 EditMode 单元测试——播放本身无法断言，
    /// 但触发规则可以，而触发规则恰恰是最容易出错的部分
    /// （重复播、该响时不响、跑起来脚步比子弹还密）。</para>
    /// </remarks>
    public static class AudioPlaybackRules
    {
        /// <summary>
        /// 同一个剪辑的最小重复间隔（秒）。
        /// </summary>
        /// <remarks>
        /// 全自动武器每秒十余发、霰弹一次命中多个目标时，同一帧会请求播放同一条音效
        /// 好几次。完全放开会让同一条波形叠加成失真的一团噪声（听感上"爆音"），
        /// 35 毫秒既保住了连射的节奏感，又把叠加限制在两次以内。
        /// </remarks>
        public const float DefaultMinRetriggerInterval = 0.035f;

        /// <summary>占几格才算长枪。与灰盒时代「3 格步枪比 2 格手枪长」的规则一致。</summary>
        private const int RifleGridWidth = 3;

        /// <summary>按武器占格宽度判定表现类别。</summary>
        /// <param name="gridWidth">武器在背包里占的格宽。</param>
        public static WeaponPresentationKind ResolveWeaponKind(int gridWidth)
        {
            return gridWidth >= RifleGridWidth
                ? WeaponPresentationKind.Rifle
                : WeaponPresentationKind.Pistol;
        }

        /// <summary>
        /// 判断一次播放请求是否应被节流丢弃。
        /// </summary>
        /// <param name="lastPlayedTime">上一次播放同一条剪辑的时间（秒，未播放过传负数）。</param>
        /// <param name="now">当前时间（秒）。</param>
        /// <param name="minInterval">最小间隔（秒）。</param>
        public static bool ShouldThrottle(float lastPlayedTime, float now, float minInterval)
        {
            return lastPlayedTime >= 0f && now - lastPlayedTime < minInterval;
        }

        /// <summary>
        /// 按脚下物体的材质名判定路面。
        /// </summary>
        /// <param name="materialName">地面渲染器的材质名，可为空。</param>
        /// <remarks>
        /// <para>用材质名而不是物理材质：物理材质在这个工程里几乎没有配（角色不是靠物理反弹运动的），
        /// 而渲染材质名是现成的，且与美术一致——把草地换成石地时脚步自然跟着变。</para>
        /// <para>判定失败一律回落到草地：草地是这个地图的默认地面，
        /// 落在默认值上比落在"听起来像走在铁板上"更不容易察觉。</para>
        /// <para>局限：靠文本匹配终究是权宜之计。真正的做法是给地面挂一个
        /// 「路面类型」组件，由它提供枚举。已记入待办 P 清单。</para>
        /// </remarks>
        public static FootstepSurface ResolveSurface(string materialName)
        {
            if (string.IsNullOrEmpty(materialName))
            {
                return FootstepSurface.Grass;
            }

            return materialName.IndexOf("grass", StringComparison.OrdinalIgnoreCase) >= 0
                ? FootstepSurface.Grass
                : FootstepSurface.Hard;
        }
    }

    /// <summary>
    /// 脚步节拍器：按「走过的距离」而不是「经过的时间」触发脚步。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么必须按距离：</b>按时间触发的话，走路与奔跑的脚步间隔一模一样，
    /// 奔跑听起来会像"原地快速踏步"；按距离触发时，速度越快脚步越密，
    /// 与画面里角色移动的距离天然一致。</para>
    /// <para>步幅随速度增长（跑起来一步迈得更远），因此高速时的密度不会失控；
    /// 另有 0.2 秒的硬下限兜底，避免被外力推飞时发出机关枪般的脚步声。</para>
    /// </remarks>
    public struct FootstepCadence
    {
        /// <summary>步行步幅（米）。成年人步幅约 0.75 米，卡通角色腿短，取 0.75 会显得碎步。</summary>
        private const float WalkStrideMeters = 0.95f;

        /// <summary>奔跑步幅（米）。</summary>
        private const float SprintStrideMeters = 1.55f;

        /// <summary>判定为奔跑的速度阈值（米/秒），与移动配置的奔跑档一致。</summary>
        private const float SprintSpeedThreshold = 3.5f;

        /// <summary>两次脚步之间的最小间隔（秒）。</summary>
        private const float MinStepIntervalSeconds = 0.2f;

        /// <summary>低于这个速度视为站定，累计距离清零。</summary>
        private const float IdleSpeedThreshold = 0.05f;

        private float m_AccumulatedMeters;
        private float m_SecondsSinceStep;
        private int m_StepIndex;

        /// <summary>已迈出的步数，供表现层轮换音效变体。</summary>
        public int StepIndex => m_StepIndex;

        /// <summary>
        /// 推进一次。返回 true 表示这一步该响了，调用方随即播放一个脚步音。
        /// </summary>
        /// <param name="speedMetersPerSecond">当前水平速度（米/秒）。</param>
        /// <param name="deltaTime">时间步长（秒）。</param>
        public bool Advance(float speedMetersPerSecond, float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return false;
            }

            m_SecondsSinceStep += deltaTime;

            if (speedMetersPerSecond <= IdleSpeedThreshold)
            {
                // 站定时清零：否则走一步停一下，再走时会立刻"补"出一步。
                m_AccumulatedMeters = 0f;
                return false;
            }

            m_AccumulatedMeters += speedMetersPerSecond * deltaTime;
            if (m_SecondsSinceStep < MinStepIntervalSeconds)
            {
                return false;
            }

            var stride = speedMetersPerSecond >= SprintSpeedThreshold
                ? SprintStrideMeters
                : WalkStrideMeters;
            if (m_AccumulatedMeters < stride)
            {
                return false;
            }

            m_AccumulatedMeters -= stride;
            m_SecondsSinceStep = 0f;
            m_StepIndex++;
            return true;
        }

        /// <summary>重置节拍（换场景、重生时调用）。</summary>
        public void Reset()
        {
            m_AccumulatedMeters = 0f;
            m_SecondsSinceStep = 0f;
        }
    }
}
