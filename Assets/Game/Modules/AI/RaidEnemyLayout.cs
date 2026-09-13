using System.Collections.Generic;
using RaidDemo.Shared;

namespace RaidDemo.AI
{
    /// <summary>
    /// 战局里的敌人布局：出生点、巡逻路线与基础数值。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单独抽出来：</b>P2-2 起 AI 只在**服务器**上跑，而客户端仍要按同一份布局
    /// 摆放敌人的表现。若两边各写一份，任何一次"挪一个出生点"都会让表现与权威错位，
    /// 而且这种错位只在联机时才暴露——单机永远测不出来。布局因此属于**内容**，
    /// 与"谁在跑它"无关，放在 AI 模块里两端共用。</para>
    ///
    /// <para><b>数值为什么也在这里：</b>生命、护甲、备弹是敌人的规则参数，
    /// 服务器要用它建战斗单位，客户端（开发者模式与调试面板）要用它显示血条。
    /// 与布局同理：只有一份。</para>
    /// </remarks>
    public static class RaidEnemyLayout
    {
        /// <summary>
        /// AI 活动范围半径（米）。
        /// </summary>
        /// <remarks>
        /// <para>M7 批次 2 起地图是下沉盆地：谷底平地 ±28 米，四条通道通向外圈塬面，
        /// 三个坡顶撤离点位于 ±34 米处。这里取 34，正好覆盖到场外的撤离点，
        /// 因此玩家在坡顶读秒时敌人仍可能一路追上来——这是刻意的：撤离前的最后一段路必须有风险。</para>
        ///
        /// <para>该值只用于夹取 AI 的目标点（避免撤退方向落到地图外），
        /// 不限制巡逻路线；巡逻点仍在谷底的四个分区内。</para>
        /// </remarks>
        public const float PlayAreaHalfExtent = 34f;

        /// <summary>敌人的生命上限。</summary>
        public const float MaxHealth = 100f;

        /// <summary>敌人的护甲等级。比玩家更容易被打穿，让 AI 不至于变成移动靶。</summary>
        public const int ArmorLevel = 2;

        /// <summary>敌人的护甲耐久。</summary>
        public const float ArmorDurability = 45f;

        /// <summary>敌人初始备弹（发）。约等于三个弹匣。</summary>
        public const int ReserveAmmo = 90;

        /// <summary>出生朝向（度）。统一背对谷口，让玩家从通道进来时先看到背影。</summary>
        public const float SpawnFacingDegrees = 180f;

        /// <summary>一个敌人的出生点与巡逻路线。</summary>
        public readonly struct Entry
        {
            /// <summary>创建一个布局条目。</summary>
            /// <param name="spawn">出生点（平面坐标）。</param>
            /// <param name="route">巡逻路线的路径点（平面坐标），第一个点通常就是出生点。</param>
            public Entry(Vector2F spawn, Vector2F[] route)
            {
                Spawn = spawn;
                Route = route;
            }

            /// <summary>出生点。</summary>
            public Vector2F Spawn { get; }

            /// <summary>巡逻路线。</summary>
            public Vector2F[] Route { get; }
        }

        /// <summary>
        /// 五个敌人的出生点与巡逻路线（M5 四分区地图版本）。
        /// </summary>
        /// <remarks>
        /// <para>五个敌人分布在四个分区：堆场两个（南、北各一）、厂房一个、
        /// 装卸平台一个、外围环道一个。数量按「玩家一路会遇到几次交火」来定：
        /// 太少则搜刮毫无压力，太多则 8 分钟根本搜不完。</para>
        ///
        /// <para>巡逻点全部落在通道与空地上，不穿箱子也不穿墙。路线刻意经过容器附近，
        /// 因此玩家搜刮时被撞见的概率是真实的——这是搜刮读条这个「成本」能成立的前提。</para>
        /// </remarks>
        private static readonly Entry[] s_Entries =
        {
            // 堆场南侧：沿南入口向北推进，覆盖弹药箱与武器架之间的通道
            new Entry(new Vector2F(5f, -3f), new[]
            {
                new Vector2F(5f, -3f), new Vector2F(12f, -8f), new Vector2F(20f, -8f),
            }),

            // 堆场北侧：绕堆场北端与东侧，守着价值最高的武器架
            new Entry(new Vector2F(16f, 22f), new[]
            {
                new Vector2F(16f, 22f), new Vector2F(25f, 24f), new Vector2F(25f, 8f),
            }),

            // 厂房内部：穿过三个厅与两处门洞，是近战交火的主要来源
            new Entry(new Vector2F(-24f, -8f), new[]
            {
                new Vector2F(-24f, -8f), new Vector2F(-24f, 2f),
                new Vector2F(-20.5f, 8f), new Vector2F(-12f, 5.5f),
            }),

            // 装卸平台：在高台上巡逻，玩家爬坡时最容易遭遇
            new Entry(new Vector2F(2f, -23f), new[]
            {
                new Vector2F(2f, -23f), new Vector2F(-6f, -24f), new Vector2F(8f, -24f),
            }),

            // 外围环道：南北向长距离巡逻，让撤离路线始终有变数
            new Entry(new Vector2F(0f, 18f), new[]
            {
                new Vector2F(0f, 18f), new Vector2F(8f, 25f), new Vector2F(-6f, 24f),
            }),
        };

        /// <summary>全部敌人布局。</summary>
        public static IReadOnlyList<Entry> Entries
        {
            get { return s_Entries; }
        }
    }
}
