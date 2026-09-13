using System.Collections.Generic;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 道具预制体的配方表：来源、源模型名、目标高度、碰撞体与透视孔标记。
    /// </summary>
    /// <remarks>
    /// <para>从 <c>M7PropPrefabBuilder.cs</c> 拆出：配方是「数据」，加工流程是「逻辑」，
    /// 两者变化的原因不同；批次 6 加了 16 条工业区与车辆配方之后，
    /// 继续放在同一个文件里会顶破单文件 400 行的工程规范上限。</para>
    ///
    /// <para><b>高度原则：</b>集装箱 2.59 米、水塔 7.5 米这类大件的目标高度照搬现实比例；
    /// 机器、货箱按「俯视下是否能挡住一个站立角色」定，管线与地面标识刻意做薄，
    /// 只提供视觉层次，不参与掩体与导航。</para>
    /// </remarks>
    public static partial class M7PropPrefabBuilder
    {
        /// <summary>道具来源：决定用哪个加载器与哪份共享材质。</summary>
        private enum SourceKind
        {
            /// <summary>Kenney City Kit (Industrial)：集装箱与工业点缀。</summary>
            CityKit,

            /// <summary>Kenney Factory Kit：厂房机器、管线、检修平台（批次 6）。</summary>
            FactoryKit,

            /// <summary>Kenney Car Kit：卡车 / 厢式车 / 挖掘装载机等车辆（批次 6）。</summary>
            CarKit,

            /// <summary>Kenney Furniture Kit：安全屋的货架、柜台与陈设（M8 开篇）。</summary>
            Furniture,

            /// <summary>Kenney Survival Kit：木箱、油桶、工作台与金属门框（M8 开篇）。</summary>
            Survival,

            /// <summary>Quaternius Toon Shooter：木箱、纸箱、围栏。</summary>
            ToonShooter
        }

        /// <summary>一个道具的加工配方。</summary>
        private readonly struct PropRecipe
        {
            public PropRecipe(
                SourceKind source,
                string sourceName,
                string prefabName,
                float targetHeight,
                bool addCollider,
                bool occluder = false)
            {
                Source = source;
                SourceName = sourceName;
                PrefabName = prefabName;
                TargetHeight = targetHeight;
                AddCollider = addCollider;
                Occluder = occluder;
            }

            public SourceKind Source { get; }

            public string SourceName { get; }

            public string PrefabName { get; }

            public float TargetHeight { get; }

            public bool AddCollider { get; }

            /// <summary>是否使用带透视孔的材质（会挡住相机的大件）。</summary>
            public bool Occluder { get; }
        }

        /// <summary>
        /// 全部道具配方。
        /// </summary>
        /// <remarks>
        /// 高度按 M7 技术标准 2.1 节：集装箱 2.59 米、小道具 0.5~1.4 米、围栏 2.2 米左右。
        /// 数值写在这里而不是散落在场景里，是为了让「场景道具到底多大」只有一个答案。
        /// </remarks>
        private static readonly PropRecipe[] s_Recipes =
        {
            // 集装箱三型：海运标准箱比例（长 6.06 × 宽 2.44 × 高 2.59），三种涂装做视觉变化
            new PropRecipe(SourceKind.CityKit, "shipping-container-a", "Prop_Container_A", 2.59f, true, occluder: true),
            new PropRecipe(SourceKind.CityKit, "shipping-container-b", "Prop_Container_B", 2.59f, true, occluder: true),
            new PropRecipe(SourceKind.CityKit, "shipping-container-c", "Prop_Container_C", 2.59f, true, occluder: true),

            // 工业点缀：储罐与水塔作为堆场地标，刻意做高，让玩家在俯视下也能定位
            new PropRecipe(SourceKind.CityKit, "detail-tank", "Prop_Tank", 1.6f, true, occluder: true),
            new PropRecipe(SourceKind.CityKit, "water-tower", "Prop_WaterTower", 7.5f, true, occluder: true),

            // 搜刮容器：木箱与纸箱（医疗箱用纸箱的浅色观感，武器架用木箱加长摆放）
            new PropRecipe(SourceKind.ToonShooter, "Crate", "Prop_Crate_Wood", 1.0f, true),
            new PropRecipe(SourceKind.ToonShooter, "CardboardBoxes_1", "Prop_Box_Cardboard_A", 0.6f, true),
            new PropRecipe(SourceKind.ToonShooter, "CardboardBoxes_2", "Prop_Box_Cardboard_B", 1.0f, true),
            new PropRecipe(SourceKind.ToonShooter, "Pallet", "Prop_Pallet", 0.19f, false),

            // 围栏与掩体：围栏不给碰撞体（阻挡由灰盒代理承担），沙袋掩体给碰撞体。
            // 目标高度大多等于模型原始高度：Quaternius 的道具本来就是按米建模的，
            // 强行缩放反而会让 1.05 米高的木栅栏变成一堵墙。
            new PropRecipe(SourceKind.ToonShooter, "Fence", "Prop_Fence_Wood", 1.05f, false),
            new PropRecipe(SourceKind.ToonShooter, "MetalFence", "Prop_Fence_Metal", 2.4f, false),
            new PropRecipe(SourceKind.ToonShooter, "SackTrench", "Prop_Barrier", 1.21f, true),

            // 远景工业建筑：只在地图外圈做天际线，不给碰撞体，也不参与导航烘焙
            new PropRecipe(SourceKind.ToonShooter, "Structure_2", "Prop_Warehouse_A", 7.79f, false, occluder: true),
            new PropRecipe(SourceKind.ToonShooter, "Structure_4", "Prop_Warehouse_B", 7.66f, false, occluder: true),

            // —— M7 批次 6：厂房内部加厚（Kenney Factory Kit）——
            // 掩体类：机器与料斗做到 2 米以上，能挡住站立角色与视线
            new PropRecipe(SourceKind.FactoryKit, "machine-fortified", "Prop_Machine_Fortified", 2.4f, true, occluder: true),
            new PropRecipe(SourceKind.FactoryKit, "machine", "Prop_Machine", 2.0f, true, occluder: true),
            new PropRecipe(SourceKind.FactoryKit, "hopper-high-square", "Prop_Hopper", 2.2f, true, occluder: true),
            new PropRecipe(SourceKind.FactoryKit, "robot-arm-a", "Prop_RobotArm", 2.2f, true, occluder: true),
            new PropRecipe(SourceKind.FactoryKit, "crane", "Prop_Crane", 5.0f, true, occluder: true),
            new PropRecipe(SourceKind.FactoryKit, "conveyor-long", "Prop_Conveyor", 1.15f, true),
            new PropRecipe(SourceKind.FactoryKit, "box-large", "Prop_BoxLarge", 1.0f, true),

            // 装饰类：管线与地面标识刻意不给碰撞体，避免在门洞与通道里绊住角色
            new PropRecipe(SourceKind.FactoryKit, "pipe-large-long", "Prop_Pipe", 0.6f, false),
            new PropRecipe(SourceKind.FactoryKit, "warning-orange", "Prop_WarningDecal", 0.06f, false),
            new PropRecipe(SourceKind.FactoryKit, "cone", "Prop_Cone", 0.5f, false),

            // —— M7 批次 6：车辆点缀（Kenney Car Kit）——
            // 车辆是天然的硬掩体：高度按车型给，轿车低于站立视线不参与透视孔。
            new PropRecipe(SourceKind.CarKit, "truck", "Prop_Truck", 2.6f, true, occluder: true),
            new PropRecipe(SourceKind.CarKit, "truck-flat", "Prop_TruckFlat", 2.2f, true, occluder: true),
            new PropRecipe(SourceKind.CarKit, "delivery", "Prop_DeliveryVan", 2.2f, true, occluder: true),
            new PropRecipe(SourceKind.CarKit, "tractor-shovel", "Prop_TractorShovel", 2.6f, true, occluder: true),
            new PropRecipe(SourceKind.CarKit, "van", "Prop_Van", 2.0f, true, occluder: true),
            new PropRecipe(SourceKind.CarKit, "sedan", "Prop_Sedan", 1.45f, true),

            // —— M8 开篇：安全屋陈设（Kenney Furniture Kit）——
            // 货架与柜体是仓储区的主体；柜台与边桌组成商人摊位；地毯与盆栽只做氛围，不给碰撞体。
            new PropRecipe(SourceKind.Furniture, "bookcaseOpen", "Prop_Shelf", 1.9f, true, occluder: true),
            new PropRecipe(SourceKind.Furniture, "bookcaseClosedWide", "Prop_Cabinet", 2.0f, true, occluder: true),
            new PropRecipe(SourceKind.Furniture, "desk", "Prop_Desk", 0.75f, true),
            new PropRecipe(SourceKind.Furniture, "tableCloth", "Prop_Counter", 0.85f, true),
            new PropRecipe(SourceKind.Furniture, "sideTableDrawers", "Prop_SideTable", 0.7f, true),
            new PropRecipe(SourceKind.Furniture, "rugRectangle", "Prop_Rug", 0.02f, false),
            new PropRecipe(SourceKind.Furniture, "pottedPlant", "Prop_Plant", 1.1f, false),

            // —— M8 开篇：仓储杂物与出口门框（Kenney Survival Kit）——
            new PropRecipe(SourceKind.Survival, "barrel", "Prop_Barrel", 1.0f, true),
            new PropRecipe(SourceKind.Survival, "chest", "Prop_Chest", 0.55f, true),
            new PropRecipe(SourceKind.Survival, "box-large", "Prop_SurvivalBox", 0.9f, true),
            new PropRecipe(SourceKind.Survival, "workbench", "Prop_Workbench", 1.0f, true),
            new PropRecipe(SourceKind.Survival, "structure-metal-doorway", "Prop_MetalDoorway", 2.4f, false),
        };

        /// <summary>共享材质路径：一个资源包一个材质，满足「全场材质数量收敛」的要求。</summary>
        private static readonly Dictionary<SourceKind, string> s_MaterialPaths = new Dictionary<SourceKind, string>
        {
            { SourceKind.CityKit, BasinTerrainMaterialBuilder.MaterialsFolder + "/M_KenneyIndustrial.mat" },
            { SourceKind.FactoryKit, BasinTerrainMaterialBuilder.MaterialsFolder + "/M_KenneyFactory.mat" },
            { SourceKind.CarKit, BasinTerrainMaterialBuilder.MaterialsFolder + "/M_KenneyCar.mat" },
            { SourceKind.Furniture, BasinTerrainMaterialBuilder.MaterialsFolder + "/M_KenneyFurniture.mat" },
            { SourceKind.Survival, BasinTerrainMaterialBuilder.MaterialsFolder + "/M_KenneySurvival.mat" },
            { SourceKind.ToonShooter, BasinTerrainMaterialBuilder.MaterialsFolder + "/M_ToonShooterProps.mat" },
        };
    }
}
