using System.Collections.Generic;
using NUnit.Framework;
using RaidDemo.Presentation;
using RaidDemo.Shared;
using UnityEngine;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 真实 PhysX 碰撞测试：低台阶可跨越、高墙不可翻越、坡道不阻塞。
    /// </summary>
    /// <remarks>
    /// <para>移动规则本身由 <c>PlayerCollisionTests</c> 用纯数学替身验证；
    /// 本组测试只覆盖引擎适配器，回答"PhysX 胶囊扫掠的边界情况"。</para>
    ///
    /// <para>这三个用例分别锁定一次真实反馈：0.25 米路缘卡住、1.2 米平台边缘不可翻越、
    /// 16 度坡道被水平扫掠误判为墙。</para>
    /// </remarks>
    [TestFixture]
    public sealed class PhysicsMovementCollisionTests
    {
        private readonly List<GameObject> m_Created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (var i = 0; i < m_Created.Count; i++)
            {
                if (m_Created[i] != null)
                {
                    Object.DestroyImmediate(m_Created[i]);
                }
            }

            m_Created.Clear();
            Physics.SyncTransforms();
        }

        [Test]
        public void 低台阶可以跨越()
        {
            var owner = CreateOwner(new Vector3(0f, 0f, 0f));
            CreateBox(new Vector3(0f, 0.125f, 1f), new Vector3(2f, 0.25f, 0.5f));
            Physics.SyncTransforms();

            var service = new PhysicsMovementCollisionService(owner.transform, 1.8f, 0.02f);

            service.TryResolveMove(
                new Vector2F(0f, 0f),
                new Vector2F(0f, 1f),
                0.4f,
                out var resolved);

            Assert.Greater(resolved.Y, 0.9f, "0.25 米路缘不应把角色完全挡住。");
        }

        [Test]
        public void 高墙仍然不可翻越()
        {
            var owner = CreateOwner(new Vector3(0f, 0f, 0f));
            CreateBox(new Vector3(0f, 1f, 1f), new Vector3(2f, 2f, 0.5f));
            Physics.SyncTransforms();

            var service = new PhysicsMovementCollisionService(owner.transform, 1.8f, 0.02f);

            service.TryResolveMove(
                new Vector2F(0f, 0f),
                new Vector2F(0f, 1f),
                0.4f,
                out var resolved);

            Assert.Less(resolved.Y, 0.5f, "2 米高墙不能被台阶跨越逻辑翻过去。");
        }

        [Test]
        public void 可行走坡道不会被当成墙()
        {
            var owner = CreateOwner(new Vector3(0f, 0f, -0.6f));
            CreateRamp();
            Physics.SyncTransforms();

            var service = new PhysicsMovementCollisionService(owner.transform, 1.8f, 0.02f);

            service.TryResolveMove(
                new Vector2F(0f, -0.6f),
                new Vector2F(0f, 1f),
                0.4f,
                out var resolved);

            Assert.Greater(resolved.Y, 0.9f, "16 度坡道不应阻塞水平移动。");
        }

        [Test]
        public void 单位层已登记()
        {
            Assert.GreaterOrEqual(
                LayerMask.NameToLayer(PhysicsLayers.UnitsLayerName),
                0,
                "TagManager 里必须登记 Units 层：移动遮罩与命中判定的分工依赖它（U-50）。");
        }

        [Test]
        public void 单位层上的胶囊不再阻挡移动()
        {
            var owner = CreateOwner(Vector3.zero);
            CreateUnitCapsule(new Vector3(0f, 0.9f, 1f));
            Physics.SyncTransforms();

            var service = new PhysicsMovementCollisionService(owner.transform, 1.8f, 0.02f);

            service.TryResolveMove(
                new Vector2F(0f, 0f),
                new Vector2F(0f, 1f),
                0.4f,
                out var resolved);

            Assert.Greater(resolved.Y, 0.9f, "单位层上的胶囊不应阻挡移动（U-50：贴身卡死）。");
        }

        [Test]
        public void 默认层上的胶囊仍然阻挡移动()
        {
            var owner = CreateOwner(Vector3.zero);
            var unit = CreateUnitCapsule(new Vector3(0f, 0.9f, 1f));
            // 对照用例：同样的胶囊留在默认层时仍然是墙，防止"排除层"写错成"排除一切"。
            unit.layer = 0;
            Physics.SyncTransforms();

            var service = new PhysicsMovementCollisionService(owner.transform, 1.8f, 0.02f);

            service.TryResolveMove(
                new Vector2F(0f, 0f),
                new Vector2F(0f, 1f),
                0.4f,
                out var resolved);

            Assert.Less(resolved.Y, 0.5f, "非单位层的实体碰撞体仍然必须挡住移动。");
        }

        [Test]
        public void 贴着高台侧面时能走出去而不是被钉死()
        {
            // U-69 复刻：走下高台侧面后，脚底已经落到下层地面，但胶囊半径还压在侧棱里。
            // 修复前 PhysX 对"起点已重叠"的碰撞体在每个方向都返回 0 距离命中，
            // 位移被压成 0——实测四个方向都无法移动。
            var owner = CreateOwner(new Vector3(0f, 0f, 0.1f));
            CreateBox(new Vector3(0f, 0.6f, -1f), new Vector3(4f, 1.2f, 2f));
            Physics.SyncTransforms();

            var service = new PhysicsMovementCollisionService(owner.transform, 1.8f, 0.02f);

            service.TryResolveMove(
                new Vector2F(0f, 0.1f),
                new Vector2F(0f, 1f),
                0.4f,
                out var resolved);

            Assert.Greater(resolved.Y, 0.9f, "贴着侧面时应该能走出去（去穿透），而不是被钉死。");
        }

        [Test]
        public void 球心正好压在棱面上时也能脱困()
        {
            // 平台边缘实测到的退化情形：角色落到下层后，胶囊球心正好压在台体侧面平面上，
            // 最近点计算退化。这里锁定兜底方向（包围盒中心 → 球心）生效。
            var owner = CreateOwner(new Vector3(0f, 0f, 0f));
            CreateBox(new Vector3(0f, 0.6f, -1f), new Vector3(4f, 1.2f, 2f));
            Physics.SyncTransforms();

            var service = new PhysicsMovementCollisionService(owner.transform, 1.8f, 0.02f);

            service.TryResolveMove(
                new Vector2F(0f, 0f),
                new Vector2F(0f, 1f),
                0.4f,
                out var resolved);

            Assert.Greater(resolved.Y, 0.9f, "球心压在棱面上时也应该被推出来并继续移动。");
        }

        [Test]
        public void 重叠时朝棱边挤不会钻进高台()
        {
            var owner = CreateOwner(new Vector3(0f, 0f, 0.1f));
            CreateBox(new Vector3(0f, 0.6f, -1f), new Vector3(4f, 1.2f, 2f));
            Physics.SyncTransforms();

            var service = new PhysicsMovementCollisionService(owner.transform, 1.8f, 0.02f);

            // 连续三帧朝高台方向推：重叠放松只在"离开"时生效，
            // 朝里走必须始终被挡住，位置不能往里挪。
            var position = new Vector2F(0f, 0.1f);
            for (var frame = 0; frame < 3; frame++)
            {
                service.TryResolveMove(position, new Vector2F(0f, -1f), 0.4f, out var resolved);
                position = new Vector2F(position.X + resolved.X, position.Y + resolved.Y);
            }

            Assert.GreaterOrEqual(position.Y, 0.08f, "重叠放松只应该放行「离开」方向，朝里走不能被放行。");
        }

        [Test]
        public void 贴着几何移动不会产生额外位移()
        {
            // U-69 第二次修复的回归：第一版「推人」式去穿透会额外改位置
            // （实测在地形网格上一帧最多多推 1 米，表现为瞬移）。
            // 这里用网格碰撞体复刻地形那一类几何，要求返回的位移就是请求的位移，一点都不能多。
            var owner = CreateOwner(new Vector3(0f, 0f, 0.1f));
            CreateMeshBox(new Vector3(0f, 0.6f, -1f), new Vector3(4f, 1.2f, 2f));
            Physics.SyncTransforms();

            var service = new PhysicsMovementCollisionService(owner.transform, 1.8f, 0.02f);

            service.TryResolveMove(
                new Vector2F(0f, 0.1f),
                new Vector2F(0f, 0.35f),
                0.4f,
                out var resolved);

            Assert.AreEqual(0.35f, resolved.Y, 0.01f, "移动解析不应该在请求位移之外额外改动位置。");
            Assert.AreEqual(0.0f, resolved.X, 0.01f, "移动解析不应该在请求位移之外额外改动位置。");
        }

        private GameObject CreateOwner(Vector3 position)
        {
            var owner = new GameObject("TestOwner");
            owner.transform.position = position;
            var collider = owner.AddComponent<CapsuleCollider>();
            collider.height = 1.8f;
            collider.radius = 0.4f;
            collider.center = new Vector3(0f, 0.9f, 0f);
            m_Created.Add(owner);
            return owner;
        }

        private GameObject CreateUnitCapsule(Vector3 position)
        {
            var unit = new GameObject("TestUnit");
            unit.transform.position = position;
            PhysicsLayers.ApplyUnitLayer(unit);
            var collider = unit.AddComponent<CapsuleCollider>();
            collider.height = 1.8f;
            collider.radius = 0.4f;
            collider.center = new Vector3(0f, 0.9f, 0f);
            m_Created.Add(unit);
            return unit;
        }

        private void CreateBox(Vector3 center, Vector3 size)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "TestBox";
            box.transform.position = center;
            box.transform.localScale = size;
            m_Created.Add(box);
        }

        /// <summary>创建一个用网格碰撞体的方块——复刻地形那种大网格几何。</summary>
        private void CreateMeshBox(Vector3 center, Vector3 size)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "TestMeshBox";
            box.transform.position = center;
            box.transform.localScale = size;
            Object.DestroyImmediate(box.GetComponent<Collider>());
            box.AddComponent<MeshCollider>();
            m_Created.Add(box);
        }

        private void CreateRamp()
        {
            const float rise = 0.6f;
            const float run = 2f;
            const float thickness = 0.2f;
            var length = Mathf.Sqrt((run * run) + (rise * rise));
            var angle = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
            var halfProjection = thickness * 0.5f * Mathf.Cos(angle * Mathf.Deg2Rad);
            var center = new Vector3(0f, (rise * 0.5f) - halfProjection, 1f);

            var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ramp.name = "TestRamp";
            ramp.transform.position = center;
            ramp.transform.localScale = new Vector3(2f, thickness, length);
            // 正角度会让本地 +Z 端下沉；这里要让 +Z 端升高，因此取负角度。
            ramp.transform.rotation = Quaternion.Euler(-angle, 0f, 0f);
            m_Created.Add(ramp);
        }
    }
}
