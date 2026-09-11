using RaidDemo.Shared;

namespace RaidDemo.AI
{
    /// <summary>
    /// AI 感知与行为的难度参数。
    /// </summary>
    /// <remarks>
    /// <para>刻意使用普通 C# 类而不是 ScriptableObject：本模块要能在
    /// EditMode 测试与无头服务端里直接构造（与 <c>PlayerMovementProfile</c> 同样的理由）。
    /// 表现层与启动层负责把资产里的数值填进这个对象。</para>
    ///
    /// <para>参数集中在一处的价值在调难度时才体现：如果视野角散落在三个状态类里，
    /// "让 AI 稍微近视一点"就要改三处，而且必然漏掉一处。</para>
    /// </remarks>
    public sealed class AIPerceptionProfile
    {
        // ── 视觉 ──────────────────────────────────────────────

        /// <summary>视野锥的全角（度）。110 度意味着左右各 55 度。</summary>
        public float ViewAngleDegrees = 110f;

        /// <summary>视距（米）。超出这个距离的目标即使无遮挡也看不见。</summary>
        public float ViewDistanceMeters = 22f;

        /// <summary>
        /// 眼睛相对脚底的高度（米）。
        /// </summary>
        /// <remarks>
        /// 灰盒阶段所有单位都在同一水平面上，因此眼高可以由"平面位置 + 这个常量"算出。
        /// M5 引入高低差之后，三维位置应改为由表现层提供（见 04_AI.md 的已知限制）。
        /// </remarks>
        public float EyeHeightMeters = 1.45f;

        // ── 听觉 ──────────────────────────────────────────────

        /// <summary>步行噪音的可听半径（米）。</summary>
        public float HearingRadiusWalk = 8f;

        /// <summary>奔跑噪音的可听半径（米）。这是玩家最容易踩到的一档。</summary>
        public float HearingRadiusSprint = 18f;

        /// <summary>超载移动的可听半径（米）。贪心的额外代价。</summary>
        public float HearingRadiusOverloaded = 26f;

        // ── 记忆 ──────────────────────────────────────────────

        /// <summary>目标消失后，记忆保留的时长（秒）。超过之后视为彻底跟丢。</summary>
        public float MemorySeconds = 6f;

        // ── 巡逻 ──────────────────────────────────────────────

        /// <summary>巡逻移动速度（米/秒）。刻意慢于玩家步行速度：巡逻是可以被绕开的。</summary>
        public float PatrolSpeed = 2f;

        /// <summary>到达路径点后原地观察的时长（秒）。</summary>
        public float WaypointScanSeconds = 2.5f;

        /// <summary>到达路径点后的扫描转向速度（度/秒）。</summary>
        public float ScanTurnSpeedDegreesPerSecond = 90f;

        // ── 调查 ──────────────────────────────────────────────

        /// <summary>调查移动速度（米/秒）。比巡逻快，因为目标明确。</summary>
        public float InvestigateSpeed = 3f;

        /// <summary>调查总时长上限（秒）。超时仍未发现目标就回到巡逻。</summary>
        public float InvestigateSeconds = 9f;

        // ── 交战 ──────────────────────────────────────────────

        /// <summary>交战时的移动速度（米/秒）。</summary>
        public float EngageSpeed = 3.2f;

        /// <summary>期望交战距离（米）。太近会后退，太远会推进。</summary>
        public float PreferredEngageDistance = 8f;

        /// <summary>交战距离容差（米）。落在期望距离正负这么宽以内就不再调整站位。</summary>
        public float EngageDistanceTolerance = 2.5f;

        /// <summary>发现目标后的反应时间（秒）。没有它，AI 会在出现的瞬间命中，体感像作弊。</summary>
        public float ReactionSeconds = 0.4f;

        /// <summary>开火所需的对准容差（度）。枪口偏得太多时不开火。</summary>
        public float AimToleranceDegrees = 7f;

        /// <summary>一轮连射的时长（秒）。</summary>
        public float FireBurstSeconds = 0.7f;

        /// <summary>两轮连射之间的停火时长（秒）。给玩家留出换位与还击的窗口。</summary>
        public float FirePauseSeconds = 0.8f;

        // ── 撤退 ──────────────────────────────────────────────

        /// <summary>生命低于最大值的这个比例时进入撤退。</summary>
        public float RetreatHealthRatio = 0.3f;

        /// <summary>撤退中治疗到的生命比例上限。治疗停止后重新交战。</summary>
        public float RetreatHealCeilingRatio = 0.6f;

        /// <summary>撤退中的每秒治疗量。</summary>
        public float HealPerSecondDuringRetreat = 6f;

