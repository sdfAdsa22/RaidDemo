using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 灰盒场景生成器的「工业区加厚」部分（M7 批次 6）。
    /// </summary>
    /// <remarks>
    /// <para>批次 6 按负责人拍板的方案 C，用 Kenney Factory Kit 与 Car Kit 给现有唯一地图加厚：
    /// 厂房内部补机器、料斗、机器人臂与管线，堆场外围补卡车、工程车辆与货箱，
    /// 让「工业区 + 集装箱仓库」的主题在俯视下也能读出来。</para>
    ///
    /// <para><b>摆放约束（本文件最重要的部分）：</b>新增物件除管线与地面标识外都带碰撞体，
    /// 因此必须避开三类东西——巡逻路线的拐点与连线、战利品容器的摆放点、撤离通道与坡道口。
    /// 位置全部使用布局坐标（谷底为 0），与其余分区表保持一致；
    /// 每条坐标都注明它为什么落在这里，方便以后改地图时不会被无声地挪进通道。</para>
    /// </remarks>
    public static partial class GreyboxSceneBuilder
    {
        /// <summary>厂房内部加厚物件。</summary>
        private static readonly (string PrefabName, Vector3 Position, float YawDegrees)[] s_FactoryProps =
        {
            // 西墙边：加固机床与机器人臂靠墙摆放，给西厅一个近战拐角；
            // 与厂房巡逻路线（x=-24，z 从 -8 到 2）保持 1.5 米以上净距。
            ("Prop_Machine_Fortified", new Vector3(-25.5f, 0f, -4.5f), 90f),
            ("Prop_RobotArm", new Vector3(-26.2f, 0f, 11.5f), 180f),
            // 北厅：料斗与机床组成一条横向掩体，避开 (-12.5, 11.5) 的武器架与 (-23, 10.5) 的旧机床。
            ("Prop_Hopper", new Vector3(-20.8f, 0f, 12.3f), 0f),
            ("Prop_Machine", new Vector3(-16.2f, 0f, 11.0f), 0f),
            // 南厅：传送带贴南墙摆放，避开 (-11.5, -12) 的弹药箱。
            ("Prop_Conveyor", new Vector3(-16.5f, 0f, -11.5f), 0f),
            // 东厅：货箱堆，避开 x=-15 的南隔断与 (-12, 5.5) 的巡逻终点。
            ("Prop_BoxLarge", new Vector3(-13.5f, 0f, -1.5f), 0f),
            // 西墙管线：纯装饰、无碰撞，贴着墙面走。
            ("Prop_Pipe", new Vector3(-26.4f, 0f, 1.5f), 0f),
            // 地面警示标识：放在西厅中部空地，不挡任何门洞。
            ("Prop_WarningDecal", new Vector3(-21f, 0f, -3.5f), 0f),
            // 东门洞两侧的锥桶：无碰撞，只用颜色提示入口位置。
            ("Prop_Cone", new Vector3(-9.6f, 0f, -1.6f), 0f),
            ("Prop_Cone", new Vector3(-9.6f, 0f, 1.6f), 0f),
        };

        /// <summary>堆场与外围的车辆 / 工业点缀。</summary>
        private static readonly (string PrefabName, Vector3 Position, float YawDegrees)[] s_VehicleProps =
        {
            // 堆场东侧的卡车：车头朝北停在东边界内侧（模型长边沿本地 Z，yaw 0 才是南北向），
            // 包围盒落在 x=[24.5, 27.5]，不再像第一版那样插进东侧土墙。
            ("Prop_Truck", new Vector3(26.0f, 0f, -7.0f), 0f),
            // 堆场北侧：平板卡车横在空地上，给北入口提供一段掩体。
            ("Prop_TruckFlat", new Vector3(0.8f, 0f, 12.0f), 0f),
            // 出生点北侧：厢式车停在谷底中部的开阔地，形成第一段可用的掩体。
            ("Prop_DeliveryVan", new Vector3(-5.0f, 0f, -15.5f), 90f),
            // 东北角：挖掘装载机作为工业地标，与 7.5 米水塔形成高低错落的天际线；
            // 向南退 1 米，避免铲斗越过北侧土墙。
            ("Prop_TractorShovel", new Vector3(18.5f, 0f, 25.0f), 210f),
            // 堆场东南角：一辆轿车与卡车错开停放，避免看起来像整齐的停车场。
            ("Prop_Sedan", new Vector3(25.5f, 0f, -13.5f), 20f),
            // 厂房北墙外：厢式车补给点，位于厂房与北坡道之间但不挡坡道走廊。
            ("Prop_Van", new Vector3(-14.5f, 0f, 18.0f), 0f),
            // 北塬面上的高位吊车：y=6 表示塬面高度（谷底为 0），放在北坡道东侧的空地上。
            // 第一版放在堆场东北角，包围盒压到 (24,16) 的武器架与巡逻点 (25,24)；
            // 移到塬面后不再占用谷底空间，只作为出谷时能看到的天际线。
            ("Prop_Crane", new Vector3(22.0f, 6f, 31.0f), 180f),
        };

        /// <summary>
        /// 创建批次 6 的工业区加厚摆件。
        /// </summary>
        /// <remarks>
        /// 分两组创建只是为了层级与命名清晰：厂房组进 <c>Zone_IndustrialProps_Factory</c>，
        /// 车辆与吊车进 <c>Zone_IndustrialProps</c>，
        /// 在层级面板里一眼能看出哪些是批次 6 新增的内容。
        /// </remarks>
        private static void CreateIndustrialProps()
        {
            var factoryParent = CreateGroup("Zone_IndustrialProps_Factory");
            for (var i = 0; i < s_FactoryProps.Length; i++)
            {
                var (prefabName, position, yaw) = s_FactoryProps[i];
                var instance = InstantiateProp(prefabName, position, yaw, factoryParent);
                if (instance != null)
                {
                    instance.name = $"FactoryProp_{i + 1:D2}_{prefabName.Replace("Prop_", string.Empty)}";
                }
            }

            var outdoorParent = CreateGroup("Zone_IndustrialProps");
            for (var i = 0; i < s_VehicleProps.Length; i++)
            {
                var (prefabName, position, yaw) = s_VehicleProps[i];
                var instance = InstantiateProp(prefabName, position, yaw, outdoorParent);
                if (instance != null)
                {
                    instance.name = $"Vehicle_{i + 1:D2}_{prefabName.Replace("Prop_", string.Empty)}";
                }
            }
        }
    }
}
