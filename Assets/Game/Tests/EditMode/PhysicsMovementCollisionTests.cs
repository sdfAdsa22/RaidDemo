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

        private void CreateBox(Vector3 center, Vector3 size)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "TestBox";
            box.transform.position = center;
            box.transform.localScale = size;
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
