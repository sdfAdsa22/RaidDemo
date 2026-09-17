using NUnit.Framework;
using RaidDemo.Combat;
using RaidDemo.Presentation;
using UnityEngine;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 物理探针的"跳过射手自己"行为测试。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么用真实物理而不是替身：</b>这条规则的成败完全取决于引擎对
    /// "射线穿过自己身体"的处理方式，只有真实 CapsuleCollider 才能证明它成立。
    /// 测试场地放在远离原点的坐标（y = 2000），不依赖当前打开的场景里有什么。</para>
    ///
    /// <para><b>背景（负责人反馈）：</b>枪口在身体外侧（身前 0.6 米＋枪管），
    /// 当瞄准点落在角色附近/侧后方时，射线会穿过自己的身体——子弹停在自己身上、
    /// 打不到身后的目标。修法是探针把射手当作透明，命中它时继续往后投射。</para>
    /// </remarks>
    [TestFixture]
    public sealed class PhysicsHitProbeTests
    {
        /// <summary>测试场地原点：远高于地图，避免与打开的场景里的物体相交。</summary>
        private static readonly Vector3 ArenaOrigin = new Vector3(0f, 2000f, 0f);

        private PhysicsHitProbe m_Probe;
        private GameObject m_Shooter;
        private GameObject m_Target;

        [SetUp]
        public void SetUp()
        {
            m_Probe = new PhysicsHitProbe();

            // 射手在原点、目标在射手**身后** 3 米；射击方向是"从射手前方朝身后"——
            // 也就是"瞄准点落在角色侧后方"时的那条射线。
            m_Shooter = CreateUnit("Shooter", 1, ArenaOrigin);
            m_Target = CreateUnit("Target", 2, ArenaOrigin + new Vector3(0f, 0f, -3f));
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(m_Shooter);
            Object.DestroyImmediate(m_Target);
        }

        /// <summary>对照组：不忽略任何目标时，子弹命中的是射手自己（旧行为）。</summary>
        [Test]
        public void 不忽略目标时_子弹命中射手自己()
        {
            var origin = ArenaOrigin + new Vector3(0f, 1.05f, 1f);

            var didHit = m_Probe.TryRaycast(origin, Vector3.back, 10f, out var hit);

            Assert.IsTrue(didHit, "射线应当命中射手自己的胶囊。");
            Assert.AreEqual(1, hit.TargetId, "命中的应当是射手自身（编号 1）。");
        }

        /// <summary>修复目标：忽略射手之后，同一条射线穿过身体命中身后的目标。</summary>
        [Test]
        public void 忽略射手后_子弹穿过身体命中身后的目标()
        {
            var origin = ArenaOrigin + new Vector3(0f, 1.05f, 1f);

            var didHit = m_Probe.TryRaycastIgnoringTarget(origin, Vector3.back, 10f, 1, out var hit);

            Assert.IsTrue(didHit, "跳过射手之后仍应命中身后的目标。");
            Assert.AreEqual(2, hit.TargetId, "命中的应是身后的目标（编号 2），而不是射手自己。");
        }

        /// <summary>忽略编号为 0 时行为不变：没有要跳过的目标，命中的仍是射手自己。</summary>
        [Test]
        public void 忽略编号为零时_行为与普通投射一致()
        {
            var origin = ArenaOrigin + new Vector3(0f, 1.05f, 1f);

            var didHit = m_Probe.TryRaycastIgnoringTarget(origin, Vector3.back, 10f, 0, out var hit);

            Assert.IsTrue(didHit);
            Assert.AreEqual(1, hit.TargetId, "没有指定要忽略的目标时，命中的仍是射手自己。");
        }

        /// <summary>环境（没有受击目标编号的碰撞体）仍然正常阻挡子弹。</summary>
        [Test]
        public void 忽略射手时_环境遮挡仍然有效()
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = ArenaOrigin + new Vector3(0f, 1f, 1.5f);
            wall.transform.localScale = new Vector3(2f, 2f, 0.2f);
            Physics.SyncTransforms();

            try
            {
                var origin = ArenaOrigin + new Vector3(0f, 1.05f, 2.5f);
                var didHit = m_Probe.TryRaycastIgnoringTarget(origin, Vector3.back, 10f, 1, out var hit);

                Assert.IsTrue(didHit, "墙应当挡住子弹。");
                Assert.AreEqual(0, hit.TargetId, "命中的应当是环境（没有受击目标编号）。");
            }
            finally
            {
                Object.DestroyImmediate(wall);
            }
        }

        /// <summary>空旷方向返回"没有命中"，而不是随便给一个结果。</summary>
        [Test]
        public void 忽略射手时_无命中返回假()
        {
            var origin = ArenaOrigin + new Vector3(0f, 1.05f, 0f);

            var didHit = m_Probe.TryRaycastIgnoringTarget(origin, Vector3.right, 10f, 1, out _);

            Assert.IsFalse(didHit, "空旷方向不应当有命中。");
        }

        /// <summary>创建一个"胶囊 + 受击标识"的单位，尺寸与玩家的碰撞载体一致。</summary>
        private static GameObject CreateUnit(string name, int targetId, Vector3 position)
        {
            var unit = new GameObject(name);
            unit.transform.position = position;

            var collider = unit.AddComponent<CapsuleCollider>();
            collider.height = 1.8f;
            collider.radius = 0.4f;
            collider.center = new Vector3(0f, 0.9f, 0f);

            var targetView = unit.AddComponent<CombatTargetView>();
            targetView.Initialize(targetId, colorFeedback: false);
            return unit;
        }
    }
}
