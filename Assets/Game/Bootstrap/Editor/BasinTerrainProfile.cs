using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 下沉盆地的解析式剖面：把「地图长什么样」从「怎么把它变成网格」里拆出来。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么要有这一层：</b>地形高度是一条纯函数 <c>h = f(x, z)</c>。
    /// 把它单独写成一个类，网格生成、导航烘焙、AI 巡逻点校验、单元测试都读同一份定义，
    /// 不会出现「网格改了高度、巡逻点还按老高度算」这类两边对不上的问题；
    /// 调地形时也只需要改这里的常量，不必在几千行生成代码里找数字。</para>
    ///
    /// <para><b>坐标约定：</b>x 向东、z 向北，原点在地图中心。
    /// 高度是**谷底相对值**：谷底为 0、塬面为 +6，与灰盒布局坐标（y 等于 0 表示谷底）一致。
    /// 生成网格时由 <c>GreyboxSceneBuilder</c> 统一减去 <c>ValleyFloorY</c>，
    /// 换算成世界坐标（谷底落在 y 等于 -6 米）。</para>
    ///
    /// <para><b>剖面结构（从内到外）：</b></para>
    /// <list type="number">
    /// <item><description>谷底：|d| ≤ 28，平坦草地，全部玩法区域都在这里；</description></item>
    /// <item><description>土墙：28 → 32，抬升到塬面高度。坡比约 56 度，刻意超过 45 度，
    /// 这样 PhysX 胶囊扫掠会把它当障碍、导航网格也不会把它烘成可行走面——
    /// 「只有坡道能上下」这条规则因此由几何本身保证，而不是靠额外规则去堵；</description></item>
    /// <item><description>塬面：32 → 36，平坦的窄边，三个撤离点放在这里，玩家必须爬坡才能到；</description></item>
    /// <item><description>外裙：36 → 48，缓缓下降到远景色，只用于视觉衔接，玩家被隐形边界挡在 36 米处。</description></item>
    /// </list>
    ///
    /// <para><b>四条通道：</b>北/东/西三条上坡道（宽 6 米、坡度约 25 度，满足「≤25 度」的设计约束，
    /// 导航网格可烘焙、胶囊扫掠不阻挡）；南侧一条谷口走廊保持谷底高度，
    /// 形成「下沉盆地 + 一个平进平出的缺口」的地形读法。</para>
    /// </remarks>
    public sealed class BasinTerrainProfile
    {
        /// <summary>谷底高度（米，相对谷底为 0）。</summary>
        public const float FloorHeight = 0f;

        /// <summary>北/东/西侧塬面高度（米）。</summary>
        public const float RimHeight = 6f;

        /// <summary>南侧矮丘高度（米）。比北侧低 1.5 米，读作「矮丘」而不是「高墙」。</summary>
        public const float SouthRimHeight = 4.5f;

        /// <summary>谷底平地的半宽（米）。</summary>
        public const float ValleyHalfExtent = 28f;

        /// <summary>土墙外沿的半宽（米）：土墙从谷底边缘一直爬到这里。</summary>
        public const float WallHalfExtent = 32f;

        /// <summary>南侧矮丘与东西两面塬面的过渡长度（米）。</summary>
        /// <remarks>
        /// 「南侧更低」只应作用在南墙那一段上，而不是整个南半张图。早期实现按 z 是否小于 0 判断，
        /// 结果东西墙的南半段也被压低 1.5 米，西坡顶的塬面比坡道顶端低了 1.5 米——
        /// 玩家爬上去要先掉一截，撤离点的门框也会悬在半空。
        /// 这里改成按「Z 是否主导」加一段平滑过渡，四面墙在转角附近自然衔接。
        /// </remarks>
        private const float SouthBlendRun = 6f;

        /// <summary>塬面外沿（米）。玩家的可活动范围到此为止，外侧由隐形边界拦住。</summary>
        public const float RimHalfExtent = 36f;

        /// <summary>
        /// 地形网格外沿（米）。超出的部分只是远景，不参与玩法。
        /// </summary>
        /// <remarks>外裙从 36 米延伸到 60 米（水平 24 米下降 6 米，约 14 度），
        /// 让远景山环有地方可放，同时保证从塬面望出去看不到网格边缘的"悬崖断层"。</remarks>
        public const float MeshHalfExtent = 60f;

        /// <summary>坡道走廊的半宽（米）。6 米宽保证双向通行、AI 会车不堵。</summary>
        public const float RampHalfWidth = 3f;

        /// <summary>坡道起点到中心的距离（米）。19 → 32 共 13 米爬升 6 米，坡度约 24.8 度。</summary>
        public const float RampInnerHalfExtent = 19f;

        /// <summary>
        /// 北坡道的横向中心（X，米）。
        /// </summary>
        /// <remarks>正对地图中轴线：这一带只有北侧环道与一个补给箱，是最干净的入口。</remarks>
        public const float NorthRampCenterX = 0f;

        /// <summary>
        /// 东坡道的横向中心（Z，米）。
        /// </summary>
        /// <remarks>
        /// <para>刻意避开集装箱堆场（z 从 -6 到 22）与堆场北侧的围栏：坡道走廊一旦压到箱体上，
        /// 玩家就会被卡在箱子边上、AI 的导航路径也会绕到别处，撤离点等于被堵死
        /// （批次 2 的东侧坡道最初开在 z=0，正好撞上集装箱）。</para>
        /// <para>25.5 是堆场北缘与北侧围栏之间的空档：南边离最近的集装箱还有 1.3 米，
        /// 北边离塬面还有 6.5 米。</para>
        /// </remarks>
        public const float EastRampCenterZ = 25.5f;

        /// <summary>
        /// 西坡道的横向中心（Z，米）。
        /// </summary>
        /// <remarks>
        /// <para>西侧整片是主厂房（x 从 -27 到 -10、z 从 -14 到 14），坡道走廊必然穿过厂房，
        /// 因此只能落在厂房的南北两侧。选南侧（-20）而不是北侧（+20），
        /// 是因为北侧还压着唯一的保险柜与一段围栏，南侧只有一块混凝土掩体需要挪开。</para>
        /// </remarks>
        public const float WestRampCenterZ = -20f;

        /// <summary>南侧谷口走廊的半宽（米）。</summary>
        public const float SouthCanyonHalfWidth = 3f;

        /// <summary>
        /// 南侧谷口的中心 X 坐标（米）。
        /// </summary>
        /// <remarks>刻意不放在地图中轴线上：中轴线正南是装卸平台（22×9 米的实体台体，占住 x 从 -9 到 13），
        /// 谷口若开在 x 等于 0，出口会被台体整个堵死，导航网格里那条走廊就是一座孤岛——
        /// 玩家与 AI 都进不去，第四个撤离点等于不存在。移到平台东侧（x 等于 17）后，
        /// 谷口直接连通谷底主空间。</remarks>
        public const float SouthCanyonCenterX = 17f;

        /// <summary>外裙的总下降高度（米）。</summary>
        public const float ApronDrop = 6f;

        /// <summary>地形网格的推荐采样间距（米）。2 米既能读出低多边形棱面，又不会让顶点数失控。</summary>
        public const float RecommendedCellSize = 2f;

        /// <summary>
        /// 求任意水平位置的地形高度（相对谷底）。
        /// </summary>
        /// <param name="x">世界 X 坐标（米）。</param>
        /// <param name="z">世界 Z 坐标（米）。</param>
        /// <returns>该位置的地形高度（米，谷底为 0）。</returns>
        public float SampleHeight(float x, float z)
        {
            // 通道优先：坡道与谷口是「切进环形剖面里的走廊」，
            // 若先算环形剖面再叠加，走廊两侧会出现高度断层接缝。
            if (TrySampleCorridor(x, z, out var corridorHeight))
            {
                return corridorHeight;
            }

            return SampleRingProfile(x, z);
        }

        /// <summary>
        /// 按「到中心的切比雪夫距离」计算环形剖面：谷底 → 土墙 → 塬面 → 外裙。
        /// </summary>
        /// <remarks>
        /// 用切比雪夫距离（max(|x|,|z|)）而不是欧氏距离，是因为地图外沿要做成**矩形**：
        /// 矩形边界让四条土墙是直的，玩家在斜俯视下更容易判断「哪边是墙、哪边是路」；
        /// 圆形盆地会让边界方向不断变化，读图成本明显更高。
        /// </remarks>
        private static float SampleRingProfile(float x, float z)
        {
            var distance = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
            if (distance <= ValleyHalfExtent)
            {
                return FloorHeight;
            }

            var rimHeight = ResolveRimHeight(x, z);

            if (distance >= WallHalfExtent)
            {
                return SampleApron(distance, rimHeight);
            }

            var t = (distance - ValleyHalfExtent) / (WallHalfExtent - ValleyHalfExtent);
            return Mathf.Lerp(FloorHeight, rimHeight, t);
        }

        /// <summary>
        /// 求某个平面位置对应的塬面高度：北/东/西三面是 <see cref="RimHeight"/>，
        /// 只有南墙那一段降到 <see cref="SouthRimHeight"/>，转角处用一段平滑过渡衔接。
        /// </summary>
        private static float ResolveRimHeight(float x, float z)
        {
            if (z >= 0f)
            {
                return RimHeight;
            }

            // 只有「Z 轴主导」的位置才算南墙：|z| 明显大于 |x| 时 z 才是决定距离的那条边。
            var southWeight = Mathf.Clamp01((Mathf.Abs(z) - Mathf.Abs(x)) / SouthBlendRun);
            return Mathf.Lerp(RimHeight, SouthRimHeight, southWeight);
        }

        /// <summary>塬面平台与外裙：36 米以内保持平坦，之后再缓缓下降。</summary>
        private static float SampleApron(float distance, float rimHeight)
        {
            if (distance <= RimHalfExtent)
            {
                return rimHeight;
            }

            var t = (distance - RimHalfExtent) / (MeshHalfExtent - RimHalfExtent);
            return rimHeight - (t * t * ApronDrop);
        }

        /// <summary>
        /// 四条通道的剖面。
        /// </summary>
        /// <param name="x">世界 X 坐标。</param>
        /// <param name="z">世界 Z 坐标。</param>
        /// <param name="height">通道高度，仅在返回 true 时有效。</param>
        /// <returns>该点位于任一通道内返回 true。</returns>
        /// <remarks>
        /// 四条通道分别写死轴向判断，而不是抽象成「轴 + 符号」的参数表：
        /// 只有四条、且每条的两个方向都不同（北/东/西是上坡、南是平进），
        /// 显式写出来可以直接和设计图对照，出错时也一眼能看出是哪一条写错了。
        /// </remarks>
        private static bool TrySampleCorridor(float x, float z, out float height)
        {
            height = FloorHeight;

            // 北坡道：x 在中心 ±3 之内，z 从 19 爬到 32
            if (Mathf.Abs(x - NorthRampCenterX) <= RampHalfWidth
                && z >= RampInnerHalfExtent
                && z <= WallHalfExtent)
            {
                height = RampHeight(z);
                return true;
            }

            // 东坡道：z 在中心 ±3 之内，x 从 19 爬到 32
            if (Mathf.Abs(z - EastRampCenterZ) <= RampHalfWidth
                && x >= RampInnerHalfExtent
                && x <= WallHalfExtent)
            {
                height = RampHeight(x);
                return true;
            }

            // 西坡道：z 在中心 ±3 之内，x 从 -32 爬到 -19
            if (Mathf.Abs(z - WestRampCenterZ) <= RampHalfWidth
                && x <= -RampInnerHalfExtent
                && x >= -WallHalfExtent)
            {
                height = RampHeight(-x);
                return true;
            }

            // 南谷口：x ∈ [-3, 3]，z ∈ [-32, -28] 保持谷底高度，形成平进平出的走廊
            if (Mathf.Abs(x - SouthCanyonCenterX) <= SouthCanyonHalfWidth
                && z <= -ValleyHalfExtent
                && z >= -WallHalfExtent)
            {
                height = FloorHeight;
                return true;
            }

            return false;
        }

        /// <summary>坡道剖面：把「距离中心的轴向距离」线性映射到谷底高度与塬面高度之间。</summary>
        private static float RampHeight(float axialDistance)
        {
            var t = Mathf.InverseLerp(RampInnerHalfExtent, WallHalfExtent, axialDistance);
            return Mathf.Lerp(FloorHeight, RimHeight, t);
        }

        /// <summary>坡道的实际坡度（度）。用于单元测试断言「必须可被导航网格烘焙」。</summary>
        public static float RampSlopeDegrees
        {
            get
            {
                var run = WallHalfExtent - RampInnerHalfExtent;
                return Mathf.Atan2(RimHeight - FloorHeight, run) * Mathf.Rad2Deg;
            }
        }

        /// <summary>土墙坡度（度）。用于单元测试断言「必须超过可行走斜面阈值」。</summary>
        public static float WallSlopeDegrees
        {
            get
            {
                var run = WallHalfExtent - ValleyHalfExtent;
                return Mathf.Atan2(RimHeight - FloorHeight, run) * Mathf.Rad2Deg;
            }
        }

        /// <summary>南侧矮丘的坡度（度）。</summary>
        public static float SouthWallSlopeDegrees
        {
            get
            {
                var run = WallHalfExtent - ValleyHalfExtent;
                return Mathf.Atan2(SouthRimHeight - FloorHeight, run) * Mathf.Rad2Deg;
            }
        }
    }
}
