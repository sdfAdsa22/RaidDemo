using UnityEngine;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 遮挡透视孔的运行时驱动：每帧把「角色在屏幕上的位置、视深度与孔半径」写进全局着色器变量。
    /// </summary>
    /// <remarks>
    /// <para><b>它解决什么问题：</b>斜俯视相机在角色走到土墙、集装箱或厂房后面时会被遮挡。
    /// 常见做法是把相机拉近，但那会改变玩家已经熟悉的构图与可视范围。
    /// 这里改成在遮挡物上开一个跟着角色走的圆孔（着色器见 <c>RaidDemo/OccluderPeephole</c>），
    /// 相机距离与俯角完全不变。</para>
    ///
    /// <para><b>为什么参数走全局变量：</b>全场景的遮挡物共用同一份孔参数，
    /// 逐材质设置会在每次开孔时产生几十次材质改动（并打断合批）。
    /// 用 <c>Shader.SetGlobalVector</c> 只写一次，所有使用该着色器的材质同时生效。</para>
    ///
    /// <para><b>角色不可见时半径写 0：</b>着色器只在半径大于 0 时判定，
    /// 因此角色不在镜头里、或功能被关掉时不会有任何像素被丢弃。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class OcclusionPeepholeController : MonoBehaviour
    {
        /// <summary>着色器里的孔参数变量名。</summary>
        private static readonly int PeepholeParamsId = Shader.PropertyToID("_PeepholeParams");

        /// <summary>跟随目标（玩家角色）。</summary>
        [SerializeField] private Transform m_Target;

        /// <summary>用于投影的相机。为空时自动取本物体上的相机。</summary>
        [SerializeField] private Camera m_Camera;

        /// <summary>
        /// 孔半径占屏幕高度的比例。
        /// </summary>
        /// <remarks>角色在画面里约占屏幕高度的 12%，因此取 0.11：直径 22% 屏幕高度，
        /// 既能完整露出角色，又不会把遮挡物开出一个夸张的大洞。</remarks>
        [SerializeField] private float m_RadiusScreenHeightRatio = 0.11f;

        /// <summary>孔心相对角色根节点的向上偏移（米）。取胸口高度，避免孔偏到脚下。</summary>
        [SerializeField] private float m_TargetHeightOffset = 0.9f;

        /// <summary>功能开关。关掉后立即收孔，便于对照排查。</summary>
        [SerializeField] private bool m_Enabled = true;

        /// <summary>绑定跟随目标与相机。</summary>
        public void Bind(Transform target, Camera camera)
        {
            m_Target = target;
            m_Camera = camera != null ? camera : GetComponent<Camera>();
        }

        private void OnDisable()
        {
            ClearPeephole();
        }

        private void LateUpdate()
        {
            if (!m_Enabled)
            {
                ClearPeephole();
                return;
            }

            var camera = m_Camera != null ? m_Camera : GetComponent<Camera>();
            if (m_Target == null || camera == null
                || !TryResolvePeephole(camera, m_Target.position, m_TargetHeightOffset, m_RadiusScreenHeightRatio, out var parameters))
            {
                ClearPeephole();
                return;
            }

            Shader.SetGlobalVector(PeepholeParamsId, parameters);
        }

        /// <summary>
        /// 计算孔参数。
        /// </summary>
        /// <param name="camera">投影相机。</param>
        /// <param name="targetPosition">角色根节点世界坐标。</param>
        /// <param name="heightOffset">孔心相对根节点的向上偏移（米）。</param>
        /// <param name="radiusRatio">孔半径占屏幕高度的比例。</param>
        /// <param name="parameters">输出：xy 为屏幕像素中心，z 为视深度，w 为半径（像素）。</param>
        /// <returns>角色在相机前方且投影有效时返回 true。</returns>
        /// <remarks>抽成静态方法是为了能在 EditMode 测试里直接验证投影结果，不必进入播放模式。</remarks>
        public static bool TryResolvePeephole(
            Camera camera,
            Vector3 targetPosition,
            float heightOffset,
            float radiusRatio,
            out Vector4 parameters)
        {
            parameters = Vector4.zero;
            if (camera == null)
            {
                return false;
            }

            var world = targetPosition + (Vector3.up * heightOffset);
            var viewport = camera.WorldToViewportPoint(world);
            if (viewport.z <= camera.nearClipPlane)
            {
                // 角色在相机背后或贴在近裁剪面上：不开孔，避免出现方向相反的鬼影孔。
                return false;
            }

            // 角色完全移出画面时收孔；留一点余量，让贴边的角色仍然能被看见。
            if (viewport.x < -0.15f || viewport.x > 1.15f || viewport.y < -0.15f || viewport.y > 1.15f)
            {
                return false;
            }

            var radiusPixels = Mathf.Max(0f, radiusRatio) * camera.pixelHeight;
            if (radiusPixels <= 0f)
            {
                return false;
            }

            parameters = new Vector4(
                viewport.x * camera.pixelWidth,
                viewport.y * camera.pixelHeight,
                viewport.z,
                radiusPixels);
            return true;
        }

        /// <summary>把孔半径清零（本帧不开孔）。</summary>
        private static void ClearPeephole()
        {
            Shader.SetGlobalVector(PeepholeParamsId, Vector4.zero);
        }
    }
}