        /// <summary>撤退速度（米/秒）。比交战速度更快。</summary>
        public float RetreatSpeed = 4.2f;

        /// <summary>撤退时与威胁保持的目标距离（米）。</summary>
        public float RetreatDistanceMeters = 12f;

        // ── 移动通用 ──────────────────────────────────────────

        /// <summary>转向速度（度/秒）。俯视角下朝向是核心反馈，因此取值偏快。</summary>
        public float TurnSpeedDegreesPerSecond = 360f;

        /// <summary>重新寻路的间隔（秒）。每帧寻路对灰盒地图是浪费，对更大地图则是灾难。</summary>
        public float RepathIntervalSeconds = 0.6f;

        /// <summary>与当前路径点的距离小于该值时视为到达，可以切到下一个点。</summary>
        public float WaypointReachDistance = 0.5f;

        /// <summary>
        /// 取某一档噪音的可听半径。
        /// </summary>
        /// <param name="tier">噪音档位。</param>
        /// <returns>可听半径（米）。静音返回 0。</returns>
        public float HearingRadiusFor(MovementNoiseTier tier)
        {
            switch (tier)
            {
                case MovementNoiseTier.Walk:
                    return HearingRadiusWalk;
                case MovementNoiseTier.Sprint:
                    return HearingRadiusSprint;
                case MovementNoiseTier.Overloaded:
                    return HearingRadiusOverloaded;
                default:
                    return 0f;
            }
        }

        /// <summary>
        /// 目标是否落在视野锥内（只算角度与距离，不含遮挡）。
        /// </summary>
        /// <param name="observerPosition">观察者位置。</param>
        /// <param name="facing">观察者朝向（单位向量）。</param>
        /// <param name="targetPosition">目标位置。</param>
        public bool IsInsideViewCone(Vector2F observerPosition, Vector2F facing, Vector2F targetPosition)
        {
            if (ViewDistanceMeters <= 0f || ViewAngleDegrees <= 0f)
            {
                return false;
            }

            var toTarget = targetPosition - observerPosition;
            var distance = toTarget.Magnitude;
            if (distance > ViewDistanceMeters || distance < 1e-4f)
            {
                // 距离恰好为 0 时没有方向可言，视为看得见，避免贴脸时反而丢失目标。
                return distance < 1e-4f;
            }

            var direction = facing.IsNearlyZero ? Vector2F.Right : facing.Normalized;
            var cosine = Vector2F.Dot(direction, toTarget / distance);

            // 用余弦比较而不是算角度：省一次反三角函数，且这里只需要快慢。
            var halfAngleRadians = (ViewAngleDegrees * 0.5f) * (System.MathF.PI / 180f);
            return cosine >= System.MathF.Cos(halfAngleRadians);
        }

        /// <summary>
        /// 校验参数是否处于合理范围，用于启动阶段尽早发现配置错误。
        /// </summary>
        /// <returns>校验通过返回 null，否则返回问题描述。</returns>
        public string Validate()
        {
            if (ViewAngleDegrees <= 0f || ViewAngleDegrees > 360f)
            {
                return "ViewAngleDegrees 必须落在 0 到 360 之间。";
            }

            if (ViewDistanceMeters <= 0f)
            {
                return "ViewDistanceMeters 必须大于 0。";
            }

            if (HearingRadiusWalk < 0f || HearingRadiusSprint < HearingRadiusWalk
                || HearingRadiusOverloaded < HearingRadiusSprint)
            {
                return "听觉半径必须满足 步行 ≤ 奔跑 ≤ 超载，且都非负。";
            }

            if (MemorySeconds <= ReactionSeconds)
            {
                return "MemorySeconds 必须大于 ReactionSeconds，否则 AI 会在反应完成前就忘记目标。";
            }

            if (RetreatHealthRatio <= 0f || RetreatHealthRatio >= 1f)
            {
                return "RetreatHealthRatio 必须落在 0 到 1 之间。";
            }

            if (RetreatHealCeilingRatio <= RetreatHealthRatio || RetreatHealCeilingRatio > 1f)
            {
                return "RetreatHealCeilingRatio 必须大于 RetreatHealthRatio 且不超过 1。";
            }

            if (EngageDistanceTolerance < 0f || PreferredEngageDistance <= 0f)
            {
                return "交战距离与其容差必须为正数。";
            }

            if (FireBurstSeconds <= 0f || FirePauseSeconds < 0f)
            {
                return "连射时长必须大于 0，停火时长不得为负。";
            }

            if (RepathIntervalSeconds <= 0f || WaypointReachDistance <= 0f)
            {
                return "寻路间隔与路径点到达距离必须大于 0。";
            }

            return null;
        }
    }
}
