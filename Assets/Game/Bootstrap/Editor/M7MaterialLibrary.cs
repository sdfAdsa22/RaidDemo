using UnityEditor;
using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// M7 场景资产的材质工具：统一用 URP Lit 创建/更新材质资产。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么所有外部模型都要重赋项目材质：</b>资源包自带的材质多半是按内置渲染管线导出的，
    /// 在本项目的 URP 下会渲染成粉色；即便能显示，不同包的金属度、光滑度也各不相同，
    /// 混在一起会出现「同一个场景里有的发亮、有的发灰」的割裂感。
    /// 统一重赋项目材质是 M7 素材技术标准里的硬性要求。</para>
    ///
    /// <para><b>为什么集中成一个工具：</b>材质参数（金属度 0、光滑度 0.05~0.2、纯色或小色块贴图）
    /// 在标准文档里只定义一次；如果每个构建器各自 new Material，改一次标准就要翻遍所有文件。</para>
    /// </remarks>
    public static class M7MaterialLibrary
    {
        /// <summary>URP Lit 着色器的名字。字符串只在这里出现一次，便于将来统一替换。</summary>
        public const string LitShaderName = "Universal Render Pipeline/Lit";

        /// <summary>
        /// 带遮挡透视孔的着色器名。
        /// </summary>
        /// <remarks>用在「会挡住相机的大件」上：地形、厂房墙体、装卸平台、集装箱、远景山石。
        /// 角色与小道具继续用普通 URP Lit——角色自己开洞会把人物抠空。</remarks>
        public const string PeepholeShaderName = "RaidDemo/OccluderPeephole";

        /// <summary>
        /// 创建或更新一个不透明 URP Lit 材质资产。
        /// </summary>
        /// <param name="assetPath">材质资产路径（工程相对）。</param>
        /// <param name="color">基础色。传入贴图时它是乘算的色调。</param>
        /// <param name="texture">基础贴图，可为 null（纯色材质）。</param>
        /// <param name="smoothness">光滑度，卡通风格取 0.05~0.2。</param>
        /// <param name="occluder">true 时使用带透视孔的着色器（用于会挡住相机的大件）。</param>
        public static Material EnsureLitMaterial(
            string assetPath,
            Color color,
            Texture texture = null,
            float smoothness = 0.1f,
            bool occluder = false)
        {
            var shaderName = occluder ? PeepholeShaderName : LitShaderName;
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"[RaidDemo] 找不到 {shaderName} 着色器，材质 {assetPath} 无法创建。");
                return null;
            }

            EnsureFolder(System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/'));

            var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
            {
                material = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(assetPath) };
                AssetDatabase.CreateAsset(material, assetPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", smoothness);
            if (texture != null)
            {
                material.SetTexture("_BaseMap", texture);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// 颜色确实变化时才写入。
        /// </summary>
        /// <param name="material">目标材质。</param>
        /// <param name="property">颜色属性名。</param>
        /// <param name="color">期望值。</param>
        /// <returns>实际写入返回 true。</returns>
        /// <remarks>
        /// <para>构建器应当是幂等的：第二次执行不产生任何改动。
        /// 反复写同一个颜色会因为浮点表示差异（0.41332123 与 0.41332126）
        /// 在材质文件里留下一行永远存在的差异，让每次构建的 diff 都变脏。</para>
        /// <para>属性不存在时静默跳过：不同着色器的颜色属性名不一样
        /// （URP Lit 用 <c>_BaseColor</c>，内置管线用 <c>_Color</c>），
        /// 调用方可以两个都试，不必自己判断着色器。</para>
        /// </remarks>
        public static bool SetColorIfDifferent(Material material, string property, Color color)
        {
            if (material == null || !material.HasProperty(property))
            {
                return false;
            }

            var current = material.GetColor(property);
            if (current.r == color.r && current.g == color.g &&
                current.b == color.b && current.a == color.a)
            {
                return false;
            }

            material.SetColor(property, color);
            return true;
        }

        /// <summary>确保目录存在（AssetDatabase 版本，避免直接操作文件系统）。</summary>
        public static void EnsureFolder(string path)
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
