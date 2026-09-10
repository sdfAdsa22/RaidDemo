using System;

namespace RaidDemo.Shared
{
    /// <summary>
    /// 瞄准方向求解。
    /// </summary>
    /// <remarks>
    /// 抽成纯函数而非写在 MonoBehaviour 里，原因是这段逻辑包含一个容易出错、
    /// 且难以在场景中手工验证的边界条件：鼠标与角色重合时方向向量长度趋近于零。
    /// 放在共享层后可以直接用单元测试覆盖各种距离情形，而不需要启动游戏、
    /// 也不需要真实鼠标。
    /// </remarks>
    public static class AimResolver
    {
        /// <summary>
        /// 由鼠标的世界投影点求角色应有的朝向。
        /// </summary>
        /// <param name="aimWorldPoint">鼠标在角色所在平面上的投影点。</param>
        /// <param name="origin">角色位置。</param>
        /// <param name="deadZoneRadius">
        /// 死区半径。当投影点与角色的距离小于该值时判定为"瞄准点被吸住"。
        /// </param>
        /// <returns>
        /// 单位朝向向量；当投影点落在死区内时返回零向量，
        /// 表示调用方应保持上一次朝向不变。
        /// </returns>
        /// <remarks>
        /// 死区存在的必要性：当鼠标停在角色身上时，方向向量极短，
        /// 玩家手指的微小抖动或角色的微小位移都会让方向在 360 度内乱跳，
        /// 表现为角色剧烈自转。引入死区后角色会稳定保持原朝向。
        /// </remarks>
        public static Vector2F Resolve(Vector2F aimWorldPoint, Vector2F origin, float deadZoneRadius)
        {
            var delta = aimWorldPoint - origin;
            var threshold = MathF.Max(0f, deadZoneRadius);

            return delta.SqrMagnitude < threshold * threshold
                ? Vector2F.Zero
                : delta.Normalized;
        }
    }
}
