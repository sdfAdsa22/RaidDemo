using NUnit.Framework;
using RaidDemo.Presentation;
using UnityEngine;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 弹道落点投影测试：覆盖"弹道穿过准星"所依赖的地面高度。
    /// </summary>
    /// <remarks>
    /// <para><b>背景（负责人反馈）：</b>弹道线与准星偶尔不在一条线上。原因是弹道画在
    /// 枪口高度（1.05 米），而准星落在地面——45° 斜俯视下两者在屏幕上必然错开。
    /// 修法是把首尾都落到各自下方的地面/平台面；这三个用例分别覆盖平地、平台与悬空。</para>
    /// </remarks>
    [TestFixture]
    public sealed class TracerRendererTests
    {
        /// <summary>测试场地原点：远离场景，避免与打开的场景物体相交。</summary>
        private static readonly Vector3 ArenaOrigin = new Vector3(0f, 3000f, 0f);

        /// <summary>与 <c>TracerRenderer</c> 的贴地偏移一致：弹道略高于地面，避免与地板重叠闪烁。</summary>
        private const float GroundOffset = 0.05f;

        private GameObject m_Ground;

        [SetUp]
        public void SetUp()
        {
            // 一块厚 1 米的地板：顶面正好在 ArenaOrigin.y，便于断言投影高度。
            m_Ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m_Ground.transform.position = ArenaOrigin + new Vector3(0f, -0.5f, 0f);
            m_Ground.transform.localScale = new Vector3(40f, 1f, 40f);
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(m_Ground);
        }

        /// <summary>平地上：位于枪口高度的点，投影后落到地面加偏移。</summary>
        [Test]
        public void 枪口高度的点_投影到脚下的地面()
        {
            var point = ArenaOrigin + new Vector3(3f, 1.05f, 2f);

            var projected = TracerRenderer.ProjectToGround(point);

            Assert.AreEqual(ArenaOrigin.y + GroundOffset, projected.y, 0.01f,
                "投影后的高度应当是地面高度加一个小偏移。");
            Assert.AreEqual(point.x, projected.x, 0.001f, "X 不应改变。");
            Assert.AreEqual(point.z, projected.z, 0.001f, "Z 不应改变。");
        }

        /// <summary>平台/台阶上：投影落在平台顶面，而不是平台下方的地面。</summary>
        [Test]
        public void 平台上的点_投影到平台顶面()
        {
            var platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
            platform.transform.position = ArenaOrigin + new Vector3(6f, 1.25f, 0f);
            platform.transform.localScale = new Vector3(4f, 2.5f, 4f);
            Physics.SyncTransforms();

            try
            {
                // 站在平台上方 1.05 米处的"枪口"。
                var point = ArenaOrigin + new Vector3(6f, 2.5f + 1.05f, 0f);
                var projected = TracerRenderer.ProjectToGround(point);

                Assert.AreEqual(ArenaOrigin.y + 2.5f + GroundOffset, projected.y, 0.01f,
                    "投影应当落在平台顶面（高 2.5 米），而不是被压到地面。");
            }
            finally
            {
                Object.DestroyImmediate(platform);
            }
        }

        /// <summary>悬空（下方没有地面）时保留原点高度加偏移，保证弹道仍然可见。</summary>
        [Test]
        public void 悬空位置_保留原高度加偏移()
        {
            // 地板（40×40）之外、下方什么都没有的位置。
            var point = ArenaOrigin + new Vector3(100f, 5f, 0f);

            var projected = TracerRenderer.ProjectToGround(point);

            Assert.AreEqual(point.y + GroundOffset, projected.y, 0.01f,
                "探测不到地面时应保留原高度加偏移。");
        }
    }
}
