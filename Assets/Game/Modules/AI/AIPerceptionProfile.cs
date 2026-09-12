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

        /// <summary>
        /// 视野锥的全角（度）。60 度意味着左右各 30 度。
        /// </summary>
        /// <remarks>
        /// 注意它**只在 6~9 米的"警惕"区间起作用**：6 米内忽略角度，
        /// 超过 <see cref="ViewDistanceMeters"/> 则什么都看不见（见 <see cref="ClassifySighting"/>）。
        /// </remarks>
        public float ViewAngleDegrees = 60f;

        /// <summary>视觉距离上限（米）。超出这个距离的目标即使无遮挡也看不见。</summary>
        public float ViewDistanceMeters = 9f;

        /// <summary>
        /// 必定发现距离（米）。
        /// </summary>
        /// <remarks>
        /// <para>在这个距离内**忽略朝向**：只要视线无遮挡就一定发现。
        /// 依据是"贴到几米内，人不可能注意不到背后有人"。</para>
        /// <para>仍然保留遮挡判定——隔着集装箱不该被发现，否则掩体失去意义。</para>
        /// </remarks>
        public float GuaranteedDetectionDistance = 6f;

        /// <summary>
        /// 眼睛相对脚底的高度（米）。
        /// </summary>
        /// <remarks>
        /// 灰盒阶段所有单位都在同一水平面上，因此眼高可以由"平面位置 + 这个常量"算出。
        /// M5 引入高低差之后，三维位置应改为由表现层提供（见 04_AI.md 的已知限制）。
        /// </remarks>
        public float EyeHeightMeters = 1.45f;

        // ── 听觉 ──────────────────────────────────────────────

        /// <summary>步行噪音的可听半径（米）。正常走路只有在贴得很近时才会被察觉。</summary>
        public float HearingRadiusWalk = 4f;

        /// <summary>奔跑噪音的可听半径（米）。这是玩家最容易踩到的一档。</summary>
        /// <remarks>
        /// <para>取值 8 米，**略小于视觉上限（9 米）但大于必定发现距离（6 米）**。
        /// 听觉的独立价值来自它**不看朝向**：站在 AI 背后 7 米奔跑会被听见，而不会被看见；
        /// 反过来，正前方 7 米的奔跑者会先被"看见"（进入警惕），听觉只是补充。</para>
        /// <para>这意味着**奔跑不再能惊动 9 米以外的敌人**——那是超载档（12 米）独有的后果。
        /// 从"贪婪循环"的角度看这反而更清晰：只有拿得太多的人才会在远处暴露自己。</para>
        /// </remarks>
        public float HearingRadiusSprint = 8f;

        /// <summary>超载移动的可听半径（米）。贪心的额外代价。</summary>
        public float HearingRadiusOverloaded = 12f;

        // ── 记忆 ──────────────────────────────────────────────

        /// <summary>目标消失后，记忆保留的时长（秒）。超过之后视为彻底跟丢。</summary>
        public float MemorySeconds = 6f;

        // ── 巡逻 ──────────────────────────────────────────────

        /// <summary>巡逻移动速度（米/秒）。刻意慢于玩家步行速度：巡逻是可以被绕开的。</summary>
        public float PatrolSpeed = 2f;

        /// <summary>
        /// 到达路径点后原地观察的时长（秒）。
        /// </summary>
        /// <remarks>
        /// 视野锥收窄到 60 度之后盲区变大，观察时间相应加长，
        /// 否则巡逻会退化成"沿着路线盲走"。
        /// </remarks>
        public float WaypointScanSeconds = 3f;

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

        /// <summary>
        /// 期望交战距离（米）。太远会推进，太近会后退。
        /// </summary>
        /// <remarks>
        /// 必须明显小于 <see cref="GuaranteedDetectionDistance"/>：AI 只有站进必定发现距离
        /// 才能看见目标并开火。取 5.5 米（小于 6 米）是为了留出站位死区。
        /// </remarks>
        public float PreferredEngageDistance = 5.5f;

        /// <summary>
        /// 交战距离容差（米）。落在期望距离正负这么宽以内就不再调整站位。
        /// </summary>
        /// <remarks>
        /// 实现里取它的一半作为实际死区。取 1.0 是**被 6 米必定发现距离反推出来的**：
        /// 站位一旦超过 6 米，AI 就会陷入"看得见但不该开火"的尴尬状态，
        /// 只能站着不动。5.5 ± 0.5 保证了它始终站在必定发现距离以内。
        /// </remarks>
        public float EngageDistanceTolerance = 1f;

        /// <summary>
        /// 非视觉来源进入交战时的反应时间（秒）。
        /// </summary>
        /// <remarks>
        /// 用于"遭到攻击"这类情形：被打中时必须立刻反应，不能套用 1 秒的观察时间。
        /// 必定发现与警惕确认各有自己的时间（见 <see cref="GuaranteedReactionSeconds"/>
        /// 与 <see cref="AlertConfirmSeconds"/>）。
        /// </remarks>
        public float ReactionSeconds = 0.4f;

        /// <summary>
        /// 必定发现之后的开火延迟（秒）。
        /// </summary>
        /// <remarks>
        /// 玩家在 6 米内被"一定发现"是规则，但**发现不等于立刻开火**：
        /// 一秒的窗口让贴脸遭遇仍有转身或抢先开枪的机会。
        /// 窗口从 2 秒收紧到 1 秒，是因为 2 秒在贴脸距离下足够玩家白打一整个弹匣，
        /// AI 的威胁感被削得太多；1 秒仍然留给玩家一次抢枪的机会。
        /// </remarks>
        public float GuaranteedReactionSeconds = 1f;

        /// <summary>
        /// 警惕升级为交战所需的持续观察时长（秒）。
        /// </summary>
        /// <remarks>
        /// 6~9 米内的目标只会引起怀疑：AI 会停下当前动作、转向目标、缓慢逼近并盯着看。
        /// 只有连续观察满这段时间仍然没有跟丢，才升级为交战。
        /// 这段时间也是玩家"侧身躲开或者拉开距离"的机会窗口。
        /// 从 5 秒收紧到 3 秒：5 秒足够玩家在警惕状态下从容绕后或换弹，
        /// 3 秒既保留反应空间，又让"被盯上"这件事有实际压力。
        /// </remarks>
        public float AlertConfirmSeconds = 3f;

        /// <summary>警惕状态下的靠近速度（米/秒）。慢于调查速度：此时还不确定。</summary>
        public float AlertSpeed = 2.5f;

        /// <summary>
        /// 警惕状态下与目标保持的距离（米）。
        /// </summary>
        /// <remarks>
        /// 取 7 米：落在 6~9 米的警惕区间之内、必定发现距离之外。
        /// 于是 AI 会"盯着你但不动手"，直到观察时间走完或你主动进入 6 米。
        /// </remarks>
        public float AlertHoldDistance = 7f;

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
        public float HearingRadiusFor(NoiseTier tier)
        {
            switch (tier)
            {
                case NoiseTier.Walk:
                    return HearingRadiusWalk;
                case NoiseTier.Sprint:
                    return HearingRadiusSprint;
                case NoiseTier.Overloaded:
                    return HearingRadiusOverloaded;
                default:
                    // 枪声不走这张表：它的半径由**武器射程**决定（见 WeaponFiredEvent.NoiseRadiusMeters），
                    // 因此手枪比步枪安静。档位在这里只是归类用的标签。
                    return 0f;
            }
        }

        /// <summary>
        /// 按距离与朝向给一次视觉观测分档（不含遮挡判定）。
        /// </summary>
        /// <param name="observerPosition">观察者位置。</param>
        /// <param name="facing">观察者朝向（单位向量）。</param>
        /// <param name="targetPosition">目标位置。</param>
        /// <returns>观测档位。遮挡需要调用方另外用射线确认。</returns>
        /// <remarks>
        /// <para>判定顺序体现了整套设计的意图：**先看距离，再看角度**。</para>
        /// <list type="number">
        /// <item><description>距离 ≤ <see cref="GuaranteedDetectionDistance"/>：必定发现，**不看朝向**。</description></item>
        /// <item><description>距离 ≤ <see cref="ViewDistanceMeters"/> 且在视野锥内：警惕。</description></item>
        /// <item><description>其余：看不见。</description></item>
        /// </list>
        /// <para>把这条规则收敛到参数对象上，AI 与开发者模式的"为什么看不见"因此共用
        /// 同一份判定，不会出现两边结论不一致的情况。</para>
        /// </remarks>
        public SightingTier ClassifySighting(Vector2F observerPosition, Vector2F facing, Vector2F targetPosition)
        {
            var distance = Vector2F.Distance(observerPosition, targetPosition);
            if (distance <= GuaranteedDetectionDistance)
            {
                return SightingTier.Guaranteed;
            }

            if (distance > ViewDistanceMeters)
            {
                return SightingTier.None;
            }

            return IsInsideViewCone(observerPosition, facing, targetPosition)
                ? SightingTier.Suspected
                : SightingTier.None;
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

            if (GuaranteedDetectionDistance <= 0f || GuaranteedDetectionDistance > ViewDistanceMeters)
            {
                return "GuaranteedDetectionDistance 必须大于 0 且不超过 ViewDistanceMeters，否则两种档位会互相包含。";
            }

            if (PreferredEngageDistance + (EngageDistanceTolerance * 0.5f) > GuaranteedDetectionDistance)
            {
                return "期望交战距离加上一半容差不能超过必定发现距离：" +
                       "否则 AI 会停在'看得见但不该开火'的位置上，站在那儿不开枪。";
            }

            if (AlertHoldDistance <= GuaranteedDetectionDistance || AlertHoldDistance > ViewDistanceMeters)
            {
                return "AlertHoldDistance 必须落在必定发现距离与视觉上限之间，否则警惕状态会站错位置。";
            }

            if (AlertConfirmSeconds <= 0f)
            {
                return "AlertConfirmSeconds 必须大于 0，否则警惕没有任何观察过程。";
            }

            if (GuaranteedReactionSeconds < 0f)
            {
                return "GuaranteedReactionSeconds 不能为负。";
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
