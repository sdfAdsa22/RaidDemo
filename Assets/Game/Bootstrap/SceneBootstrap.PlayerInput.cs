using RaidDemo.Kernel;
using RaidDemo.Presentation;
using RaidDemo.Shared;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// SceneBootstrap 的玩家输入与准星部分。
    /// </summary>
    /// <remarks>
    /// <para>把「读输入、发命令、画准星」这三件与玩家直接相关的事集中在一个文件里。
    /// 它们共同构成「输入 - 命令 - 路由」这条链路的前半段，
    /// 与战局、背包、AI 的装配属于不同的关注点。</para>
    /// </remarks>
    public sealed partial class SceneBootstrap
    {
        /// <summary>
        /// 确保准星组件存在。
        /// </summary>
        /// <remarks>
        /// 准星在运行时创建而非烘焙进场景：它属于纯表现层元素，
        /// 没有需要美术调整的序列化状态，放在运行时创建可以让场景文件保持干净。
        /// </remarks>
        private void EnsureCrosshair()
        {
            if (m_Crosshair != null)
            {
                return;
            }

            var host = new GameObject("AimCrosshair");
            host.transform.SetParent(transform, worldPositionStays: false);
            m_Crosshair = host.AddComponent<AimCrosshair>();

            // 准星贴图来自表现层资产目录；目录为空时组件自动退回程序化十字。
            m_Crosshair.Initialize(
                m_PresentationCatalog != null ? m_PresentationCatalog.CrosshairSprite : null,
                m_PresentationCatalog != null ? m_PresentationCatalog.CrosshairReloadSprite : null);
        }

        /// <summary>
        /// 更新准星位置。
        /// </summary>
        /// <remarks>
        /// 准星位置由世界瞄准点反投影回屏幕得到，与角色朝向同源，
        /// 因此不会出现「准星在一个地方、角色朝另一个地方」的偏差。
        /// </remarks>
        private void UpdateCrosshair()
        {
            if (m_Crosshair == null || m_InputCollector == null || m_CameraController == null)
            {
                return;
            }

            var worldAim = m_InputCollector.AimWorldPosition;

            // 换弹时换一张准星造型：这是"现在打不出去"最直接的提示。
            m_Crosshair.SetReloading(
                m_WeaponController != null
                && m_WeaponController.Runtime != null
                && m_WeaponController.Runtime.IsReloading);

            if (worldAim.IsNearlyZero)
            {
                m_Crosshair.Hide();
                return;
            }

            var cam = m_CameraController.GetComponent<Camera>();
            if (cam == null)
            {
                m_Crosshair.Hide();
                return;
            }

            // 瞄准点位于角色所在高度，反投影时使用相同高度，避免透视造成的偏移。
            var world = new Vector3(worldAim.X, m_PlayerMotor.transform.position.y, worldAim.Y);
            var screen = cam.WorldToScreenPoint(world);

            if (screen.z < 0f)
            {
                // 点在相机背后，此时不应绘制准星。
                m_Crosshair.Hide();
                return;
            }

            m_Crosshair.SetScreenPosition(new Vector2(screen.x, screen.y));
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            // 编辑器下失去焦点时释放光标，避免开发过程中无法操作其他窗口。
            // 发行构建中窗口失去焦点并不是常见场景，保持锁定更符合预期。
#if UNITY_EDITOR
            if (m_InputCollector != null)
            {
                m_InputCollector.SetCursorLock(hasFocus);
            }
#endif
        }

        /// <summary>读取本帧输入。</summary>
        private void CollectInput()
        {
            if (m_InputCollector == null)
            {
                m_PendingMoveIntent = Vector2F.Zero;
                m_PendingLookDirection = Vector2F.Zero;
                m_PendingWantsToSprint = false;
                return;
            }

            m_PendingMoveIntent = m_InputCollector.ReadMoveIntent(out var sprint);
            m_PendingLookDirection = m_InputCollector.LookDirection;
            m_PendingWantsToSprint = sprint;
        }

        /// <summary>
        /// 把输入意图封装成命令并交给命令路由。
        /// </summary>
        /// <remarks>
        /// 这是整个项目最关键的架构约束的落点：输入不直接驱动移动，
        /// 而是先变成命令经统一入口执行。联机时只需把这里的本地执行换成网络发送。
        /// </remarks>
        private void DispatchMoveCommand()
        {
            var intent = new PlayerMoveIntent(
                m_InputCollector != null ? m_InputCollector.PlayerId : 0,
                m_PendingMoveIntent,
                m_PendingLookDirection,
                m_PendingWantsToSprint,
                ++m_CommandSequence);

            var result = m_CommandRouter.Dispatch(intent);
            if (!result.Success)
            {
                Debug.LogWarning($"[RaidDemo] 移动命令被拒绝：{result.Code} - {result.Message}", this);
            }
        }
    }
}
