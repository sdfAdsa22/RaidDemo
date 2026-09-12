using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 灰盒场景生成器的「盆地地形与边界」部分。
    /// </summary>
    /// <remarks>
    /// <para>本文件负责三件事：生成地形的网格与导航表面、沿土墙铺一圈悬崖装饰瓦片、
    /// 在地图外圈摆一圈远景山石并用隐形墙收口。三者都属于「地图边界」这个主题，
    /// 因此放在一起；玩法内容（厂房、堆场、撤离点）仍在 Layout 文件里。</para>
    ///
    /// <para><b>素材缺失时的行为：</b>悬崖瓦片与远景山石都是外部素材，仓库里不一定存在
    /// （Broken Vector 的授权明确不允许再分发原始文件，所以它永远不在仓库里）。
    /// 缺少时本文件只做两件事：跳过装饰、由解析器汇总一条提示。
    /// 地形本身是程序化生成的，因此**任何情况下都能得到一张完整可玩的地图**。</para>
    /// </remarks>
    public static partial class GreyboxSceneBuilder
    {
        /// <summary>地形网格资产的保存路径。</summary>
        private const string TerrainMeshPath = "Assets/Game/Content/Art/Environment/BasinTerrain.asset";

        /// <summary>共享的地形剖面实例（只读使用，方法本身无状态）。</summary>
        private static readonly BasinTerrainProfile s_TerrainProfile = new BasinTerrainProfile();

        /// <summary>坡道与谷口两侧要为通道让出的半宽（米），比通道本身略宽以留出视觉余量。</summary>
        private const float CorridorDecorationGap = 4.4f;

        /// <summary>远景山石数量。环向均匀铺开，保证任何方向看出去都有山体。</summary>
        private const int FarRidgeCount = 36;

        /// <summary>创建盆地地形：程序化网格 + 双材质 + 网格碰撞体 + 导航烘焙表面。</summary>
        private static void CreateBasinTerrain()
        {
            var mesh = BasinTerrainMeshBuilder.BuildMesh(s_TerrainProfile, ValleyFloorY);
            var savedMesh = BasinTerrainMeshBuilder.SaveMeshAsset(mesh, TerrainMeshPath);

            var terrain = new GameObject("Terrain_Basin");
            terrain.AddComponent<MeshFilter>().sharedMesh = savedMesh;

            var renderer = terrain.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[]
            {
                BasinTerrainMaterialBuilder.EnsureGrassMaterial(),
                BasinTerrainMaterialBuilder.EnsureClayMaterial()
            };

            // 网格碰撞体直接复用可见网格：地形是静态的，不需要简化碰撞体，
            // 而且「看到的地面」与「走上去的地面」完全一致，不会出现视觉与碰撞错位。
            terrain.AddComponent<MeshCollider>().sharedMesh = savedMesh;

            // 导航表面继续用运行时烘焙（原因见主文件的说明）。
            // 采集范围是全场物理碰撞体：地形、土墙装饰之外的实体、边界隐形墙都会被烘进去。
            var surface = terrain.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        }

        /// <summary>
        /// 沿四条土墙铺一圈悬崖瓦片。
        /// </summary>
        /// <remarks>
        /// <para>瓦片只做「装饰」：不挂碰撞体、不参与导航烘焙（<c>ignoreFromBuild</c>）。
        /// 因为土墙的坡度已经由地形网格本身保证（超过 45 度，胶囊扫掠会挡住、导航不会烘上去），
        /// 装饰件再参与物理只会带来两类麻烦——瓦片之间的小缝卡住角色、装饰的顶面被当成可站立地面。</para>
        ///
        /// <para>间距由**实测模型宽度**决定，而不是写死的 4 米：Broken Vector 的瓦片是按整数格设计的，
        /// 但不同版本尺寸略有差异，量一次比赌一次可靠。</para>
        /// </remarks>
        private static void CreateCliffRing()
        {
            var tiles = LoadCliffTiles();
            if (tiles.Count == 0)
            {
                return;
            }

            var parent = CreateGroup("Cliff_Decor");
            var reference = tiles[0];
            var scale = BasinTerrainProfile.RimHeight / Mathf.Max(0.001f, reference.Height);
            var spacing = reference.Width * scale * 0.98f;
            var half = BasinTerrainProfile.ValleyHalfExtent;
            var material = BasinTerrainMaterialBuilder.EnsureCliffTileMaterial();

            // 北墙（面向谷内，即朝 -Z）、南墙（朝 +Z）、西墙（朝 +X）、东墙（朝 -X）。
            // 最后一个参数是墙体向外的方向符号：北/东墙朝正轴延伸，南/西墙朝负轴延伸，
            // 瓦片必须贴着坡体一侧摆放，符号写反时瓦片会整排插进谷底。
            PlaceCliffRow(parent, tiles, scale, spacing, -half, half, half, true, 180f, 1f, BasinTerrainProfile.NorthRampCenterX, material);
            PlaceCliffRow(parent, tiles, scale, spacing, -half, half, -half, true, 0f, -1f, BasinTerrainProfile.SouthCanyonCenterX, material);
            PlaceCliffRow(parent, tiles, scale, spacing, -half, half, -half, false, 90f, -1f, BasinTerrainProfile.WestRampCenterZ, material);
            PlaceCliffRow(parent, tiles, scale, spacing, -half, half, half, false, -90f, 1f, BasinTerrainProfile.EastRampCenterZ, material);
        }

        /// <summary>载入并测量可用的悬崖瓦片（数量不足时返回空表，由调用方跳过装饰）。</summary>
        private static List<CliffTile> LoadCliffTiles()
        {
            var names = new[]
            {
                "Clifftile Straight 1", "Clifftile Straight 2", "Clifftile Straight 3",
                "Clifftile Convex", "Clifftile Concave", "Clifftile Diagonal"
            };

            var tiles = new List<CliffTile>();
            foreach (var name in names)
            {
                var model = M7SceneAssetResolver.LoadCliffTile(name);
                if (model == null)
                {
                    continue;
                }

                if (MeasureAsset(model, out var bounds))
                {
                    tiles.Add(new CliffTile(model, bounds.size.x, bounds.size.z, bounds.size.y));
                }
            }

            return tiles;
        }

        /// <summary>沿一条墙线铺瓦片，自动跳过坡道与谷口占用的位置。</summary>
        private static void PlaceCliffRow(
            Transform parent,
            List<CliffTile> tiles,
            float scale,
            float spacing,
            float start,
            float end,
            float fixedCoordinate,
            bool alongX,
            float yaw,
            float outwardSign,
            float gapCenter,
            Material material)
        {
            var index = 0;
            for (var cursor = start + (spacing * 0.5f); cursor <= end - (spacing * 0.5f); cursor += spacing)
            {
                index++;
                // 通道口要留出缺口：坡道的横向中心已经不在原点（东/西坡道分别在 z=25.5 与 -20），
                // 若仍按「靠近 0 就跳过」判断，瓦片会整排堵在新坡道的入口上。
                if (Mathf.Abs(cursor - gapCenter) <= CorridorDecorationGap)
                {
                    continue;
                }

                var tile = tiles[index % tiles.Count];
                // 旋转 90 度后瓦片的“进深”仍然沿墙体的法线方向，因此两种情况用同一个公式。
                var depth = tile.Depth * scale;
                var offset = fixedCoordinate + ((depth * 0.5f) - 0.25f) * outwardSign;
                var position = alongX
                    ? new Vector3(cursor, 0f, offset)
                    : new Vector3(offset, 0f, cursor);

                var instance = InstantiateDecoration(tile.Model, position, yaw, parent, scale, material);
                if (instance != null)
                {
                    instance.name = $"Cliff_{index:D2}";
                }
            }
        }


        /// <summary>
        /// 实例化一个纯装饰（无碰撞、不参与导航烘焙）的外部模型。
        /// </summary>
        private static GameObject InstantiateDecoration(
            GameObject model,
            Vector3 valleyPosition,
            float yawDegrees,
            Transform parent,
            float uniformScale,
            Material overrideMaterial = null)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.transform.position = new Vector3(
                valleyPosition.x,
                valleyPosition.y + ValleyFloorY,
                valleyPosition.z);
            instance.transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            instance.transform.localScale = Vector3.one * uniformScale;
            instance.transform.SetParent(parent, worldPositionStays: true);

            if (overrideMaterial != null)
            {
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
                {
                    // 必须整份替换 sharedMaterials：模型网格可能有多个子网格
                    // （Kenney 的山石就是"岩石 + 草"两个槽位），只写 sharedMaterial 只会替换第一个槽，
                    // 剩下的槽位会保留资源包原本的材质——表现就是石头上长出一块青色的面。
                    var slots = renderer.sharedMaterials.Length;
                    var materials = new Material[slots];
                    for (var i = 0; i < slots; i++)
                    {
                        materials[i] = overrideMaterial;
                    }

                    renderer.sharedMaterials = materials;
                }
            }

            foreach (var collider in instance.GetComponentsInChildren<Collider>())
            {
                Object.DestroyImmediate(collider);
            }

            var modifier = instance.GetComponent<NavMeshModifier>();
            if (modifier == null)
            {
                modifier = instance.AddComponent<NavMeshModifier>();
            }

            modifier.ignoreFromBuild = true;
            return instance;
        }

        /// <summary>
        /// 从模型自带的材质里取基础贴图，生成/复用一个项目 URP 材质。
        /// </summary>
        /// <remarks>
        /// <para>Kenney 的自然套件模型是**纯色材质**（材质名形如 dirt / grass / stone），包里没有贴图，
        /// 而导入后的材质基础色是白色——直接沿用会得到一片惨白的"冰晶"，与暖色黏土土墙完全不搭。
        /// 因此取不到贴图时使用给定的石灰岩灰作为基础色。</para>
        /// <para>资源包自带的材质多为内置管线，直接使用会在 URP 下渲染成粉色，必须重赋。</para>
        /// </remarks>
        private static Material EnsureMaterialFromModel(GameObject model, string materialName, Color fallbackColor)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            Texture2D texture = null;
            var sourceColor = fallbackColor;
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
            {
                if (renderer.sharedMaterial == null)
                {
                    continue;
                }

                if (renderer.sharedMaterial.mainTexture is Texture2D candidate)
                {
                    texture = candidate;
                    break;
                }
            }

            Object.DestroyImmediate(instance);
            var path = $"{BasinTerrainMaterialBuilder.MaterialsFolder}/{materialName}.mat";
            // 远景山石也要参与开孔：站在塬面边缘时它们同样会挡在相机与角色之间。
            return M7MaterialLibrary.EnsureLitMaterial(
                path,
                sourceColor,
                texture,
                smoothness: 0.06f,
                occluder: true);
        }

        /// <summary>把模型实例化一次以测量包围盒（预制体资产本身读不到渲染器边界）。</summary>
        private static bool MeasureAsset(GameObject model, out Bounds bounds)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            var measured = M7PropPrefabBuilder.TryMeasureBounds(instance, out bounds);
            Object.DestroyImmediate(instance);
            return measured;
        }

        /// <summary>悬崖瓦片的实测尺寸（米，未缩放）。</summary>
        private readonly struct CliffTile
        {
            public CliffTile(GameObject model, float width, float depth, float height)
            {
                Model = model;
                Width = width;
                Depth = depth;
                Height = height;
            }

            public GameObject Model { get; }

            public float Width { get; }

            public float Depth { get; }

            public float Height { get; }
        }
    }
}
