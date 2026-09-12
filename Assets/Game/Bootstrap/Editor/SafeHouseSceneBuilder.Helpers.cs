using UnityEditor;
using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 安全屋生成器的几何体工厂。
    /// </summary>
    /// <remarks>与主文件拆开是为了遵守单文件行数上限；这里只有「怎么造一个方块/一段墙」。</remarks>
    public static partial class SafeHouseSceneBuilder
    {
        /// <summary>创建一个带碰撞体的方块。</summary>
        private static GameObject CreateBox(string name, Vector3 center, Vector3 size, Transform parent)
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

        /// <summary>创建一段轴对齐的墙。</summary>
        private static void CreateWall(
            string name,
            float centerX,
            float centerZ,
            float length,
            bool alongX,
            Transform parent,
            Color color)
        {
            var size = alongX
                ? new Vector3(length, WallHeight, WallThickness)
                : new Vector3(WallThickness, WallHeight, length);
            var wall = CreateBox(name, new Vector3(centerX, WallHeight * 0.5f, centerZ), size, parent);
            SetColor(wall, color);
        }

        /// <summary>设置材质颜色。</summary>
        private static void SetColor(GameObject target, Color color)
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

            var material = new Material(shader) { name = $"SafeHouse_{target.name}" };
            material.SetColor("_BaseColor", color);
            renderer.sharedMaterial = material;
        }

        /// <summary>确保资源目录存在。</summary>
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
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
