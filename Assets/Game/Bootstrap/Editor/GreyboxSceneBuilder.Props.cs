using UnityEditor;
using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 灰盒场景生成器的「道具与地形装饰」部分：所有摆件的统一入口。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么摆件要单独成文件：</b>M7 之后场景里同时存在三类东西——
    /// 灰盒几何体（纯色方块）、项目层预制体（外部素材加工而来）、程序化地形。
    /// 它们的坐标换算方式不同（灰盒与预制体用「谷底为 0」的布局坐标，地形网格直接用世界坐标），
    /// 把换算集中在这个文件里，是为了让每种摆件的调用点都不必自己记得加减 6 米。</para>
    /// </remarks>
    public static partial class GreyboxSceneBuilder
    {
        /// <summary>
        /// 谷底在世界坐标里的高度（米）。
        /// </summary>
        /// <remarks>
        /// <para>M7 批次 2 把地图改成下沉盆地后，谷底落到 y 等于 -6 米。
        /// 但布局表（箱子在哪、撤离点在哪）继续使用「谷底为 0」的坐标：
        /// 布局表描述的是**关卡设计意图**，不应该被地形高度污染——
        /// 若让布局表写 -6，将来盆地深度改了，几十行坐标都得跟着改。</para>
        ///
        /// <para>换算只在两个地方发生：灰盒工厂（<c>CreateBox</c> / <c>CreateDecoration</c>）
        /// 与预制体摆放（本文件的 <see cref="InstantiateProp"/>）。
        /// 因此看到布局代码里的 y 等于 0，指的就是「谷底地面」。</para>
        /// </remarks>
        private const float ValleyFloorY = -BasinTerrainProfile.RimHeight;

        /// <summary>
        /// 实例化一个项目层道具预制体。
        /// </summary>
        /// <param name="prefabName">预制体名（不含扩展名，位于 Art/Props）。</param>
        /// <param name="valleyPosition">位置（谷底为 0 的布局坐标）。</param>
        /// <param name="yawDegrees">水平朝向（度）。</param>
        /// <param name="parent">父节点，可为 null。</param>
        /// <param name="uniformScale">额外等比缩放，用于同一模型的不同体量。</param>
        /// <returns>实例；预制体缺失时返回 null（调用方应能接受回退）。</returns>
        private static GameObject InstantiateProp(
            string prefabName,
            Vector3 valleyPosition,
            float yawDegrees,
            Transform parent = null,
            float uniformScale = 1f)
        {
            var prefab = M7PropPrefabBuilder.LoadPrefab(prefabName);
            if (prefab == null)
            {
                return null;
            }

            return InstantiateProp(prefab, prefabName, valleyPosition, yawDegrees, parent, uniformScale);
        }

        /// <summary>按已载入的预制体实例化（调用方需要先量尺寸时用这个重载，避免重复加载资产）。</summary>
        private static GameObject InstantiateProp(
            GameObject prefab,
            string displayName,
            Vector3 valleyPosition,
            float yawDegrees,
            Transform parent = null,
            float uniformScale = 1f)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = displayName;
            instance.transform.position = new Vector3(
                valleyPosition.x,
                valleyPosition.y + ValleyFloorY,
                valleyPosition.z);
            instance.transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            if (!Mathf.Approximately(uniformScale, 1f))
            {
                instance.transform.localScale = Vector3.one * uniformScale;
            }

            if (parent != null)
            {
                instance.transform.SetParent(parent, worldPositionStays: true);
            }

            return instance;
        }

        /// <summary>
        /// 创建一个只有碰撞体、没有渲染的方块，用作地图边界。
        /// </summary>
        /// <remarks>
        /// <para>地形外裙是缓坡，玩家在塬面上会自然地继续往外走；如果没有边界，
        /// 角色会走出地形网格悬空（或者被地面吸附卡在网格边缘）。用隐形墙拦住，
        /// 视觉上则由外圈的远景山石暗示「这里过不去」。</para>
        ///
        /// <para>不渲染但保留碰撞体，是为了让导航烘焙一并把它当作障碍：
        /// AI 的寻路同样不会把目标点算到地图外面去。</para>
        /// </remarks>
        private static GameObject CreateInvisibleBarrier(
            string name,
            Vector3 valleyCenter,
            Vector3 size,
            Transform parent)
        {
            var barrier = new GameObject(name);
            barrier.transform.position = new Vector3(
                valleyCenter.x,
                valleyCenter.y + ValleyFloorY,
                valleyCenter.z);
            barrier.transform.localScale = size;

            var collider = barrier.AddComponent<BoxCollider>();
            collider.size = Vector3.one;

            if (parent != null)
            {
                barrier.transform.SetParent(parent, worldPositionStays: true);
            }

            return barrier;
        }

        /// <summary>
        /// 创建一段围栏：一块隐形阻挡体 + 沿墙线等距铺开的外观预制体。
        /// </summary>
        /// <param name="name">对象名。</param>
        /// <param name="centerX">墙线中心 X（布局坐标）。</param>
        /// <param name="centerZ">墙线中心 Z（布局坐标）。</param>
        /// <param name="length">墙线长度（米）。</param>
        /// <param name="alongX">true 表示沿 X 轴延伸。</param>
        /// <param name="height">围栏高度（米）。</param>
        /// <param name="thickness">阻挡体厚度（米）。</param>
        /// <param name="parent">父节点。</param>
        /// <remarks>
        /// <para><b>为什么是「隐形代理 + 外观」而不是直接用模型碰撞：</b>围栏模型是一段一段的，
        /// 若逐段使用模型自身的碰撞体，段与段之间的缝隙会让角色卡住，网面镂空处还可能被子弹射线穿过。
        /// 用一整块与旧灰盒围栏尺寸完全一致的阻挡体承担碰撞、模型只负责好看，
        /// 「只换外观、不改碰撞与导航」这条规则就在这里落地。</para>
        /// </remarks>
        private static void CreateFenceLine(
            string name,
            float centerX,
            float centerZ,
            float length,
            bool alongX,
            float height,
            float thickness,
            Transform parent)
        {
            var proxy = new GameObject($"{name}_Blocker");
            proxy.transform.position = new Vector3(centerX, (height * 0.5f) + ValleyFloorY, centerZ);
            proxy.transform.localScale = alongX
                ? new Vector3(length, height, thickness)
                : new Vector3(thickness, height, length);
            proxy.AddComponent<BoxCollider>().size = Vector3.one;
            if (parent != null)
            {
                proxy.transform.SetParent(parent, worldPositionStays: true);
            }

            var prefab = M7PropPrefabBuilder.LoadPrefab("Prop_Fence_Metal");
            if (prefab == null || !MeasureAsset(prefab, out var bounds) || bounds.size.x <= 0.01f)
            {
                return;
            }

            var scale = height / Mathf.Max(0.01f, bounds.size.y);
            var sectionWidth = bounds.size.x * scale;
            var count = Mathf.Max(1, Mathf.RoundToInt(length / sectionWidth));
            var step = length / count;
            var start = -(length * 0.5f) + (step * 0.5f);

            for (var i = 0; i < count; i++)
            {
                var offset = start + (step * i);
                var position = alongX
                    ? new Vector3(centerX + offset, 0f, centerZ)
                    : new Vector3(centerX, 0f, centerZ + offset);

                // 每段按「实际间距 / 模型宽度」再沿长边缩放一次，让围栏正好铺满整条墙线：
                // 既不留下空隙，也不会因为模型宽度与设计长度不一致而互相穿插。
                // 缩放只作用于长边，高度保持 scale，否则矮墙会被拉成高墙。
                var stretch = step / Mathf.Max(0.01f, sectionWidth);
                var instance = InstantiateProp(prefab, $"{name}_{i + 1:D2}", position, alongX ? 0f : 90f, parent, scale);
                if (instance != null)
                {
                    instance.transform.localScale = new Vector3(scale * stretch, scale, scale);
                }
            }
        }
    }
}
