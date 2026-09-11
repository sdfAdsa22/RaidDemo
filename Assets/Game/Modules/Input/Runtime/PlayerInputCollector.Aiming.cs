using UnityEngine;
using UnityEngine.InputSystem;
using RaidDemo.Shared;

namespace RaidDemo.Input
{
    /// <summary>
    /// PlayerInputCollector 的瞄准解算部分。
    /// </summary>
    /// <remarks>
    /// <para>拆成 partial 文件的原因只有一个：单文件超过了项目规定的 400 行上限。
    /// 按职责切分之后，这里放的是"鼠标如何变成世界瞄准点"，
    /// 主文件放的是"键位如何变成意图"。</para>
    /// <para>瞄准解算是输入层里唯一涉及三维空间的部分，单独成文件也便于阅读。</para>
    /// </remarks>
    public sealed partial class PlayerInputCollector
    {
        /// <summary>
        /// 推进瞄准点并计算角色朝向。
        /// </summary>
        /// <remarks>
        /// 流程是：鼠标移动增量推进屏幕瞄准点，限制在屏幕内，
        /// 再由射线与地面求交得到世界瞄准点，最后限制在最大射程内并求朝向。
        /// 世界瞄准点会被保存下来，供准星绘制复用，
        /// 从而保证准星与角色朝向指向同一个位置。
        /// </remarks>
        private Vector2F ResolveLookDirection()
        {
            var mouse = Mouse.current;
            if (mouse == null)
            {
                return Vector2F.Zero;
            }

            var cam = m_AimCamera != null ? m_AimCamera : Camera.main;
            if (cam == null)
            {
                return Vector2F.Zero;
            }

            UpdateAimScreenPosition(mouse);

            var world = ScreenToWorldOnPlane(cam, m_AimScreenPosition, m_OriginHeight);
            var worldAim = AimResolver.ClampToMaxRange(
                new Vector2F(world.x, world.y),
                new Vector2F(m_OriginPosition.x, m_OriginPosition.y),
                m_MaxAimDistance);

            m_AimWorldPosition = worldAim;
            m_HasAimPosition = true;

            // 死区判定与方向归一化由共享层的纯函数完成，便于单元测试覆盖边界情形。
            // 返回零向量表示"保持上一次朝向不变"。
            return AimResolver.Resolve(
                worldAim,
                new Vector2F(m_OriginPosition.x, m_OriginPosition.y),
                m_AimDeadZoneRadius);
        }

        /// <summary>
        /// 用鼠标移动增量推进屏幕瞄准点。
        /// </summary>
        /// <remarks>
        /// 鼠标被锁定时，系统光标固定在窗口内不再移动，但仍然会上报移动增量。
        /// 因此这里不使用光标的绝对位置，而是自行累加增量维护瞄准点，
        /// 这样才能在锁定状态下正常瞄准。
        /// 未锁定时直接采用光标位置，保持编辑器下的常规操作习惯。
        /// </remarks>
        private void UpdateAimScreenPosition(Mouse mouse)
        {
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                if (!m_HasAimPosition)
                {
                    // 首次进入锁定时以屏幕中心作为初始瞄准点，避免从角落开始。
                    m_AimScreenPosition = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                    m_HasAimPosition = true;
                }

                m_AimScreenPosition += mouse.delta.ReadValue();

                var clamp = AimResolver.ClampToScreen(
                    new Vector2F(m_AimScreenPosition.x, m_AimScreenPosition.y),
                    Screen.width,
                    Screen.height);
                m_AimScreenPosition = new Vector2(clamp.X, clamp.Y);
                return;
            }

            // 未锁定状态（例如编辑器失去焦点，或玩家打开了界面菜单）：
            // 此时鼠标位置由系统光标决定，但系统光标可能位于游戏窗口之外，
            // 其坐标对游戏没有意义。因此这里重置跟踪状态，
            // 等下次重新锁定时再从屏幕中心开始。
            if (!m_HasAimPosition)
            {
                m_AimScreenPosition = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            }
        }

        /// <summary>
        /// 把屏幕坐标转换为角色所在水平面上的世界坐标。
        /// </summary>
        /// <remarks>
        /// 不直接使用 ScreenToWorldPoint，是因为它需要知道目标点的深度；
        /// 这里改用一条从摄像机出发的射线与水平面求交，结果与角色所在高度无关。
        /// </remarks>
        private static Vector2 ScreenToWorldOnPlane(Camera cam, Vector2 screenPoint, float planeHeight)
        {
            var ray = cam.ScreenPointToRay(screenPoint);
            var plane = new Plane(Vector3.up, new Vector3(0f, planeHeight, 0f));

            if (plane.Raycast(ray, out var distance))
            {
                var hit = ray.GetPoint(distance);
                return new Vector2(hit.x, hit.z);
            }

            return Vector2.zero;
        }
    }
}
