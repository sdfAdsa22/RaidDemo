using UnityEditor;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 灰盒场景生成器的几何体工厂。
    /// </summary>
    /// <remarks>
    /// <para>所有灰盒几何体都经由本文件创建，好处是「灰盒长什么样」这件事只有一处定义：
    /// 想整体改配色、改墙体厚度、给箱子统一加倒角，都只需要改这里。</para>
    ///
    /// <para>工厂方法一律**显式传入父节点**。场景层级不只是给人看的：
    /// 生成器把同属一个分区的物体挂在同一个父节点下，排查问题（以及以后整块替换成美术资源）
    /// 时可以直接按分区处理，不必在几十个平铺对象里挑。</para>
    /// </remarks>
    public static partial class GreyboxSceneBuilder
    {
        /// <summary>创建一个空节点，作为一组几何体的父级。</summary>
        private static Transform CreateGroup(string name)
        {
            var group = new GameObject(name);
            return group.transform;
        }

        /// <summary>创建一个带碰撞体的方块，作为灰盒几何体。</summary>
        /// <param name="name">对象名。</param>
        /// <param name="center">世界坐标下的几何中心。</param>
        /// <param name="size">长宽高（米）。</param>
        /// <param name="parent">父节点，可为 null。</param>
        private static GameObject CreateBox(
            string name,
            Vector3 center,
            Vector3 size,
            Transform parent = null)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.position = center;
            box.transform.localScale = size;
            if (parent != null)
            {
                box.transform.SetParent(parent, worldPositionStays: true);
            }

            return box;
        }

        /// <summary>
        /// 创建一段轴对齐的墙体。
        /// </summary>
        /// <param name="name">对象名。</param>
        /// <param name="centerX">墙体中心的世界 X 坐标。</param>
        /// <param name="centerZ">墙体中心的世界 Z 坐标。</param>
        /// <param name="length">墙体长度（米），沿 X 轴或 Z 轴延伸。</param>
        /// <param name="alongX">true 表示沿 X 轴延伸，false 表示沿 Z 轴延伸。</param>
        /// <param name="height">墙体高度（米）。</param>
        /// <param name="thickness">墙体厚度（米）。</param>
        /// <param name="parent">父节点。</param>
        /// <param name="color">材质颜色。</param>
        /// <remarks>
        /// 只支持轴对齐的直墙：斜墙在斜俯视下的可读性反而更差（玩家难以判断能不能绕过去），
        /// 而灰盒阶段需要的只是「通道与掩体」，轴对齐已经够用，且数值好核对、不容易摆错。
        /// </remarks>
        private static GameObject CreateWall(
            string name,
            float centerX,
            float centerZ,
            float length,
            bool alongX,
            float height,
            float thickness,
            Transform parent,
            Color color)
        {
            var size = alongX
                ? new Vector3(length, height, thickness)
                : new Vector3(thickness, height, length);
            var wall = CreateBox(
                name,
                new Vector3(centerX, height * 0.5f, centerZ),
                size,
                parent);
            SetMaterialColor(wall, color);
            return wall;
        }

        /// <summary>
        /// 创建一段连接地面与高台的坡道。
        /// </summary>
        /// <param name="name">对象名。</param>
        /// <param name="centerX">坡道中心的世界 X 坐标。</param>
        /// <param name="lowZ">低端（贴地那端）的世界 Z 坐标，必须位于高端的北侧（Z 更大）。</param>
        /// <param name="highZ">高端（接高台那端）的世界 Z 坐标。</param>
        /// <param name="width">坡道宽度（米）。</param>
        /// <param name="rise">两端高差（米）。</param>
        /// <param name="thickness">坡道板厚度（米）。</param>
        /// <param name="parent">父节点。</param>
        /// <remarks>
        /// <para>坡道是一块**绕 X 轴旋转的板**，不是若干级台阶。理由有两条：
        /// 一是导航网格对连续斜面烘焙出的可行走面更平滑，AI 不会在台阶上抖；
        /// 二是玩家的地面吸附靠向下射线，连续斜面得到的高度是连续变化的，
        /// 而台阶会产生一格一格的跳变，视觉上很像卡顿。</para>
        ///
        /// <para>板厚会带来一个容易忽略的偏差：若直接把板的几何中心放在高差一半的位置，
        /// 低端会高出地面约半个板厚，玩家走上去会「踩空一级」。
        /// 因此这里把中心沿板的法线方向下沉半个板厚的投影量，
        /// 让板的**上表面**恰好低端齐地、高端齐台面。</para>
        /// </remarks>
        private static GameObject CreateRamp(
            string name,
            float centerX,
            float lowZ,
            float highZ,
            float width,
            float rise,
            float thickness,
            Transform parent)
        {
            var run = Mathf.Abs(highZ - lowZ);
            var length = Mathf.Sqrt((run * run) + (rise * rise));
            var angleDegrees = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;

            // 绕 X 轴正角度旋转时，本地 +Z 端会向下沉。低端在北侧（Z 更大）时用正角度，
            // 反之取负，保证高低方向与参数语义一致，而不是要求调用方自己换算符号。
            if (lowZ < highZ)
            {
                angleDegrees = -angleDegrees;
            }

            var halfThicknessProjection = thickness * 0.5f * Mathf.Cos(angleDegrees * Mathf.Deg2Rad);
            var center = new Vector3(
                centerX,
                (rise * 0.5f) - halfThicknessProjection,
                (lowZ + highZ) * 0.5f);

            var ramp = CreateBox(name, center, new Vector3(width, thickness, length), parent);
            ramp.transform.rotation = Quaternion.Euler(angleDegrees, 0f, 0f);
            SetMaterialColor(ramp, new Color(0.44f, 0.45f, 0.47f));
            return ramp;
        }

        /// <summary>
        /// 创建一个仅用于观看的几何体（不参与碰撞与导航烘焙）。
        /// </summary>
        /// <remarks>
        /// 撤离点的地面色块、朝向指示物之类的纯装饰件必须去掉碰撞体：
        /// 它们贴着地面，留着碰撞体会让玩家在色块边缘被抬高一点点，
        /// 表现为人物莫名其妙地抖一下——这种问题很难从画面上定位。
        /// </remarks>
        private static GameObject CreateDecoration(
            string name,
            PrimitiveType primitive,
            Vector3 position,
            Vector3 scale,
            Color color,
            Transform parent)
        {
            var decoration = GameObject.CreatePrimitive(primitive);
            decoration.name = name;
            decoration.transform.position = position;
            decoration.transform.localScale = scale;
            if (parent != null)
            {
                decoration.transform.SetParent(parent, worldPositionStays: true);
            }

            var collider = decoration.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }

            var navMeshModifier = decoration.GetComponent<NavMeshModifier>();
            if (navMeshModifier == null)
            {
                navMeshModifier = decoration.AddComponent<NavMeshModifier>();
            }

            // 被忽略的对象不参与导航烘焙。即使已经删掉碰撞体，
            // 运行时烘焙仍可能把某些几何体算进去，这里显式声明意图，避免 AI 绕着一块色块走。
            navMeshModifier.ignoreFromBuild = true;
            SetMaterialColor(decoration, color);
            return decoration;
        }

        /// <summary>
        /// 设置对象的材质颜色。
        /// </summary>
        /// <remarks>
        /// 通过创建材质资产而非直接改 renderer.material，是为了避免在场景中
        /// 隐式生成匿名材质实例——那会让材质无法被版本控制统一管理。
        /// 生成器创建的是随场景序列化保存的材质对象，场景文件本身就是它的载体。
        /// </remarks>
        private static void SetMaterialColor(GameObject target, Color color)
        {
            var renderer = target.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                return;
            }

            var material = new Material(shader) { name = $"Greybox_{target.name}" };
            material.SetColor("_BaseColor", color);
            renderer.sharedMaterial = material;
        }
    }
}
