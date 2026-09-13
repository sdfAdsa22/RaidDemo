using RaidDemo.Presentation;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 战局装配根的遮挡透视部分：相机与角色之间的遮挡处理。
    /// </summary>
    /// <remarks>
    /// 从 <c>SceneBootstrap.cs</c> 拆出是为了守住单文件行数上限（工程规范 400 行）。
    /// 拆分按职责而不是按行数硬切：这里只有"相机上的透视孔"这一件事。
    /// </remarks>
    public sealed partial class SceneBootstrap
    {
        /// <summary>
        /// 绑定遮挡透视孔：角色被土墙、集装箱挡住时，在遮挡物上以角色为中心开一个圆形透明孔。
        /// </summary>
        /// <remarks>
        /// 组件挂在相机上、目标指向玩家。每次初始化都确保组件存在并重新绑定——
        /// 场景重载会重建相机，只做一次绑定的话会出现「第一局有透视孔、第二局没有」。
        /// </remarks>
        private void BindOcclusionPeephole()
        {
            var camera = m_CameraController != null ? m_CameraController.GetComponent<Camera>() : null;
            if (camera == null || m_PlayerMotor == null)
            {
                return;
            }

            var peephole = camera.GetComponent<OcclusionPeepholeController>();
            if (peephole == null)
            {
                peephole = camera.gameObject.AddComponent<OcclusionPeepholeController>();
            }

            peephole.Bind(m_PlayerMotor.transform, camera);
        }
    }
}
