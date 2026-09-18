using System;
using RaidDemo.Presentation;

namespace RaidDemo.Bootstrap
{
    /// <summary>战利品生成点的确定性排序：让"第 i 个箱子"在两个进程里是同一个箱子。</summary>
    /// <remarks>
    /// <para><b>它防的是哪一类缺陷：</b>M13-23。两端都用
    /// <c>FindObjectsByType&lt;LootSpawnPoint&gt;(FindObjectsSortMode.None)</c> 拿数组，
    /// 再用数组下标当容器编号 <c>ContainerIds.SceneContainer(i)</c>；而该 API 的返回顺序
    /// 没有稳定保证——两次运行、服务器与客户端都可能不同。顺序一变，同一个编号在两端
    /// 就指向不同的箱子：客户端按自己的箱子尺寸建网格（4×3），服务器按自己的箱子尺寸
    /// 下发（5×4），于是"容量两端不一致"与"坐标放不下、退回自动摆放"接连出现。</para>
    ///
    /// <para>排序键取世界坐标 (x, z)，再按定义 ID 与对象名兜底；箱子位置是场景里的静态数据，
    /// 因此两个进程排出来的顺序必然一致。</para>
    /// </remarks>
    public static class LootSpawnPointOrder
    {
        /// <summary>原地排序生成点数组。</summary>
        public static void Sort(LootSpawnPoint[] points)
        {
            if (points == null || points.Length < 2)
            {
                return;
            }

            Array.Sort(points, Compare);
        }

        /// <summary>比较两个生成点：x → z → 定义 ID → 对象名。</summary>
        private static int Compare(LootSpawnPoint left, LootSpawnPoint right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return -1;
            }

            if (right == null)
            {
                return 1;
            }

            var a = left.transform.position;
            var b = right.transform.position;
            var compare = a.x.CompareTo(b.x);
            if (compare != 0)
            {
                return compare;
            }

            compare = a.z.CompareTo(b.z);
            if (compare != 0)
            {
                return compare;
            }

            compare = string.CompareOrdinal(left.ContainerDefinitionId, right.ContainerDefinitionId);
            return compare != 0 ? compare : string.CompareOrdinal(left.name, right.name);
        }
    }
}
