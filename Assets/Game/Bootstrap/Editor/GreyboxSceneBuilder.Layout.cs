using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 灰盒场景生成器的地图布局部分（M5 的四分区版本）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么布局写成数据表而不是一长串创建调用：</b>
    /// 布局数据（箱子在哪、箱子朝哪、撤离点在哪）与创建逻辑（怎么造一个箱子）
    /// 是两件会分别变化的事。分开之后，调地图只需要改数据表里的一行数字，
    /// 不必读懂创建逻辑；也方便一眼核对「这块地方到底放了什么」。</para>
    ///
    /// <para><b>坐标约定：</b>X 轴向东为正、Z 轴向北为正，地面范围为 ±30 米。
    /// 斜俯视相机从南向北看，因此屏幕上方是 +Z、屏幕右方是 +X。</para>
    ///
    /// <para><b>四分区</b>：主厂房（西，室内近战）、集装箱堆场（东，主搜刮区）、
    /// 装卸平台（南，高地伏击）、外围环道（外圈，撤离点所在，撤离前的最后一段路）。</para>
    /// </remarks>
    public static partial class GreyboxSceneBuilder
    {
        /// <summary>集装箱标准尺寸（长 x 高 x 宽）。</summary>
        private static readonly Vector3 ContainerSize = new Vector3(6f, 3f, 2.5f);

        /// <summary>外墙与围栏的配色。</summary>
        private static readonly Color PerimeterColor = new Color(0.45f, 0.43f, 0.40f);

        /// <summary>建筑墙体配色。</summary>
        private static readonly Color BuildingColor = new Color(0.52f, 0.50f, 0.47f);

        /// <summary>室内隔断配色，比外墙略深以便在俯视下分辨「承重墙」与「隔断」。</summary>
        private static readonly Color PartitionColor = new Color(0.44f, 0.43f, 0.42f);

        /// <summary>围栏配色。</summary>
        private static readonly Color FenceColor = new Color(0.58f, 0.58f, 0.60f);

        /// <summary>厂区内设备与货架的配色。</summary>
        private static readonly Color MachineryColor = new Color(0.40f, 0.44f, 0.48f);

        /// <summary>混凝土掩体配色。</summary>
        private static readonly Color BarrierColor = new Color(0.55f, 0.54f, 0.52f);

        /// <summary>装卸平台配色。</summary>
        private static readonly Color DockColor = new Color(0.42f, 0.42f, 0.44f);

        /// <summary>平台高度（米）。取 1.2 米：约等于一个人的胸口，站上去能获得视野优势，又不至于完全打不到。</summary>
        private const float DockHeight = 1.2f;

        /// <summary>平台范围：X 从 -9 到 13、Z 从 -28 到 -19。</summary>
        private const float DockCenterX = 2f;

        private const float DockCenterZ = -23.5f;

        /// <summary>
        /// 集装箱堆场的箱体摆放表。
        /// </summary>
        /// <remarks>
        /// 按「三排一列」的思路摆放：横放的箱子之间留出 4 米以上的通道，
        /// 让玩家能选择绕行或穿越；箱体本身既是掩体也是视线阻断，
        /// 形成「视野开阔但到处是拐角」的主搜刮区。朝向 0 表示长边沿 X 轴，90 表示沿 Z 轴。
        /// </remarks>
        private static readonly (Vector3 Position, float YawDegrees)[] s_YardContainers =
        {
            (new Vector3(10f, 0f, 2f), 0f),
            (new Vector3(20f, 0f, 2f), 0f),
            (new Vector3(12f, 0f, 8f), 90f),
            (new Vector3(22f, 0f, 8f), 90f),
            (new Vector3(8f, 0f, 14f), 0f),
            (new Vector3(18f, 0f, 14f), 0f),
            (new Vector3(12f, 0f, 20f), 90f),
            (new Vector3(22f, 0f, 20f), 0f),
            (new Vector3(10f, 0f, -3f), 0f),
            (new Vector3(20f, 0f, -3f), 90f),
        };

        /// <summary>
        /// 双层堆叠的集装箱位置。
        /// </summary>
        /// <remarks>
        /// 全场只有一处堆两层：6 米高的箱体会在斜俯视下投出大片阴影并彻底挡住视线，
        /// 给堆场一个明确的「地标」。如果到处都叠两层，地图会变成迷宫，
        /// 玩家反而读不出空间结构。
        /// </remarks>
        private static readonly Vector3 s_StackedContainerPosition = new Vector3(16f, 0f, 11f);

        /// <summary>
        /// 撤离点布局。
        /// </summary>
        /// <remarks>
        /// <para>三个撤离点刻意分布在地图的三个方向：北门（开阔，最危险）、
        /// 西南角（厂房侧，路程短）、东南货场（堆场侧，物资多）。
        /// 玩家可以按自己的贪心程度选择「就近撤」还是「再搜一处再走」。</para>
        ///
        /// <para>全部无条件可用、不要求钥匙：本项目的压力来自玩家的贪心决策，
        /// 而不是来自「找不到撤离点」这种外部障碍。</para>
        /// </remarks>
        private static readonly (int ZoneId, string DisplayName, Vector2 Center, float Radius)[] s_ExtractionZones =
        {
            (1, "北门", new Vector2(0f, 25.5f), 3.5f),
            (2, "西南角", new Vector2(-25f, -25f), 3.5f),
            (3, "东南货场", new Vector2(25f, -25f), 3.5f),
        };

        /// <summary>
        /// 战利品容器摆放表。
        /// </summary>
        /// <remarks>
        /// <para>摆放原则是「价值越高的地方越危险」：普通补给箱多在外围与厂房，
        /// 武器架放在厂房深处与堆场角落，唯一的保险柜在地图西北角的空地上——
        /// 想拿它就必须跑到最远、最没有掩体的地方，这正是贪婪循环要制造的选择。</para>
        ///
        /// <para>GroundY 表示箱子底面所在高度：地面为 0，装卸平台上为 1.2。</para>
        /// </remarks>
        private static readonly (string DefinitionId, Vector2 Position, float GroundY, float YawDegrees)[] s_LootPlacements =
        {
            // 主厂房：室内近战区的战利品，价值偏高
            ("crate.weapon", new Vector2(-12.5f, 11.5f), 0f, 0f),
            ("crate.common", new Vector2(-25f, -12f), 0f, 0f),
            ("crate.medical", new Vector2(-25.5f, 7f), 0f, 0f),
            ("crate.ammo", new Vector2(-11.5f, -12f), 0f, 0f),

            // 集装箱堆场：主搜刮区，数量最多
            ("crate.common", new Vector2(6f, 6f), 0f, 0f),
            ("crate.common", new Vector2(15f, -1f), 0f, 0f),
            ("crate.ammo", new Vector2(25f, 4f), 0f, 0f),
            ("crate.weapon", new Vector2(24f, 16f), 0f, 0f),
            ("crate.ammo", new Vector2(7f, 20f), 0f, 0f),

            // 装卸平台：高台上的补给，需要先爬坡
            ("crate.common", new Vector2(-6f, -22f), DockHeight, 0f),
            ("crate.medical", new Vector2(8f, -22f), DockHeight, 0f),
            ("crate.common", new Vector2(0f, -21f), DockHeight, 0f),

            // 外围环道：最远的目标与撤离点附近的补给
            ("safe.rare", new Vector2(-24f, 20f), 0f, 0f),
            ("crate.ammo", new Vector2(22f, -20f), 0f, 0f),
            ("crate.common", new Vector2(0f, 20f), 0f, 0f),
        };

        /// <summary>创建四面外围围墙，把玩家限制在场景内。</summary>
        private static void CreatePerimeterWalls()
        {
            var parent = CreateGroup("Perimeter");
            var half = (GroundSize * 0.5f) - (WallThickness * 0.5f);

            CreateWall("Wall_North", 0f, half, GroundSize, true, WallHeight, WallThickness, parent, PerimeterColor);
            CreateWall("Wall_South", 0f, -half, GroundSize, true, WallHeight, WallThickness, parent, PerimeterColor);
            CreateWall("Wall_East", half, 0f, GroundSize, false, WallHeight, WallThickness, parent, PerimeterColor);
            CreateWall("Wall_West", -half, 0f, GroundSize, false, WallHeight, WallThickness, parent, PerimeterColor);
        }

        /// <summary>
        /// 创建主厂房：西侧的长条形建筑，室内近战交火区。
        /// </summary>
        /// <remarks>
        /// <para>厂房不做屋顶。斜俯视相机会被屋顶完全挡住内部，而灰盒阶段最重要的是
        /// 「玩家能看见里面有什么」。缺的屋顶由三面外墙与内部隔断在视觉上补足，
        /// 玩家仍然能一眼读出这是室内空间。</para>
        ///
        /// <para>出入口刻意留了四个（东侧两个、北侧一个、南侧一个）：
        /// 只有一个入口的建筑在俯视射击里会变成「谁先进谁死」的屠宰场，
        /// 多开几个口子才能让交火有迂回。</para>
        /// </remarks>
        private static void CreateFactoryZone()
        {
            var parent = CreateGroup("Zone_Factory");
            const float height = 3.2f;
            const float thickness = 0.6f;

            // 外墙：西侧完整，东/北/南各留出入口
            CreateWall("Factory_Wall_West", -27f, 0f, 28f, false, height, thickness, parent, BuildingColor);
            CreateWall("Factory_Wall_East_South", -10f, -8f, 12f, false, height, thickness, parent, BuildingColor);
            CreateWall("Factory_Wall_East_North", -10f, 8f, 12f, false, height, thickness, parent, BuildingColor);
            CreateWall("Factory_Wall_North_West", -22f, 14f, 10f, true, height, thickness, parent, BuildingColor);
            CreateWall("Factory_Wall_North_East", -11.5f, 14f, 3f, true, height, thickness, parent, BuildingColor);
            CreateWall("Factory_Wall_South_West", -24f, -14f, 6f, true, height, thickness, parent, BuildingColor);
            CreateWall("Factory_Wall_South_East", -13.5f, -14f, 7f, true, height, thickness, parent, BuildingColor);

            // 内部隔断：把厂房切成三个厅与两条通道，制造近战距离上的拐角
            CreateWall("Factory_Partition_West", -19f, -5f, 18f, false, height, thickness, parent, PartitionColor);
            CreateWall("Factory_Partition_North", -24.5f, 4f, 5f, true, height, thickness, parent, PartitionColor);
            CreateWall("Factory_Partition_South", -15f, -7f, 14f, false, height, thickness, parent, PartitionColor);

            // 设备与货架：既是掩体也是视线阻断
            var machinery = new (Vector3 Position, Vector3 Size)[]
            {
                (new Vector3(-24f, 0f, -10f), new Vector3(2.5f, 2.2f, 1.2f)),
                (new Vector3(-23f, 0f, 10.5f), new Vector3(2f, 1.6f, 2f)),
                (new Vector3(-13f, 0f, 8f), new Vector3(1.6f, 2.4f, 1.6f)),
                (new Vector3(-22f, 0f, -1f), new Vector3(1.2f, 1f, 3f)),
                (new Vector3(-12.5f, 0f, -8f), new Vector3(2f, 1.4f, 1.4f)),
            };

            for (var i = 0; i < machinery.Length; i++)
            {
                var (position, size) = machinery[i];
                var machine = CreateBox(
                    $"Factory_Machine_{i + 1:D2}",
                    new Vector3(position.x, size.y * 0.5f, position.z),
                    size,
                    parent);
                SetMaterialColor(machine, MachineryColor);
            }
        }

        /// <summary>创建集装箱堆场：地图东侧的主搜刮区。</summary>
        private static void CreateContainerYardZone()
        {
            var parent = CreateGroup("Zone_ContainerYard");

            for (var i = 0; i < s_YardContainers.Length; i++)
            {
                var (position, yaw) = s_YardContainers[i];
                CreateContainer(parent, i + 1, position, yaw, layers: 1);
            }

            // 唯一的双层堆叠：作为堆场的地标，同时提供一块彻底阻断视线的硬掩体
            CreateContainer(parent, s_YardContainers.Length + 1, s_StackedContainerPosition, 0f, layers: 2);
        }

        /// <summary>
        /// 创建一个集装箱（可堆叠两层）。
        /// </summary>
        /// <param name="parent">父节点。</param>
        /// <param name="index">编号，用于命名与配色交替。</param>
        /// <param name="position">底面中心位置（y 取 0）。</param>
        /// <param name="yawDegrees">水平朝向。</param>
        /// <param name="layers">层数。</param>
        private static void CreateContainer(
            Transform parent,
            int index,
            Vector3 position,
            float yawDegrees,
            int layers)
        {
            for (var layer = 0; layer < layers; layer++)
            {
                var center = new Vector3(
                    position.x,
                    (ContainerSize.y * (layer + 0.5f)),
                    position.z);
                var name = layers > 1
                    ? $"Container_{index:D2}_L{layer + 1}"
                    : $"Container_{index:D2}";

                var container = CreateBox(name, center, ContainerSize, parent);
                container.transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);

                // 交替使用两种明确的橙色调，便于在斜俯视下区分相邻箱体、判断空间关系。
                // 注意配色必须是「红 > 绿 > 蓝」的暖色系才能读出集装箱的观感：
                // 若绿色分量高于红色，物体在 URP 光照下会呈现紫色，与预期完全相反。
                var containerColor = index % 2 == 0
                    ? new Color(0.82f, 0.48f, 0.22f)
                    : new Color(0.68f, 0.38f, 0.18f);
                SetMaterialColor(container, containerColor);
            }
        }

        /// <summary>
        /// 创建装卸平台：南侧的高台与两条坡道。
        /// </summary>
        /// <remarks>
        /// <para>高台是本作唯一的高低差：站在上面能越过大部分掩体看到堆场，
        /// 但上去只有两条坡道，撤离时也容易被堵。它同时验证了三件事——
        /// 玩家地面吸附、玩家移动碰撞的坡道处理、AI 的导航网格高度采样。</para>
        ///
        /// <para>坡道正对平台北侧边缘，从地面直接接上平台面：坡道的高端必须与台面同高，
        /// 否则会在衔接处形成一个上不去的台阶。</para>
        /// </remarks>
        private static void CreateLoadingDockZone()
        {
            var parent = CreateGroup("Zone_LoadingDock");

            // 台体
            var platform = CreateBox(
                "Dock_Platform",
                new Vector3(DockCenterX, DockHeight * 0.5f, DockCenterZ),
                new Vector3(22f, DockHeight, 9f),
                parent);
            SetMaterialColor(platform, DockColor);

            // 两条坡道，分别位于台体东、西两端，让上下台都有两条路
            CreateRamp("Dock_Ramp_West", -4f, -15f, -19f, 3.5f, DockHeight, 0.3f, parent);
            CreateRamp("Dock_Ramp_East", 8f, -15f, -19f, 3.5f, DockHeight, 0.3f, parent);

            // 台上的货箱与挡墙：挡墙沿西、东两端布置，留着北面朝向坡道
            var dockProps = new (Vector3 Position, Vector3 Size, Color Color)[]
            {
                (new Vector3(-3f, DockHeight, -25f), new Vector3(2f, 1.4f, 2f), new Color(0.62f, 0.50f, 0.32f)),
                (new Vector3(5f, DockHeight, -26f), new Vector3(2.5f, 1.2f, 2f), new Color(0.58f, 0.47f, 0.30f)),
                (new Vector3(-8.4f, DockHeight, -23.5f), new Vector3(0.8f, 0.9f, 9f), BarrierColor),
                (new Vector3(12.4f, DockHeight, -23.5f), new Vector3(0.8f, 0.9f, 9f), BarrierColor),
            };

            for (var i = 0; i < dockProps.Length; i++)
            {
                var (position, size, color) = dockProps[i];
                var prop = CreateBox(
                    $"Dock_Prop_{i + 1:D2}",
                    new Vector3(position.x, position.y + (size.y * 0.5f), position.z),
                    size,
                    parent);
                SetMaterialColor(prop, color);
            }
        }

        /// <summary>
        /// 创建外围环道：围栏与散落的混凝土掩体。
        /// </summary>
        /// <remarks>
        /// 外围是玩家往返于各分区与撤离点之间的必经之路，如果完全空旷，
        /// 就会退化成「谁先看见谁赢」的长距离对枪。几段围栏把环道切成有拐角的走廊，
        /// 让移动本身也需要判断。
        /// </remarks>
        private static void CreateOuterRing()
        {
            var parent = CreateGroup("Zone_OuterRing");
            const float fenceHeight = 2.2f;
            const float fenceThickness = 0.25f;

            CreateWall("Fence_SpawnEast", 3f, -10f, 8f, false, fenceHeight, fenceThickness, parent, FenceColor);
            CreateWall("Fence_NorthMid", -16f, 18f, 8f, true, fenceHeight, fenceThickness, parent, FenceColor);
            CreateWall("Fence_DockEast", 16f, -15f, 6f, false, fenceHeight, fenceThickness, parent, FenceColor);
            CreateWall("Fence_YardNorth", 20f, 26f, 10f, true, fenceHeight, fenceThickness, parent, FenceColor);

            var barriers = new (Vector3 Position, Vector3 Size)[]
            {
                (new Vector3(-14f, 0f, 22f), new Vector3(3f, 1.4f, 0.8f)),
                (new Vector3(6f, 0f, 22f), new Vector3(3f, 1.4f, 0.8f)),
                (new Vector3(-26f, 0f, -20f), new Vector3(0.8f, 1.4f, 3f)),
                (new Vector3(28f, 0f, 14f), new Vector3(0.8f, 1.4f, 3f)),
            };

            for (var i = 0; i < barriers.Length; i++)
            {
                var (position, size) = barriers[i];
                var barrier = CreateBox(
                    $"Ring_Barrier_{i + 1:D2}",
                    new Vector3(position.x, size.y * 0.5f, position.z),
                    size,
                    parent);
                SetMaterialColor(barrier, BarrierColor);
            }
        }

    }
}
