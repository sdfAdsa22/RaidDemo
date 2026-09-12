using NUnit.Framework;
using RaidDemo.Bootstrap.Editor;
using UnityEngine;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 下沉盆地剖面的单元测试。
    /// </summary>
    /// <remarks>
    /// <para>地形是本项目里唯一「几何即规则」的东西：土墙只要比 45 度缓一点，
    /// 玩家就能从任意位置爬上塬面，坡道与撤离点的设计意图会当场失效；
    /// 坡道只要比 45 度陡一点，玩家又会上不去。这类错误在画面里看起来只是
    /// 「走得有点怪」，靠实机试玩很难穷尽，因此把关键约束写成断言。</para>
    ///
    /// <para>测试全部基于解析函数与网格数据，不依赖场景、导航网格或素材，因此运行很快。</para>
    /// </remarks>
    [TestFixture]
    public sealed class BasinTerrainProfileTests
    {
        private const float Tolerance = 0.001f;

        private BasinTerrainProfile m_Profile;

        [SetUp]
        public void SetUp()
        {
            m_Profile = new BasinTerrainProfile();
        }

        [Test]
        public void ValleyFloor_IsFlat()
        {
            for (var x = -26f; x <= 26f; x += 2f)
            {
                for (var z = -26f; z <= 26f; z += 2f)
                {
                    // 四条通道是「切进谷底的走廊」：坡道在谷底一侧会抬起，
                    // 谷口则把地面延伸到土墙之外。它们不属于「谷底平面」，测试要跳过。
                    if (IsInsideCorridor(x, z))
                    {
                        continue;
                    }

                    Assert.AreEqual(
                        BasinTerrainProfile.FloorHeight,
                        m_Profile.SampleHeight(x, z),
                        Tolerance,
                        $"谷底应当是平的，({x}, {z}) 处高度不符。");
                }
            }
        }

        [Test]
        public void Rim_IsFlatAtDesignHeight()
        {
            // 三条上坡道所在的塬面
            Assert.AreEqual(BasinTerrainProfile.RimHeight, m_Profile.SampleHeight(34f, 0f), Tolerance);
            Assert.AreEqual(BasinTerrainProfile.RimHeight, m_Profile.SampleHeight(0f, 34f), Tolerance);
            Assert.AreEqual(BasinTerrainProfile.RimHeight, m_Profile.SampleHeight(-34f, 0f), Tolerance);

            // 南侧矮丘比其余三面低 1.5 米，这是设计上的差异而不是误差
            Assert.AreEqual(BasinTerrainProfile.SouthRimHeight, m_Profile.SampleHeight(0f, -34f), Tolerance);
            Assert.Less(BasinTerrainProfile.SouthRimHeight, BasinTerrainProfile.RimHeight);
        }

        [Test]
        public void WallSlopes_AreSteeperThanWalkableLimit()
        {
            // 45 度是 PhysX 胶囊扫掠与导航烘焙共同使用的可行走斜面阈值
            // （见 PhysicsMovementCollisionService.WalkableSlopeAngle）。
            // 土墙必须明显更陡，否则玩家可以不走坡道直接爬上去。
            Assert.Greater(BasinTerrainProfile.WallSlopeDegrees, 45f, "北侧土墙太缓，玩家可以直接爬上去。");
            Assert.Greater(BasinTerrainProfile.SouthWallSlopeDegrees, 45f, "南侧矮丘太缓，玩家可以直接爬上去。");
        }

        [Test]
        public void RampSlope_IsWalkableAndGentle()
        {
            Assert.LessOrEqual(
                BasinTerrainProfile.RampSlopeDegrees,
                25f,
                "坡道超过了设计约束的 25 度，地面吸附会出现明显的台阶感。");
            Assert.Greater(BasinTerrainProfile.RampSlopeDegrees, 5f, "坡道太平，读不出高低差。");
        }

        [Test]
        public void NorthRamp_RisesContinuouslyFromFloorToRim()
        {
            var previous = m_Profile.SampleHeight(0f, BasinTerrainProfile.RampInnerHalfExtent);
            Assert.AreEqual(BasinTerrainProfile.FloorHeight, previous, Tolerance, "坡道起点应当与谷底齐平。");

            for (var z = BasinTerrainProfile.RampInnerHalfExtent + 0.5f;
                 z <= BasinTerrainProfile.WallHalfExtent;
                 z += 0.5f)
            {
                var height = m_Profile.SampleHeight(0f, z);
                Assert.Greater(height, previous - Tolerance, $"坡道在 z={z} 处出现回落，角色会卡住。");
                previous = height;
            }

            Assert.AreEqual(
                BasinTerrainProfile.RimHeight,
                previous,
                Tolerance,
                "坡道顶端没有接到塬面高度。");
        }

        [Test]
        public void RampCorridors_ExistOnThreeSidesOnly()
        {
            // 东、西两条坡道与北坡道同高：坡道顶端（土墙外沿）必须与塬面齐平
            Assert.AreEqual(
                BasinTerrainProfile.RimHeight,
                m_Profile.SampleHeight(32f, BasinTerrainProfile.EastRampCenterZ),
                Tolerance);
            Assert.AreEqual(
                BasinTerrainProfile.RimHeight,
                m_Profile.SampleHeight(-32f, BasinTerrainProfile.WestRampCenterZ),
                Tolerance);

            // 南侧没有上坡道：谷底之外是一条平进平出的走廊
            Assert.AreEqual(
                BasinTerrainProfile.FloorHeight,
                m_Profile.SampleHeight(BasinTerrainProfile.SouthCanyonCenterX, -30f),
                Tolerance,
                "南侧谷口应当保持谷底高度。");
        }

        [Test]
        public void RampHasSteepSideWalls()
        {
            // 坡道宽 6 米（±3）。取相邻两个采样点跨过坡道边缘，检查这段的高差是否陡于可行走阈值：
            // 坡道是「高出谷底的路堤」，两侧的高差方向与土墙相反（坡道更高），
            // 因此这里比较的是坡比而不是高度差的正负。
            var onRamp = m_Profile.SampleHeight(2f, 26f);
            var besideRamp = m_Profile.SampleHeight(4f, 26f);
            var slopeDegrees = Mathf.Atan2(Mathf.Abs(onRamp - besideRamp), 2f) * Mathf.Rad2Deg;

            Assert.Greater(
                slopeDegrees,
                45f,
                "坡道侧面没有形成陡坎，玩家可以从旁边走上去。");
        }

        /// <summary>判断一个平面点是否落在四条通道内（坡道或谷口）。</summary>
        private static bool IsInsideCorridor(float x, float z)
        {
            var lateralX = Mathf.Abs(x - BasinTerrainProfile.NorthRampCenterX)
                           <= BasinTerrainProfile.RampHalfWidth;
            var lateralEast = Mathf.Abs(z - BasinTerrainProfile.EastRampCenterZ)
                              <= BasinTerrainProfile.RampHalfWidth;
            var lateralWest = Mathf.Abs(z - BasinTerrainProfile.WestRampCenterZ)
                              <= BasinTerrainProfile.RampHalfWidth;
            var canyon = Mathf.Abs(x - BasinTerrainProfile.SouthCanyonCenterX)
                         <= BasinTerrainProfile.SouthCanyonHalfWidth;

            // 北坡道
            if (lateralX && z >= BasinTerrainProfile.RampInnerHalfExtent)
            {
                return true;
            }

            // 东坡道
            if (lateralEast && x >= BasinTerrainProfile.RampInnerHalfExtent)
            {
                return true;
            }

            // 西坡道
            if (lateralWest && x <= -BasinTerrainProfile.RampInnerHalfExtent)
            {
                return true;
            }

            // 南谷口
            return canyon && z <= -BasinTerrainProfile.ValleyHalfExtent;
        }

        [Test]
        public void Mesh_HasGroundAndCliffSubmeshes()
        {
            var mesh = BasinTerrainMeshBuilder.BuildMesh(m_Profile, worldFloorY: -BasinTerrainProfile.RimHeight);
            try
            {
                Assert.AreEqual(2, mesh.subMeshCount, "地形网格应当分为草地与黏土两个子网格。");
                Assert.Greater(mesh.GetTriangles(0).Length, 0, "草地子网格为空，地形会只剩土墙。");
                Assert.Greater(mesh.GetTriangles(1).Length, 0, "黏土子网格为空，土墙会显示成草地色。");

                // 网格的世界高度范围要覆盖「谷底 -6」到「塬面 +6-6=0」：
                // 少一个方向就说明世界高度偏移用错了符号。
                Assert.LessOrEqual(mesh.bounds.min.y, -BasinTerrainProfile.RimHeight + Tolerance);
                Assert.GreaterOrEqual(mesh.bounds.max.y, -Tolerance);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }
    }
}
