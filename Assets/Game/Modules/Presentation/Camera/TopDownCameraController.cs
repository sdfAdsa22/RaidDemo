using UnityEngine;

namespace RaidDemo.Presentation
{
    /// <summary>
    /// 斜俯视跟随相机。
    /// </summary>
    /// <remarks>
    /// 采用固定俯角（默认 45 度）跟随目标，不做旋转。这是本项目选择的视角形式：
    /// 相比正俯视能保留立体感与高低差可读性，相比第三人称又能大幅压低动画与美术成本。
    ///
    /// 相机不参与任何游戏逻辑，只读取目标位置。因此它可以被随时替换或删除，
    /// 不影响模拟层的正确性。
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class TopDownCameraController : MonoBehaviour
    {
        /// <summary>跟随目标。为空时相机保持原位。</summary>
        [SerializeField] private Transform m_Target;

        /// <summary>俯角（度）。45 度是俯视与立体感的折中值。</summary>
        [SerializeField] private float m_PitchDegrees = 45f;

        /// <summary>相机到目标的距离。</summary>
        [SerializeField] private float m_Distance = 14f;

        /// <summary>
        /// 相机位置的世界坐标偏移。
        /// 用于把镜头稍微抬高，使玩家获得更远的视野；不需要时保持零即可。
        /// </summary>
        [SerializeField] private Vector3 m_WorldOffset = Vector3.zero;

        /// <summary>
        /// 平滑时间（秒）。取 0 表示硬跟随。
        /// 默认 0.12 秒在"跟手"与"稳定"之间取得平衡：既不会明显滞后，也能吸收微小抖动。
        /// </summary>
        [SerializeField] private float m_SmoothTime = 0.12f;

        private Vector3 m_Velocity;

        /// <summary>设置跟随目标。</summary>
        public void SetTarget(Transform target, bool snap = true)
        {
            m_Target = target;
            if (snap)
            {
                SnapToTarget();
            }
        }

        /// <summary>立即把相机放到目标位置，不经过平滑。用于进入战局时的初始化。</summary>
        public void SnapToTarget()
        {
            var desired = ResolveDesiredPosition();
            if (desired.HasValue)
            {
                transform.position = desired.Value;
                transform.rotation = Quaternion.Euler(m_PitchDegrees, 0f, 0f);
            }
        }

        private void LateUpdate()
        {
            var desired = ResolveDesiredPosition();
            if (!desired.HasValue)
            {
                return;
            }

            // 在 LateUpdate 中更新：此时角色移动已完成，可避免相机与角色在同一帧内互相追赶产生抖动。
            transform.position = m_SmoothTime <= 0f
                ? desired.Value
                : Vector3.SmoothDamp(transform.position, desired.Value, ref m_Velocity, m_SmoothTime);

            transform.rotation = Quaternion.Euler(m_PitchDegrees, 0f, 0f);
        }

        /// <summary>
        /// 计算相机应当在的位置。
        /// </summary>
        /// <remarks>
        /// 俯角与距离是极坐标参数，这里显式展开为世界坐标，而不是使用 Transform 的
        /// 局部偏移。原因是相机自身的旋转由俯角决定，若再用局部偏移定位，
        /// 旋转与位置会互相影响，调整俯角时相机会绕圈而非原地改变角度。
        /// </remarks>
        private Vector3? ResolveDesiredPosition()
        {
            if (m_Target == null)
            {
                return null;
            }

            var pitchRadians = m_PitchDegrees * Mathf.Deg2Rad;
            var forward = new Vector3(0f, -Mathf.Sin(pitchRadians), Mathf.Cos(pitchRadians));

            var focus = m_Target.position + m_WorldOffset;
            return focus - (forward * m_Distance);
        }
    }
}
