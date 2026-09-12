using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 灰盒场景生成器的远景与边界：外圈山石、远景工业建筑与隐形边界墙。
    /// </summary>
    /// <remarks>
    /// <para>从 Terrain 文件拆出（单文件行数上限见 PathRulesTests）。主题上它们是同一件事：
    /// 「地图之外那圈看不到的东西」。盆地本身怎么长、土墙怎么贴装饰仍在 Terrain 文件里。</para>
    /// <para>这里的所有装饰都是纯视觉的：没有碰撞体、不参与导航烘焙；
    /// 唯一有碰撞体的是隐形边界墙，它负责把玩家与 AI 都留在 72×72 米之内。</para>
    /// </remarks>
    public static partial class GreyboxSceneBuilder
    {        /// <summary>
        /// 在地图外圈摆一圈远景山石。
        /// </summary>
        /// <remarks>
        /// <para>站上塬面撤离时，玩家的视线会越过地形外沿看到远处，若那里是空的，
        /// 「下沉盆地」的空间感会立刻垮掉。这一圈山石只做视觉：没有碰撞体、不参与导航烘焙。</para>
        ///
        /// <para>高度取 7~14 米、切比雪夫距离取 47~55 米：这个距离既保证从塬面望出去时山脊明显高于地平线，
        /// 又留出足够余量，避免相机跟随玩家走到地图外沿时正好卡进山石内部
        /// （玩家的相机在身后约 6 米处，如果山石贴着边界放，站在南侧边缘就会对着石头的内部渲染）。</para>
        /// </remarks>
        private static void CreateFarRidge()
        {
            var models = M7SceneAssetResolver.LoadAllNatureRocks();
            if (models.Count == 0)
            {
                return;
            }

            var parent = CreateGroup("Far_Ridge");
            var random = new System.Random(7202);
            CreateFarWarehouses(parent);
            // 石灰岩灰：与橙色黏土土墙同属暖色系，远看是山，不会像白色模型那样"发光"。
            var rockMaterial = EnsureMaterialFromModel(
                models[0],
                "M_NatureRock",
                new Color(0.52f, 0.50f, 0.47f));

            for (var i = 0; i < FarRidgeCount; i++)
            {
                var angle = (i / (float)FarRidgeCount) * Mathf.PI * 2f;
                var radius = 47f + ((float)random.NextDouble() * 8f);
                var x = SquareRingOffset(angle, radius, out var z);

                var model = models[i % models.Count];
                if (!MeasureAsset(model, out var bounds) || bounds.size.y <= 0.001f)
                {
                    continue;
                }

                var targetHeight = 7f + ((float)random.NextDouble() * 7f);
                var scale = targetHeight / bounds.size.y;
                var ground = s_TerrainProfile.SampleHeight(x, z);

                // 底部压进地面 0.8 米：山石底面是平的，直接坐在缓坡上会露出一条缝。
                var instance = InstantiateDecoration(
                    model,
                    new Vector3(x, ground - 0.8f, z),
                    (float)random.NextDouble() * 360f,
                    parent,
                    scale,
                    rockMaterial);
                if (instance != null)
                {
                    instance.name = $"Ridge_{i:D2}";
                }
            }
        }

        /// <summary>
        /// 在地图外侧的四个斜角方向摆几栋工业建筑，作为远景天际线的一部分。
        /// </summary>
        /// <remarks>
        /// <para>只放斜角方向：正北/正东/正西正好是三条坡道撤离点的视线方向，
        /// 建筑放在那里会挡住玩家爬上塬面后想看的地平线，反而削弱「爬出来了」的成就感。</para>
        ///
        /// <para>建筑与山石都只做视觉，没有碰撞体也不参与导航烘焙（见 <see cref="InstantiateDecoration"/>）。</para>
        /// </remarks>
        private static void CreateFarWarehouses(Transform parent)
        {
            var names = new[] { "Prop_Warehouse_A", "Prop_Warehouse_B" };
            for (var i = 0; i < 4; i++)
            {
                var prefab = M7PropPrefabBuilder.LoadPrefab(names[i % names.Length]);
                if (prefab == null)
                {
                    return;
                }

                var angle = (Mathf.PI * 0.25f) + (i * Mathf.PI * 0.5f);
                var radius = 48f + (i * 1.8f);
                var x = SquareRingOffset(angle, radius, out var z);

                // 让建筑正面朝向地图中心：模型本地 +Z 为正面，yaw = atan2(方向X, 方向Z)。
                var yaw = Mathf.Atan2(-Mathf.Cos(angle), -Mathf.Sin(angle)) * Mathf.Rad2Deg;
                var position = new Vector3(x, s_TerrainProfile.SampleHeight(x, z) - 0.4f, z);
                InstantiateDecoration(prefab, position, yaw, parent, 1.2f);
            }
        }

        /// <summary>
        /// 按「切比雪夫距离」把环形方向换算成方形环上的坐标。
        /// </summary>
        /// <param name="angle">方向角（弧度）。</param>
        /// <param name="ringDistance">到中心的切比雪夫距离（米）。</param>
        /// <param name="z">输出 Z 坐标。</param>
        /// <returns>X 坐标。</returns>
        /// <remarks>
        /// 地形剖面用的是方形等距（max(|x|,|z|)），因此外圈装饰若按欧氏半径摆放，
        /// 斜角方向会落到更靠内的等高线上——45 度方向摆 45 米的石头，其实会落在 31.8 米的土墙上。
        /// 这里先把方向归一化到方形边上，装饰物才会沿着与地形一致的等高线排布。
        /// </remarks>
        private static float SquareRingOffset(float angle, float ringDistance, out float z)
        {
            var directionX = Mathf.Cos(angle);
            var directionZ = Mathf.Sin(angle);
            var scale = ringDistance / Mathf.Max(Mathf.Abs(directionX), Mathf.Abs(directionZ));
            z = directionZ * scale;
            return directionX * scale;
        }

        /// <summary>创建四面隐形边界墙，把玩家与 AI 都限制在 72×72 的地图内。</summary>
        private static void CreateBoundaryBarriers()
        {
            var parent = CreateGroup("Boundary");
            var half = BasinTerrainProfile.RimHalfExtent;
            var span = (half * 2f) + 2f;
            const float height = 4f;
            const float thickness = 1f;

            // 北/东/西三面站在塬面高度上；南面是矮丘，边界随之降低 1.5 米。
            var northCenterY = BasinTerrainProfile.RimHeight + (height * 0.5f);
            var southCenterY = BasinTerrainProfile.SouthRimHeight + (height * 0.5f);
            var offset = half + (thickness * 0.5f);

            CreateInvisibleBarrier(
                "Boundary_North",
                new Vector3(0f, northCenterY, offset),
                new Vector3(span, height, thickness),
                parent);
            CreateInvisibleBarrier(
                "Boundary_South",
                new Vector3(0f, southCenterY, -offset),
                new Vector3(span, height, thickness),
                parent);
            CreateInvisibleBarrier(
                "Boundary_West",
                new Vector3(-offset, northCenterY, 0f),
                new Vector3(thickness, height, span),
                parent);
            CreateInvisibleBarrier(
                "Boundary_East",
                new Vector3(offset, northCenterY, 0f),
                new Vector3(thickness, height, span),
                parent);
        }
    }
}
