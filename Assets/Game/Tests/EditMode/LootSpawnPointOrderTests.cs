using NUnit.Framework;
using RaidDemo.Bootstrap;
using RaidDemo.Presentation;
using UnityEngine;

namespace RaidDemo.Tests.EditMode
{
    /// <summary>
    /// 战利品生成点排序测试。
    /// </summary>
    /// <remarks>
    /// <para><b>它防的是哪一类缺陷：</b>M13-23。容器编号来自生成点数组下标，
    /// 而 <c>FindObjectsByType</c> 的返回顺序在服务器与客户端可能不同——
    /// 顺序一变，同一个编号在两端的箱子尺寸就不一样，日志里出现
    /// "容量两端不一致"与"坐标放不下、退回自动摆放"。排序规则由两端共用，
    /// 这里钉住"结果与插入顺序无关"。</para>
    /// </remarks>
    [TestFixture]
    public sealed class LootSpawnPointOrderTests
    {
        private GameObject m_Root;

        /// <summary>每个用例后清掉创建的场景对象。</summary>
        [TearDown]
        public void TearDown()
        {
            if (m_Root != null)
            {
                Object.DestroyImmediate(m_Root);
                m_Root = null;
            }
        }

        private LootSpawnPoint CreatePoint(Vector3 position, string name)
        {
            var host = new GameObject(name);
            host.transform.SetParent(m_Root.transform);
            host.transform.position = position;
            return host.AddComponent<LootSpawnPoint>();
        }

        /// <summary>同一批箱子无论按什么顺序交给排序，编号都指向同一个对象。</summary>
        [Test]
        public void 排序结果与插入顺序无关()
        {
            m_Root = new GameObject("LootOrderTest");
            var positions = new[]
            {
                new Vector3(10f, 0f, 0f),
                new Vector3(-5f, 0f, 3f),
                new Vector3(-5f, 0f, -2f),
                new Vector3(2f, 0f, 8f),
            };

            var forward = new LootSpawnPoint[positions.Length];
            for (var index = 0; index < positions.Length; index++)
            {
                forward[index] = CreatePoint(positions[index], "P" + index);
            }

            LootSpawnPointOrder.Sort(forward);

            var reversed = new LootSpawnPoint[forward.Length];
            for (var index = 0; index < forward.Length; index++)
            {
                reversed[index] = forward[forward.Length - 1 - index];
            }

            LootSpawnPointOrder.Sort(reversed);

            for (var index = 0; index < forward.Length; index++)
            {
                Assert.AreSame(forward[index], reversed[index], $"第 {index} 个箱子在不同插入顺序下应当相同");
            }
        }

        /// <summary>排序先比 x 再比 z，保证地图上左右、前后的顺序都是确定的。</summary>
        [Test]
        public void 排序先按横坐标再按纵坐标()
        {
            m_Root = new GameObject("LootOrderTest2");
            var rightFar = CreatePoint(new Vector3(1f, 0f, 5f), "A");
            var rightNear = CreatePoint(new Vector3(1f, 0f, 2f), "B");
            var leftFar = CreatePoint(new Vector3(-3f, 0f, 9f), "C");

            var points = new[] { rightFar, rightNear, leftFar };
            LootSpawnPointOrder.Sort(points);

            Assert.AreSame(leftFar, points[0], "x 最小的排最前");
            Assert.AreSame(rightNear, points[1], "x 相同时 z 小的排前");
            Assert.AreSame(rightFar, points[2]);
        }
    }
}
