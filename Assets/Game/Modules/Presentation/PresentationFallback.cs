using UnityEngine;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 占位外观的共享材质与着色：给运行时用 <see cref="GameObject.CreatePrimitive(PrimitiveType)"/>
    /// 兜底出来的图元一个能看的颜色。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要它：</b>`CreatePrimitive` 拿的是引擎内置的默认材质，而本工程用 URP——
    /// 内置材质在**编辑器里正常、在构建版里渲染成洋红**（missing shader）。
    /// 症状是"地图上出现紫色胶囊"，而代码与日志毫无异常：远端玩家/敌人的预制体一旦取不到，
    /// 兜底占位物就会以这个颜色出现在地图上（P4 用户反馈里就把它当成了"神秘的紫色椭圆体"）。</para>
    ///
    /// <para>材质是共享的（所有占位物共用一个实例）：占位物是异常路径的产物，
    /// 不值得为每个实例生成材质，也不该参与后续的颜色改动（受击闪烁用的是属性块）。</para>
    /// </remarks>
    public static class PresentationFallback
    {
        /// <summary>占位色：中性浅灰，既不像敌人也不像任何品质色。</summary>
        private static readonly Color PlaceholderColor = new Color(0.82f, 0.84f, 0.88f, 1f);

        private static Material s_SolidMaterial;

        /// <summary>共享的占位材质（首次访问时创建）。</summary>
        public static Material SolidMaterial
        {
            get
            {
                if (s_SolidMaterial == null)
                {
                    s_SolidMaterial = CreateMaterial();
                }

                return s_SolidMaterial;
            }
        }

        /// <summary>把一个运行时图元换成占位材质。</summary>
        /// <param name="primitive">`CreatePrimitive` 生成的对象。</param>
        public static void Apply(GameObject primitive)
        {
            if (primitive == null)
            {
                return;
            }

            var renderer = primitive.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = SolidMaterial;
            }
        }

        /// <summary>创建占位材质；shader 都找不到时返回 null（此时保持引擎默认，不做更坏的事）。</summary>
        private static Material CreateMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader == null)
            {
                return null;
            }

            var material = new Material(shader) { color = PlaceholderColor };
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", PlaceholderColor);
            }

            return material;
        }
    }
}
