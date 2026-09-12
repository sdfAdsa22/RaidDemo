using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 灰盒场景生成器的分区布局（装卸平台、外围环道与工业地标）。
    /// </summary>
    /// <remarks>
    /// <para>从 Layout 文件拆出：单文件超过 400 行会触发工程规范检查（见 PathRulesTests）。</para>
    /// <para>拆分按「内容分区」而不是按行数硬切：装卸平台与外围环道都是「在谷底之上加高低差与掩体」的分区，
    /// 放在一起便于对照；厂房与集装箱堆场的布局仍在 Layout 文件里。</para>
    /// <para>M7 批次 2 起这些分区都位于下沉盆地的谷底（布局坐标 y 等于 0 表示谷底），
    /// 世界坐标的换算由灰盒工厂与预制体摆放工具统一完成。</para>
    /// </remarks>
    public static partial class GreyboxSceneBuilder
    {
        /// 创建装卸平台：南侧的高台与两条坡道。
        /// </summary>
        /// <remarks>
        /// <para>高台是本作唯一的高低差：站在上面能越过大部分掩体看到堆场，
        /// 但上去只有两条坡道，撤离时也容易被堵。它同时验证了三件事——
        /// 玩家地面吸附、玩家移动碰撞的坡道处理、AI 的导航网格高度采样。</para>
        ///
        /// <para>坡道正对平台北侧边缘，从地面直接接上平台面。高端刻意向台体内多伸入
        /// 0.5 米：如果坡道的高端刚好停在台缘，坡道板的厚度端面会在台缘前留下一条
        /// 极窄的斜面缝——向下射线会先打到端面而不是坡道顶面，角色在这里会被卡住。
        /// 伸入台体后，坡道顶面连续覆盖到台面下方，衔接处不再有可见的端面。</para>
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
            CreateRamp("Dock_Ramp_West", -4f, -15f, -19.5f, 3.5f, DockHeight, 0.3f, parent);
            CreateRamp("Dock_Ramp_East", 8f, -15f, -19.5f, 3.5f, DockHeight, 0.3f, parent);

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

            // M7 批次 2：围栏外观换成 Toon Shooter 的铁丝网，阻挡仍由与旧灰盒等尺寸的隐形代理承担，
            // 因此 AI 的绕行路线与玩家的碰撞手感与替换前一致。
            CreateFenceLine("Fence_SpawnEast", 3f, -10f, 8f, alongX: false, fenceHeight, fenceThickness, parent);
            CreateFenceLine("Fence_NorthMid", -16f, 18f, 8f, alongX: true, fenceHeight, fenceThickness, parent);
            CreateFenceLine("Fence_DockEast", 16f, -15f, 6f, alongX: false, fenceHeight, fenceThickness, parent);
            CreateFenceLine("Fence_YardNorth", 20f, 26f, 10f, alongX: true, fenceHeight, fenceThickness, parent);

            var barriers = new (Vector3 Position, float Yaw)[]
            {
                (new Vector3(-14f, 0f, 22f), 0f),
                (new Vector3(6f, 0f, 22f), 0f),
                (new Vector3(-26f, 0f, -20f), 90f),
                (new Vector3(26f, 0f, 14f), 90f),
            };

            for (var i = 0; i < barriers.Length; i++)
            {
                var (position, yaw) = barriers[i];
                var instance = InstantiateProp("Prop_Barrier", position, yaw, parent);
                if (instance != null)
                {
                    instance.name = $"Ring_Barrier_{i + 1:D2}";
                    continue;
                }

                // 素材缺失时的灰盒回退：尺寸与旧版本一致，保证地图结构不变。
                var size = new Vector3(3f, 1.4f, 0.8f);
                var barrier = CreateBox(
                    $"Ring_Barrier_{i + 1:D2}",
                    new Vector3(position.x, size.y * 0.5f, position.z),
                    size,
                    parent);
                barrier.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                SetMaterialColor(barrier, BarrierColor);
            }
        }

        /// <summary>
        /// 工业地标：水塔与储罐。
        /// </summary>
        /// <remarks>
        /// <para>斜俯视相机的可视范围只有身前十米左右，玩家很容易在相似的草地与集装箱之间失去方位感。
        /// 高体量的地标（7.5 米的水塔）能在很远的地方露出顶部，是「我在哪、要往哪走」的主要线索。</para>
        ///
        /// <para>位置避开了巡逻路线的拐点、战利品容器与围栏：水塔放在堆场东侧的空地，
        /// 储罐放在厂房北墙外的空地，两者都不改变任何一条通道的宽度。</para>
        ///
        /// <para>这两个是纯装饰件，没有灰盒回退：素材缺失时地图仍然完整可玩，只是少了两处地标，
        /// 因此不值得为它们再维护一套占位几何。</para>
        /// </remarks>
        private static void CreateIndustrialLandmarks()
        {
            var parent = CreateGroup("Zone_Landmarks");
            var waterTower = InstantiateProp("Prop_WaterTower", new Vector3(26.5f, 0f, 10f), 0f, parent);
            if (waterTower != null)
            {
                waterTower.name = "Landmark_WaterTower";
            }

            var tank = InstantiateProp("Prop_Tank", new Vector3(-25.5f, 0f, 16.5f), 25f, parent);
            if (tank != null)
            {
                tank.name = "Landmark_Tank";
            }
        }
    }
}
