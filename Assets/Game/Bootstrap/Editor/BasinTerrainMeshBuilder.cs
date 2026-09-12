using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    // 说明：本文件负责「剖面 → 网格」的转换，不涉及任何资产导入设置。
    /// <summary>
    /// 把 <see cref="BasinTerrainProfile"/> 的解析式剖面烘成一张低多边形地形网格。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么是程序化网格而不是 Unity Terrain：</b>项目只需要「谷底 + 土墙 + 塬面」这一种固定结构，
    /// 不需要高度笔刷、贴图混合与树木草皮。用解析函数生成网格有三个直接收益：
    /// 一是地形完全由常量决定，改一个数字整张地图一致地更新，不会出现手绘地形与巡逻点对不上的情况；
    /// 二是面数与配色可按低多边形风格精确控制；三是场景文件里只保存一份网格资产，而不是几十万个高度采样。</para>
    ///
    /// <para><b>为什么每个三角形独立顶点：</b>低多边形风格要求棱面感——
    /// 共享顶点会被平均成圆滑法线，看起来像被磨平的塑料。
    /// 每个三角形用独立顶点后 <c>RecalculateNormals</c> 得到的就是面法线，
    /// 光照下自然形成一块块明暗不同的棱面。代价是顶点数约为共享方案的 3 倍，
    /// 本图约 1.4 万顶点，对静态地形完全可以接受。</para>
    ///
    /// <para><b>两个子网格：</b>按三角形法线把面分成「地面（草地）」与「立面（黏土）」两组。
    /// 这样只需要两个材质，不需要顶点色或自定义 Shader 就能得到参考图里
    /// 「纯草地 + 橙色土墙」的分离感；分界由地形坡度决定，因此坡道是草、土墙是土，不会出现手绘分界的毛边。</para>
    /// </remarks>
    public static class BasinTerrainMeshBuilder
    {
        /// <summary>
        /// 判为「地面」的最小法线 Y 分量。
        /// </summary>
        /// <remarks>
        /// 取 cos(44°) ≈ 0.72，与移动碰撞服务里的可行走斜面阈值（45 度）刻意对齐：
        /// 一处定义「能不能走」，一处定义「看起来是不是地」，两者用同一个角度才不会出现
        /// 「看起来是土墙、走上去却像地面」这种互相矛盾的反馈。
        /// 坡道 24.8 度的法线 Y 约 0.91，会正确落进草地；土墙 56 度约 0.56，落进黏土。
        /// </remarks>
        private const float GroundNormalY = 0.72f;

        /// <summary>草地的世界贴图密度：几米一个纹理循环。</summary>
        private const float TextureMetersPerTile = 4f;

        /// <summary>生成地形网格（含世界高度偏移，顶点直接落在地图的实际高度上）。</summary>
        /// <param name="profile">地形剖面。</param>
        /// <param name="worldFloorY">谷底在世界坐标中的高度（米）。</param>
        public static Mesh BuildMesh(BasinTerrainProfile profile, float worldFloorY)
        {
            var half = BasinTerrainProfile.MeshHalfExtent;
            var cell = BasinTerrainProfile.RecommendedCellSize;
            var steps = Mathf.RoundToInt((half * 2f) / cell);
            var samplesPerAxis = steps + 1;

            // 高度采样只算一次：相邻三角形共用采样点，若逐三角形求值，
            // 同一个位置的浮点结果虽然一致，但会把 4 次求值变成 6 次，白白浪费生成时间。
            var heights = new float[samplesPerAxis * samplesPerAxis];
            for (var iz = 0; iz < samplesPerAxis; iz++)
            {
                var z = -half + (iz * cell);
                for (var ix = 0; ix < samplesPerAxis; ix++)
                {
                    var x = -half + (ix * cell);
                    heights[(iz * samplesPerAxis) + ix] = profile.SampleHeight(x, z) + worldFloorY;
                }
            }

            var vertices = new List<Vector3>(steps * steps * 6);
            var uvs = new List<Vector2>(steps * steps * 6);
            var groundTriangles = new List<int>(steps * steps * 3);
            var cliffTriangles = new List<int>(steps * steps * 3);

            for (var iz = 0; iz < steps; iz++)
            {
                for (var ix = 0; ix < steps; ix++)
                {
                    var x0 = -half + (ix * cell);
                    var x1 = x0 + cell;
                    var z0 = -half + (iz * cell);
                    var z1 = z0 + cell;

                    var p00 = new Vector3(x0, heights[(iz * samplesPerAxis) + ix], z0);
                    var p10 = new Vector3(x1, heights[(iz * samplesPerAxis) + ix + 1], z0);
                    var p01 = new Vector3(x0, heights[((iz + 1) * samplesPerAxis) + ix], z1);
                    var p11 = new Vector3(x1, heights[((iz + 1) * samplesPerAxis) + ix + 1], z1);

                    // 绕序按「从上方看逆时针」排列，面法线朝上；反了的话地形在 URP 下会整片消失，
                    // 这是程序化网格最常见也最难从画面反推的错误之一。
                    EmitTriangle(vertices, uvs, groundTriangles, cliffTriangles, p00, p01, p11);
                    EmitTriangle(vertices, uvs, groundTriangles, cliffTriangles, p00, p11, p10);
                }
            }

            var mesh = new Mesh { name = "BasinTerrain" };
            mesh.indexFormat = vertices.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(groundTriangles, 0);
            mesh.SetTriangles(cliffTriangles, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// 把网格写入资产路径；已存在时原地覆盖数据而不是删除重建。
        /// </summary>
        /// <remarks>
        /// 原地覆盖的意义在于**引用不失效**：场景里的 MeshCollider 与 MeshFilter 指向的是这个资产，
        /// 若先删再建，Unity 会认为场景引用丢失（Missing），必须重建整个场景才能恢复。
        /// 覆盖则允许「只重生地形、不动场景里的其它对象」，迭代时节省大量时间。
        /// </remarks>
        public static Mesh SaveMeshAsset(Mesh mesh, string assetPath)
        {
            EnsureFolder(System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/'));

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, assetPath);
                return mesh;
            }

            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        /// <summary>输出一个三角形，并按面法线归入草地或黏土子网格。</summary>
        private static void EmitTriangle(
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> groundTriangles,
            List<int> cliffTriangles,
            Vector3 a,
            Vector3 b,
            Vector3 c)
        {
            var normal = Vector3.Cross(b - a, c - a).normalized;
            var isGround = normal.y >= GroundNormalY;

            var index = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);

            // 平面投影 UV：低多边形地形只关心「草纹的密度」，不需要沿坡面展开，
            // 俯视角下平面投影不会产生拉伸感。
            uvs.Add(new Vector2(a.x, a.z) / TextureMetersPerTile);
            uvs.Add(new Vector2(b.x, b.z) / TextureMetersPerTile);
            uvs.Add(new Vector2(c.x, c.z) / TextureMetersPerTile);

            var target = isGround ? groundTriangles : cliffTriangles;
            target.Add(index);
            target.Add(index + 1);
            target.Add(index + 2);
        }

        /// <summary>确保资产目录存在（与场景生成器同名的私有方法保持行为一致）。</summary>
        private static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            var leaf = System.IO.Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
